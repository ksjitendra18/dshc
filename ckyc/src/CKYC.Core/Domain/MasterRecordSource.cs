namespace CKYC.Core.Domain;

/// <summary>
/// Intake channel a <see cref="MasterRecord"/> originated from. Persisted as a
/// lowercase string in <c>master_record.Source</c>. Values are append-only — never
/// rename or reuse an existing value.
/// </summary>
public enum MasterRecordSource
{
    /// <summary>Created through the app intake channel.</summary>
    App,

    /// <summary>Created through the beckyc intake channel.</summary>
    Beckyc,
}

/// <summary>
/// The lowercase text persisted in <c>master_record.Source</c> (append-only — never
/// rename or reuse), plus the default applied when a record is created without an
/// explicit intake channel.
/// </summary>
public static class MasterRecordSourceValue
{
    public const string App = "app";
    public const string Beckyc = "beckyc";

    /// <summary>Source applied to records created without an explicit intake channel.</summary>
    public const string Default = Beckyc;

    public static string For(MasterRecordSource source) => source switch
    {
        MasterRecordSource.App => App,
        MasterRecordSource.Beckyc => Beckyc,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown master-record source."),
    };

    /// <summary>Parses a stored value, falling back to <see cref="Default"/> when blank/unknown.</summary>
    public static MasterRecordSource ParseOrDefault(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            App => MasterRecordSource.App,
            Beckyc => MasterRecordSource.Beckyc,
            _ => MasterRecordSource.Beckyc,
        };
}
