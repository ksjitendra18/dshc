using CKYC.Processor.Commands;

namespace CKYC.Processor;

public sealed class CommandRegistry
{
    private readonly Dictionary<string, ICommand> _commands;

    public CommandRegistry(IEnumerable<ICommand> commands)
        => _commands = commands.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ICommand> All => _commands.Values;

    public ICommand? Resolve(string name)
        => _commands.TryGetValue(name, out var cmd) ? cmd : null;

    public static CommandRegistry Build() => new(
        new ICommand[]
        {
            new FetchCommand(),
            new InsertCommand(),
            new CrmServeCommand(),
            new StoreCommand(),
            new RetryCommand(),
            new ReattemptCommand(),
            new BuildZipCommand(),
            new FvuCommand(),
            new ResponseCommand(),
            new ReconcileCommand(),
            new StatusCommand(),
        });

    public string Help()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Centralized CKYC Processor");
        sb.AppendLine();
        sb.AppendLine("Usage: CKYCProcessor.exe <command> [options]");
        sb.AppendLine();
        sb.AppendLine("Commands:");
        foreach (var cmd in All.OrderBy(c => c.Name))
            sb.AppendLine($"  {cmd.Usage}");
        sb.AppendLine();
        sb.AppendLine("Examples:");
        sb.AppendLine("  CKYCProcessor.exe fetch cust");
        sb.AppendLine("  CKYCProcessor.exe crm serve");
        sb.AppendLine("  CKYCProcessor.exe store");
        sb.AppendLine("  CKYCProcessor.exe retry");
        sb.AppendLine("  CKYCProcessor.exe reattempt --customer CUST202608240001 --reason \"PAN corrected\"");
        sb.AppendLine("  CKYCProcessor.exe build-zip");
        sb.AppendLine("  CKYCProcessor.exe fvu");
        sb.AppendLine("  CKYCProcessor.exe response read");
        sb.AppendLine("  CKYCProcessor.exe reconcile --kind cersai --stakeholder \"Operations\"");
        sb.AppendLine("  CKYCProcessor.exe status");
        return sb.ToString();
    }
}
