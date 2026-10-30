using System.Text.RegularExpressions;

namespace MyQuestion.Core;

/// <summary>Detecta pistas no texto do print: CPF, CNPJ, PDF anexado, menção a agro etc.</summary>
public static partial class SignalDetector
{
    [GeneratedRegex(@"\b\d{3}\.\d{3}\.\d{3}[-\s]?\d{2}\b")] private static partial Regex CpfRegex();
    [GeneratedRegex(@"\b\d{2}\.\d{3}\.\d{3}\s?/\s?\d{4}[-\s]?\d{2}\b")] private static partial Regex CnpjRegex();
    [GeneratedRegex(@"\b[\w\-]+\.pdf\b", RegexOptions.IgnoreCase)] private static partial Regex PdfRegex();

    public static IReadOnlyList<Signal> Detect(string? text)
    {
        var list = new List<Signal>();
        if (string.IsNullOrWhiteSpace(text)) return list;

        var searchable = TextNormalizer.ToSearchable(text);
        bool Has(string term) => searchable.Contains(TextNormalizer.KeywordForm(term), StringComparison.Ordinal);

        if (CnpjRegex().IsMatch(text) || Has("cnpj") || Has("ltda") || Has("eireli") || Has("mei") || Has("razao social"))
            list.Add(new Signal("PJ", "Pessoa jurídica"));
        if (CpfRegex().IsMatch(text) || Has("cpf") || Has("pessoa fisica"))
            list.Add(new Signal("PF", "Pessoa física"));
        if (Has("produtor rural") || Has("rural") || Has("safra") || Has("agro") || Has("pronaf") || Has("ccir") || Has("itr"))
            list.Add(new Signal("Agro", "Agronegócio"));

        var pdfs = PdfRegex().Matches(text).Select(m => m.Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
        foreach (var pdf in pdfs)
            list.Add(new Signal("PDF", pdf));

        return list;
    }
}
