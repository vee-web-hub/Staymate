using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models;

public partial class User
{
    public uint UserId { get; set; }

    public string Username { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public string? Phone { get; set; }

    public bool? IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    public virtual ICollection<FinancialTransaction> FinancialTransactions { get; set; } = new List<FinancialTransaction>();

    public virtual ICollection<Holiday> Holidays { get; set; } = new List<Holiday>();

    public virtual ICollection<HousekeepingTask> HousekeepingTasks { get; set; } = new List<HousekeepingTask>();

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual ICollection<Promotion> Promotions { get; set; } = new List<Promotion>();

    public virtual ICollection<Receipt> Receipts { get; set; } = new List<Receipt>();

    public virtual ICollection<Review> Reviews { get; set; } = new List<Review>();

    public virtual ICollection<RoomRatePeriod> RoomRatePeriods { get; set; } = new List<RoomRatePeriod>();

    public virtual ICollection<RoomStatusHistory> RoomStatusHistories { get; set; } = new List<RoomStatusHistory>();

    public virtual ICollection<StayRecord> StayRecordCheckedInByNavigations { get; set; } = new List<StayRecord>();

    public virtual ICollection<StayRecord> StayRecordCheckedOutByNavigations { get; set; } = new List<StayRecord>();

    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
