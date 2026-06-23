using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Seerlens.Collector;

// Optional chat client used to run evals from the dashboard or the CLI. Points at
// any OpenAI-compatible endpoint (Groq, Gemini, OpenAI, ...) set via
// SEERLENS_AI_BASE_URL / SEERLENS_AI_KEY / SEERLENS_AI_MODEL.
public sealed class AiProvider
{
    readonly Func<string, IChatClient?> _clientFor;

    public IChatClient? Client { get; }
    public string Model { get; }
    public string? Endpoint { get; }   // host only, never the key

    public IEmbeddingGenerator<string, Embedding<float>>? Embedder { get; }
    public string EmbedModel { get; }

    public AiProvider(IConfiguration config)
    {
        var baseUrl = config["SEERLENS_AI_BASE_URL"];
        var key = config["SEERLENS_AI_KEY"];
        Model = config["SEERLENS_AI_MODEL"] ?? "gpt-4o-mini";
        EmbedModel = config["SEERLENS_EMBED_MODEL"] ?? "text-embedding-3-small";

        if (!string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(key))
        {
            Endpoint = Uri.TryCreate(baseUrl, UriKind.Absolute, out var u) ? u.Host : baseUrl;
            var openai = new OpenAIClient(new ApiKeyCredential(key),
                new OpenAIClientOptions { Endpoint = new Uri(baseUrl) });
            _clientFor = m => openai.GetChatClient(m).AsIChatClient();
            Client = _clientFor(Model);
        }
        else
        {
            _clientFor = _ => null;
        }

        // Embeddings default to the chat provider, but can point elsewhere via
        // SEERLENS_EMBED_BASE_URL/_KEY for when the judge provider has no embeddings.
        var embedUrl = config["SEERLENS_EMBED_BASE_URL"] ?? baseUrl;
        var embedKey = config["SEERLENS_EMBED_KEY"] ?? key;
        if (!string.IsNullOrWhiteSpace(embedUrl) && !string.IsNullOrWhiteSpace(embedKey))
            Embedder = new OpenAIClient(new ApiKeyCredential(embedKey),
                new OpenAIClientOptions { Endpoint = new Uri(embedUrl) })
                .GetEmbeddingClient(EmbedModel).AsIEmbeddingGenerator();
    }

    // Test seam: a provider backed by a single fake client for every model.
    internal AiProvider(IChatClient client, string model)
    {
        Client = client;
        Model = model;
        EmbedModel = "";
        _clientFor = _ => client;
    }

    public bool Configured => Client is not null;

    // A client for a specific model on the same provider. Used by model comparison,
    // where one base URL/key serves several models.
    public IChatClient? ClientFor(string model) => _clientFor(model);
}
