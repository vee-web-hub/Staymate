using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models;

public partial class Promotion
{
    public uint PromotionId { get; set; }

    public string PromoCode { get; set; } = null!;

    public string PromoName { get; set; } = null!;

    public string? Description { get; set; }

    public string DiscountType { get; set; } = null!;

    public decimal? DiscountPercent { get; set; }

    public decimal? DiscountPercentMax { get; set; }

    public byte? MinGuests { get; set; }

    public ushort? MinNights { get; set; }

    public byte? MaxDiscountNights { get; set; }

    public byte? ApplicableRoomTypeId { get; set; }

    public bool RequiresHoliday { get; set; }

    public decimal? VoucherMinValue { get; set; }

    public decimal? VoucherMaxValue { get; set; }

    public bool? IsActive { get; set; }

    public DateOnly? ValidFrom { get; set; }

    public DateOnly? ValidTo { get; set; }

    public uint? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual RoomType? ApplicableRoomType { get; set; }

    public virtual ICollection<BookingPromotion> BookingPromotions { get; set; } = new List<BookingPromotion>();

    public virtual User? CreatedByNavigation { get; set; }

    public virtual ICollection<Voucher> Vouchers { get; set; } = new List<Voucher>();
}
