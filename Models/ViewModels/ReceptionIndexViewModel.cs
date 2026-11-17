using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.ViewModels
{
    public class ReceptionIndexViewModel
    {
        // Dashboard Summary
        public int TotalRooms { get; set; }
        public int AvailableRooms { get; set; }
        public int OccupiedRooms { get; set; }
        public int NeedsCleaningRooms { get; set; }
        public int MaintenanceRooms { get; set; }

        public int TodayCheckIns { get; set; }
        public int TodayCheckOuts { get; set; }

        // จำนวนผู้เข้าพักที่ Check-in แล้วและยังไม่ Check-out
        public int CompletedCheckInsCount { get; set; }

        public int PendingBookings { get; set; }
        public int ConfirmedBookings { get; set; }

        // Search
        public string? SearchTerm { get; set; }

        // Booking Lists
        public List<ReceptionBookingViewModel> RecentBookings { get; set; } = new();
        public List<ReceptionBookingViewModel> Bookings { get; set; } = new();

        // Check-in / Check-out
        public List<ReceptionStayViewModel> CheckInList { get; set; } = new();
        public List<ReceptionStayViewModel> CheckOutList { get; set; } = new();

        // Room Calendar / Room Status
        public List<ReceptionRoomViewModel> Rooms { get; set; } = new();

        // Housekeeping Requests (แจ้งงานแม่บ้าน)
        public List<ReceptionTaskViewModel> HousekeepingRequests { get; set; } = new();
    }

    public class ReceptionBookingViewModel
    {
        public uint BookingId { get; set; }
        public string BookingCode { get; set; } = string.Empty;

        public string ContactName { get; set; } = string.Empty;
        public string ContactPhone { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;

        public DateOnly CheckInDate { get; set; }
        public DateOnly CheckOutDate { get; set; }

        public ushort Nights { get; set; }
        public byte NumGuests { get; set; }

        public decimal DiscountAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }

        public string Status { get; set; } = string.Empty;

        public DateTime? CreatedAt { get; set; }

        public List<string> RoomNumbers { get; set; } = new();
        public List<string> RoomTypes { get; set; } = new();

        public int GuestCount { get; set; }

        // Payment (Reception ดูได้อย่างเดียว)
        public string PaymentStatus { get; set; } = string.Empty;
        public DateTime? PaymentSubmittedAt { get; set; }
        public DateTime? PaymentApprovedAt { get; set; }

        // Stay timeline
        public DateTime? CheckedInAt { get; set; }
        public DateTime? CheckedOutAt { get; set; }
    }

    public class ReceptionStayViewModel
    {
        public uint StayId { get; set; }
        public uint BookingId { get; set; }

        public string BookingCode { get; set; } = string.Empty;

        // สถานะที่หน้า Check-in ใช้ตรวจเงื่อนไข
        public string BookingStatus { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;
        public string RoomStatus { get; set; } = string.Empty;

        public string GuestName { get; set; } = string.Empty;
        public string GuestPhone { get; set; } = string.Empty;

        public uint RoomId { get; set; }
        public string RoomNumber { get; set; } = string.Empty;
        public string RoomTypeName { get; set; } = string.Empty;

        public DateOnly CheckInDate { get; set; }
        public DateOnly CheckOutDate { get; set; }

        public DateTime? CheckedInAt { get; set; }
        public DateTime? CheckedOutAt { get; set; }

        public byte NumGuests { get; set; }
        public byte? ActualGuests { get; set; }
    }

    public class ReceptionRoomViewModel
    {
        public uint RoomId { get; set; }
        public string RoomNumber { get; set; } = string.Empty;

        public byte FloorNo { get; set; }

        public string RoomTypeName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        public bool? IsActive { get; set; }
    }

    public class ReceptionTaskViewModel
    {
        public uint TaskId { get; set; }

        public uint RoomId { get; set; }
        public string RoomNumber { get; set; } = string.Empty;

        public uint? BookingId { get; set; }
        public string? BookingCode { get; set; }

        public string Status { get; set; } = string.Empty;

        public string? Note { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public string? AssignedToName { get; set; }
    }

    // โปรไฟล์ของฉัน
    public class ReceptionProfileViewModel
    {
        public uint UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string RoleName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}