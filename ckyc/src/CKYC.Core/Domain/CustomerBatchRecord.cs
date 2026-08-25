namespace CKYC.Core.Domain;

/// <summary>One historical membership of an organization customer in a CKYC upload batch.</summary>
public sealed class CustomerBatchRecord
{
    public long Id { get; set; }
    public long MasterRecordId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string BatchFile { get; set; } = string.Empty;
    public int? Record20LineNumber { get; set; }
    public DateTime BatchedAt { get; set; }
}
