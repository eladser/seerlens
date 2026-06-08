using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Seerlens.Sdk;

namespace Seerlens.SemanticKernel;

public static class SeerlensKernelBuilderExtensions
{
    /// <summary>
    /// Point Seerlens at a collector and register the tracing filter on the kernel.
    /// After this, every function the kernel runs shows up in the dashboard.
    /// </summary>
    public static IKernelBuilder AddSeerlens(this IKernelBuilder builder, string collectorUrl)
    {
        SeerlensTrace.Configure(collectorUrl);
        builder.Services.AddSingleton<IFunctionInvocationFilter, SeerlensFilter>();
        return builder;
    }
}
