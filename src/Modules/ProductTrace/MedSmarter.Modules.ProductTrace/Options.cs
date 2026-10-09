namespace MedSmarter.Modules.ProductTrace;

public sealed class ManufacturerReportsOptions
{
    public const string Section = "ManufacturerReports";

    /// <summary>Disabled (default) or Mock. There is no real provider: a real connection needs an agreement with the manufacturer or authority.</summary>
    public string Provider { get; set; } = "Disabled";

    /// <summary>Sensitive (default): adverse events, severe or unknown severity and reports that name other medicines need a pharmacist/physician review. Always: every report. None: only the sensitive ones.</summary>
    public string ReviewPolicy { get; set; } = "Sensitive";

    public int MaxAttempts { get; set; } = 5;

    /// <summary>Minutes to wait before attempt 2, 3, ... (the last value repeats).</summary>
    public int[] BackoffMinutes { get; set; } = [1, 5, 30, 120, 720];

    public int LeaseSeconds { get; set; } = 120;

    /// <summary>Loads FICTIONAL demo products and reports. Refused outside Development/Testing.</summary>
    public bool SeedDemoData { get; set; }

    public MockOptions Mock { get; set; } = new();

    public sealed class MockOptions
    {
        /// <summary>Test hook: the mock fails this many times per report before it accepts (to exercise retry and backoff).</summary>
        public int FailFirstAttempts { get; set; }
    }
}

public static class ManufacturerReportsGuard
{
    public static void EnsureSafe(string environmentName, ManufacturerReportsOptions options)
    {
        var devLike = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);
        if (!devLike && string.Equals(options.Provider, "Mock", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"ManufacturerReports:Provider=Mock is only allowed in Development/Testing (environment is '{environmentName}'). Refusing to start.");
        }

        if (!devLike && options.SeedDemoData)
        {
            throw new InvalidOperationException($"ManufacturerReports:SeedDemoData is only allowed in Development/Testing (environment is '{environmentName}'). Refusing to start.");
        }

        if (!string.Equals(options.Provider, "Mock", StringComparison.OrdinalIgnoreCase) && !string.Equals(options.Provider, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ManufacturerReports:Provider must be Disabled or Mock (no real provider exists).");
        }

        if (!new[] { "Sensitive", "Always", "None" }.Contains(options.ReviewPolicy, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ManufacturerReports:ReviewPolicy must be Sensitive, Always or None.");
        }
    }
}
