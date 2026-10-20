using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models;

public partial class BookingRoom
{
    public uint BookingRoomId { get; set; }

    public uint BookingId { get; set; }

    public byte RoomTypeId { get; set; }

    public uint? RoomId { get; set; }

    public decimal PricePerNight { get; set; }

    public ushort Nights { get; set; }

    public byte ExtraBedQty { get; set; }

    public decimal ExtraBedAmount { get; set; }

    public decimal LineTotal { get; set; }

    public virtual Booking Booking { get; set; } = null!;

    public virtual Room? Room { get; set; }

    public virtual RoomType RoomType { get; set; } = null!;
}
