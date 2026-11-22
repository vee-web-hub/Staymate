namespace StayMate_Kavinvee_0576.Models.ViewModels;

public static class OwnerReportNotes
{
    public const string NetProfitDisclaimer =
        "Net Profit คำนวณจาก Total Revenue − Total Expenses ตามข้อมูลที่บันทึกในระบบเท่านั้น " +
        "ไม่ใช่กำไรสุทธิทางบัญชีที่ผ่านการปิดงบแล้ว และอาจยังไม่รวมต้นทุนที่ไม่ได้บันทึกใน FinancialTransaction";
}

public class MonthlyAmountPoint
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class PeriodComparison
{
    public string CurrentLabel { get; set; } = string.Empty;
    public string PreviousLabel { get; set; } = string.Empty;
    public decimal CurrentValue { get; set; }
    public decimal PreviousValue { get; set; }
    public decimal Difference { get; set; }
    /// <summary>null เมื่อค่าก่อนหน้าเป็น 0 (คำนวณเปอร์เซ็นต์ไม่ได้)</summary>
    public decimal? PercentChange { get; set; }
}

public class CategoryAmount
{
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Percent { get; set; }
}

/// <summary>แถวรายงานผลประกอบการ ใช้ร่วมกันทั้งรายเดือน (Month != null) และรายปี (Month == null)</summary>
public class PeriodReportRow
{
    public int Year { get; set; }
    public int? Month { get; set; }
    public string Label { get; set; } = string.Empty;
    public string MonthName => Label;
    public decimal Revenue { get; set; }
    public decimal Expense { get; set; }
    public decimal Expenses => Expense;
    public decimal Profit { get; set; }
    public decimal NetProfit => Profit;
    public int Bookings { get; set; }
    public int OccupiedRoomNights { get; set; }
    public int AvailableRoomNights { get; set; }
    public decimal OccupancyRate { get; set; }
}

public class CountByLabel
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class StaffRoleRow
{
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Active { get; set; }
    public int Inactive { get; set; }
    public int UnknownStatus { get; set; }
}

/// <summary>
/// ViewModel หลักสำหรับหน้า Owner Executive Dashboard (Index.cshtml)
/// </summary>
public class OwnerViewModel
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string CurrentPeriodLabel { get; set; } = string.Empty;
    public List<int> AvailableYears { get; set; } = new();

    // ───── KPI ─────
    public decimal TotalRevenue { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal TotalExpenses => TotalExpense;
    public decimal NetProfit { get; set; }
    public decimal OccupancyRate { get; set; }
    public int TotalBookings { get; set; }
    public decimal AverageRevenuePerBooking { get; set; }

    // ───── Occupancy Analytics ─────
    public decimal OccupiedRoomRate { get; set; }
    public decimal AvailableRoomRate { get; set; }
    public int OccupiedRoomNights { get; set; }
    public int AvailableRoomNights { get; set; }
    public int ActiveRoomCount { get; set; }
    public List<CategoryAmount> OccupancyBreakdown { get; set; } = new();

    // ───── Revenue Analytics ─────
    public List<MonthlyAmountPoint> MonthlyRevenue { get; set; } = new();
    public List<MonthlyAmountPoint> MonthlyExpense { get; set; } = new();
    public List<MonthlyAmountPoint> MonthlyProfit { get; set; } = new();

    // ───── Performance Comparison ─────
    public PeriodComparison RevenueComparison { get; set; } = new();
    public PeriodComparison ExpenseComparison { get; set; } = new();
    public PeriodComparison ProfitComparison { get; set; } = new();

    // ───── Monthly Reports ─────
    public List<PeriodReportRow> MonthlyReports { get; set; } = new();

    // ───── Executive Summary & Notes ─────
    public string BusinessStatusLabel { get; set; } = string.Empty;
    public string BusinessStatusTone { get; set; } = "neutral";
    public string NetProfitDisclaimer { get; set; } = OwnerReportNotes.NetProfitDisclaimer;
    public List<string> DataNotes { get; set; } = new();
}

/// <summary>
/// คงไว้เพื่อความเข้ากันได้หากส่วนอื่นในโปรเจกต์อ้างอิงชื่อ OwnerDashboardViewModel
/// </summary>
public class OwnerDashboardViewModel : OwnerViewModel
{
}

public class FinancialTransactionRow
{
    public DateTime TransactionDate { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class OwnerFinancialOverviewViewModel
{
    public int Year { get; set; }
    public string FilterPreset { get; set; } = "year";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string PeriodLabel { get; set; } = string.Empty;
    public List<int> AvailableYears { get; set; } = new();

    /// <summary>รายรับสุทธิ (หลังหัก refund)</summary>
    public decimal TotalIncome { get; set; }
    public decimal TotalRevenue => TotalIncome;
    public decimal GrossIncome { get; set; }
    public decimal TotalRefunds { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal OperatingCosts { get; set; }
    public decimal NetProfit { get; set; }

    /// <summary>อัตรากำไรสุทธิ (%) = NetProfit / TotalIncome * 100 (เป็น null เมื่อ TotalIncome &lt;= 0)</summary>
    public decimal? ProfitMargin { get; set; }

    public List<CategoryAmount> RevenueBreakdown { get; set; } = new();
    public List<CategoryAmount> ExpenseByCategory { get; set; } = new();

    /// <summary>กราฟรายเดือนแสดงทั้งปี Year เสมอ ไม่ขึ้นกับ Preset</summary>
    public List<MonthlyAmountPoint> IncomeByMonth { get; set; } = new();
    public List<MonthlyAmountPoint> ExpenseByMonth { get; set; } = new();
    public List<MonthlyAmountPoint> ProfitByMonth { get; set; } = new();

    public PeriodComparison RevenueComparison { get; set; } = new();
    public PeriodComparison ExpenseComparison { get; set; } = new();
    public PeriodComparison ProfitComparison { get; set; } = new();

    public List<FinancialTransactionRow> Transactions { get; set; } = new();

    public string NetProfitDisclaimer { get; set; } = OwnerReportNotes.NetProfitDisclaimer;
    public List<string> DataNotes { get; set; } = new();
}

public class OwnerPerformanceReportsViewModel
{
    public int Year { get; set; }
    public int Month { get; set; }

    public List<PeriodReportRow> MonthlyReports { get; set; } = new();
    public List<PeriodReportRow> YearlyReports { get; set; } = new();

    public PeriodComparison RevenueComparison { get; set; } = new();
    public PeriodComparison ExpenseComparison { get; set; } = new();
    public PeriodComparison ProfitComparison { get; set; } = new();

    public string NetProfitDisclaimer { get; set; } = OwnerReportNotes.NetProfitDisclaimer;
    public List<string> DataNotes { get; set; } = new();
}

public class OwnerStaffOverviewViewModel
{
    public int TotalStaff { get; set; }
    public int ActiveStaff { get; set; }
    public int InactiveStaff { get; set; }
    public int UnknownStatusStaff { get; set; }

    public int? SuspendedStaff { get; set; }
    public int? ResignedStaff { get; set; }

    public List<StaffRoleRow> StaffByRole { get; set; } = new();
    public List<CountByLabel> StaffByStatus { get; set; } = new();

    public List<StaffDirectoryRow> StaffDirectory { get; set; } = new();   // ← เพิ่มบรรทัดนี้

    public List<string> DataNotes { get; set; } = new();
}
public class OwnerProfileViewModel
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
public class StaffDirectoryRow
{
    public uint UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public List<string> RoleCodes { get; set; } = new();
    public List<string> RoleNames { get; set; } = new();
    public bool? IsActive { get; set; }
}