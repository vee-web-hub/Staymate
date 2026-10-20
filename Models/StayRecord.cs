using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models;

public partial class StayRecord
{
    public uint StayId { get; set; }

    public uint BookingId { get; set; }

    public uint RoomId { get; set; }

    public DateTime CheckedInAt { get; set; }

    public DateTime? CheckedOutAt { get; set; }

    public uint? CheckedInBy { get; set; }

    public uint? CheckedOutBy { get; set; }

    public byte? ActualGuests { get; set; }

    public virtual Booking Booking { get; set; } = null!;

    public virtual User? CheckedInByNavigation { get; set; }

    public virtual User? CheckedOutByNavigation { get; set; }

    public virtual Room Room { get; set; } = null!;
}
