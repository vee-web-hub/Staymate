using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayMate_Kavinvee_0576.Models;
using StayMate_Kavinvee_0576.Models.ViewModels;

namespace StayMate_Kavinvee_0576.Controllers
{
    /// <summary>
    /// Owner Portal: หน้ารายงานทั้งหมดเป็น GET แบบ Read-only (AsNoTracking)
    /// มีเพียง UpdateProfile / ChangePassword ที่เขียนข้อมูลของบัญชี OWNER เอง
    /// (ไม่แตะข้อมูลการปฏิบัติงาน ราคา โปรโมชั่น หรือบัญชีผู้ใช้อื่น)
    /// </summary>
    [Authorize(Roles = "OWNER")]
    public class OwnerController : Controller
    {
        private const string PaymentApproved = "approved";
        private const string TxnIncome = "income";
        private const string TxnExpense = "expense";
        private const string TxnRefund = "refund";
        private const string BookingCheckedIn = "checked_in";
        private const string OwnerRoleCode = "OWNER";
        private const int MinPasswordLength = 8;

        private readonly StayMateDbContext _db;
        private readonly ILogger<OwnerController> _logger;

        public OwnerController(StayMateDbContext db, ILogger<OwnerController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ───────────────────────── Pages (GET) ─────────────────────────

        // GET: /Owner?year=2026&month=11
        [HttpGet]
        public async Task<IActionResult> Index(int? year, int? month, CancellationToken cancellationToken)
        {
            ViewData["Title"] = "Executive Dashboard";
            var model = await BuildDashboardAsync(year, month, cancellationToken);
            return View(model);
        }

        // GET: /Owner/FinancialOverview?year=2026&preset=year&startDate=2026-01-01&endDate=2026-12-31
        [HttpGet]
        public async Task<IActionResult> FinancialOverview(
            int? year,
            string? preset,
            DateOnly? startDate,
            DateOnly? endDate,
            CancellationToken cancellationToken)
        {
            ViewData["Title"] = "Financial Overview";
            return View(await BuildFinancialOverviewAsync(year, preset, startDate, endDate, cancellationToken));
        }

        // GET: /Owner/PerformanceReports?year=2026&month=11
        [HttpGet]
        public async Task<IActionResult> PerformanceReports(int? year, int? month, CancellationToken cancellationToken)
        {
            ViewData["Title"] = "Performance Reports";
            return View(await BuildPerformanceReportsAsync(year, month, cancellationToken));
        }

        // GET: /Owner/StaffOverview
        [HttpGet]
        public async Task<IActionResult> StaffOverview(CancellationToken cancellationToken)
        {
            ViewData["Title"] = "Staff Overview";
            return View(await BuildStaffOverviewAsync(cancellationToken));
        }

        // GET: /Owner/Profile
        [HttpGet]
        public async Task<IActionResult> Profile(CancellationToken cancellationToken)
        {
            ViewData["Title"] = "My Profile";

            var model = await BuildOwnerProfileAsync(GetCurrentUserId(), User.Identity?.Name, cancellationToken);
            if (model is null)
            {
                return NotFound();
            }

            return View(model);
        }

        // ───────────────────────── Profile (POST) ─────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(string fullName, string? phone, CancellationToken cancellationToken)
        {
            try
            {
                var owner = await GetValidOwnerAsync(cancellationToken);
                if (owner == null) return RedirectToAction("Login", "Account");

                var cleanName = (fullName ?? "").Trim();
                var cleanPhone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

                if (cleanName.Length == 0)
                {
                    TempData["ErrorMessage"] = "กรุณากรอกชื่อ";
                    return RedirectToAction(nameof(Profile));
                }
                if (cleanName.Length > 120) cleanName = cleanName.Substring(0, 120);

                if (cleanPhone != null && !Regex.IsMatch(cleanPhone, @"^[0-9+\-\s]{6,20}$"))
                {
                    TempData["ErrorMessage"] = "รูปแบบเบอร์โทรศัพท์ไม่ถูกต้อง";
                    return RedirectToAction(nameof(Profile));
                }

                var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == owner.Value, cancellationToken);
                if (user == null) return RedirectToAction("Login", "Account");

                user.FullName = cleanName;
                user.Phone = cleanPhone;
                await _db.SaveChangesAsync(cancellationToken);

                TempData["SuccessMessage"] = "บันทึกข้อมูลส่วนตัวเรียบร้อยแล้ว";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Owner UpdateProfile failed");
                TempData["ErrorMessage"] = "เกิดข้อผิดพลาดในการบันทึกข้อมูล กรุณาลองใหม่อีกครั้ง";
            }

            return RedirectToAction(nameof(Profile));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(
            string oldPassword, string newPassword, string confirmPassword, CancellationToken cancellationToken)
        {
            try
            {
                var owner = await GetValidOwnerAsync(cancellationToken);
                if (owner == null) return RedirectToAction("Login", "Account");

                if (string.IsNullOrWhiteSpace(oldPassword) ||
                    string.IsNullOrWhiteSpace(newPassword) ||
                    newPassword.Length < MinPasswordLength)
                {
                    TempData["ErrorMessage"] = $"กรุณากรอกข้อมูลให้ครบ และรหัสผ่านใหม่ต้องมีอย่างน้อย {MinPasswordLength} ตัวอักษร";
                    TempData["OpenPasswordModal"] = true;
                    return RedirectToAction(nameof(Profile));
                }

                if (newPassword != confirmPassword)
                {
                    TempData["ErrorMessage"] = "รหัสผ่านใหม่และการยืนยันรหัสผ่านไม่ตรงกัน";
                    TempData["OpenPasswordModal"] = true;
                    return RedirectToAction(nameof(Profile));
                }

                if (newPassword == oldPassword)
                {
                    TempData["ErrorMessage"] = "รหัสผ่านใหม่ต้องไม่ซ้ำกับรหัสผ่านเดิม";
                    TempData["OpenPasswordModal"] = true;
                    return RedirectToAction(nameof(Profile));
                }

                var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == owner.Value, cancellationToken);
                if (user == null) return RedirectToAction("Login", "Account");

                if (!BCrypt.Net.BCrypt.Verify(oldPassword, user.PasswordHash))
                {
                    TempData["ErrorMessage"] = "รหัสผ่านเดิมไม่ถูกต้อง";
                    TempData["OpenPasswordModal"] = true;
                    return RedirectToAction(nameof(Profile));
                }

                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
                await _db.SaveChangesAsync(cancellationToken);

                TempData["SuccessMessage"] = "เปลี่ยนรหัสผ่านเรียบร้อยแล้ว";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Owner ChangePassword failed");
                TempData["ErrorMessage"] = "เกิดข้อผิดพลาดในการเปลี่ยนรหัสผ่าน กรุณาลองใหม่อีกครั้ง";
                TempData["OpenPasswordModal"] = true;
            }

            return RedirectToAction(nameof(Profile));
        }

        // ───────────────────────── JSON endpoints (GET) ─────────────────────────

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> DashboardData(int? year, int? month, CancellationToken cancellationToken)
            => Json(await BuildDashboardAsync(year, month, cancellationToken));

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> FinancialData(
            int? year,
            string? preset,
            DateOnly? startDate,
            DateOnly? endDate,
            CancellationToken cancellationToken)
            => Json(await BuildFinancialOverviewAsync(year, preset, startDate, endDate, cancellationToken));

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> PerformanceData(int? year, int? month, CancellationToken cancellationToken)
            => Json(await BuildPerformanceReportsAsync(year, month, cancellationToken));

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> StaffData(CancellationToken cancellationToken)
            => Json(await BuildStaffOverviewAsync(cancellationToken));

        // ───────────────────────── Builders ─────────────────────────

        private async Task<OwnerViewModel> BuildDashboardAsync(int? year, int? month, CancellationToken ct)
        {
            var y = ResolveYear(year);
            var m = ResolveMonth(y, month);
            var today = Today;

            var firstYear = await GetFirstDataYearAsync(ct) ?? today.Year;
            firstYear = Math.Min(firstYear, y);

            var availableYears = new List<int>();
            for (var yr = today.Year; yr >= firstYear; yr--)
            {
                availableYears.Add(yr);
            }

            var yearFrom = new DateTime(y, 1, 1);
            var yearTo = yearFrom.AddYears(1);
            var curStart = new DateTime(y, m, 1);
            var curEnd = curStart.AddMonths(1);
            var prevStart = curStart.AddMonths(-1);

            // โหลดตั้งแต่เดือนธันวาคมปีก่อน เพื่อใช้เทียบเดือนมกราคม
            var data = await LoadPeriodDataAsync(yearFrom.AddMonths(-1), yearTo, ct);

            var monthlyRows = BuildMonthlyRows(data, y);
            var cur = Summarize(data, curStart, curEnd);
            var prev = Summarize(data, prevStart, curStart);

            var occupiedRate = cur.OccupancyRate;
            var availableRate = cur.AvailableRoomNights > 0
                ? Math.Round(Math.Max(0m, 100m - occupiedRate), 2)
                : 0m;

            string statusLabel;
            string statusTone;
            if (cur.Revenue == 0m && cur.Expense == 0m && cur.Bookings == 0)
            {
                statusLabel = $"ยังไม่มีรายการธุรกรรมในเดือน {MonthLabel(curStart)}";
                statusTone = "neutral";
            }
            else if (cur.Profit > 0m)
            {
                statusLabel = "ผลประกอบการเป็นกำไร (Profitable Operation)";
                statusTone = "positive";
            }
            else if (cur.Profit < 0m)
            {
                statusLabel = "ผลประกอบการขาดทุน (Operating Deficit)";
                statusTone = "negative";
            }
            else
            {
                statusLabel = "ผลประกอบการเท่าทุน (Break-Even)";
                statusTone = "warning";
            }

            return new OwnerViewModel
            {
                Year = y,
                Month = m,
                CurrentPeriodLabel = MonthLabel(curStart),
                AvailableYears = availableYears,

                // KPI Cards แสดงตามเดือนที่เลือก
                TotalRevenue = cur.Revenue,
                TotalExpense = cur.Expense,
                NetProfit = cur.Profit,
                OccupancyRate = occupiedRate,
                TotalBookings = cur.Bookings,
                AverageRevenuePerBooking = cur.AverageRevenuePerBooking,

                // Occupancy Donut แสดงตามเดือนที่เลือก
                OccupiedRoomRate = occupiedRate,
                AvailableRoomRate = availableRate,
                OccupiedRoomNights = cur.OccupiedRoomNights,
                AvailableRoomNights = cur.AvailableRoomNights,
                ActiveRoomCount = data.ActiveRoomCount,
                OccupancyBreakdown = new List<CategoryAmount>
                {
                    new()
                    {
                        Category = "ห้องที่มีผู้เข้าพัก (Occupied)",
                        Amount = occupiedRate,
                        Percent = occupiedRate
                    },
                    new()
                    {
                        Category = "ห้องว่างพร้อมให้บริการ (Available)",
                        Amount = availableRate,
                        Percent = availableRate
                    }
                },

                // กราฟและตารางรายเดือนแสดงแนวโน้มทั้งปีที่เลือก
                MonthlyRevenue = monthlyRows.Select(r => ToPoint(r, r.Revenue)).ToList(),
                MonthlyExpense = monthlyRows.Select(r => ToPoint(r, r.Expense)).ToList(),
                MonthlyProfit = monthlyRows.Select(r => ToPoint(r, r.Profit)).ToList(),

                RevenueComparison = Compare(MonthLabel(curStart), cur.Revenue, MonthLabel(prevStart), prev.Revenue),
                ExpenseComparison = Compare(MonthLabel(curStart), cur.Expense, MonthLabel(prevStart), prev.Expense),
                ProfitComparison = Compare(MonthLabel(curStart), cur.Profit, MonthLabel(prevStart), prev.Profit),

                MonthlyReports = monthlyRows,
                BusinessStatusLabel = statusLabel,
                BusinessStatusTone = statusTone,
                DataNotes = BuildCommonNotes()
            };
        }

        private async Task<OwnerFinancialOverviewViewModel> BuildFinancialOverviewAsync(
            int? year,
            string? preset,
            DateOnly? startDate,
            DateOnly? endDate,
            CancellationToken ct)
        {
            var today = Today;
            var y = ResolveYear(year);
            var firstYear = await GetFirstDataYearAsync(ct) ?? today.Year;
            firstYear = Math.Min(firstYear, y);

            var availableYears = new List<int>();
            for (var yr = today.Year; yr >= firstYear; yr--)
            {
                availableYears.Add(yr);
            }

            var mode = string.IsNullOrWhiteSpace(preset) ? "year" : preset.Trim().ToLowerInvariant();
            DateTime periodFrom;
            DateTime periodToExclusive;
            DateTime prevFrom;
            DateTime prevToExclusive;
            string periodLabel;
            string prevLabel;

            if (mode == "today")
            {
                periodFrom = today;
                periodToExclusive = today.AddDays(1);
                prevFrom = today.AddDays(-1);
                prevToExclusive = today;
                periodLabel = $"วันนี้ ({DateLabel(today)})";
                prevLabel = $"เมื่อวาน ({DateLabel(prevFrom)})";
            }
            else if (mode == "month")
            {
                periodFrom = new DateTime(today.Year, today.Month, 1);
                periodToExclusive = periodFrom.AddMonths(1);
                prevFrom = periodFrom.AddMonths(-1);
                prevToExclusive = periodFrom;
                periodLabel = $"เดือนนี้ ({MonthLabel(periodFrom)})";
                prevLabel = $"เดือนก่อน ({MonthLabel(prevFrom)})";
            }
            else if (mode == "custom" && startDate.HasValue && endDate.HasValue && endDate.Value >= startDate.Value)
            {
                periodFrom = startDate.Value.ToDateTime(TimeOnly.MinValue);
                periodToExclusive = endDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
                var spanDays = Math.Max(1, (periodToExclusive - periodFrom).Days);
                prevFrom = periodFrom.AddDays(-spanDays);
                prevToExclusive = periodFrom;
                periodLabel = $"{DateLabel(periodFrom)} – {DateLabel(endDate.Value.ToDateTime(TimeOnly.MinValue))}";
                prevLabel = $"ช่วงก่อนหน้า ({spanDays} วัน)";
                y = Math.Clamp(startDate.Value.Year, 2000, today.Year);
            }
            else
            {
                mode = "year";
                periodFrom = new DateTime(y, 1, 1);
                periodToExclusive = periodFrom.AddYears(1);
                prevFrom = periodFrom.AddYears(-1);

                if (y == today.Year)
                {
                    prevToExclusive = today.AddDays(1).AddYears(-1);
                    periodLabel = $"ประจำปี {y} (ถึงปัจจุบัน)";
                    prevLabel = $"ช่วงเดียวกันของปี {y - 1}";
                }
                else
                {
                    prevToExclusive = periodFrom;
                    periodLabel = $"ประจำปี {y}";
                    prevLabel = $"ปี {y - 1}";
                }
            }

            var yearFrom = new DateTime(y, 1, 1);
            var yearTo = yearFrom.AddYears(1);

            var loadFrom = new[] { periodFrom, prevFrom, yearFrom }.Min();
            var loadTo = new[] { periodToExclusive, prevToExclusive, yearTo }.Max();

            var data = await LoadPeriodDataAsync(loadFrom, loadTo, ct);
            var curSummary = Summarize(data, periodFrom, periodToExclusive);
            var prevSummary = Summarize(data, prevFrom, prevToExclusive);
            var monthlyRows = BuildMonthlyRows(data, y);

            var pFromDate = DateOnly.FromDateTime(periodFrom);
            var pToDate = DateOnly.FromDateTime(periodToExclusive);

            var approvedPayments = data.Income
                .Where(r => r.PaymentId.HasValue && r.Date >= periodFrom && r.Date < periodToExclusive)
                .OrderByDescending(r => r.Date)
                .ToList();

            var finTxns = await _db.FinancialTransactions
                .AsNoTracking()
                .Where(t => t.TxnDate >= pFromDate && t.TxnDate < pToDate)
                .Select(t => new
                {
                    t.TxnId,
                    t.TxnType,
                    t.Category,
                    t.Description,
                    t.Amount,
                    t.TxnDate,
                    t.PaymentId
                })
                .OrderByDescending(t => t.TxnDate)
                .ThenByDescending(t => t.TxnId)
                .ToListAsync(ct);

            var roomPaymentSum = approvedPayments.Sum(p => p.Amount);
            var manualIncomeSum = finTxns.Where(t => t.TxnType == TxnIncome && t.PaymentId == null).Sum(t => t.Amount);
            var grossTotal = roomPaymentSum + manualIncomeSum;

            var revenueBreakdown = new List<CategoryAmount>();
            if (roomPaymentSum > 0)
            {
                revenueBreakdown.Add(new CategoryAmount
                {
                    Category = "รายได้ชำระค่าห้องพัก (Approved Payments)",
                    Amount = roomPaymentSum,
                    Percent = grossTotal > 0 ? Math.Round(roomPaymentSum * 100m / grossTotal, 2) : 0m
                });
            }

            var manualIncomeByCategory = finTxns
                .Where(t => t.TxnType == TxnIncome && t.PaymentId == null)
                .GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "รายรับอื่นๆ (ไม่ระบุหมวด)" : t.Category!.Trim())
                .Select(g =>
                {
                    var amount = g.Sum(x => x.Amount);
                    return new CategoryAmount
                    {
                        Category = g.Key,
                        Amount = amount,
                        Percent = grossTotal > 0 ? Math.Round(amount * 100m / grossTotal, 2) : 0m
                    };
                })
                .Where(c => c.Amount > 0)
                .OrderByDescending(c => c.Amount);
            revenueBreakdown.AddRange(manualIncomeByCategory);

            var expenseRows = data.Expenses.Where(e => e.Date >= periodFrom && e.Date < periodToExclusive).ToList();
            var totalExpense = expenseRows.Sum(e => e.Amount);

            var byCategory = expenseRows
                .GroupBy(e => string.IsNullOrWhiteSpace(e.Category) ? "(ไม่ระบุหมวด)" : e.Category.Trim())
                .Select(g =>
                {
                    var amount = g.Sum(x => x.Amount);
                    return new CategoryAmount
                    {
                        Category = g.Key,
                        Amount = amount,
                        Percent = totalExpense > 0 ? Math.Round(amount * 100m / totalExpense, 2) : 0m
                    };
                })
                .OrderByDescending(c => c.Amount)
                .ToList();

            var txRows = new List<FinancialTransactionRow>();

            foreach (var p in approvedPayments)
            {
                txRows.Add(new FinancialTransactionRow
                {
                    TransactionDate = p.Date,
                    TransactionId = $"PAY-{p.PaymentId!.Value:D5}",
                    Category = "Room Booking Payment",
                    Description = $"รับชำระเงินสำหรับการจอง #{p.BookingId}",
                    TransactionType = "Room Payment",
                    Amount = p.Amount,
                    Status = PaymentApproved
                });
            }

            foreach (var t in finTxns)
            {
                if (t.TxnType == TxnIncome && t.PaymentId != null)
                {
                    continue;
                }

                var displayType = t.TxnType switch
                {
                    TxnIncome => "Income",
                    TxnExpense => "Expense",
                    TxnRefund => "Refund",
                    _ => t.TxnType
                };

                txRows.Add(new FinancialTransactionRow
                {
                    TransactionDate = t.TxnDate.ToDateTime(TimeOnly.MinValue),
                    TransactionId = $"TXN-{t.TxnId:D5}",
                    Category = string.IsNullOrWhiteSpace(t.Category) ? "(ไม่ระบุหมวด)" : t.Category.Trim(),
                    Description = t.Description ?? string.Empty,
                    TransactionType = displayType,
                    Amount = t.Amount,
                    Status = "recorded"
                });
            }

            txRows = txRows
                .OrderByDescending(r => r.TransactionDate)
                .ThenByDescending(r => r.TransactionId)
                .ToList();

            decimal? profitMargin = curSummary.Revenue > 0m
                ? Math.Round(curSummary.Profit * 100m / curSummary.Revenue, 2)
                : null;

            var notes = BuildCommonNotes();
            notes.Add("OperatingCosts = รายจ่ายทั้งหมดที่บันทึกใน FinancialTransaction (txn_type = expense)");
            notes.Add($"กราฟรายเดือนแสดงทั้งปี {y} เสมอ ส่วน KPI, การเปรียบเทียบ และตารางธุรกรรมเป็นไปตามช่วงเวลาที่เลือก ({periodLabel})");

            return new OwnerFinancialOverviewViewModel
            {
                Year = y,
                FilterPreset = mode,
                StartDate = pFromDate,
                EndDate = DateOnly.FromDateTime(periodToExclusive.AddDays(-1)),
                PeriodLabel = periodLabel,
                AvailableYears = availableYears,

                TotalIncome = curSummary.Revenue,
                GrossIncome = curSummary.GrossIncome,
                TotalRefunds = curSummary.Refunds,
                TotalExpenses = curSummary.Expense,
                OperatingCosts = curSummary.Expense,
                NetProfit = curSummary.Profit,
                ProfitMargin = profitMargin,

                RevenueBreakdown = revenueBreakdown,
                ExpenseByCategory = byCategory,

                IncomeByMonth = monthlyRows.Select(r => ToPoint(r, r.Revenue)).ToList(),
                ExpenseByMonth = monthlyRows.Select(r => ToPoint(r, r.Expense)).ToList(),
                ProfitByMonth = monthlyRows.Select(r => ToPoint(r, r.Profit)).ToList(),

                RevenueComparison = Compare(periodLabel, curSummary.Revenue, prevLabel, prevSummary.Revenue),
                ExpenseComparison = Compare(periodLabel, curSummary.Expense, prevLabel, prevSummary.Expense),
                ProfitComparison = Compare(periodLabel, curSummary.Profit, prevLabel, prevSummary.Profit),

                Transactions = txRows,
                DataNotes = notes
            };
        }

        private async Task<OwnerPerformanceReportsViewModel> BuildPerformanceReportsAsync(int? year, int? month, CancellationToken ct)
        {
            var y = ResolveYear(year);
            var m = ResolveMonth(y, month);
            var today = Today;

            var firstYear = await GetFirstDataYearAsync(ct) ?? today.Year;
            firstYear = Math.Min(firstYear, y);

            var rangeFrom = new DateTime(firstYear, 1, 1);
            var rangeTo = new DateTime(today.Year + 1, 1, 1);
            var data = await LoadPeriodDataAsync(rangeFrom, rangeTo, ct);

            var monthlyRows = BuildMonthlyRows(data, y);

            var yearlyRows = new List<PeriodReportRow>();
            for (var yr = firstYear; yr <= today.Year; yr++)
            {
                var from = new DateTime(yr, 1, 1);
                yearlyRows.Add(ToRow(Summarize(data, from, from.AddYears(1)), yr, null, yr.ToString(CultureInfo.InvariantCulture)));
            }

            var curStart = new DateTime(y, m, 1);
            var curEnd = curStart.AddMonths(1);
            var prevStart = curStart.AddMonths(-1);
            var cur = Summarize(data, curStart, curEnd);
            var prev = Summarize(data, prevStart, curStart);

            return new OwnerPerformanceReportsViewModel
            {
                Year = y,
                Month = m,
                MonthlyReports = monthlyRows,
                YearlyReports = yearlyRows,
                RevenueComparison = Compare(MonthLabel(curStart), cur.Revenue, MonthLabel(prevStart), prev.Revenue),
                ExpenseComparison = Compare(MonthLabel(curStart), cur.Expense, MonthLabel(prevStart), prev.Expense),
                ProfitComparison = Compare(MonthLabel(curStart), cur.Profit, MonthLabel(prevStart), prev.Profit),
                DataNotes = BuildCommonNotes()
            };
        }

        private async Task<OwnerStaffOverviewViewModel> BuildStaffOverviewAsync(CancellationToken ct)
        {
            var rows = await _db.UserRoles
                .AsNoTracking()
                .Where(ur => ur.Role.RoleCode != "CUSTOMER" && ur.Role.RoleCode != "GUEST")
                .Select(ur => new
                {
                    ur.UserId,
                    ur.Role.RoleCode,
                    ur.Role.RoleName,
                    ur.User.IsActive
                })
                .ToListAsync(ct);

            var distinctUsers = rows
                .GroupBy(r => r.UserId)
                .Select(g => g.First().IsActive)
                .ToList();

            var active = distinctUsers.Count(s => s == true);
            var inactive = distinctUsers.Count(s => s == false);
            var unknown = distinctUsers.Count(s => s == null);

            var byRole = rows
                .GroupBy(r => new { r.RoleCode, r.RoleName })
                .Select(g =>
                {
                    var users = g.GroupBy(x => x.UserId).Select(x => x.First().IsActive).ToList();
                    return new StaffRoleRow
                    {
                        RoleCode = g.Key.RoleCode,
                        RoleName = g.Key.RoleName,
                        Total = users.Count,
                        Active = users.Count(s => s == true),
                        Inactive = users.Count(s => s == false),
                        UnknownStatus = users.Count(s => s == null)
                    };
                })
                .OrderBy(r => r.RoleCode)
                .ToList();

            var byStatus = new List<CountByLabel>
            {
                new() { Label = "Active", Count = active },
                new() { Label = "Inactive", Count = inactive }
            };
            if (unknown > 0)
            {
                byStatus.Add(new CountByLabel { Label = "Unknown", Count = unknown });
            }

            var directory = await _db.Users
                .AsNoTracking()
                .Where(u => u.UserRoles.Any(ur => ur.Role.RoleCode != "CUSTOMER" && ur.Role.RoleCode != "GUEST"))
                .OrderBy(u => u.UserId)
                .Select(u => new StaffDirectoryRow
                {
                    UserId = u.UserId,
                    FullName = u.FullName,
                    Username = u.Username,
                    IsActive = u.IsActive,
                    RoleCodes = u.UserRoles
                        .Where(ur => ur.Role.RoleCode != "CUSTOMER" && ur.Role.RoleCode != "GUEST")
                        .Select(ur => ur.Role.RoleCode)
                        .ToList(),
                    RoleNames = u.UserRoles
                        .Where(ur => ur.Role.RoleCode != "CUSTOMER" && ur.Role.RoleCode != "GUEST")
                        .Select(ur => ur.Role.RoleName)
                        .ToList()
                })
                .ToListAsync(ct);

            return new OwnerStaffOverviewViewModel
            {
                TotalStaff = distinctUsers.Count,
                ActiveStaff = active,
                InactiveStaff = inactive,
                UnknownStatusStaff = unknown,
                SuspendedStaff = null,
                ResignedStaff = null,
                StaffByRole = byRole,
                StaffByStatus = byStatus,
                StaffDirectory = directory,
                DataNotes = new List<string>
                {
                    "ฐานข้อมูลมีเพียง users.is_active จึงแยกสถานะ Suspended และ Resigned ไม่ได้ ใช้ Inactive แทน",
                    "ตำแหน่งพนักงานอ้างอิงจาก roles.role_code ผ่าน user_roles",
                    "ผู้ที่มี Role เป็น CUSTOMER หรือ GUEST ไม่ถูกนับเป็นพนักงาน"
                }
            };
        }

        private async Task<OwnerProfileViewModel?> BuildOwnerProfileAsync(uint? userId, string? userName, CancellationToken ct)
        {
            var ownerCode = OwnerRoleCode;

            var query = _db.Users
                .AsNoTracking()
                .Where(u => u.UserRoles.Any(ur => ur.Role.RoleCode == ownerCode));

            if (userId.HasValue)
            {
                var id = userId.Value;
                query = query.Where(u => u.UserId == id);
            }
            else if (!string.IsNullOrWhiteSpace(userName))
            {
                var name = userName;
                query = query.Where(u => u.Username == name || u.Email == name);
            }
            else
            {
                return null;
            }

            return await query
                .Select(u => new OwnerProfileViewModel
                {
                    UserId = u.UserId,
                    Username = u.Username,
                    Email = u.Email,
                    FullName = u.FullName,
                    Phone = u.Phone,
                    IsActive = u.IsActive,
                    CreatedAt = u.CreatedAt,
                    RoleCodes = u.UserRoles.Select(ur => ur.Role.RoleCode).ToList()
                })
                .FirstOrDefaultAsync(ct);
        }

        private uint? GetCurrentUserId()
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return uint.TryParse(raw, out var id) ? id : null;
        }

        private async Task<uint?> GetValidOwnerAsync(CancellationToken ct)
        {
            var id = GetCurrentUserId();
            if (id == null) return null;
            var uid = id.Value;

            var exists = await _db.Users
                .AsNoTracking()
                .AnyAsync(u => u.UserId == uid &&
                               u.IsActive == true &&
                               u.UserRoles.Any(ur => ur.Role.RoleCode == OwnerRoleCode), ct);

            return exists ? uid : null;
        }

        // ───────────────────────── Data loading ─────────────────────────

        private sealed record MoneyRow(DateTime Date, decimal Amount, bool IsRefund, uint? PaymentId = null, uint? BookingId = null);
        private sealed record ExpenseRow(DateTime Date, string Category, decimal Amount);
        private sealed record StayRow(uint UniqueSlotId, DateTime CheckedInAt, DateTime? CheckedOutAt, string BookingStatus, DateOnly PlannedCheckOut);

        private sealed class PeriodData
        {
            public List<MoneyRow> Income { get; init; } = new();
            public List<ExpenseRow> Expenses { get; init; } = new();
            public List<DateTime> BookingDates { get; init; } = new();
            public List<StayRow> Stays { get; init; } = new();
            public int ActiveRoomCount { get; init; }
        }

        private sealed record PeriodSummary(
            decimal GrossIncome,
            decimal Refunds,
            decimal Revenue,
            decimal Expense,
            decimal Profit,
            int Bookings,
            int OccupiedRoomNights,
            int AvailableRoomNights,
            decimal OccupancyRate,
            decimal AverageRevenuePerBooking);

        private async Task<PeriodData> LoadPeriodDataAsync(DateTime from, DateTime toExclusive, CancellationToken ct)
        {
            var fromDate = DateOnly.FromDateTime(from);
            var toDate = DateOnly.FromDateTime(toExclusive);

            // 1) เงินรับจริง: Payment ที่อนุมัติแล้ว
            var payments = await _db.Payments
                .AsNoTracking()
                .Where(p => p.Status == PaymentApproved)
                .Select(p => new { p.PaymentId, p.BookingId, Date = p.PaidAt ?? p.ReviewedAt ?? p.CreatedAt, p.Amount })
                .Where(x => x.Date >= from && x.Date < toExclusive)
                .ToListAsync(ct);

            // 2) รายรับที่บันทึกเองและไม่ผูก Payment
            var manualIncome = await _db.FinancialTransactions
                .AsNoTracking()
                .Where(t => t.TxnType == TxnIncome && t.PaymentId == null && t.TxnDate >= fromDate && t.TxnDate < toDate)
                .Select(t => new { t.TxnDate, t.Amount })
                .ToListAsync(ct);

            // 3) เงินคืน
            var refunds = await _db.FinancialTransactions
                .AsNoTracking()
                .Where(t => t.TxnType == TxnRefund && t.TxnDate >= fromDate && t.TxnDate < toDate)
                .Select(t => new { t.TxnDate, t.Amount })
                .ToListAsync(ct);

            // 4) รายจ่ายจริง
            var expenses = await _db.FinancialTransactions
                .AsNoTracking()
                .Where(t => t.TxnType == TxnExpense && t.TxnDate >= fromDate && t.TxnDate < toDate)
                .Select(t => new { t.TxnDate, t.Category, t.Amount })
                .ToListAsync(ct);

            // 5) Booking ที่มีผล (confirmed, checked_in, checked_out)
            var bookingDates = await _db.Bookings
                .AsNoTracking()
                .Where(b => (b.Status == "confirmed" || b.Status == "checked_in" || b.Status == "checked_out")
                            && b.CreatedAt >= from
                            && b.CreatedAt < toExclusive)
                .Select(b => b.CreatedAt)
                .ToListAsync(ct);

            // 6) การเข้าพัก: ดึงจาก StayRecord จริง และเสริมด้วย Booking ที่ยืนยันแล้วแต่ยังไม่ได้สร้าง StayRecord
            var stayRecords = await _db.StayRecords
                .AsNoTracking()
                .Where(s => s.Booking.Status != "cancelled"
                            && s.Booking.Status != "rejected"
                            && s.CheckedInAt < toExclusive
                            && (s.CheckedOutAt == null || s.CheckedOutAt >= from))
                .Select(s => new
                {
                    s.BookingId,
                    Row = new StayRow(s.RoomId, s.CheckedInAt, s.CheckedOutAt, s.Booking.Status, s.Booking.CheckOutDate)
                })
                .ToListAsync(ct);

            var stayedBookingIds = stayRecords.Select(x => x.BookingId).ToHashSet();

            var bookedNotCheckedIn = await _db.Bookings
                .AsNoTracking()
                .Where(b => !stayedBookingIds.Contains(b.BookingId)
                            && (b.Status == "confirmed" || b.Status == "checked_in" || b.Status == "checked_out")
                            && b.CheckInDate < toDate
                            && b.CheckOutDate > fromDate)
                .Select(b => new StayRow(
                    1000000u + b.BookingId,
                    b.CheckInDate.ToDateTime(TimeOnly.MinValue),
                    b.CheckOutDate.ToDateTime(TimeOnly.MinValue),
                    b.Status,
                    b.CheckOutDate))
                .ToListAsync(ct);

            var stays = stayRecords.Select(x => x.Row).Concat(bookedNotCheckedIn).ToList();

            // 7) จำนวนห้องที่เปิดให้บริการ
            var activeRooms = await _db.Rooms
                .AsNoTracking()
                .CountAsync(r => r.IsActive != false, ct);

            var income = new List<MoneyRow>();
            income.AddRange(payments.Select(p => new MoneyRow(p.Date, p.Amount, false, p.PaymentId, p.BookingId)));
            income.AddRange(manualIncome.Select(t => new MoneyRow(t.TxnDate.ToDateTime(TimeOnly.MinValue), t.Amount, false)));
            income.AddRange(refunds.Select(t => new MoneyRow(t.TxnDate.ToDateTime(TimeOnly.MinValue), t.Amount, true)));

            return new PeriodData
            {
                Income = income,
                Expenses = expenses.Select(t => new ExpenseRow(t.TxnDate.ToDateTime(TimeOnly.MinValue), t.Category, t.Amount)).ToList(),
                BookingDates = bookingDates,
                Stays = stays,
                ActiveRoomCount = activeRooms
            };
        }

        private async Task<int?> GetFirstDataYearAsync(CancellationToken ct)
        {
            var firstPayment = await _db.Payments.AsNoTracking()
                .OrderBy(p => p.CreatedAt).Select(p => (DateTime?)p.CreatedAt).FirstOrDefaultAsync(ct);
            var firstBooking = await _db.Bookings.AsNoTracking()
                .OrderBy(b => b.CreatedAt).Select(b => (DateTime?)b.CreatedAt).FirstOrDefaultAsync(ct);
            var firstTxn = await _db.FinancialTransactions.AsNoTracking()
                .OrderBy(t => t.TxnDate).Select(t => (DateOnly?)t.TxnDate).FirstOrDefaultAsync(ct);

            var years = new List<int>();
            if (firstPayment.HasValue) years.Add(firstPayment.Value.Year);
            if (firstBooking.HasValue) years.Add(firstBooking.Value.Year);
            if (firstTxn.HasValue) years.Add(firstTxn.Value.Year);

            return years.Count == 0 ? null : Math.Max(years.Min(), 2000);
        }

        // ───────────────────────── Calculation ─────────────────────────

        private static PeriodSummary Summarize(PeriodData d, DateTime from, DateTime toExclusive)
        {
            var gross = d.Income.Where(r => !r.IsRefund && r.Date >= from && r.Date < toExclusive).Sum(r => r.Amount);
            var refunds = d.Income.Where(r => r.IsRefund && r.Date >= from && r.Date < toExclusive).Sum(r => r.Amount);
            var revenue = gross - refunds;
            var expense = d.Expenses.Where(e => e.Date >= from && e.Date < toExclusive).Sum(e => e.Amount);
            var bookings = d.BookingDates.Count(b => b >= from && b < toExclusive);

            var (occupied, available) = CalculateRoomNights(d, from, toExclusive);
            var occupancy = available > 0
                ? Math.Round(Math.Min(100m, occupied * 100m / available), 2)
                : 0m;

            var avg = bookings > 0 ? Math.Round(revenue / bookings, 2) : 0m;

            return new PeriodSummary(gross, refunds, revenue, expense, revenue - expense,
                bookings, occupied, available, occupancy, avg);
        }

        private static (int Occupied, int Available) CalculateRoomNights(PeriodData d, DateTime from, DateTime toExclusive)
        {
            var start = DateOnly.FromDateTime(from);
            var endExclusive = DateOnly.FromDateTime(toExclusive);
            var capExclusive = DateOnly.FromDateTime(Today).AddDays(1);

            if (endExclusive > capExclusive) endExclusive = capExclusive;
            if (endExclusive <= start) return (0, 0);

            var days = endExclusive.DayNumber - start.DayNumber;
            var available = d.ActiveRoomCount * days;

            var occupiedNights = new HashSet<(uint SlotId, int Day)>();
            foreach (var s in d.Stays)
            {
                var inDate = DateOnly.FromDateTime(s.CheckedInAt);

                DateOnly outDate;
                if (s.CheckedOutAt.HasValue)
                {
                    outDate = DateOnly.FromDateTime(s.CheckedOutAt.Value);
                }
                else if (s.BookingStatus == BookingCheckedIn)
                {
                    outDate = capExclusive;
                }
                else
                {
                    outDate = s.PlannedCheckOut;
                }

                var a = inDate > start ? inDate : start;
                var b = outDate < endExclusive ? outDate : endExclusive;

                for (var day = a; day < b; day = day.AddDays(1))
                {
                    occupiedNights.Add((s.UniqueSlotId, day.DayNumber));
                }
            }

            return (occupiedNights.Count, available);
        }

        private static List<PeriodReportRow> BuildMonthlyRows(PeriodData data, int year)
        {
            var today = Today;
            var lastMonth = year == today.Year ? today.Month : 12;
            var rows = new List<PeriodReportRow>();

            for (var m = 1; m <= lastMonth; m++)
            {
                var from = new DateTime(year, m, 1);
                rows.Add(ToRow(Summarize(data, from, from.AddMonths(1)), year, m, MonthLabel(from)));
            }

            return rows;
        }

        private static PeriodReportRow ToRow(PeriodSummary s, int year, int? month, string label) => new()
        {
            Year = year,
            Month = month,
            Label = label,
            Revenue = s.Revenue,
            Expense = s.Expense,
            Profit = s.Profit,
            Bookings = s.Bookings,
            OccupiedRoomNights = s.OccupiedRoomNights,
            AvailableRoomNights = s.AvailableRoomNights,
            OccupancyRate = s.OccupancyRate
        };

        private static MonthlyAmountPoint ToPoint(PeriodReportRow row, decimal amount) => new()
        {
            Year = row.Year,
            Month = row.Month ?? 0,
            Label = row.Label,
            Amount = amount
        };

        private static PeriodComparison Compare(string curLabel, decimal cur, string prevLabel, decimal prev) => new()
        {
            CurrentLabel = curLabel,
            PreviousLabel = prevLabel,
            CurrentValue = cur,
            PreviousValue = prev,
            Difference = cur - prev,
            PercentChange = prev == 0m ? null : Math.Round((cur - prev) / Math.Abs(prev) * 100m, 2)
        };

        // ───────────────────────── Helpers ─────────────────────────

        private static readonly Lazy<TimeZoneInfo> ThaiTimeZone = new(() =>
        {
            foreach (var id in new[] { "SE Asia Standard Time", "Asia/Bangkok" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return TimeZoneInfo.Local;
        });

        private static DateTime Today => TimeZoneInfo.ConvertTime(DateTime.UtcNow, ThaiTimeZone.Value).Date;

        private static string MonthLabel(DateTime d) => d.ToString("MMM yyyy", CultureInfo.InvariantCulture);

        private static string DateLabel(DateTime d) => d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

        private static int ResolveYear(int? year)
        {
            var current = Today.Year;
            return year.HasValue ? Math.Clamp(year.Value, 2000, current) : current;
        }

        private static int ResolveMonth(int year, int? month)
        {
            var today = Today;
            var maxMonth = year == today.Year ? today.Month : 12;
            return month.HasValue ? Math.Clamp(month.Value, 1, maxMonth) : maxMonth;
        }

        private static List<string> BuildCommonNotes() => new()
        {
            "Revenue = Payment ที่อนุมัติแล้ว (approved) + FinancialTransaction ประเภท income ที่ไม่ผูก Payment − refund",
            "Expenses = FinancialTransaction ประเภท expense",
            "Occupancy = ห้อง-คืนที่มีการเข้าพักจริงและยืนยันแล้ว ÷ (จำนวนห้อง Active × จำนวนวัน)",
            "TotalBookings นับ Booking สถานะ confirmed/checked_in/checked_out ตามวันที่สร้าง Booking",
            OwnerReportNotes.NetProfitDisclaimer
        };
    }
}