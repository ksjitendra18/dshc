namespace CKYC.Core.Configuration;

/// <summary>
/// Root configuration object for the CKYC processor. Bound from appsettings.json
/// (or the --settings override) with System.Text.Json.
/// </summary>
public sealed class AppSettings
{
    public DatabaseSettings Database { get; set; } = new();
    public SourceSettings Source { get; set; } = new();
    public CrmSettings Crm { get; set; } = new();
    public BatchSettings Batch { get; set; } = new();
    public FvuSettings Fvu { get; set; } = new();
    public SimulationSettings Simulation { get; set; } = new();
    public RetrySettings Retry { get; set; } = new();
}

/// <summary>Persistence settings. The "provider" switch lets a SQL Server deployment be used in production.</summary>
public sealed class DatabaseSettings
{
    // Provider: "sqlite" (default, zero-dependency) or "sqlserver" (production).
    public string Provider { get; set; } = "sqlite";
    public string ConnectionString { get; set; } = "Data Source=ckyc.db;Cache=Shared";
    public bool CreateSchemaOnStartup { get; set; } = true;
    public int CommandTimeoutSeconds { get; set; } = 30;
}

/// <summary>Where the daily source customer ids come from (step 1).</summary>
public sealed class SourceSettings
{
    // "generate" seeds a deterministic daily set of customer ids for the demo.
    // "file" reads a plain text file with one customer-id per line.
    public string Mode { get; set; } = "generate";
    public string? FilePath { get; set; }
    public int GenerateCount { get; set; } = 12;
    public int GenerateSeed { get; set; } = 20260824;
}

/// <summary>Dummy CRM API wiring. Replace with the production endpoint later.</summary>
public sealed class CrmSettings
{
    // InProcess: an embedded Kestrel server is launched by `crm serve` and used directly.
    // Http: a remote client pointed at an external API (production).
    public string Mode { get; set; } = "InProcess";
    public string BaseUrl { get; set; } = "http://127.0.0.1:5291";
    public string CustomersEndpoint { get; set; } = "/api/customers/{id}";
    public string ListEndpoint { get; set; } = "/api/customers";
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>CKYC batch / file naming settings (used when building the .UPL and zip).</summary>
public sealed class BatchSettings
{
    public string UserId { get; set; } = "IAU010441";
    public string FiCode { get; set; } = "IN0238";
    public string RegionCode { get; set; } = "01";
    public string ClientType { get; set; } = "I";
    public string VersionNumber { get; set; } = "V1.0";
    public int SequenceStart { get; set; } = 1;
    public string OutputRoot { get; set; } = "output";
    public string? AppendedDocSuffix { get; set; }
}

/// <summary>FVU (File Validation Utility) integration + local simulation settings.</summary>
public sealed class FvuSettings
{
    public string ExePath { get; set; } = @"D:\centralprocessing\vendor\FVU_RUN_UTILITY.exe";
    public string WorkspaceRoot { get; set; } = @"D:\centralprocessing\testfvu";
    public string ApiBaseUrl { get; set; } = "http://localhost:9091";
    public string ApiEndpoint { get; set; } = "/api/search-validate/file-with-support";
    public string TriggerSource { get; set; } = "BATCH";
    public int RequestTimeoutSeconds { get; set; } = 600;
    public int StartupTimeoutSeconds { get; set; } = 20;
    public int MaxRetries { get; set; } = 3;
    // true -> run the real FVU_RUN_UTILITY.exe; false -> deterministic local simulation.
    public bool UseRealFvu { get; set; } = true;
}

/// <summary>
/// Deterministic knobs used to demonstrate the "error saving" scenario (step 3) and
/// the FVU failure path, without touching real data.
/// </summary>
public sealed class SimulationSettings
{
    public bool SaveErrorsEnabled { get; set; } = true;

    // Every Nth record save is deliberately failed to exercise the retry path.
    public int SaveErrorEvery { get; set; } = 4;

    // A specific source customer id that always fails to save (useful for a targeted test).
    public string? SaveErrorForCustomerId { get; set; }

    // When FVU simulation mode is used with a generated batch, the Nth batch fails validation.
    public int FvuFailEvery { get; set; }

    // ---- CBS fetch retry simulation ----
    // The fetch (step 1) is the retryable example. When enabled, every Nth customer-id's CBS
    // call is made to fail so the retry/backoff path can be exercised. Off by default so the
    // normal end-to-end pipeline is unaffected.
    public bool CbsFetchErrorsEnabled { get; set; }

    // Every Nth customer id fails the CBS fetch (0 disables).
    public int CbsFetchFailEvery { get; set; }

    // A specific customer id that always fails the CBS fetch.
    public string? CbsFetchFailForCustomerId { get; set; }
}

/// <summary>
/// Default retry policy used when seeding the <c>activity_type</c> master. The requested
/// policy is exponential backoff of <see cref="BackoffBaseHours"/> hours (double per failure)
/// with at most <see cref="MaxAttempts"/> tries. Individual activities can override these
/// values in the activity-type master.
/// </summary>
public sealed class RetrySettings
{
    public int MaxAttempts { get; set; } = 3;
    public int BackoffBaseHours { get; set; } = 24;
    public double BackoffMultiplier { get; set; } = 2.0;
}
