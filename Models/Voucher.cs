using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models;

public partial class Voucher
{
    public uint VoucherId { get; set; }

    public string VoucherCode { get; set; } = null!;

    public uint BookingId { get; set; }

    public uint? PromotionId { get; set; }

    public string VoucherType { get; set; } = null!;

    public decimal? ValueAmount { get; set; }

    public string? PartnerName { get; set; }

    public string Status { get; set; } = null!;

    public DateTime IssuedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime? UsedAt { get; set; }

    public virtual Booking Booking { get; set; } = null!;

    public virtual Promotion? Promotion { get; set; }
}
