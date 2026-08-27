using System.Text;
using CKYC.Processor;
using NLog;
using AppContext = CKYC.Processor.AppContext;

// ---------------------------------------------------------------------------
// CKYCProcessor.exe — centralized CKYC processing CLI.
//
//   fetch cust    : step 1  customer ids -> master table (CBS fetch; retryable)
//   crm serve     : step 2  dummy CRM API
//   store         : step 3  CRM -> record tables (with simulated error saving)
//   retry         :        retry failed records (per retryable activity, exponential backoff)
//   reattempt     :        re-push a single rejected record after a backend DB fix
//   build-zip     : step 4  saved records -> pipe-delimited .UPL + zip
//   fvu           : step 5  batch -> FVU -> processed zip + hash (marks records Uploaded)
//   response read : step 6  CERSAI reply (.UPL.RESm) -> response table + master summary
//   reconcile     :        manual-intervention report (retry-exhausted + CERSAI-failed)
//   status        :        pipeline snapshot (current stage per record)
//   search-load/process/fvu/response : individual search JSON -> SRC -> validated SRC.zip -> response tables
//   update-load/process/fvu/response : bulk-update JSON -> .UPD (I/L formats) -> validated UPD.zip -> response
// ---------------------------------------------------------------------------

Console.OutputEncoding = Encoding.UTF8;

// NLog is configured from NLog.config (copied to the output dir). autoReload means edits to
// the config are picked up without a restart. Register a shutdown hook so buffered logs flush.
LogManager.Setup().LoadConfigurationFromFile("NLog.config", optional: false);
var logger = LogManager.GetCurrentClassLogger();
AppDomain.CurrentDomain.ProcessExit += (_, _) => LogManager.Shutdown();

var settingsPath = ArgValue(args, "--settings");
var settings = SettingsLoader.Load(settingsPath);
var app = new AppContext(settings);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var registry = CommandRegistry.Build();

if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
{
    Console.Write(registry.Help());
    return 0;
}

var commandName = args[0];
var command = registry.Resolve(commandName);
if (command is null)
{
    logger.Error("Unknown command '{Command}'", commandName);
    Console.Write(registry.Help());
    return 1;
}

try
{
    await app.InitializeAsync(cts.Token);
    return await command.ExecuteAsync(app, args.Skip(1).ToArray(), cts.Token);
}
catch (OperationCanceledException)
{
    logger.Warn("Cancelled.");
    return 130;
}
catch (Exception ex)
{
    logger.Error(ex, "Unhandled exception during command '{Command}'", commandName);
    if (settings.Simulation.SaveErrorsEnabled) logger.Error(ex, "Full exception details");
    return 1;
}

static string? ArgValue(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
