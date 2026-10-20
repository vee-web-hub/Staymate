using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models;

public partial class RoomRatePeriod
{
    public uint RateId { get; set; }

    public byte RoomTypeId { get; set; }

    public string? PeriodName { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public decimal PricePerNight { get; set; }

    public uint? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User? CreatedByNavigation { get; set; }

    public virtual RoomType RoomType { get; set; } = null!;
}
