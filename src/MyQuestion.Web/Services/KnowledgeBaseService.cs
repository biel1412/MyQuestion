using MyQuestion.Core;

namespace MyQuestion.Web.Services;

/// <summary>Carrega data/base.json uma vez e mantém o motor de busca em memória.</summary>
public sealed class KnowledgeBaseService
{
    private readonly HttpClient _http;
    private Task<RegramentoSearchEngine>? _loading;

    public KnowledgeBaseService(HttpClient http) => _http = http;

    public Task<RegramentoSearchEngine> GetEngineAsync() => _loading ??= LoadAsync();

    private async Task<RegramentoSearchEngine> LoadAsync()
    {
        try
        {
            // Parâmetro evita versão antiga em cache depois de editar a base no GitHub.
            var json = await _http.GetStringAsync($"data/base.json?v={DateTime.UtcNow:yyyyMMddHHmm}");
            return new RegramentoSearchEngine(KnowledgeBase.FromJson(json));
        }
        catch
        {
            _loading = null; // permite tentar de novo
            throw;
        }
    }
}
