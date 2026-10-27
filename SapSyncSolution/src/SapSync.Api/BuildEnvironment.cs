using System.Reflection;

namespace SapSync.Api;

/// <summary>
/// Environment of builds produced with <c>-p:SapEnvironment=Test|Prod</c>. Such a build only ships the settings of its own
/// environment, so the baked environment is authoritative: a Test build can never run with Production settings and vice versa.
/// Developer builds (no SapEnvironment) resolve the environment as usual (launchSettings / ASPNETCORE_ENVIRONMENT).
/// </summary>
internal static class BuildEnvironment
{
    public const string MetadataKey = "SapEnvironment";

    /// <summary>Environment baked into the assembly at build time, or <c>null</c> for a developer build.</summary>
    public static string? BakedEnvironment { get; } = typeof(BuildEnvironment).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == MetadataKey)?.Value;

    /// <summary>Environment requested at runtime via ASPNETCORE_ENVIRONMENT / DOTNET_ENVIRONMENT, if any.</summary>
    public static string? RuntimeEnvironment { get; } =
        NullIfEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"))
        ?? NullIfEmpty(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));

    /// <summary>True when the runtime asked for a different environment than the one this build was made for.</summary>
    public static bool RuntimeOverrideIgnored =>
        BakedEnvironment is not null
        && RuntimeEnvironment is not null
        && !string.Equals(BakedEnvironment, RuntimeEnvironment, StringComparison.OrdinalIgnoreCase);

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
