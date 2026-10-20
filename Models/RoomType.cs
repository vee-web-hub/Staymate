using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models;

public partial class RoomType
{
    public byte RoomTypeId { get; set; }

    public string TypeCode { get; set; } = null!;

    public string TypeName { get; set; } = null!;

    public string? Description { get; set; }

    public string? BedType { get; set; }

    public byte MinGuests { get; set; }

    public byte MaxGuests { get; set; }

    public bool AllowExtraBed { get; set; }

    public decimal ExtraBedPrice { get; set; }

    public decimal BasePrice { get; set; }

    public virtual ICollection<BookingRoom> BookingRooms { get; set; } = new List<BookingRoom>();

    public virtual ICollection<Promotion> Promotions { get; set; } = new List<Promotion>();

    public virtual ICollection<RoomRatePeriod> RoomRatePeriods { get; set; } = new List<RoomRatePeriod>();

    public virtual ICollection<Room> Rooms { get; set; } = new List<Room>();
}
