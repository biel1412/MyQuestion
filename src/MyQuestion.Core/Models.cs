using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyQuestion.Core;

/// <summary>Base de conhecimento carregada de wwwroot/data/base.json.</summary>
public sealed class KnowledgeBase
{
    [JsonPropertyName("atualizadoEm")] public string? AtualizadoEm { get; set; }
    [JsonPropertyName("fonte")] public string? Fonte { get; set; }
    [JsonPropertyName("manuais")] public List<Manual> Manuais { get; set; } = new();
    [JsonPropertyName("regras")] public List<Regra> Regras { get; set; } = new();

    /// <summary>Usa serializador gerado em compilação (seguro para o trimming do Blazor WebAssembly).</summary>
    public static KnowledgeBase FromJson(string json) =>
        JsonSerializer.Deserialize(json, KnowledgeBaseJsonContext.Default.KnowledgeBase) ?? new KnowledgeBase();

    public Manual? FindManual(string id) =>
        Manuais.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Um manual da Central de Regramento (caderno OneNote no SharePoint).</summary>
public sealed class Manual
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("titulo")] public string Titulo { get; set; } = "";
    [JsonPropertyName("categoria")] public string Categoria { get; set; } = "";
    [JsonPropertyName("descricao")] public string Descricao { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";

    /// <summary>"PF", "PJ", "Agro" ou vazio. Usado para reforçar o resultado quando o print mostra CPF/CNPJ.</summary>
    [JsonPropertyName("publico")] public string? Publico { get; set; }

    /// <summary>Termos e expressões que indicam este manual. Aceita acentos e plural.</summary>
    [JsonPropertyName("palavrasChave")] public List<string> PalavrasChave { get; set; } = new();
}

/// <summary>Trecho de regra copiado do OneNote para respostas diretas.</summary>
public sealed class Regra
{
    [JsonPropertyName("manual")] public string Manual { get; set; } = "";
    [JsonPropertyName("titulo")] public string Titulo { get; set; } = "";
    [JsonPropertyName("texto")] public string Texto { get; set; } = "";
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new();
}

public sealed record Signal(string Codigo, string Rotulo);

public sealed record ManualMatch(
    Manual Manual,
    double Score,
    int Confianca,
    IReadOnlyList<string> TermosEncontrados,
    int RegrasRelacionadas);

public sealed record SnippetPart(string Texto, bool Destaque);

public sealed record RegraMatch(
    Regra Regra,
    Manual? Manual,
    double Score,
    IReadOnlyList<SnippetPart> Trecho);

public sealed record SearchResult(
    IReadOnlyList<ManualMatch> Manuais,
    IReadOnlyList<RegraMatch> Regras,
    IReadOnlyList<Signal> Sinais,
    IReadOnlyList<string> TermosDaPergunta)
{
    public static readonly SearchResult Empty = new(
        Array.Empty<ManualMatch>(), Array.Empty<RegraMatch>(), Array.Empty<Signal>(), Array.Empty<string>());

    public bool TemResultado => Manuais.Count > 0 || Regras.Count > 0;
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(KnowledgeBase))]
internal partial class KnowledgeBaseJsonContext : JsonSerializerContext
{
}
