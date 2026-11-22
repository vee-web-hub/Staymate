using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models.ViewModels;

// ───────────────────────── Shared ─────────────────────────

public class ManagerOption
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public class ManagerChartPoint
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal Value { get; set; }
}

public class ManagerCountPoint
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

// ───────────────────────── Dashboard (Index) ─────────────────────────

public class ManagerRecentBookingRow
{
    public uint BookingId { get; set; }
    public string BookingCode { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public DateOnly CheckInDate { get; set; }
    public DateOnly CheckOutDate { get; set; }
    public int Nights { get; set; }
    public int NumGuests { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<string> RoomTypeNames { get; set; } = new();
}

public class ManagerDashboardViewModel
{
    // ช่วงวันที่ของรายงาน
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int PeriodDays { get; set; }

    // ห้องพัก (นับเฉพาะห้องที่เปิดใช้งาน)
    public int TotalRooms { get; set; }
    public int AvailableRooms { get; set; }
    public int OccupiedRooms { get; set; }
    public int NeedsCleaningRooms { get; set; }
    public int MaintenanceRooms { get; set; }
    public decimal OccupancyRate { get; set; }

    // การจอง
    public int TotalBookings { get; set; }
    public int PendingCheckInBookings { get; set; }
    public int PendingApprovalBookings { get; set; }
    public int PeriodBookings { get; set; }

    // รายรับจริง (Payment ที่ approved)
    public decimal TotalRevenue { get; set; }
    public decimal PeriodRevenue { get; set; }
    public int PeriodPaymentCount { get; set; }

    // ข้อมูลสรุปอื่น
    public int ActivePromotions { get; set; }
    public int TotalReviews { get; set; }
    public decimal? AverageRating { get; set; }

    // ข้อมูลกราฟ
    public List<ManagerCountPoint> RoomsByStatus { get; set; } = new();
    public List<ManagerCountPoint> BookingsByStatus { get; set; } = new();
    public List<ManagerChartPoint> DailyRevenue { get; set; } = new();
    public List<ManagerCountPoint> DailyBookings { get; set; } = new();

    public List<ManagerRecentBookingRow> RecentBookings { get; set; } = new();
    public List<string> DataNotes { get; set; } = new();
}

// ───────────────────────── Rooms & Pricing (RoomManagement) ─────────────────────────

public class ManagerRoomTypeRow
{
    public byte RoomTypeId { get; set; }
    public string TypeCode { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string? BedType { get; set; }
    public byte MinGuests { get; set; }
    public byte MaxGuests { get; set; }
    public bool AllowExtraBed { get; set; }
    public decimal ExtraBedPrice { get; set; }
    public decimal BasePrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public string? CurrentPeriodName { get; set; }
    public int RoomCount { get; set; }
    public int AvailableCount { get; set; }
    public int OccupiedCount { get; set; }
    public int NeedsCleaningCount { get; set; }
    public int MaintenanceCount { get; set; }
}

public class ManagerRoomRow
{
    public uint RoomId { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public byte FloorNo { get; set; }
    public byte RoomTypeId { get; set; }
    public string RoomTypeName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public decimal BasePrice { get; set; }
    public decimal CurrentPrice { get; set; }
}

public class ManagerPricePeriodRow
{
    public uint RateId { get; set; }
    public byte RoomTypeId { get; set; }
    public string RoomTypeName { get; set; } = string.Empty;
    public string? PeriodName { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal PricePerNight { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>current | upcoming | past</summary>
    public string PeriodStatus { get; set; } = string.Empty;
    public string PeriodStatusLabel { get; set; } = string.Empty;
}

public class ManagerRoomManagementViewModel
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public byte? RoomTypeId { get; set; }
    public bool IncludePastPeriods { get; set; }
    public DateOnly Today { get; set; }

    public int TotalRooms { get; set; }
    public int FilteredRoomCount { get; set; }

    public List<ManagerRoomTypeRow> RoomTypes { get; set; } = new();
    public List<ManagerRoomRow> Rooms { get; set; } = new();
    public List<ManagerPricePeriodRow> PricePeriods { get; set; } = new();

    public List<ManagerOption> StatusOptions { get; set; } = new();
    public List<ManagerOption> RoomTypeOptions { get; set; } = new();
    public List<string> DataNotes { get; set; } = new();
}

// ───────────────────────── Promotions & Holidays (PromotionManagement) ─────────────────────────

public class ManagerPromotionRow
{
    public uint PromotionId { get; set; }
    public string PromoCode { get; set; } = string.Empty;
    public string PromoName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DiscountType { get; set; } = string.Empty;
    public string DiscountTypeLabel { get; set; } = string.Empty;
    public decimal? DiscountPercent { get; set; }
    public decimal? DiscountPercentMax { get; set; }
    public byte? MinGuests { get; set; }
    public ushort? MinNights { get; set; }
    public byte? MaxDiscountNights { get; set; }
    public byte? ApplicableRoomTypeId { get; set; }
    public string? ApplicableRoomTypeName { get; set; }
    public bool RequiresHoliday { get; set; }
    public decimal? VoucherMinValue { get; set; }
    public decimal? VoucherMaxValue { get; set; }
    public bool IsActive { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    /// <summary>active | scheduled | expired | inactive (คำนวณจาก IsActive + ValidFrom/ValidTo)</summary>
    public string Status { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public int UsageCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ManagerHolidayRow
{
    public uint HolidayId { get; set; }
    public string HolidayName { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int DayCount { get; set; }
    public string? EventNote { get; set; }
    public string? CreatedByName { get; set; }
    /// <summary>ongoing | upcoming | past</summary>
    public string Status { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
}

public class ManagerPromotionManagementViewModel
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? DiscountType { get; set; }
    public DateOnly Today { get; set; }

    public int ActiveCount { get; set; }
    public int ScheduledCount { get; set; }
    public int ExpiredCount { get; set; }
    public int InactiveCount { get; set; }

    public List<ManagerPromotionRow> Promotions { get; set; } = new();
    public List<ManagerHolidayRow> Holidays { get; set; } = new();

    public List<ManagerOption> StatusOptions { get; set; } = new();
    public List<ManagerOption> DiscountTypeOptions { get; set; } = new();
    public List<ManagerOption> RoomTypeOptions { get; set; } = new();
    public List<string> DataNotes { get; set; } = new();
}

// ───────────────────────── Reviews (ReviewManagement) ─────────────────────────

public class ManagerReviewRow
{
    public uint ReviewId { get; set; }
    public uint BookingId { get; set; }
    public string BookingCode { get; set; } = string.Empty;
    public string ReviewerName { get; set; } = string.Empty;
    public byte Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ManagerReviewManagementViewModel
{
    public string? Search { get; set; }
    public byte? Rating { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages { get; set; } = 1;

    public decimal? AverageRating { get; set; }
    public List<ManagerCountPoint> RatingDistribution { get; set; } = new();
    public List<ManagerReviewRow> Reviews { get; set; } = new();
    public List<string> DataNotes { get; set; } = new();
}

// ───────────────────────── Staff (StaffManagement) ─────────────────────────

public class ManagerStaffRoleItem
{
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
}

/// <summary>ไม่มี PasswordHash โดยเจตนา</summary>
public class ManagerStaffRow
{
    public uint UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<ManagerStaffRoleItem> Roles { get; set; } = new();
}

public class ManagerStaffManagementViewModel
{
    public string? Search { get; set; }
    public string? RoleCode { get; set; }

    public int TotalStaff { get; set; }
    public int ActiveStaff { get; set; }
    public int InactiveStaff { get; set; }

    public List<ManagerStaffRow> Staff { get; set; } = new();
    public List<ManagerOption> RoleOptions { get; set; } = new();
    public List<string> DataNotes { get; set; } = new();
}

// ───────────────────────── Profile ─────────────────────────

public class ManagerProfileViewModel
{
    public uint UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool? IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> RoleCodes { get; set; } = new();
}

// ───────────────────────── POST inputs (ใช้ nullable เพื่อให้ Controller ตรวจและตอบข้อความภาษาไทยเอง) ─────────────────────────

public class ManagerPricePeriodInput
{
    public uint? RateId { get; set; }
    public byte? RoomTypeId { get; set; }
    public string? PeriodName { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public decimal? PricePerNight { get; set; }
}

public class ManagerPromotionInput
{
    public uint? PromotionId { get; set; }
    public string? PromoCode { get; set; }
    public string? PromoName { get; set; }
    public string? Description { get; set; }
    public string? DiscountType { get; set; }
    public decimal? DiscountPercent { get; set; }
    public decimal? DiscountPercentMax { get; set; }
    public byte? MinGuests { get; set; }
    public ushort? MinNights { get; set; }
    public byte? MaxDiscountNights { get; set; }
    public byte? ApplicableRoomTypeId { get; set; }
    public bool RequiresHoliday { get; set; }
    public decimal? VoucherMinValue { get; set; }
    public decimal? VoucherMaxValue { get; set; }
    public bool IsActive { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
}

public class ManagerHolidayInput
{
    public uint? HolidayId { get; set; }
    public string? HolidayName { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? EventNote { get; set; }
}