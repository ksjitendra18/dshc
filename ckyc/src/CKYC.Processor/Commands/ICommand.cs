namespace CKYC.Processor.Commands;

/// <summary>A CLI verb (e.g. "fetch cust", "store", "fvu").</summary>
public interface ICommand
{
    string Name { get; }
    /// <summary>Human readable usage line.</summary>
    string Usage { get; }
    Task<int> ExecuteAsync(AppContext ctx, string[] args, CancellationToken ct = default);
}
