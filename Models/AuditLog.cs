using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models;

public partial class AuditLog
{
    public ulong LogId { get; set; }

    public uint? UserId { get; set; }

    public string Action { get; set; } = null!;

    public string TableName { get; set; } = null!;

    public string? RecordId { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User? User { get; set; }
}
