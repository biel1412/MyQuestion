namespace MyQuestion.Core;

/// <summary>
/// Motor de busca: recebe o texto extraído do print/PDF e devolve
/// os manuais mais prováveis e os trechos de regra mais relevantes.
/// </summary>
public sealed class RegramentoSearchEngine
{
    private const double K1 = 1.2;
    private const double B = 0.75;

    private readonly KnowledgeBase _base;
    private readonly List<IndexedManual> _manuais;
    private readonly List<IndexedRegra> _regras;
    private readonly Dictionary<string, int> _docFreq = new(StringComparer.Ordinal);
    private readonly double _avgLen;

    public RegramentoSearchEngine(KnowledgeBase knowledgeBase)
    {
        _base = knowledgeBase;

        _manuais = knowledgeBase.Manuais.Select(m => new IndexedManual(
            m,
            TextNormalizer.KeywordForm(m.Titulo),
            m.PalavrasChave
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => (Original: k.Trim(), Form: TextNormalizer.KeywordForm(k)))
                .Where(k => k.Form.Trim().Length > 0)
                .GroupBy(k => k.Form).Select(g => g.First())
                .ToList()
        )).ToList();

        _regras = knowledgeBase.Regras.Select(r =>
        {
            var tokens = TextNormalizer.Tokenize($"{r.Titulo} {r.Titulo} {string.Join(' ', r.Tags)} {string.Join(' ', r.Tags)} {r.Texto}");
            var tf = tokens.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            return new IndexedRegra(r, knowledgeBase.FindManual(r.Manual), tf, tokens.Count);
        }).ToList();

        foreach (var regra in _regras)
            foreach (var term in regra.TermFreq.Keys)
                _docFreq[term] = _docFreq.GetValueOrDefault(term) + 1;

        _avgLen = _regras.Count == 0 ? 1 : _regras.Average(r => (double)r.Length);
    }

    public KnowledgeBase Base => _base;

    public SearchResult Search(string? pergunta, int maxManuais = 5, int maxRegras = 6)
    {
        if (string.IsNullOrWhiteSpace(pergunta)) return SearchResult.Empty;

        var searchable = TextNormalizer.ToSearchable(pergunta);
        var queryTokens = TextNormalizer.Tokenize(pergunta).Distinct().ToList();
        var sinais = SignalDetector.Detect(pergunta);
        var publicos = sinais.Select(s => s.Codigo).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 1) Regras (BM25)
        var regraMatches = ScoreRegras(queryTokens)
            .OrderByDescending(r => r.Score)
            .ToList();

        // 2) Manuais (palavras-chave + título + sinais + regras relacionadas)
        var manualMatches = new List<ManualMatch>();
        foreach (var im in _manuais)
        {
            double score = 0;
            var termos = new List<string>();

            if (searchable.Contains(im.TitleForm, StringComparison.Ordinal))
            {
                var titleWords = im.TitleForm.Count(c => c == ' ') - 1;
                score += 1.5 + 0.8 * (titleWords - 1);
                termos.Add(im.Manual.Titulo);
            }

            foreach (var (original, form) in im.Keywords)
            {
                var count = CountOccurrences(searchable, form);
                if (count == 0) continue;
                var words = form.Count(c => c == ' ') - 1;           // nº de palavras da expressão
                var weight = 1.0 + 0.8 * (words - 1);                // expressões compostas valem mais
                score += weight * (1 + Math.Log(count) * 0.35);
                termos.Add(original);
            }

            var regrasDoManual = regraMatches.Where(r => r.Manual == im.Manual).ToList();
            if (regrasDoManual.Count > 0)
                score += Math.Min(4, regrasDoManual.Max(r => r.Score) * 0.5);

            if (score > 0 && !string.IsNullOrEmpty(im.Manual.Publico) && publicos.Contains(im.Manual.Publico))
                score += 1.5;

            if (score <= 0) continue;

            manualMatches.Add(new ManualMatch(
                im.Manual,
                Math.Round(score, 2),
                0,
                termos.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                regrasDoManual.Count));
        }

        manualMatches = manualMatches.OrderByDescending(m => m.Score).ThenBy(m => m.Manual.Titulo).Take(maxManuais).ToList();

        // Confiança: força absoluta + distância para o segundo colocado.
        var top = manualMatches.FirstOrDefault()?.Score ?? 0;
        manualMatches = manualMatches.Select((m, i) =>
        {
            var absolute = m.Score / (m.Score + 3.0);
            var relative = top > 0 ? m.Score / top : 0;
            var conf = (int)Math.Round(100 * absolute * (0.55 + 0.45 * relative));
            return m with { Confianca = Math.Clamp(conf, 5, 98) };
        }).ToList();

        var regraTop = regraMatches.Count > 0 ? regraMatches[0].Score : 0;
        var regraMin = Math.Max(0.6, regraTop * 0.35);
        var regrasFinal = regraMatches
            .Where(r => r.Score >= regraMin)
            .Take(maxRegras)
            .Select(r => new RegraMatch(r.Regra, r.Manual, Math.Round(r.Score, 2), BuildSnippet(r.Regra.Texto, queryTokens)))
            .ToList();

        return new SearchResult(manualMatches, regrasFinal, sinais, queryTokens);
    }

    private IEnumerable<(Regra Regra, Manual? Manual, double Score)> ScoreRegras(List<string> queryTokens)
    {
        if (_regras.Count == 0 || queryTokens.Count == 0) yield break;
        var n = _regras.Count;
        foreach (var r in _regras)
        {
            double score = 0;
            foreach (var q in queryTokens)
            {
                if (!r.TermFreq.TryGetValue(q, out var tf)) continue;
                var df = _docFreq.GetValueOrDefault(q);
                var idf = Math.Log(1 + (n - df + 0.5) / (df + 0.5)) + 0.25;
                score += idf * (tf * (K1 + 1)) / (tf + K1 * (1 - B + B * r.Length / _avgLen));
            }
            if (score > 0) yield return (r.Regra, r.Manual, score);
        }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length - 1; // mantém o espaço final para a próxima borda
        }
        return count;
    }

    /// <summary>Escolhe a janela do texto com mais termos da pergunta e marca os termos encontrados.</summary>
    public static IReadOnlyList<SnippetPart> BuildSnippet(string texto, IReadOnlyCollection<string> queryTokens, int maxWords = 45)
    {
        var words = texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return Array.Empty<SnippetPart>();

        var query = queryTokens.ToHashSet(StringComparer.Ordinal);
        bool IsHit(string w)
        {
            var tokens = TextNormalizer.Tokenize(w);
            return tokens.Count > 0 && tokens.Any(query.Contains);
        }

        var hits = words.Select(IsHit).ToArray();
        int bestStart = 0, bestCount = -1;
        var window = Math.Min(maxWords, words.Length);
        for (int start = 0; start + window <= words.Length; start++)
        {
            var c = 0;
            for (int i = start; i < start + window; i++) if (hits[i]) c++;
            if (c > bestCount) { bestCount = c; bestStart = start; }
        }

        var parts = new List<SnippetPart>();
        if (bestStart > 0) parts.Add(new SnippetPart("… ", false));
        var buffer = new System.Text.StringBuilder();
        for (int i = bestStart; i < bestStart + window; i++)
        {
            if (hits[i])
            {
                if (buffer.Length > 0) { parts.Add(new SnippetPart(buffer.ToString(), false)); buffer.Clear(); }
                parts.Add(new SnippetPart(words[i], true));
                buffer.Append(' ');
            }
            else
            {
                buffer.Append(words[i]).Append(' ');
            }
        }
        if (bestStart + window < words.Length) buffer.Append('…');
        if (buffer.Length > 0) parts.Add(new SnippetPart(buffer.ToString(), false));
        return parts;
    }

    private sealed record IndexedManual(Manual Manual, string TitleForm, List<(string Original, string Form)> Keywords);
    private sealed record IndexedRegra(Regra Regra, Manual? Manual, Dictionary<string, int> TermFreq, int Length);
}
