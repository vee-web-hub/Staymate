using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models;

public partial class HousekeepingTask
{
    public uint TaskId { get; set; }

    public uint RoomId { get; set; }

    public uint? BookingId { get; set; }

    public uint? AssignedTo { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? Note { get; set; }

    public virtual User? AssignedToNavigation { get; set; }

    public virtual Booking? Booking { get; set; }

    public virtual Room Room { get; set; } = null!;
}
