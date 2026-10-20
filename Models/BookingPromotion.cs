using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models;

public partial class BookingPromotion
{
    public uint BookingPromotionId { get; set; }

    public uint BookingId { get; set; }

    public uint PromotionId { get; set; }

    public string StackRole { get; set; } = null!;

    public decimal AppliedPercent { get; set; }

    public decimal AppliedAmount { get; set; }

    public DateTime AppliedAt { get; set; }

    public virtual Booking Booking { get; set; } = null!;

    public virtual Promotion Promotion { get; set; } = null!;
}
