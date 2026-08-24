namespace CKYC.Core.Domain;

/// <summary>
/// Status master: the reference lookup for the single "current stage" flag on the
/// <c>master_record</c> table (<c>master_record.Status</c>).
///
/// <c>Status</c> is persisted as an <b>INTEGER</b> value (0–10, the
/// <see cref="MasterRecordStatus"/> enum) and this table maps that value to a short
/// 2–3 character <see cref="Code"/> (e.g. <c>PND</c>, <c>SAV</c>, <c>FVP</c>), the enum
/// <see cref="Name"/>, and a human <see cref="Description"/> — so reports can show a
/// compact code and a readable description without changing the numeric storage.
///
/// Seeded idempotently (only rows whose <see cref="StatusValue"/> is missing), mirroring
/// the <see cref="ActivityType"/> master. <see cref="IsTerminal"/> matches
/// <see cref="MasterRecordStatusExtensions.IsTerminal"/>.
/// </summary>
public sealed class StatusMaster
{
    public long Id { get; set; }

    /// <summary>Numeric value stored in <c>master_record.Status</c> (0–10).</summary>
    public int StatusValue { get; set; }

    /// <summary>Short 2–3 character flag (e.g. <c>PND</c>, <c>SAV</c>, <c>FVP</c>).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Enum member name (e.g. <c>Pending</c>, <c>Saved</c>, <c>FvuPassed</c>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable description of what this status means.</summary>
    public string? Description { get; set; }

    /// <summary>True when this is a terminal state (record is finished — no further transitions expected).</summary>
    public bool IsTerminal { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
}
