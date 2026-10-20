using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models;

public partial class BookingGuest
{
    public uint GuestId { get; set; }

    public uint BookingId { get; set; }

    public string FullName { get; set; } = null!;

    public string? IdCardNo { get; set; }

    public string? Phone { get; set; }

    public bool IsPrimary { get; set; }

    public bool IsExtraBed { get; set; }

    public virtual Booking Booking { get; set; } = null!;
}
