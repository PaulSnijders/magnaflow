using MagnaFlow.MfRun.Config;

namespace MagnaFlow.MfRun.Runtime;

/// <summary>Which services a verb acts on. No name means every `run.services` entry and never the
/// stable instance; the name `stable` means the stable instance alone, when configured
/// (docs/specs/run/mf-run.md "Usage").</summary>
public static class RunTargets
{
    /// <summary>Null targets with an error for an unknown service name (exit 2).</summary>
    public static (IReadOnlyList<ServiceConfig>? Targets, string? Error) Select(RunConfig config, string? serviceName, string projectRoot)
    {
        if (serviceName is null)
            return (config.Services, null);

        if (serviceName == StableConfig.ServiceName && config.Stable is not null)
            return ([config.Stable.ToService(projectRoot)], null);

        var match = config.Services.FirstOrDefault(s => s.Name == serviceName);
        return match is null
            ? (null, $"unknown service '{serviceName}'")
            : ([match], null);
    }
}
