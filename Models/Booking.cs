using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models;

public partial class Booking
{
    public uint BookingId { get; set; }

    public string BookingCode { get; set; } = null!;

    public uint? UserId { get; set; }

    public string ContactName { get; set; } = null!;

    public string ContactEmail { get; set; } = null!;

    public string ContactPhone { get; set; } = null!;

    public DateOnly CheckInDate { get; set; }

    public DateOnly CheckOutDate { get; set; }

    public ushort Nights { get; set; }

    public byte NumGuests { get; set; }

    public decimal RoomAmount { get; set; }

    public decimal DiscountPercent { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal TotalAmount { get; set; }

    public string Status { get; set; } = null!;

    public string? QrCode { get; set; }

    public string? SpecialRequest { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<BookingGuest> BookingGuests { get; set; } = new List<BookingGuest>();

    public virtual ICollection<BookingPromotion> BookingPromotions { get; set; } = new List<BookingPromotion>();

    public virtual ICollection<BookingRoom> BookingRooms { get; set; } = new List<BookingRoom>();

    public virtual ICollection<HousekeepingTask> HousekeepingTasks { get; set; } = new List<HousekeepingTask>();

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual Review? Review { get; set; }

    public virtual ICollection<RoomStatusHistory> RoomStatusHistories { get; set; } = new List<RoomStatusHistory>();

    public virtual ICollection<StayRecord> StayRecords { get; set; } = new List<StayRecord>();

    public virtual User? User { get; set; }

    public virtual ICollection<Voucher> Vouchers { get; set; } = new List<Voucher>();
}
