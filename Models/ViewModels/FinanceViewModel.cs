//Models/ViewModels/FinanceViewModel.cs
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace StayMate_Kavinvee_0576.Models.ViewModels;

// ---------------------------------------------------------------
// Shared
// ---------------------------------------------------------------
internal static class FinanceJson
{
    // camelCase เพื่อให้ตรงกับ finance.js (label, income, net ...)
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

public class PagedList<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}

public class BookingRoomLine
{
    public string RoomTypeName { get; set; } = "";
    public string? RoomNumber { get; set; }
    public decimal PricePerNight { get; set; }
    public int Nights { get; set; }
    public int ExtraBedQty { get; set; }
    public decimal ExtraBedAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public class BookingPromotionLine
{
    public string PromoCode { get; set; } = "";
    public string PromoName { get; set; } = "";
    public string StackRole { get; set; } = "";
    public decimal AppliedPercent { get; set; }
    public decimal AppliedAmount { get; set; }
}

public class DailyIncomeRow
{
    public DateOnly Date { get; set; }
    public string Label => Date.ToString("dd/MM");           // [เพิ่ม] ให้ chart ใน JS ใช้
    public decimal Income { get; set; }
    public decimal Refund { get; set; }
    public decimal Expense { get; set; }
    public decimal Net => Income - Refund;
}

// [เพิ่ม] รายสัปดาห์ (ใช้กับกราฟหน้า Reports)
public class WeeklyIncomeRow
{
    public DateOnly WeekStart { get; set; }
    public string Label => $"{WeekStart:dd/MM}-{WeekStart.AddDays(6):dd/MM}";
    public decimal Income { get; set; }
    public decimal Refund { get; set; }
    public decimal Expense { get; set; }
    public decimal Net => Income - Refund;
}

public class MonthlyIncomeRow
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string Label => $"{Year}-{Month:D2}";
    public decimal Income { get; set; }
    public decimal Refund { get; set; }
    public decimal Expense { get; set; }
    public decimal Net => Income - Refund;
}

// [เพิ่ม] ใช้กับ Donut chart (รายรับ/รายจ่ายตามหมวด)
public class BreakdownItem
{
    public string Label { get; set; } = "";
    public decimal Amount { get; set; }
    public string Color { get; set; } = "#64748B";
}

// ---------------------------------------------------------------
// 1. Dashboard
// ---------------------------------------------------------------
public class FinanceDashboardViewModel
{
    public decimal TotalIncome { get; set; }      // รายรับสุทธิทั้งหมด (income - refund)
    public decimal TodayIncome { get; set; }
    public decimal MonthIncome { get; set; }

    // [เพิ่ม] spec ระบุให้แสดง "สรุปรายจ่ายทั้งหมด"
    public decimal TotalExpense { get; set; }
    public decimal MonthExpense { get; set; }
    public decimal NetProfit => TotalIncome - TotalExpense;

    public int PendingCount { get; set; }
    public decimal PendingAmount { get; set; }

    public int ApprovedCount { get; set; }
    public decimal ApprovedAmount { get; set; }

    public int RejectedCount { get; set; }
    public decimal RejectedAmount { get; set; }

    public List<DailyIncomeRow> Last7Days { get; set; } = new();
    public List<MonthlyIncomeRow> Last6Months { get; set; } = new();
    public List<PaymentListItem> OldestPending { get; set; } = new();

    /// <summary>[เพิ่ม] JSON สำหรับ &lt;script id="financeDashboardData"&gt; ใน Index.cshtml: @Html.Raw(Model.ChartDataJson)</summary>
    public string ChartDataJson => JsonSerializer.Serialize(new
    {
        last7Days = Last7Days,
        last6Months = Last6Months,
        monthIncome = MonthIncome,
        todayIncome = TodayIncome,
        totalIncome = TotalIncome,
        statusCounts = new { pending = PendingCount, approved = ApprovedCount, rejected = RejectedCount },
        statusAmounts = new { pending = PendingAmount, approved = ApprovedAmount, rejected = RejectedAmount }
    }, FinanceJson.Options);
}

// ---------------------------------------------------------------
// 2. Payment Approval
// ---------------------------------------------------------------
public class PaymentListItem
{
    public uint PaymentId { get; set; }
    public uint BookingId { get; set; }
    public string BookingCode { get; set; } = "";
    public string ContactName { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal BookingTotal { get; set; }
    public string Method { get; set; } = "";
    public string? SlipImagePath { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = "";
}

public class PaymentApprovalViewModel
{
    public string? Keyword { get; set; }
    public string? Status { get; set; } = "pending";   // pending / approved / rejected / all
    public PagedList<PaymentListItem> Payments { get; set; } = new();
}

public class PaymentReviewViewModel
{
    // Payment
    public uint PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = "";
    public string? SlipImagePath { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = "";
    public string? RejectReason { get; set; }
    public string? ReviewedByName { get; set; }
    public DateTime? ReviewedAt { get; set; }

    // Booking
    public uint BookingId { get; set; }
    public string BookingCode { get; set; } = "";
    public string BookingStatus { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string ContactPhone { get; set; } = "";
    public DateOnly CheckInDate { get; set; }
    public DateOnly CheckOutDate { get; set; }
    public int Nights { get; set; }
    public int NumGuests { get; set; }
    public decimal RoomAmount { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }

    // Amount check
    public decimal AlreadyPaid { get; set; }      // ยอดที่อนุมัติไปแล้วของ booking นี้
    public decimal AmountDue { get; set; }        // TotalAmount - AlreadyPaid
    public decimal Difference => Amount - AmountDue;
    public bool AmountMatches => Amount == AmountDue;

    // Guard
    public bool CanApprove { get; set; }
    public bool CanReject { get; set; }
    public string? BlockReason { get; set; }

    public List<BookingRoomLine> Rooms { get; set; } = new();
    public List<BookingPromotionLine> Promotions { get; set; } = new();
}

public class RejectPaymentInput
{
    public uint PaymentId { get; set; }

    [Required(ErrorMessage = "กรุณาระบุเหตุผลในการปฏิเสธ")]
    [StringLength(255, ErrorMessage = "เหตุผลต้องไม่เกิน 255 ตัวอักษร")]
    public string Reason { get; set; } = "";
}

// ---------------------------------------------------------------
// 3. Transaction History
// ---------------------------------------------------------------
public class TransactionRow
{
    public uint TxnId { get; set; }
    public DateOnly TxnDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public string TxnType { get; set; } = "";      // income / expense / refund
    public string Category { get; set; } = "";
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public uint? PaymentId { get; set; }
    public string? PaymentStatus { get; set; }
    public string? Method { get; set; }            // [เพิ่ม] ช่องทางชำระเงิน (ใช้ใน Modal/CSV)
    public uint? BookingId { get; set; }
    public string? BookingCode { get; set; }
    public string? RecordedByName { get; set; }
}

public class TransactionsViewModel
{
    public string? Keyword { get; set; }           // Transaction ID / Booking code / category / description
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? TxnType { get; set; }           // income / expense / refund / null = all

    public decimal TotalIncome { get; set; }
    public decimal TotalRefund { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal Net => TotalIncome - TotalRefund;

    public PagedList<TransactionRow> Transactions { get; set; } = new();
}

// ---------------------------------------------------------------
// 4. Receipt Management
// ---------------------------------------------------------------
public class ReceiptRow
{
    public uint ReceiptId { get; set; }
    public string ReceiptNo { get; set; } = "";
    public uint PaymentId { get; set; }
    public uint BookingId { get; set; }
    public string BookingCode { get; set; } = "";
    public string ContactName { get; set; } = "";
    public decimal Amount { get; set; }
    public string Method { get; set; } = "";
    public DateTime IssuedAt { get; set; }
    public string? IssuedByName { get; set; }
}

public class ReceiptsViewModel
{
    public string? Keyword { get; set; }           // Receipt no / booking code / name
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public PagedList<ReceiptRow> Receipts { get; set; } = new();
}

public class ReceiptDetailViewModel
{
    public uint ReceiptId { get; set; }
    public string ReceiptNo { get; set; } = "";
    public DateTime IssuedAt { get; set; }
    public string? IssuedByName { get; set; }

    public uint PaymentId { get; set; }
    public string Method { get; set; } = "";
    public DateTime? PaidAt { get; set; }
    public decimal Amount { get; set; }

    public uint BookingId { get; set; }
    public string BookingCode { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string ContactPhone { get; set; } = "";
    public DateOnly CheckInDate { get; set; }
    public DateOnly CheckOutDate { get; set; }
    public int Nights { get; set; }

    public decimal RoomAmount { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }

    public List<BookingRoomLine> Rooms { get; set; } = new();
    public List<BookingPromotionLine> Promotions { get; set; } = new();
}

// ---------------------------------------------------------------
// 5. Financial Reports
// ---------------------------------------------------------------
public class RoomTypeRevenueRow
{
    public string RoomTypeName { get; set; } = "";
    public int Rooms { get; set; }
    public int Nights { get; set; }
    public decimal Revenue { get; set; }
}

public class PromotionDiscountRow
{
    public string PromoCode { get; set; } = "";
    public string PromoName { get; set; } = "";
    public int UsedCount { get; set; }
    public decimal TotalDiscount { get; set; }
}

public class ReportsViewModel
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }

    public decimal TotalIncome { get; set; }
    public decimal TotalRefund { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal NetIncome => TotalIncome - TotalRefund;
    public decimal NetProfit => NetIncome - TotalExpense;   // [เพิ่ม] ผลประกอบการ
    public decimal TotalDiscount { get; set; }

    public List<DailyIncomeRow> Daily { get; set; } = new();
    public List<WeeklyIncomeRow> Weekly { get; set; } = new();          // [เพิ่ม]
    public List<MonthlyIncomeRow> Monthly { get; set; } = new();
    public List<BreakdownItem> RevenueBreakdown { get; set; } = new();  // [เพิ่ม]
    public List<BreakdownItem> ExpenseBreakdown { get; set; } = new();  // [เพิ่ม]
    public List<RoomTypeRevenueRow> RoomTypes { get; set; } = new();
    public List<PromotionDiscountRow> Promotions { get; set; } = new();
    public List<TransactionRow> Transactions { get; set; } = new();

    /// <summary>[เพิ่ม] JSON สำหรับ &lt;script id="frepChartDataJson"&gt; ใน Reports.cshtml: @Html.Raw(Model.ChartDataJson)</summary>
    public string ChartDataJson => JsonSerializer.Serialize(new
    {
        daily = Daily.Select(d => new { label = d.Label, income = d.Net, expense = d.Expense }),
        weekly = Weekly.Select(w => new { label = w.Label, income = w.Net, expense = w.Expense }),
        monthly = Monthly.Select(m => new { label = m.Label, income = m.Net, expense = m.Expense }),
        revenueBreakdown = RevenueBreakdown,
        expenseBreakdown = ExpenseBreakdown,
        totals = new
        {
            grossRevenue = TotalIncome,
            totalRevenue = NetIncome,
            totalExpenses = TotalExpense,
            totalRefund = TotalRefund,
            netProfit = NetProfit
        }
    }, FinanceJson.Options);
}