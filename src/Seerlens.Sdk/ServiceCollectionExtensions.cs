using Microsoft.Extensions.DependencyInjection;

namespace Seerlens.Sdk;

public static class SeerlensServiceCollectionExtensions
{
    /// <summary>
    /// Point Seerlens at a collector once, at startup, instead of calling
    /// <see cref="SeerlensTrace.Configure"/> by hand. Wrap the client you register
    /// with Seerlens too, e.g. <c>services.AddChatClient(...).Use(c =&gt; c.UseSeerlens())</c>,
    /// or call <c>.UseSeerlens()</c> on the IChatClient you build.
    /// </summary>
    public static IServiceCollection AddSeerlens(this IServiceCollection services, string collectorUrl)
    {
        SeerlensTrace.Configure(collectorUrl);
        return services;
    }
}
