using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Seerlens.Collector;

// Optional chat client used to run evals from the dashboard or the CLI. Points at
// any OpenAI-compatible endpoint (Groq, Gemini, OpenAI, ...) set via
// SEERLENS_AI_BASE_URL / SEERLENS_AI_KEY / SEERLENS_AI_MODEL.
public sealed class AiProvider
{
    readonly OpenAIClient? _openai;

    public IChatClient? Client { get; }
    public string Model { get; }
    public string? Endpoint { get; }   // host only, never the key

    public AiProvider(IConfiguration config)
    {
        var baseUrl = config["SEERLENS_AI_BASE_URL"];
        var key = config["SEERLENS_AI_KEY"];
        Model = config["SEERLENS_AI_MODEL"] ?? "gpt-4o-mini";

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(key))
            return;

        Endpoint = Uri.TryCreate(baseUrl, UriKind.Absolute, out var u) ? u.Host : baseUrl;

        _openai = new OpenAIClient(new ApiKeyCredential(key),
            new OpenAIClientOptions { Endpoint = new Uri(baseUrl) });
        Client = _openai.GetChatClient(Model).AsIChatClient();
    }

    public bool Configured => Client is not null;

    // A client for a specific model on the same provider. Used by model comparison,
    // where one base URL/key serves several models.
    public IChatClient? ClientFor(string model) =>
        _openai?.GetChatClient(model).AsIChatClient();
}
