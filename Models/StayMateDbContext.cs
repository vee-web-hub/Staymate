using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace StayMate_Kavinvee_0576.Models;

public partial class StayMateDbContext : DbContext
{
    public StayMateDbContext(DbContextOptions<StayMateDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<Booking> Bookings { get; set; }

    public virtual DbSet<BookingGuest> BookingGuests { get; set; }

    public virtual DbSet<BookingPromotion> BookingPromotions { get; set; }

    public virtual DbSet<BookingRoom> BookingRooms { get; set; }

    public virtual DbSet<FinancialTransaction> FinancialTransactions { get; set; }

    public virtual DbSet<Holiday> Holidays { get; set; }

    public virtual DbSet<HousekeepingTask> HousekeepingTasks { get; set; }

    public virtual DbSet<Payment> Payments { get; set; }

    public virtual DbSet<Promotion> Promotions { get; set; }

    public virtual DbSet<Receipt> Receipts { get; set; }

    public virtual DbSet<Review> Reviews { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Room> Rooms { get; set; }

    public virtual DbSet<RoomRatePeriod> RoomRatePeriods { get; set; }

    public virtual DbSet<RoomStatusHistory> RoomStatusHistories { get; set; }

    public virtual DbSet<RoomType> RoomTypes { get; set; }

    public virtual DbSet<StayRecord> StayRecords { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserRole> UserRoles { get; set; }

    public virtual DbSet<Voucher> Vouchers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("utf8mb4_unicode_ci")
            .HasCharSet("utf8mb4");

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.LogId).HasName("PRIMARY");

            entity.ToTable("audit_logs");

            entity.HasIndex(e => new { e.TableName, e.RecordId }, "idx_audit_table");

            entity.HasIndex(e => e.CreatedAt, "idx_audit_time");

            entity.HasIndex(e => e.UserId, "idx_audit_user");

            entity.Property(e => e.LogId).HasColumnName("log_id");
            entity.Property(e => e.Action)
                .HasMaxLength(50)
                .HasColumnName("action");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.IpAddress)
                .HasMaxLength(45)
                .HasColumnName("ip_address");
            entity.Property(e => e.NewValue)
                .HasColumnType("json")
                .HasColumnName("new_value");
            entity.Property(e => e.OldValue)
                .HasColumnType("json")
                .HasColumnName("old_value");
            entity.Property(e => e.RecordId)
                .HasMaxLength(64)
                .HasColumnName("record_id");
            entity.Property(e => e.TableName)
                .HasMaxLength(64)
                .HasColumnName("table_name");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.AuditLogs)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_audit_user");
        });

        modelBuilder.Entity<Booking>(entity =>
        {
            entity.HasKey(e => e.BookingId).HasName("PRIMARY");

            entity.ToTable("bookings");

            entity.HasIndex(e => new { e.CheckInDate, e.CheckOutDate }, "idx_bookings_dates");

            entity.HasIndex(e => e.Status, "idx_bookings_status");

            entity.HasIndex(e => e.UserId, "idx_bookings_user");

            entity.HasIndex(e => e.BookingCode, "uq_bookings_code").IsUnique();

            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.BookingCode)
                .HasMaxLength(20)
                .HasColumnName("booking_code");
            entity.Property(e => e.CheckInDate).HasColumnName("check_in_date");
            entity.Property(e => e.CheckOutDate).HasColumnName("check_out_date");
            entity.Property(e => e.ContactEmail)
                .HasMaxLength(120)
                .HasColumnName("contact_email");
            entity.Property(e => e.ContactName)
                .HasMaxLength(150)
                .HasColumnName("contact_name");
            entity.Property(e => e.ContactPhone)
                .HasMaxLength(20)
                .HasColumnName("contact_phone");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.DiscountAmount)
                .HasPrecision(12, 2)
                .HasColumnName("discount_amount");
            entity.Property(e => e.DiscountPercent)
                .HasPrecision(5, 2)
                .HasColumnName("discount_percent");
            entity.Property(e => e.Nights).HasColumnName("nights");
            entity.Property(e => e.NumGuests).HasColumnName("num_guests");
            entity.Property(e => e.QrCode)
                .HasMaxLength(255)
                .HasColumnName("qr_code");
            entity.Property(e => e.RoomAmount)
                .HasPrecision(12, 2)
                .HasColumnName("room_amount");
            entity.Property(e => e.SpecialRequest)
                .HasColumnType("text")
                .HasColumnName("special_request");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'pending_payment'")
                .HasColumnType("enum('pending_payment','pending_approval','confirmed','checked_in','checked_out','cancelled','rejected')")
                .HasColumnName("status");
            entity.Property(e => e.TaxAmount)
                .HasPrecision(12, 2)
                .HasColumnName("tax_amount");
            entity.Property(e => e.TotalAmount)
                .HasPrecision(12, 2)
                .HasColumnName("total_amount");
            entity.Property(e => e.UpdatedAt)
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.Bookings)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_bookings_user");
        });

        modelBuilder.Entity<BookingGuest>(entity =>
        {
            entity.HasKey(e => e.GuestId).HasName("PRIMARY");

            entity.ToTable("booking_guests");

            entity.HasIndex(e => e.BookingId, "idx_bg_booking");

            entity.Property(e => e.GuestId).HasColumnName("guest_id");
            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.FullName)
                .HasMaxLength(150)
                .HasColumnName("full_name");
            entity.Property(e => e.IdCardNo)
                .HasMaxLength(30)
                .HasColumnName("id_card_no");
            entity.Property(e => e.IsExtraBed).HasColumnName("is_extra_bed");
            entity.Property(e => e.IsPrimary).HasColumnName("is_primary");
            entity.Property(e => e.Phone)
                .HasMaxLength(20)
                .HasColumnName("phone");

            entity.HasOne(d => d.Booking).WithMany(p => p.BookingGuests)
                .HasForeignKey(d => d.BookingId)
                .HasConstraintName("fk_bg_booking");
        });

        modelBuilder.Entity<BookingPromotion>(entity =>
        {
            entity.HasKey(e => e.BookingPromotionId).HasName("PRIMARY");

            entity.ToTable("booking_promotions");

            entity.HasIndex(e => e.PromotionId, "fk_bp_promo");

            entity.HasIndex(e => new { e.BookingId, e.PromotionId }, "uq_bp").IsUnique();

            entity.Property(e => e.BookingPromotionId).HasColumnName("booking_promotion_id");
            entity.Property(e => e.AppliedAmount)
                .HasPrecision(12, 2)
                .HasColumnName("applied_amount");
            entity.Property(e => e.AppliedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("applied_at");
            entity.Property(e => e.AppliedPercent)
                .HasPrecision(5, 2)
                .HasColumnName("applied_percent");
            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.PromotionId).HasColumnName("promotion_id");
            entity.Property(e => e.StackRole)
                .HasDefaultValueSql("'base'")
                .HasColumnType("enum('base','on_top')")
                .HasColumnName("stack_role");

            entity.HasOne(d => d.Booking).WithMany(p => p.BookingPromotions)
                .HasForeignKey(d => d.BookingId)
                .HasConstraintName("fk_bp_booking");

            entity.HasOne(d => d.Promotion).WithMany(p => p.BookingPromotions)
                .HasForeignKey(d => d.PromotionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_bp_promo");
        });

        modelBuilder.Entity<BookingRoom>(entity =>
        {
            entity.HasKey(e => e.BookingRoomId).HasName("PRIMARY");

            entity.ToTable("booking_rooms");

            entity.HasIndex(e => e.RoomTypeId, "fk_br_type");

            entity.HasIndex(e => e.BookingId, "idx_br_booking");

            entity.HasIndex(e => e.RoomId, "idx_br_room");

            entity.Property(e => e.BookingRoomId).HasColumnName("booking_room_id");
            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.ExtraBedAmount)
                .HasPrecision(10, 2)
                .HasColumnName("extra_bed_amount");
            entity.Property(e => e.ExtraBedQty).HasColumnName("extra_bed_qty");
            entity.Property(e => e.LineTotal)
                .HasPrecision(12, 2)
                .HasColumnName("line_total");
            entity.Property(e => e.Nights).HasColumnName("nights");
            entity.Property(e => e.PricePerNight)
                .HasPrecision(10, 2)
                .HasColumnName("price_per_night");
            entity.Property(e => e.RoomId).HasColumnName("room_id");
            entity.Property(e => e.RoomTypeId).HasColumnName("room_type_id");

            entity.HasOne(d => d.Booking).WithMany(p => p.BookingRooms)
                .HasForeignKey(d => d.BookingId)
                .HasConstraintName("fk_br_booking");

            entity.HasOne(d => d.Room).WithMany(p => p.BookingRooms)
                .HasForeignKey(d => d.RoomId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_br_room");

            entity.HasOne(d => d.RoomType).WithMany(p => p.BookingRooms)
                .HasForeignKey(d => d.RoomTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_br_type");
        });

        modelBuilder.Entity<FinancialTransaction>(entity =>
        {
            entity.HasKey(e => e.TxnId).HasName("PRIMARY");

            entity.ToTable("financial_transactions");

            entity.HasIndex(e => e.PaymentId, "fk_ft_payment");

            entity.HasIndex(e => e.RecordedBy, "fk_ft_user");

            entity.HasIndex(e => e.TxnDate, "idx_ft_date");

            entity.HasIndex(e => e.TxnType, "idx_ft_type");

            entity.Property(e => e.TxnId).HasColumnName("txn_id");
            entity.Property(e => e.Amount)
                .HasPrecision(12, 2)
                .HasColumnName("amount");
            entity.Property(e => e.Category)
                .HasMaxLength(100)
                .HasColumnName("category");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.Description)
                .HasMaxLength(255)
                .HasColumnName("description");
            entity.Property(e => e.PaymentId).HasColumnName("payment_id");
            entity.Property(e => e.RecordedBy).HasColumnName("recorded_by");
            entity.Property(e => e.TxnDate).HasColumnName("txn_date");
            entity.Property(e => e.TxnType)
                .HasColumnType("enum('income','expense','refund')")
                .HasColumnName("txn_type");

            entity.HasOne(d => d.Payment).WithMany(p => p.FinancialTransactions)
                .HasForeignKey(d => d.PaymentId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_ft_payment");

            entity.HasOne(d => d.RecordedByNavigation).WithMany(p => p.FinancialTransactions)
                .HasForeignKey(d => d.RecordedBy)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_ft_user");
        });

        modelBuilder.Entity<Holiday>(entity =>
        {
            entity.HasKey(e => e.HolidayId).HasName("PRIMARY");

            entity.ToTable("holidays");

            entity.HasIndex(e => e.CreatedBy, "fk_holiday_user");

            entity.HasIndex(e => new { e.StartDate, e.EndDate }, "idx_holiday_dates");

            entity.Property(e => e.HolidayId).HasColumnName("holiday_id");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.EventNote)
                .HasMaxLength(255)
                .HasColumnName("event_note");
            entity.Property(e => e.HolidayName)
                .HasMaxLength(150)
                .HasColumnName("holiday_name");
            entity.Property(e => e.StartDate).HasColumnName("start_date");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.Holidays)
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_holiday_user");
        });

        modelBuilder.Entity<HousekeepingTask>(entity =>
        {
            entity.HasKey(e => e.TaskId).HasName("PRIMARY");

            entity.ToTable("housekeeping_tasks");

            entity.HasIndex(e => e.BookingId, "fk_hk_booking");

            entity.HasIndex(e => e.RoomId, "fk_hk_room");

            entity.HasIndex(e => e.AssignedTo, "idx_hk_assignee");

            entity.HasIndex(e => e.Status, "idx_hk_status");

            entity.Property(e => e.TaskId).HasColumnName("task_id");
            entity.Property(e => e.AssignedTo).HasColumnName("assigned_to");
            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.CompletedAt)
                .HasColumnType("datetime")
                .HasColumnName("completed_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.Note)
                .HasMaxLength(255)
                .HasColumnName("note");
            entity.Property(e => e.RoomId).HasColumnName("room_id");
            entity.Property(e => e.StartedAt)
                .HasColumnType("datetime")
                .HasColumnName("started_at");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'pending'")
                .HasColumnType("enum('pending','in_progress','done')")
                .HasColumnName("status");

            entity.HasOne(d => d.AssignedToNavigation).WithMany(p => p.HousekeepingTasks)
                .HasForeignKey(d => d.AssignedTo)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_hk_assignee");

            entity.HasOne(d => d.Booking).WithMany(p => p.HousekeepingTasks)
                .HasForeignKey(d => d.BookingId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_hk_booking");

            entity.HasOne(d => d.Room).WithMany(p => p.HousekeepingTasks)
                .HasForeignKey(d => d.RoomId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_hk_room");
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.PaymentId).HasName("PRIMARY");

            entity.ToTable("payments");

            entity.HasIndex(e => e.ReviewedBy, "fk_pay_reviewer");

            entity.HasIndex(e => e.BookingId, "idx_pay_booking");

            entity.HasIndex(e => e.Status, "idx_pay_status");

            entity.Property(e => e.PaymentId).HasColumnName("payment_id");
            entity.Property(e => e.Amount)
                .HasPrecision(12, 2)
                .HasColumnName("amount");
            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.Method)
                .HasDefaultValueSql("'bank_transfer'")
                .HasColumnType("enum('bank_transfer','promptpay','credit_card','cash')")
                .HasColumnName("method");
            entity.Property(e => e.PaidAt)
                .HasColumnType("datetime")
                .HasColumnName("paid_at");
            entity.Property(e => e.RejectReason)
                .HasMaxLength(255)
                .HasColumnName("reject_reason");
            entity.Property(e => e.ReviewedAt)
                .HasColumnType("datetime")
                .HasColumnName("reviewed_at");
            entity.Property(e => e.ReviewedBy).HasColumnName("reviewed_by");
            entity.Property(e => e.SlipImagePath)
                .HasMaxLength(255)
                .HasColumnName("slip_image_path");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'pending'")
                .HasColumnType("enum('pending','approved','rejected')")
                .HasColumnName("status");

            entity.HasOne(d => d.Booking).WithMany(p => p.Payments)
                .HasForeignKey(d => d.BookingId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_pay_booking");

            entity.HasOne(d => d.ReviewedByNavigation).WithMany(p => p.Payments)
                .HasForeignKey(d => d.ReviewedBy)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_pay_reviewer");
        });

        modelBuilder.Entity<Promotion>(entity =>
        {
            entity.HasKey(e => e.PromotionId).HasName("PRIMARY");

            entity.ToTable("promotions");

            entity.HasIndex(e => e.ApplicableRoomTypeId, "fk_promo_room_type");

            entity.HasIndex(e => e.CreatedBy, "fk_promo_user");

            entity.HasIndex(e => e.PromoCode, "uq_promo_code").IsUnique();

            entity.Property(e => e.PromotionId).HasColumnName("promotion_id");
            entity.Property(e => e.ApplicableRoomTypeId).HasColumnName("applicable_room_type_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.Description)
                .HasColumnType("text")
                .HasColumnName("description");
            entity.Property(e => e.DiscountPercent)
                .HasPrecision(5, 2)
                .HasColumnName("discount_percent");
            entity.Property(e => e.DiscountPercentMax)
                .HasPrecision(5, 2)
                .HasColumnName("discount_percent_max");
            entity.Property(e => e.DiscountType)
                .HasColumnType("enum('free_extra_bed','percent_room','percent_extra_nights')")
                .HasColumnName("discount_type");
            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasColumnName("is_active");
            entity.Property(e => e.MaxDiscountNights).HasColumnName("max_discount_nights");
            entity.Property(e => e.MinGuests).HasColumnName("min_guests");
            entity.Property(e => e.MinNights).HasColumnName("min_nights");
            entity.Property(e => e.PromoCode)
                .HasMaxLength(30)
                .HasColumnName("promo_code");
            entity.Property(e => e.PromoName)
                .HasMaxLength(150)
                .HasColumnName("promo_name");
            entity.Property(e => e.RequiresHoliday).HasColumnName("requires_holiday");
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to");
            entity.Property(e => e.VoucherMaxValue)
                .HasPrecision(10, 2)
                .HasColumnName("voucher_max_value");
            entity.Property(e => e.VoucherMinValue)
                .HasPrecision(10, 2)
                .HasColumnName("voucher_min_value");

            entity.HasOne(d => d.ApplicableRoomType).WithMany(p => p.Promotions)
                .HasForeignKey(d => d.ApplicableRoomTypeId)
                .HasConstraintName("fk_promo_room_type");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.Promotions)
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_promo_user");
        });

        modelBuilder.Entity<Receipt>(entity =>
        {
            entity.HasKey(e => e.ReceiptId).HasName("PRIMARY");

            entity.ToTable("receipts");

            entity.HasIndex(e => e.IssuedBy, "fk_rc_user");

            entity.HasIndex(e => e.ReceiptNo, "uq_receipt_no").IsUnique();

            entity.HasIndex(e => e.PaymentId, "uq_receipt_payment").IsUnique();

            entity.Property(e => e.ReceiptId).HasColumnName("receipt_id");
            entity.Property(e => e.Amount)
                .HasPrecision(12, 2)
                .HasColumnName("amount");
            entity.Property(e => e.IssuedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("issued_at");
            entity.Property(e => e.IssuedBy).HasColumnName("issued_by");
            entity.Property(e => e.PaymentId).HasColumnName("payment_id");
            entity.Property(e => e.ReceiptNo)
                .HasMaxLength(30)
                .HasColumnName("receipt_no");

            entity.HasOne(d => d.IssuedByNavigation).WithMany(p => p.Receipts)
                .HasForeignKey(d => d.IssuedBy)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_rc_user");

            entity.HasOne(d => d.Payment).WithOne(p => p.Receipt)
                .HasForeignKey<Receipt>(d => d.PaymentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_rc_payment");
        });

        modelBuilder.Entity<Review>(entity =>
        {
            entity.HasKey(e => e.ReviewId).HasName("PRIMARY");

            entity.ToTable("reviews");

            entity.HasIndex(e => e.UserId, "fk_rev_user");

            entity.HasIndex(e => e.BookingId, "uq_review_booking").IsUnique();

            entity.Property(e => e.ReviewId).HasColumnName("review_id");
            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.Comment)
                .HasColumnType("text")
                .HasColumnName("comment");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.Rating).HasColumnName("rating");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Booking).WithOne(p => p.Review)
                .HasForeignKey<Review>(d => d.BookingId)
                .HasConstraintName("fk_rev_booking");

            entity.HasOne(d => d.User).WithMany(p => p.Reviews)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_rev_user");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.RoleId).HasName("PRIMARY");

            entity.ToTable("roles");

            entity.HasIndex(e => e.RoleCode, "uq_roles_code").IsUnique();

            entity.Property(e => e.RoleId)
                .ValueGeneratedOnAdd()
                .HasColumnName("role_id");
            entity.Property(e => e.Description)
                .HasMaxLength(255)
                .HasColumnName("description");
            entity.Property(e => e.RoleCode)
                .HasMaxLength(30)
                .HasColumnName("role_code");
            entity.Property(e => e.RoleName)
                .HasMaxLength(100)
                .HasColumnName("role_name");
        });

        modelBuilder.Entity<Room>(entity =>
        {
            entity.HasKey(e => e.RoomId).HasName("PRIMARY");

            entity.ToTable("rooms");

            entity.HasIndex(e => e.Status, "idx_rooms_status");

            entity.HasIndex(e => e.RoomTypeId, "idx_rooms_type");

            entity.HasIndex(e => e.RoomNumber, "uq_rooms_number").IsUnique();

            entity.Property(e => e.RoomId).HasColumnName("room_id");
            entity.Property(e => e.FloorNo).HasColumnName("floor_no");
            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasColumnName("is_active");
            entity.Property(e => e.RoomNumber)
                .HasMaxLength(10)
                .HasColumnName("room_number");
            entity.Property(e => e.RoomTypeId).HasColumnName("room_type_id");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'available'")
                .HasColumnType("enum('available','occupied','needs_cleaning','maintenance')")
                .HasColumnName("status");

            entity.HasOne(d => d.RoomType).WithMany(p => p.Rooms)
                .HasForeignKey(d => d.RoomTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_rooms_type");
        });

        modelBuilder.Entity<RoomRatePeriod>(entity =>
        {
            entity.HasKey(e => e.RateId).HasName("PRIMARY");

            entity.ToTable("room_rate_periods");

            entity.HasIndex(e => e.CreatedBy, "fk_rate_user");

            entity.HasIndex(e => new { e.RoomTypeId, e.StartDate, e.EndDate }, "idx_rate_lookup");

            entity.Property(e => e.RateId).HasColumnName("rate_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.PeriodName)
                .HasMaxLength(100)
                .HasColumnName("period_name");
            entity.Property(e => e.PricePerNight)
                .HasPrecision(10, 2)
                .HasColumnName("price_per_night");
            entity.Property(e => e.RoomTypeId).HasColumnName("room_type_id");
            entity.Property(e => e.StartDate).HasColumnName("start_date");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.RoomRatePeriods)
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_rate_user");

            entity.HasOne(d => d.RoomType).WithMany(p => p.RoomRatePeriods)
                .HasForeignKey(d => d.RoomTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_rate_type");
        });

        modelBuilder.Entity<RoomStatusHistory>(entity =>
        {
            entity.HasKey(e => e.HistoryId).HasName("PRIMARY");

            entity.ToTable("room_status_history");

            entity.HasIndex(e => e.BookingId, "fk_rsh_booking");

            entity.HasIndex(e => e.ChangedBy, "fk_rsh_user");

            entity.HasIndex(e => new { e.RoomId, e.ChangedAt }, "idx_rsh_room");

            entity.Property(e => e.HistoryId).HasColumnName("history_id");
            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.ChangedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("changed_at");
            entity.Property(e => e.ChangedBy).HasColumnName("changed_by");
            entity.Property(e => e.NewStatus)
                .HasColumnType("enum('available','occupied','needs_cleaning','maintenance')")
                .HasColumnName("new_status");
            entity.Property(e => e.Note)
                .HasMaxLength(255)
                .HasColumnName("note");
            entity.Property(e => e.OldStatus)
                .HasColumnType("enum('available','occupied','needs_cleaning','maintenance')")
                .HasColumnName("old_status");
            entity.Property(e => e.RoomId).HasColumnName("room_id");

            entity.HasOne(d => d.Booking).WithMany(p => p.RoomStatusHistories)
                .HasForeignKey(d => d.BookingId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_rsh_booking");

            entity.HasOne(d => d.ChangedByNavigation).WithMany(p => p.RoomStatusHistories)
                .HasForeignKey(d => d.ChangedBy)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_rsh_user");

            entity.HasOne(d => d.Room).WithMany(p => p.RoomStatusHistories)
                .HasForeignKey(d => d.RoomId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_rsh_room");
        });

        modelBuilder.Entity<RoomType>(entity =>
        {
            entity.HasKey(e => e.RoomTypeId).HasName("PRIMARY");

            entity.ToTable("room_types");

            entity.HasIndex(e => e.TypeCode, "uq_room_types_code").IsUnique();

            entity.Property(e => e.RoomTypeId)
                .ValueGeneratedOnAdd()
                .HasColumnName("room_type_id");
            entity.Property(e => e.AllowExtraBed).HasColumnName("allow_extra_bed");
            entity.Property(e => e.BasePrice)
                .HasPrecision(10, 2)
                .HasColumnName("base_price");
            entity.Property(e => e.BedType)
                .HasMaxLength(100)
                .HasColumnName("bed_type");
            entity.Property(e => e.Description)
                .HasColumnType("text")
                .HasColumnName("description");
            entity.Property(e => e.ExtraBedPrice)
                .HasPrecision(10, 2)
                .HasColumnName("extra_bed_price");
            entity.Property(e => e.MaxGuests).HasColumnName("max_guests");
            entity.Property(e => e.MinGuests).HasColumnName("min_guests");
            entity.Property(e => e.TypeCode)
                .HasMaxLength(20)
                .HasColumnName("type_code");
            entity.Property(e => e.TypeName)
                .HasMaxLength(100)
                .HasColumnName("type_name");
        });

        modelBuilder.Entity<StayRecord>(entity =>
        {
            entity.HasKey(e => e.StayId).HasName("PRIMARY");

            entity.ToTable("stay_records");

            entity.HasIndex(e => e.CheckedInBy, "fk_stay_in");

            entity.HasIndex(e => e.CheckedOutBy, "fk_stay_out");

            entity.HasIndex(e => e.BookingId, "idx_stay_booking");

            entity.HasIndex(e => new { e.RoomId, e.CheckedInAt }, "idx_stay_room");

            entity.Property(e => e.StayId).HasColumnName("stay_id");
            entity.Property(e => e.ActualGuests).HasColumnName("actual_guests");
            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.CheckedInAt)
                .HasColumnType("datetime")
                .HasColumnName("checked_in_at");
            entity.Property(e => e.CheckedInBy).HasColumnName("checked_in_by");
            entity.Property(e => e.CheckedOutAt)
                .HasColumnType("datetime")
                .HasColumnName("checked_out_at");
            entity.Property(e => e.CheckedOutBy).HasColumnName("checked_out_by");
            entity.Property(e => e.RoomId).HasColumnName("room_id");

            entity.HasOne(d => d.Booking).WithMany(p => p.StayRecords)
                .HasForeignKey(d => d.BookingId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_stay_booking");

            entity.HasOne(d => d.CheckedInByNavigation).WithMany(p => p.StayRecordCheckedInByNavigations)
                .HasForeignKey(d => d.CheckedInBy)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_stay_in");

            entity.HasOne(d => d.CheckedOutByNavigation).WithMany(p => p.StayRecordCheckedOutByNavigations)
                .HasForeignKey(d => d.CheckedOutBy)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_stay_out");

            entity.HasOne(d => d.Room).WithMany(p => p.StayRecords)
                .HasForeignKey(d => d.RoomId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_stay_room");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PRIMARY");

            entity.ToTable("users");

            entity.HasIndex(e => e.Email, "uq_users_email").IsUnique();

            entity.HasIndex(e => e.Username, "uq_users_username").IsUnique();

            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.Email)
                .HasMaxLength(120)
                .HasColumnName("email");
            entity.Property(e => e.FullName)
                .HasMaxLength(150)
                .HasColumnName("full_name");
            entity.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasColumnName("is_active");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .HasColumnName("password_hash");
            entity.Property(e => e.Phone)
                .HasMaxLength(20)
                .HasColumnName("phone");
            entity.Property(e => e.UpdatedAt)
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("updated_at");
            entity.Property(e => e.Username)
                .HasMaxLength(50)
                .HasColumnName("username");
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.RoleId })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity.ToTable("user_roles");

            entity.HasIndex(e => e.RoleId, "fk_ur_role");

            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.AssignedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("assigned_at");

            entity.HasOne(d => d.Role).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_ur_role");

            entity.HasOne(d => d.User).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("fk_ur_user");
        });

        modelBuilder.Entity<Voucher>(entity =>
        {
            entity.HasKey(e => e.VoucherId).HasName("PRIMARY");

            entity.ToTable("vouchers");

            entity.HasIndex(e => e.PromotionId, "fk_vc_promo");

            entity.HasIndex(e => e.BookingId, "idx_voucher_booking");

            entity.HasIndex(e => e.VoucherCode, "uq_voucher_code").IsUnique();

            entity.Property(e => e.VoucherId).HasColumnName("voucher_id");
            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("datetime")
                .HasColumnName("expires_at");
            entity.Property(e => e.IssuedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("issued_at");
            entity.Property(e => e.PartnerName)
                .HasMaxLength(150)
                .HasColumnName("partner_name");
            entity.Property(e => e.PromotionId).HasColumnName("promotion_id");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'issued'")
                .HasColumnType("enum('issued','used','expired','cancelled')")
                .HasColumnName("status");
            entity.Property(e => e.UsedAt)
                .HasColumnType("datetime")
                .HasColumnName("used_at");
            entity.Property(e => e.ValueAmount)
                .HasPrecision(10, 2)
                .HasColumnName("value_amount");
            entity.Property(e => e.VoucherCode)
                .HasMaxLength(30)
                .HasColumnName("voucher_code");
            entity.Property(e => e.VoucherType)
                .HasColumnType("enum('e_voucher','event_pass')")
                .HasColumnName("voucher_type");

            entity.HasOne(d => d.Booking).WithMany(p => p.Vouchers)
                .HasForeignKey(d => d.BookingId)
                .HasConstraintName("fk_vc_booking");

            entity.HasOne(d => d.Promotion).WithMany(p => p.Vouchers)
                .HasForeignKey(d => d.PromotionId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_vc_promo");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
