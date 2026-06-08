using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Seerlens.Sdk;

namespace Seerlens.SemanticKernel;

/// <summary>
/// Traces Semantic Kernel function invocations into Seerlens. Register it (see
/// <see cref="SeerlensKernelBuilderExtensions.AddSeerlens"/>) and every function call,
/// prompt or native, becomes a span, grouped per top-level invocation, with no
/// SeerlensTrace calls in your own code. Token counts and model are read from the
/// result when the connector reports them.
/// </summary>
public sealed class SeerlensFilter : IFunctionInvocationFilter
{
    static readonly AsyncLocal<int> Depth = new();

    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        // the first function in a call opens the trace; nested calls land under it
        var outermost = Depth.Value == 0;
        var trace = outermost ? SeerlensTrace.Begin(Name(context)) : null;
        Depth.Value++;

        var started = Stopwatch.GetTimestamp();
        string? error = null;
        try
        {
            await next(context);
        }
        catch (Exception e)
        {
            error = e.Message;
            throw;
        }
        finally
        {
            Depth.Value--;
            var ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var meta = context.Result?.Metadata;

            // a function whose result carries token usage was a model call
            var usage = meta is not null && meta.TryGetValue("Usage", out var u) ? u : null;
            var isLlm = usage is not null;

            SeerlensTrace.AddSpan(
                Name(context),
                isLlm ? "llm" : "tool",
                ms,
                isLlm ? Model(context, meta) : null,
                ReadLong(usage, "InputTokenCount", "PromptTokens", "InputTokens"),
                ReadLong(usage, "OutputTokenCount", "CompletionTokens", "OutputTokens"),
                Args(context.Arguments), context.Result?.ToString(), error);
            trace?.Dispose();
        }
    }

    static string Name(FunctionInvocationContext c) =>
        string.IsNullOrEmpty(c.Function.PluginName) ? c.Function.Name : $"{c.Function.PluginName}.{c.Function.Name}";

    static string? Args(KernelArguments? args) =>
        args is null || args.Count == 0 ? null : string.Join(", ", args.Select(a => $"{a.Key}={a.Value}"));

    // The connector doesn't put the model in the result metadata, but the kernel's
    // chat service knows it, so cost can still be computed.
    static string? Model(FunctionInvocationContext context, IReadOnlyDictionary<string, object?>? meta)
    {
        if (meta is not null && meta.TryGetValue("ModelId", out var m) && m is string s && s.Length > 0) return s;
        // the metadata doesn't carry the model, but the kernel's chat service does
        var svc = context.Kernel.Services.GetService<IChatCompletionService>();
        return svc is not null && svc.Attributes.TryGetValue("ModelId", out var mid) ? mid?.ToString() : null;
    }

    static long? ReadLong(object? obj, params string[] names)
    {
        if (obj is null) return null;
        foreach (var n in names)
            if (obj.GetType().GetProperty(n)?.GetValue(obj) is { } v && long.TryParse(v.ToString(), out var num))
                return num;
        return null;
    }
}
