using Seerlens.SemanticKernel;

namespace Seerlens.Sdk.Tests;

public class SemanticKernelUsageTests
{
    // shaped like OpenAI's ChatTokenUsage
    sealed record InDetails(int CachedTokenCount);
    sealed record OutDetails(int ReasoningTokenCount);
    sealed record OpenAiUsage(int InputTokenCount, int OutputTokenCount, InDetails InputTokenDetails, OutDetails OutputTokenDetails);

    // shaped like MEAI's UsageDetails
    sealed record MeaiUsage(long? InputTokenCount, long? CachedInputTokenCount, long? ReasoningTokenCount);

    [Fact]
    public void Reads_the_nested_openai_shape()
    {
        var (cache, reasoning) = SeerlensFilter.CacheUsage(new OpenAiUsage(100, 20, new InDetails(64), new OutDetails(9)));
        Assert.Equal(64, cache);
        Assert.Equal(9, reasoning);
    }

    [Fact]
    public void Reads_the_flat_meai_shape()
    {
        var (cache, reasoning) = SeerlensFilter.CacheUsage(new MeaiUsage(100, 50, 7));
        Assert.Equal(50, cache);
        Assert.Equal(7, reasoning);
    }

    [Fact]
    public void Missing_or_null_details_give_null()
    {
        Assert.Equal((null, null), SeerlensFilter.CacheUsage(null));
        Assert.Equal((null, null), SeerlensFilter.CacheUsage(new MeaiUsage(100, null, null)));
        Assert.Equal((null, null), SeerlensFilter.CacheUsage(new { InputTokenCount = 5 }));
    }
}
