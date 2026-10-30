using System.Globalization;
using System.Text;

namespace MyQuestion.Core;

/// <summary>
/// Normaliza texto em português para busca: minúsculas, sem acentos, sem pontuação,
/// sem stopwords e com um "stemmer" leve de plural (documentos → documento, declarações → declaracao).
/// </summary>
public static class TextNormalizer
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a","o","as","os","um","uma","uns","umas","de","do","da","dos","das","em","no","na","nos","nas",
        "por","pelo","pela","pelos","pelas","para","pra","pro","com","sem","sob","sobre","entre","ate",
        "e","ou","mas","se","que","qual","quais","quando","onde","como","porque","pois","entao","tambem",
        "ja","nao","sim","mais","menos","muito","muita","muitos","muitas","pouco","isso","isto","esse","essa",
        "este","esta","aquele","aquela","ele","ela","eles","elas","eu","voce","voces","nos","me","te","lhe",
        "meu","minha","seu","sua","seus","suas","nosso","nossa","ao","aos","foi","ser","sao","era","estar",
        "esta","estao","tem","ter","tenho","temos","ha","pode","podem","posso","podemos","deve","devo",
        "devemos","fazer","faz","feito","vai","vou","bom","boa","dia","tarde","noite","ola","oi","pessoal",
        "galera","alguem","duvida","duvidas","pergunta","favor","obrigado","obrigada","ok","certo","aqui",
        "ai","la","so","ainda","agora","hoje","ontem","caso","sobre","segue","seguinte","anexo","print",
        "consegue","conseguem","consigo","alguma","algum","algo","tudo","todo","toda","todos","todas",
        "mesmo","mesma","outro","outra","outros","outras","cada","the","de","pm","am","editado","responder",
        "curtir","reagir","mensagem","chat","teams","kkk","kkkk","rs","blz","vlw","tmj"
    };

    public static string RemoveDiacritics(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Minúsculas, sem acento e apenas letras/números separados por espaço.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var clean = RemoveDiacritics(text.ToLowerInvariant());
        var sb = new StringBuilder(clean.Length);
        var lastWasSpace = true;
        foreach (var c in clean)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                sb.Append(' ');
                lastWasSpace = true;
            }
        }
        return sb.ToString().Trim();
    }

    /// <summary>Reduz plurais comuns para comparar "certidões" com "certidão".</summary>
    public static string Stem(string token)
    {
        if (token.Length <= 3 || token.All(char.IsDigit)) return token;
        if (token.EndsWith("oes") || token.EndsWith("aes")) return token[..^3] + "ao";
        if (token.EndsWith("ais")) return token[..^3] + "al";
        if (token.EndsWith("eis") && token.Length > 4) return token[..^3] + "el";
        if (token.EndsWith("res") && token.Length > 4) return token[..^2];
        if (token.EndsWith("ns")) return token[..^2] + "m";
        if (token.EndsWith('s') && !token.EndsWith("ss")) return token[..^1];
        return token;
    }

    /// <summary>Tokens normalizados e reduzidos. Mantém stopwords se <paramref name="keepStopWords"/>.</summary>
    public static List<string> Tokenize(string? text, bool keepStopWords = false)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0) return new List<string>();
        var result = new List<string>();
        foreach (var raw in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!keepStopWords && (StopWords.Contains(raw) || raw.Length < 2)) continue;
            result.Add(Stem(raw));
        }
        return result;
    }

    /// <summary>Texto como sequência de tokens separada por espaço, com bordas, para casar expressões inteiras.</summary>
    public static string ToSearchable(string? text) => " " + string.Join(' ', Tokenize(text, keepStopWords: true)) + " ";

    /// <summary>Forma de busca de uma palavra-chave (mantém stopwords internas como em "comprovante de renda").</summary>
    public static string KeywordForm(string keyword) => " " + string.Join(' ', Tokenize(keyword, keepStopWords: true)) + " ";
}
