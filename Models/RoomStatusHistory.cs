using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models;

public partial class RoomStatusHistory
{
    public ulong HistoryId { get; set; }

    public uint RoomId { get; set; }

    public string? OldStatus { get; set; }

    public string NewStatus { get; set; } = null!;

    public uint? BookingId { get; set; }

    public uint? ChangedBy { get; set; }

    public DateTime ChangedAt { get; set; }

    public string? Note { get; set; }

    public virtual Booking? Booking { get; set; }

    public virtual User? ChangedByNavigation { get; set; }

    public virtual Room Room { get; set; } = null!;
}
