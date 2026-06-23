using Microsoft.Extensions.AI;
using Seerlens.Evals;

namespace Seerlens.Collector;

// Maps a scorer name to a scorer. "agent" is handled separately (it needs the tool
// loop, not a plain IScorer), so it isn't here.
static class Scoring
{
    public static IScorer For(string? name, IChatClient judge,
        IEmbeddingGenerator<string, Embedding<float>>? embedder = null) => name switch
    {
        "llm-judge" => new LlmJudgeScorer(judge),
        "rubric" => new RubricScorer(judge),
        "consensus" => new ConsensusScorer(judge),
        "regex" => new RegexScorer(),
        "json-schema" => new JsonSchemaScorer(),
        "embedding" => embedder is not null ? new EmbeddingScorer(embedder)
            : throw new InvalidOperationException(
                "the embedding scorer needs an embeddings provider; set SEERLENS_EMBED_* or SEERLENS_AI_*"),
        _ => new KeywordScorer(),   // "keyword" and the unset default
    };

    public static bool IsKnown(string? name) =>
        name is "keyword" or "llm-judge" or "rubric" or "consensus"
            or "regex" or "json-schema" or "embedding" or "agent";
}
