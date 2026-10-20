using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models;

public partial class Review
{
    public uint ReviewId { get; set; }

    public uint BookingId { get; set; }

    public uint? UserId { get; set; }

    public byte Rating { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Booking Booking { get; set; } = null!;

    public virtual User? User { get; set; }
}
