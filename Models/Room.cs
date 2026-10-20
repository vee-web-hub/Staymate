using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models;

public partial class Room
{
    public uint RoomId { get; set; }

    public string RoomNumber { get; set; } = null!;

    public byte RoomTypeId { get; set; }

    public byte FloorNo { get; set; }

    public string Status { get; set; } = null!;

    public bool? IsActive { get; set; }

    public virtual ICollection<BookingRoom> BookingRooms { get; set; } = new List<BookingRoom>();

    public virtual ICollection<HousekeepingTask> HousekeepingTasks { get; set; } = new List<HousekeepingTask>();

    public virtual ICollection<RoomStatusHistory> RoomStatusHistories { get; set; } = new List<RoomStatusHistory>();

    public virtual RoomType RoomType { get; set; } = null!;

    public virtual ICollection<StayRecord> StayRecords { get; set; } = new List<StayRecord>();
}
