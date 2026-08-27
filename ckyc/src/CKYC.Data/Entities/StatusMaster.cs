using System;
using System.Collections.Generic;

namespace CKYC.Data.Entities;

public partial class StatusMaster
{
    public long Id { get; set; }

    public int? StatusValue { get; set; }

    public string? Code { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public int? IsTerminal { get; set; }

    public int? IsActive { get; set; }

    public DateTime? CreatedAt { get; set; }
}
