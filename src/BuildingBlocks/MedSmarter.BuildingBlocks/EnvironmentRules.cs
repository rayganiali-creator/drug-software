namespace MedSmarter.BuildingBlocks;

/// <summary>
/// The one place that decides which environments may run fictional-demo features (mock sign-in, in-memory storage, demo seeds,
/// the Mock AI provider). "Demo" is a dedicated, deliberately named environment for a public walkthrough: it is NOT Development
/// (no developer exception page, no Development settings) and it is guarded further by the host (see DemoGuard).
/// Production and every other environment name stay refused.
/// </summary>
public static class EnvironmentRules
{
    public const string Demo = "Demo";

    public static bool IsDevelopmentOrTesting(string environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);

    /// <summary>Exact, case-sensitive match: "demo" would not load appsettings.Demo.json on a case-sensitive file system, so it is not the Demo environment.</summary>
    public static bool IsDemo(string environmentName) => string.Equals(environmentName, Demo, StringComparison.Ordinal);

    /// <summary>A near miss such as "demo" or "DEMO": the host refuses to start rather than run half-configured.</summary>
    public static bool IsMisspelledDemo(string environmentName) => !IsDemo(environmentName) && string.Equals(environmentName, Demo, StringComparison.OrdinalIgnoreCase);

    /// <summary>True where fictional-demo features may be switched on: Development, Testing and the dedicated Demo environment.</summary>
    public static bool AllowsDemoFeatures(string environmentName) => IsDevelopmentOrTesting(environmentName) || IsDemo(environmentName);
}
