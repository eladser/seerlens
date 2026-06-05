using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Seerlens.Collector;

// Optional chat client used to run evals from the dashboard. Points at any
// OpenAI-compatible endpoint (Groq, Gemini, OpenAI, ...) set via
// SEERLENS_AI_BASE_URL / SEERLENS_AI_KEY / SEERLENS_AI_MODEL.
public sealed class AiProvider
{
    public IChatClient? Client { get; }
    public string Model { get; }

    public AiProvider(IConfiguration config)
    {
        var baseUrl = config["SEERLENS_AI_BASE_URL"];
        var key = config["SEERLENS_AI_KEY"];
        Model = config["SEERLENS_AI_MODEL"] ?? "gpt-4o-mini";

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(key))
            return;

        var openai = new OpenAIClient(new ApiKeyCredential(key),
            new OpenAIClientOptions { Endpoint = new Uri(baseUrl) });
        Client = openai.GetChatClient(Model).AsIChatClient();
    }

    public bool Configured => Client is not null;
}
