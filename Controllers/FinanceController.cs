//Controllers/FinanceController.cs
using System.Linq.Expressions;
using System.Security.Claims;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using StayMate_Kavinvee_0576.Models;
using StayMate_Kavinvee_0576.Models.ViewModels;
using Microsoft.AspNetCore.Identity;
using StayMate_Kavinvee_0576.ViewModels;

namespace StayMate_Kavinvee_0576.Controllers
{
    /// <summary>
    /// ฝ่ายการเงิน: ตรวจสอบ/อนุมัติ/ปฏิเสธการชำระเงิน, ออกใบเสร็จ, รายงานการเงิน
    /// Separation of Duties:
    ///  - Finance ทำได้เฉพาะ Payment / Receipt / FinancialTransaction (+ เปลี่ยนสถานะ booking จาก pending -> confirmed เท่านั้น)
    ///  - ไม่แตะต้อง Room / Check-in / Housekeeping / Promotion / Rate
    ///  - ห้ามอนุมัติ/ปฏิเสธ payment ของ booking ที่ตนเองเป็นเจ้าของ
    ///  - ทุกการอนุมัติ/ปฏิเสธบันทึก reviewed_by, reviewed_at และ audit_logs
    /// </summary>
    [Authorize(Roles = "FINANCE")] // ต้องให้ claim ของ role ตรงกับ roles.role_code ที่ใช้ตอน login
    public class FinanceController : Controller
    {
        private readonly StayMateDbContext _db;
        private readonly ILogger<FinanceController> _logger;
        private const int PageSize = 20;

        private static readonly string[] ReviewableBookingStatuses = { "pending_payment", "pending_approval" };

        private static readonly string[] ChartPalette =
            { "#2563EB", "#00E5FF", "#16A34A", "#F59E0B", "#8B5CF6", "#EC4899", "#64748B" };

        // รายรับสุทธิ = income - refund (expense ไม่นับเป็นรายรับ)
        private static readonly Expression<Func<FinancialTransaction, decimal>> NetIncomeExpr =
            t => t.TxnType == "income" ? t.Amount : (t.TxnType == "refund" ? -t.Amount : 0m);

        static FinanceController()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public FinanceController(StayMateDbContext db, ILogger<FinanceController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // =====================================================================
        // 1. Finance Dashboard
        // =====================================================================
        public async Task<IActionResult> Index()
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var monthStart = new DateOnly(today.Year, today.Month, 1);
            var sevenDaysAgo = today.AddDays(-6);
            var sixMonthsAgo = monthStart.AddMonths(-5);

            var txns = _db.FinancialTransactions.AsNoTracking();

            var vm = new FinanceDashboardViewModel
            {
                TotalIncome = await txns.SumAsync(NetIncomeExpr),
                TodayIncome = await txns.Where(t => t.TxnDate == today).SumAsync(NetIncomeExpr),
                MonthIncome = await txns.Where(t => t.TxnDate >= monthStart && t.TxnDate <= today).SumAsync(NetIncomeExpr),
                // [เพิ่ม] รายจ่ายรวม / รายจ่ายเดือนนี้
                TotalExpense = await txns.Where(t => t.TxnType == "expense").SumAsync(t => t.Amount),
                MonthExpense = await txns.Where(t => t.TxnType == "expense" && t.TxnDate >= monthStart && t.TxnDate <= today)
                                         .SumAsync(t => t.Amount)
            };

            // สรุป payment ตามสถานะ
            var stats = await _db.Payments.AsNoTracking()
                .GroupBy(p => p.Status)
                .Select(g => new { Status = g.Key, Count = g.Count(), Sum = g.Sum(p => p.Amount) })
                .ToListAsync();

            foreach (var s in stats)
            {
                switch (s.Status)
                {
                    case "pending": vm.PendingCount = s.Count; vm.PendingAmount = s.Sum; break;
                    case "approved": vm.ApprovedCount = s.Count; vm.ApprovedAmount = s.Sum; break;
                    case "rejected": vm.RejectedCount = s.Count; vm.RejectedAmount = s.Sum; break;
                }
            }

            // รายวัน 7 วันล่าสุด
            var dailyRaw = await txns.Where(t => t.TxnDate >= sevenDaysAgo && t.TxnDate <= today)
                .Select(t => new { t.TxnDate, t.TxnType, t.Amount }).ToListAsync();
            vm.Last7Days = Enumerable.Range(0, 7).Select(i => sevenDaysAgo.AddDays(i))
                .Select(d => BuildDaily(d, dailyRaw.Where(x => x.TxnDate == d).Select(x => (x.TxnType, x.Amount))))
                .ToList();

            // รายเดือน 6 เดือนล่าสุด
            var monthlyRaw = await txns.Where(t => t.TxnDate >= sixMonthsAgo && t.TxnDate <= today)
                .Select(t => new { t.TxnDate, t.TxnType, t.Amount }).ToListAsync();
            vm.Last6Months = Enumerable.Range(0, 6).Select(i => sixMonthsAgo.AddMonths(i))
                .Select(m => BuildMonthly(m.Year, m.Month,
                    monthlyRaw.Where(x => x.TxnDate.Year == m.Year && x.TxnDate.Month == m.Month)
                              .Select(x => (x.TxnType, x.Amount))))
                .ToList();

            // รายการรอตรวจสอบที่ค้างนานที่สุด 5 รายการ
            vm.OldestPending = await PaymentListQuery()
                .Where(p => p.Status == "pending")
                .OrderBy(p => p.CreatedAt)
                .Take(5)
                .ToListAsync();

            return View(vm);
        }

        // =====================================================================
        // 2. Payment Approval
        // =====================================================================
        public async Task<IActionResult> PaymentApproval(string? keyword, string? status = "pending", int page = 1)
        {
            var q = PaymentListQuery();

            if (!string.IsNullOrWhiteSpace(status) && status != "all")
                q = q.Where(p => p.Status == status);

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var k = keyword.Trim();
                q = q.Where(p => p.BookingCode.Contains(k)
                              || p.ContactName.Contains(k)
                              || p.PaymentId.ToString().Contains(k)
                              || p.BookingId.ToString().Contains(k));
            }

            // pending: เก่าสุดก่อน (FIFO) / อื่นๆ: ใหม่สุดก่อน
            q = status == "pending" ? q.OrderBy(p => p.CreatedAt) : q.OrderByDescending(p => p.CreatedAt);

            var statusCounts = await _db.Payments.AsNoTracking()
                .GroupBy(p => p.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            int pendingCnt = statusCounts.FirstOrDefault(x => x.Status == "pending")?.Count ?? 0;
            int approvedCnt = statusCounts.FirstOrDefault(x => x.Status == "approved")?.Count ?? 0;
            int rejectedCnt = statusCounts.FirstOrDefault(x => x.Status == "rejected")?.Count ?? 0;

            ViewBag.SummaryPending = pendingCnt;
            ViewBag.SummaryApproved = approvedCnt;
            ViewBag.SummaryRejected = rejectedCnt;
            ViewBag.SummaryTotal = pendingCnt + approvedCnt + rejectedCnt;

            var vm = new PaymentApprovalViewModel
            {
                Keyword = keyword,
                Status = status,
                Payments = await ToPagedAsync(q, page)
            };
            return View(vm);
        }

        // [เพิ่ม] หน้ารายละเอียดการชำระเงิน (finance.js ลิงก์ไป /Finance/PaymentDetail/{id})
        [HttpGet]
        public async Task<IActionResult> PaymentDetail(uint id)
        {
            var vm = await BuildReviewViewModelAsync(id);
            if (vm == null) return NotFound();
            return View(vm);
        }

        // [เพิ่ม] อนุมัติแบบ Form Post (ใช้โดย PaymentDetail.cshtml -> fpdApproveForm)
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(uint id)
        {
            var userId = CurrentUserId();
            if (userId == null) return RedirectToAction("Login", "Account");

            var r = await ApproveCoreAsync(id, userId.Value);
            TempData[r.Success ? "SuccessMessage" : "ErrorMessage"] = r.Message;
            return RedirectToAction(nameof(PaymentDetail), new { id });
        }

        // [เพิ่ม] ปฏิเสธแบบ Form Post (ใช้โดย PaymentDetail.cshtml -> fpdRejectForm)
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(RejectPaymentInput input)
        {
            var userId = CurrentUserId();
            if (userId == null) return RedirectToAction("Login", "Account");

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage
                                           ?? "กรุณาระบุเหตุผลในการปฏิเสธ";
                return RedirectToAction(nameof(PaymentDetail), new { id = input.PaymentId });
            }

            var r = await RejectCoreAsync(input.PaymentId, input.Reason, userId.Value);
            TempData[r.Success ? "SuccessMessage" : "ErrorMessage"] = r.Message;
            return RedirectToAction(nameof(PaymentDetail), new { id = input.PaymentId });
        }

        // ---------------------------------------------------------------------
        // JSON API: โหลดรายการสลิป + ค้นหา + กรอง + ประวัติ + Summary
        // ---------------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetPaymentSlipsJson(
            string? keyword,
            string? status = "pending",
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? sortOrder = "fifo",
            int page = 1,
            int pageSize = 10)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 100) pageSize = 100;

            var baseQuery = _db.Payments.AsNoTracking();

            var stats = await baseQuery
                .GroupBy(p => p.Status)
                .Select(g => new { Status = g.Key, Count = g.Count(), Amount = g.Sum(p => p.Amount) })
                .ToListAsync();

            int pendingCount = stats.FirstOrDefault(x => x.Status == "pending")?.Count ?? 0;
            int approvedCount = stats.FirstOrDefault(x => x.Status == "approved")?.Count ?? 0;
            int rejectedCount = stats.FirstOrDefault(x => x.Status == "rejected")?.Count ?? 0;
            int totalTransactions = pendingCount + approvedCount + rejectedCount;

            var q = baseQuery.AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && status != "all")
            {
                if (status == "history")
                    q = q.Where(p => p.Status == "approved" || p.Status == "rejected");
                else
                    q = q.Where(p => p.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var k = keyword.Trim();
                bool isUint = uint.TryParse(k, out uint parsedId);
                q = q.Where(p => p.Booking.ContactName.Contains(k)
                              || p.Booking.ContactEmail.Contains(k)
                              || p.Booking.BookingCode.Contains(k)
                              || (isUint && (p.PaymentId == parsedId || p.BookingId == parsedId)));
            }

            if (fromDate.HasValue)
            {
                var startDt = fromDate.Value.Date;
                q = q.Where(p => (p.PaidAt ?? p.CreatedAt) >= startDt);
            }
            if (toDate.HasValue)
            {
                var endDt = toDate.Value.Date.AddDays(1);
                q = q.Where(p => (p.PaidAt ?? p.CreatedAt) < endDt);
            }

            q = sortOrder switch
            {
                "oldest" => q.OrderBy(p => p.CreatedAt),
                "newest" => q.OrderByDescending(p => p.CreatedAt),
                "reviewed_desc" => q.OrderByDescending(p => p.ReviewedAt ?? p.CreatedAt),
                _ => status == "pending" ? q.OrderBy(p => p.CreatedAt) : q.OrderByDescending(p => p.CreatedAt)
            };

            var totalFiltered = await q.CountAsync();
            var totalPages = (int)Math.Ceiling(totalFiltered / (double)pageSize);

            var rawItems = await q
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new
                {
                    p.PaymentId,
                    p.BookingId,
                    BookingCode = p.Booking.BookingCode,
                    UserId = p.Booking.UserId,
                    ContactName = p.Booking.ContactName,
                    ContactEmail = p.Booking.ContactEmail,
                    ContactPhone = p.Booking.ContactPhone,
                    Amount = p.Amount,
                    BookingTotal = p.Booking.TotalAmount,
                    Method = p.Method,
                    SlipImagePath = p.SlipImagePath,
                    PaidAt = p.PaidAt,
                    CreatedAt = p.CreatedAt,
                    Status = p.Status,
                    ReviewedAt = p.ReviewedAt,
                    ReviewedByName = p.ReviewedByNavigation != null ? p.ReviewedByNavigation.FullName : null,
                    RejectReason = p.RejectReason
                })
                .ToListAsync();

            var items = rawItems.Select(x => new
            {
                x.PaymentId,
                x.BookingId,
                x.BookingCode,
                x.UserId,
                x.ContactName,
                x.ContactEmail,
                x.ContactPhone,
                x.Amount,
                x.BookingTotal,
                x.Method,
                SlipUrl = NormalizeSlipWebPath(x.SlipImagePath),
                SubmittedAt = (x.PaidAt ?? x.CreatedAt).ToString("dd/MM/yyyy HH:mm"),
                x.Status,
                ReviewedAt = x.ReviewedAt.HasValue ? x.ReviewedAt.Value.ToString("dd/MM/yyyy HH:mm") : null,
                x.ReviewedByName,
                x.RejectReason
            });

            return Json(new
            {
                success = true,
                summary = new { pending = pendingCount, approved = approvedCount, rejected = rejectedCount, total = totalTransactions },
                pagination = new
                {
                    page,
                    pageSize,
                    totalCount = totalFiltered,
                    totalPages,
                    hasPrevious = page > 1,
                    hasNext = page < totalPages
                },
                items
            });
        }

        // ---------------------------------------------------------------------
        // JSON API: รายละเอียดสลิปสำหรับ Modal
        // ---------------------------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetPaymentSlipDetailJson(uint id)
        {
            var vm = await BuildReviewViewModelAsync(id);
            if (vm == null)
                return NotFound(new { success = false, message = "ไม่พบข้อมูลรายการชำระเงิน" });

            var bookingUserId = await _db.Bookings.AsNoTracking()
                .Where(b => b.BookingId == vm.BookingId)
                .Select(b => b.UserId)
                .FirstOrDefaultAsync();

            return Json(new
            {
                success = true,
                data = new
                {
                    paymentId = vm.PaymentId,
                    bookingId = vm.BookingId,
                    bookingCode = vm.BookingCode,
                    bookingStatus = vm.BookingStatus,
                    userId = bookingUserId,
                    contactName = vm.ContactName,
                    contactEmail = vm.ContactEmail,
                    contactPhone = vm.ContactPhone,
                    checkInDate = vm.CheckInDate.ToString("dd/MM/yyyy"),
                    checkOutDate = vm.CheckOutDate.ToString("dd/MM/yyyy"),
                    nights = vm.Nights,
                    amountDue = vm.AmountDue,
                    amountPaid = vm.Amount,
                    alreadyPaid = vm.AlreadyPaid,
                    totalAmount = vm.TotalAmount,
                    difference = vm.Difference,
                    amountMatches = vm.AmountMatches,
                    method = vm.Method,
                    slipUrl = NormalizeSlipWebPath(vm.SlipImagePath),
                    submittedAt = (vm.PaidAt ?? vm.CreatedAt).ToString("dd/MM/yyyy HH:mm"),
                    status = vm.Status,
                    reviewedByName = vm.ReviewedByName,
                    reviewedAt = vm.ReviewedAt?.ToString("dd/MM/yyyy HH:mm"),
                    rejectReason = vm.RejectReason,
                    canApprove = vm.CanApprove,
                    canReject = vm.CanReject,
                    blockReason = vm.BlockReason,
                    rooms = vm.Rooms
                }
            });
        }

        // ---------------------------------------------------------------------
        // JSON API: อนุมัติ / ปฏิเสธ (ไม่ Reload หน้า)
        // ---------------------------------------------------------------------
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveAjax(uint id)
        {
            var userId = CurrentUserId();
            if (userId == null)
                return Unauthorized(new { success = false, message = "กรุณาเข้าสู่ระบบใหม่" });

            var r = await ApproveCoreAsync(id, userId.Value);
            if (!r.Success) return StatusCode(r.Status, new { success = false, message = r.Message });
            return Json(new { success = true, message = r.Message, receiptNo = r.ReceiptNo });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectAjax([FromForm] RejectPaymentInput input)
        {
            var userId = CurrentUserId();
            if (userId == null)
                return Unauthorized(new { success = false, message = "กรุณาเข้าสู่ระบบใหม่" });

            if (!ModelState.IsValid)
            {
                var err = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "กรุณาระบุเหตุผลในการปฏิเสธ";
                return BadRequest(new { success = false, message = err });
            }

            var r = await RejectCoreAsync(input.PaymentId, input.Reason, userId.Value);
            if (!r.Success) return StatusCode(r.Status, new { success = false, message = r.Message });
            return Json(new { success = true, message = r.Message });
        }

        // ---------------------------------------------------------------------
        // Core: อนุมัติ (ใช้ร่วมกันทั้ง Ajax และ Form)
        // กันอนุมัติซ้ำแบบ atomic: UPDATE ... WHERE status='pending' ถ้า 0 แถว = มีคนตรวจสอบไปก่อนแล้ว
        // ---------------------------------------------------------------------
        private async Task<(bool Success, int Status, string Message, string? ReceiptNo)> ApproveCoreAsync(uint id, uint userId)
        {
            var payment = await _db.Payments.Include(p => p.Booking).FirstOrDefaultAsync(p => p.PaymentId == id);
            if (payment == null)
                return (false, 404, "ไม่พบรายการชำระเงินนี้ในระบบ", null);

            var due = await GetAmountDueAsync(payment);
            var block = GetBlockReason(payment, userId, forApprove: true, due);
            if (block != null)
                return (false, 400, block, null);

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var now = DateTime.Now;

                var claimed = await _db.Payments
                    .Where(p => p.PaymentId == id && p.Status == "pending")
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.Status, "approved")
                        .SetProperty(p => p.ReviewedBy, (uint?)userId)
                        .SetProperty(p => p.ReviewedAt, (DateTime?)now));

                if (claimed == 0)
                {
                    await tx.RollbackAsync();
                    return (false, 409, "รายการนี้ถูกตรวจสอบไปแล้ว", null);
                }

                var oldPayment = new { Status = "pending", payment.ReviewedBy, payment.ReviewedAt };
                var oldBookingStatus = payment.Booking.Status;

                payment.Status = "approved";
                payment.ReviewedBy = userId;
                payment.ReviewedAt = now;
                payment.RejectReason = null;
                payment.PaidAt ??= now;
                payment.Booking.Status = "confirmed";

                _db.FinancialTransactions.Add(new FinancialTransaction
                {
                    TxnType = "income",
                    Category = "Room Booking",
                    Amount = payment.Amount,
                    PaymentId = payment.PaymentId,
                    TxnDate = DateOnly.FromDateTime(now),
                    Description = $"Payment approved for booking {payment.Booking.BookingCode}",
                    RecordedBy = userId,
                    CreatedAt = now
                });

                var receiptNo = await GenerateReceiptNoAsync(now);
                _db.Receipts.Add(new Receipt
                {
                    ReceiptNo = receiptNo,
                    PaymentId = payment.PaymentId,
                    Amount = payment.Amount,
                    IssuedBy = userId,
                    IssuedAt = now
                });

                AddAudit(userId, "APPROVE_PAYMENT", "payments", payment.PaymentId.ToString(),
                    oldPayment,
                    new
                    {
                        Status = "approved",
                        ReviewedBy = userId,
                        ReviewedAt = now,
                        ReceiptNo = receiptNo,
                        BookingStatusFrom = oldBookingStatus,
                        BookingStatusTo = "confirmed"
                    });

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return (true, 200, $"อนุมัติการชำระเงินเรียบร้อย ออกใบเสร็จเลขที่ {receiptNo}", receiptNo);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Approve payment {PaymentId} failed", id);
                return (false, 500, "ไม่สามารถอนุมัติรายการได้ กรุณาลองใหม่อีกครั้ง", null);
            }
        }

        private async Task<(bool Success, int Status, string Message)> RejectCoreAsync(uint id, string? reason, uint userId)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return (false, 400, "กรุณาระบุเหตุผลในการปฏิเสธ");

            var cleanReason = reason.Trim();

            var payment = await _db.Payments.Include(p => p.Booking).FirstOrDefaultAsync(p => p.PaymentId == id);
            if (payment == null)
                return (false, 404, "ไม่พบรายการชำระเงินนี้ในระบบ");

            var block = GetBlockReason(payment, userId, forApprove: false, 0m);
            if (block != null)
                return (false, 400, block);

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var now = DateTime.Now;

                var claimed = await _db.Payments
                    .Where(p => p.PaymentId == id && p.Status == "pending")
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.Status, "rejected")
                        .SetProperty(p => p.ReviewedBy, (uint?)userId)
                        .SetProperty(p => p.ReviewedAt, (DateTime?)now)
                        .SetProperty(p => p.RejectReason, cleanReason));

                if (claimed == 0)
                {
                    await tx.RollbackAsync();
                    return (false, 409, "รายการนี้ถูกตรวจสอบไปแล้ว");
                }

                var oldBookingStatus = payment.Booking.Status;

                payment.Status = "rejected";
                payment.ReviewedBy = userId;
                payment.ReviewedAt = now;
                payment.RejectReason = cleanReason;

                if (payment.Booking.Status == "pending_approval")
                    payment.Booking.Status = "pending_payment";

                AddAudit(userId, "REJECT_PAYMENT", "payments", payment.PaymentId.ToString(),
                    new { Status = "pending" },
                    new
                    {
                        Status = "rejected",
                        Reason = cleanReason,
                        ReviewedBy = userId,
                        ReviewedAt = now,
                        BookingStatusFrom = oldBookingStatus,
                        BookingStatusTo = payment.Booking.Status
                    });

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return (true, 200, "บันทึกการปฏิเสธสลิปเรียบร้อยแล้ว");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Reject payment {PaymentId} failed", id);
                return (false, 500, "ไม่สามารถปฏิเสธรายการได้ กรุณาลองใหม่อีกครั้ง");
            }
        }

        // Helper: แปลง SlipImagePath เป็น Web Path (/uploads/slips/...) ไม่เปิดเผย Physical Path
        private static string? NormalizeSlipWebPath(string? rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) return null;
            var clean = rawPath.Replace("\\", "/").Trim();

            var wwwrootIdx = clean.IndexOf("wwwroot/", StringComparison.OrdinalIgnoreCase);
            if (wwwrootIdx >= 0)
                clean = clean.Substring(wwwrootIdx + 7);

            if (clean.StartsWith("~/"))
                clean = clean.Substring(1);

            if (clean.StartsWith("/uploads/slips/", StringComparison.OrdinalIgnoreCase))
                return clean;

            if (clean.StartsWith("uploads/slips/", StringComparison.OrdinalIgnoreCase))
                return "/" + clean;

            var fileName = System.IO.Path.GetFileName(clean);
            return string.IsNullOrWhiteSpace(fileName) ? null : $"/uploads/slips/{fileName}";
        }

        // =====================================================================
        // 3. Transaction History
        // =====================================================================
        public async Task<IActionResult> Transactions(string? keyword, DateTime? from, DateTime? to, string? txnType, int page = 1)
        {
            if (page < 1) page = 1;

            // 1. ดึงรายการจาก FinancialTransactions (income, refund, expense)
            var ledgerRows = await FilterTransactions(keyword, from, to, txnType == "rejected" ? "__none__" : txnType)
                .Select(TransactionRowExpr())
                .ToListAsync();

            // 2. ดึงรายการที่ถูกปฏิเสธ (Payments.Status == "rejected") มารวมด้วย
            var rejectedRows = new List<TransactionRow>();
            if (string.IsNullOrWhiteSpace(txnType) || txnType == "rejected")
            {
                var existingPaymentIds = ledgerRows
                    .Where(x => x.PaymentId.HasValue)
                    .Select(x => x.PaymentId!.Value)
                    .ToHashSet();

                var pq = _db.Payments.AsNoTracking()
                    .Where(p => p.Status == "rejected" && !existingPaymentIds.Contains(p.PaymentId));

                if (from.HasValue)
                {
                    var fDt = from.Value.Date;
                    pq = pq.Where(p => (p.ReviewedAt ?? p.PaidAt ?? p.CreatedAt) >= fDt);
                }
                if (to.HasValue)
                {
                    var eDt = to.Value.Date.AddDays(1);
                    pq = pq.Where(p => (p.ReviewedAt ?? p.PaidAt ?? p.CreatedAt) < eDt);
                }
                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    var k = keyword.Trim();
                    bool isUint = uint.TryParse(k, out uint parsedId);
                    pq = pq.Where(p => p.Booking.BookingCode.Contains(k)
                                    || p.Booking.ContactName.Contains(k)
                                    || (p.RejectReason != null && p.RejectReason.Contains(k))
                                    || (isUint && (p.PaymentId == parsedId || p.BookingId == parsedId)));
                }

                var rawRejected = await pq
                    .Select(p => new
                    {
                        p.PaymentId,
                        p.BookingId,
                        BookingCode = p.Booking.BookingCode,
                        p.Amount,
                        p.Method,
                        p.RejectReason,
                        EventAt = p.ReviewedAt ?? p.PaidAt ?? p.CreatedAt,
                        ReviewedByName = p.ReviewedByNavigation != null ? p.ReviewedByNavigation.FullName : null
                    })
                    .ToListAsync();

                rejectedRows = rawRejected.Select(p => new TransactionRow
                {
                    TxnId = 0,
                    TxnDate = DateOnly.FromDateTime(p.EventAt),
                    CreatedAt = p.EventAt,
                    TxnType = "rejected",
                    Category = "Payment Rejected",
                    Amount = p.Amount,
                    Description = !string.IsNullOrWhiteSpace(p.RejectReason)
                        ? $"ปฏิเสธสลิป ({p.BookingCode}): {p.RejectReason}"
                        : $"Payment rejected for booking {p.BookingCode}",
                    PaymentId = p.PaymentId,
                    PaymentStatus = "rejected",
                    Method = p.Method,
                    BookingId = p.BookingId,
                    BookingCode = p.BookingCode,
                    RecordedByName = p.ReviewedByName ?? "Finance"
                }).ToList();
            }

            // 3. รวมรายการทั้งหมดและเรียงตามวันที่/เวลาล่าสุด
            var allRows = ledgerRows
                .Concat(rejectedRows)
                .OrderByDescending(x => x.TxnDate)
                .ThenByDescending(x => x.CreatedAt)
                .ToList();

            ViewBag.TotalRejectedCount = allRows.Count(x => x.TxnType == "rejected");
            ViewBag.TotalRejectedAmount = allRows.Where(x => x.TxnType == "rejected").Sum(x => x.Amount);

            var vm = new TransactionsViewModel
            {
                Keyword = keyword,
                From = from,
                To = to,
                TxnType = txnType,
                TotalIncome = allRows.Where(x => x.TxnType == "income").Sum(x => x.Amount),
                TotalRefund = allRows.Where(x => x.TxnType == "refund").Sum(x => x.Amount),
                TotalExpense = allRows.Where(x => x.TxnType == "expense").Sum(x => x.Amount),
                Transactions = new PagedList<TransactionRow>
                {
                    Items = allRows.Skip((page - 1) * PageSize).Take(PageSize).ToList(),
                    Page = page,
                    PageSize = PageSize,
                    TotalCount = allRows.Count
                }
            };

            return View(vm);
        }

        // =====================================================================
        // 4. Receipt Management
        // =====================================================================
        public async Task<IActionResult> Receipts(string? keyword, DateTime? from, DateTime? to, int page = 1)
        {
            var q = _db.Receipts.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var k = keyword.Trim();
                bool isUint = uint.TryParse(k, out uint parsedId);
                q = q.Where(r => r.ReceiptNo.Contains(k)
                              || r.Payment.Booking.BookingCode.Contains(k)
                              || r.Payment.Booking.ContactName.Contains(k)
                              || (isUint && (r.ReceiptId == parsedId || r.Payment.BookingId == parsedId)));
            }
            if (from.HasValue) q = q.Where(r => r.IssuedAt >= from.Value.Date);
            if (to.HasValue) q = q.Where(r => r.IssuedAt < to.Value.Date.AddDays(1));

            var rows = q.OrderByDescending(r => r.IssuedAt).Select(r => new ReceiptRow
            {
                ReceiptId = r.ReceiptId,
                ReceiptNo = r.ReceiptNo,
                PaymentId = r.PaymentId,
                BookingId = r.Payment.BookingId,
                BookingCode = r.Payment.Booking.BookingCode,
                ContactName = r.Payment.Booking.ContactName,
                Amount = r.Amount,
                Method = r.Payment.Method,
                IssuedAt = r.IssuedAt,
                IssuedByName = r.IssuedByNavigation != null ? r.IssuedByNavigation.FullName : null
            });

            var allReceiptsCount = await _db.Receipts.AsNoTracking().CountAsync();
            var allReceiptsSum = await _db.Receipts.AsNoTracking().SumAsync(r => (decimal?)r.Amount) ?? 0m;

            var cancelledIds = await _db.AuditLogs.AsNoTracking()
                .Where(a => a.Action == "CANCEL_RECEIPT" && a.TableName == "receipts")
                .Select(a => a.RecordId)
                .Distinct()
                .ToListAsync();

            var unissuedPayments = await _db.Payments.AsNoTracking()
                .Where(p => p.Status == "approved" && !_db.Receipts.Any(r => r.PaymentId == p.PaymentId))
                .Select(p => new
                {
                    p.PaymentId,
                    p.BookingId,
                    BookingCode = p.Booking.BookingCode,
                    ContactName = p.Booking.ContactName,
                    p.Amount,
                    p.Method,
                    PaidAt = p.PaidAt ?? p.CreatedAt,
                    RoomAmount = p.Booking.RoomAmount,
                    DiscountAmount = p.Booking.DiscountAmount,
                    TaxAmount = p.Booking.TaxAmount,
                    TotalAmount = p.Booking.TotalAmount
                })
                .ToListAsync();

            ViewBag.TotalReceiptsCount = allReceiptsCount;
            ViewBag.IssuedReceiptsCount = Math.Max(0, allReceiptsCount - cancelledIds.Count);
            ViewBag.PendingReceiptsCount = unissuedPayments.Count;
            ViewBag.CancelledReceiptsCount = cancelledIds.Count;
            ViewBag.TotalReceiptValue = allReceiptsSum;
            ViewBag.CancelledReceiptIds = cancelledIds;
            ViewBag.UnissuedPaymentsJson = JsonSerializer.Serialize(unissuedPayments);

            var pagedReceipts = await ToPagedAsync(rows, page);
            var bookingIdsOnPage = pagedReceipts.Items.Select(x => x.BookingId).Distinct().ToList();
            var bookingTaxMap = await _db.Bookings.AsNoTracking()
                .Where(b => bookingIdsOnPage.Contains(b.BookingId))
                .Select(b => new { b.BookingId, b.RoomAmount, b.DiscountAmount, b.TaxAmount, b.TotalAmount })
                .ToDictionaryAsync(x => x.BookingId);

            ViewBag.BookingTaxMap = bookingTaxMap;

            return View(new ReceiptsViewModel
            {
                Keyword = keyword,
                From = from,
                To = to,
                Receipts = pagedReceipts
            });
        }

        // [เพิ่ม] หน้ารายละเอียดใบเสร็จ (finance.js ลิงก์ไป /Finance/ReceiptDetail/{id})
        [HttpGet]
        public async Task<IActionResult> ReceiptDetail(uint id)
        {
            var vm = await BuildReceiptDetailAsync(id);
            if (vm == null) return NotFound();

            var cancelAudit = await _db.AuditLogs.AsNoTracking()
                .Where(a => a.Action == "CANCEL_RECEIPT" && a.TableName == "receipts" && a.RecordId == id.ToString())
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync();

            ViewBag.IsCancelled = cancelAudit != null;
            ViewBag.CancelNote = cancelAudit?.NewValue;
            return View(vm);
        }

        // JSON API: ข้อมูลใบเสร็จเต็มรูปแบบสำหรับ Preview & Print Modal
        [HttpGet]
        public async Task<IActionResult> GetReceiptDetailJson(uint id)
        {
            var vm = await BuildReceiptDetailAsync(id);
            if (vm == null)
                return NotFound(new { success = false, message = "ไม่พบข้อมูลใบเสร็จรับเงินในระบบ" });

            var cancelAudit = await _db.AuditLogs.AsNoTracking()
                .Where(a => a.Action == "CANCEL_RECEIPT" && a.TableName == "receipts" && a.RecordId == id.ToString())
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync();

            bool isCancelled = cancelAudit != null;
            decimal subtotal = Math.Max(0m, vm.Amount - vm.TaxAmount);

            return Json(new
            {
                success = true,
                data = new
                {
                    receiptId = vm.ReceiptId,
                    receiptNo = vm.ReceiptNo,
                    issuedAt = vm.IssuedAt.ToString("dd/MM/yyyy HH:mm"),
                    issuedByName = vm.IssuedByName ?? "Finance Officer",
                    paymentId = vm.PaymentId,
                    method = vm.Method,
                    paidAt = vm.PaidAt?.ToString("dd/MM/yyyy HH:mm") ?? vm.IssuedAt.ToString("dd/MM/yyyy HH:mm"),
                    amount = vm.Amount,
                    bookingId = vm.BookingId,
                    bookingCode = vm.BookingCode,
                    contactName = vm.ContactName,
                    contactEmail = vm.ContactEmail,
                    contactPhone = vm.ContactPhone,
                    checkInDate = vm.CheckInDate.ToString("dd/MM/yyyy"),
                    checkOutDate = vm.CheckOutDate.ToString("dd/MM/yyyy"),
                    nights = vm.Nights,
                    roomAmount = vm.RoomAmount,
                    discountPercent = vm.DiscountPercent,
                    discountAmount = vm.DiscountAmount,
                    subtotal,
                    taxAmount = vm.TaxAmount,
                    totalAmount = vm.TotalAmount,
                    status = isCancelled ? "Cancelled" : "Issued",
                    cancelNote = cancelAudit?.NewValue,
                    rooms = vm.Rooms,
                    promotions = vm.Promotions,
                    pdfUrl = Url.Action(nameof(ReceiptPdf), "Finance", new { id = vm.ReceiptId })
                }
            });
        }

        // PDF: Preview / Print / Download ใบเสร็จรับเงิน
        [HttpGet]
        public async Task<IActionResult> ReceiptPdf(uint id)
        {
            var vm = await BuildReceiptDetailAsync(id);
            if (vm == null)
                return NotFound("ไม่พบข้อมูลใบเสร็จรับเงินในระบบ");

            var cancelAudit = await _db.AuditLogs.AsNoTracking()
                .Where(a => a.Action == "CANCEL_RECEIPT" && a.TableName == "receipts" && a.RecordId == id.ToString())
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync();

            bool isCancelled = cancelAudit != null;

            var pdfBytes = Document.Create(document =>
            {
                document.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    // [แก้] กำหนดฟอนต์ที่รองรับภาษาไทย (เดิมไม่ได้กำหนด ทำให้ภาษาไทยอาจเป็นกล่อง)
                    page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Tahoma"));

                    page.Header().Column(header =>
                    {
                        header.Item().Text("STAYMATE").FontSize(26).Bold().FontColor(Colors.Blue.Darken2);
                        header.Item().Text("ใบเสร็จรับเงิน / RECEIPT").FontSize(18).Bold();

                        if (isCancelled)
                        {
                            header.Item().PaddingTop(5)
                                .Text("CANCELLED / ยกเลิกแล้ว").Bold().FontColor(Colors.Red.Medium);
                        }

                        header.Item().PaddingTop(5).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                    });

                    page.Content().PaddingVertical(15).Column(col =>
                    {
                        col.Spacing(8);

                        col.Item().Text($"เลขที่ใบเสร็จ: {vm.ReceiptNo}");
                        col.Item().Text($"วันที่ออกใบเสร็จ: {vm.IssuedAt:dd/MM/yyyy HH:mm}");
                        col.Item().Text($"ผู้ออกใบเสร็จ: {vm.IssuedByName ?? "Finance Officer"}");

                        col.Item().PaddingTop(12).Text("ข้อมูลลูกค้า").Bold().FontSize(13);
                        col.Item().Text($"ชื่อ: {vm.ContactName}");
                        col.Item().Text($"อีเมล: {vm.ContactEmail}");
                        col.Item().Text($"โทรศัพท์: {vm.ContactPhone}");

                        col.Item().PaddingTop(12).Text("ข้อมูลการจอง").Bold().FontSize(13);
                        col.Item().Text($"Booking Code: {vm.BookingCode}");
                        col.Item().Text($"วันที่เข้าพัก: {vm.CheckInDate:dd/MM/yyyy}");
                        col.Item().Text($"วันที่ออก: {vm.CheckOutDate:dd/MM/yyyy}");
                        col.Item().Text($"จำนวนคืน: {vm.Nights}");

                        col.Item().PaddingTop(12).Text("รายละเอียดห้องพัก").Bold().FontSize(13);

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                            });

                            table.Header(header =>
                            {
                                ReceiptCell(header.Cell()).Text("รายการ").Bold();
                                ReceiptCell(header.Cell()).AlignRight().Text("จำนวน").Bold();
                                ReceiptCell(header.Cell()).AlignRight().Text("ราคา (บาท)").Bold();
                            });

                            foreach (var room in vm.Rooms)
                            {
                                ReceiptCell(table.Cell()).Text(room.RoomTypeName);
                                ReceiptCell(table.Cell()).AlignRight().Text($"{room.Nights} คืน");
                                ReceiptCell(table.Cell()).AlignRight().Text(room.LineTotal.ToString("N2"));
                            }
                        });

                        col.Item().PaddingTop(15).Text("สรุปยอดชำระ").Bold().FontSize(13);

                        ReceiptSummary(col, "ค่าห้องพัก", vm.RoomAmount);
                        ReceiptSummary(col, "ส่วนลด", -vm.DiscountAmount);
                        ReceiptSummary(col, "ยอดก่อนภาษี", Math.Max(0m, vm.Amount - vm.TaxAmount));
                        ReceiptSummary(col, "ภาษี", vm.TaxAmount);

                        col.Item().PaddingTop(8).BorderTop(1).BorderColor(Colors.Grey.Medium)
                            .PaddingTop(10).AlignRight()
                            .Text($"ยอดชำระจริง: {vm.Amount:N2} บาท").FontSize(18).Bold();

                        col.Item().PaddingTop(12).Text($"วิธีชำระเงิน: {vm.Method}");
                        col.Item().Text($"วันที่ชำระ: {vm.PaidAt?.ToString("dd/MM/yyyy HH:mm") ?? "-"}");

                        if (vm.Promotions != null && vm.Promotions.Any())
                        {
                            col.Item().PaddingTop(12).Text("โปรโมชั่นที่ได้รับ").Bold().FontSize(13);
                            foreach (var promo in vm.Promotions)
                            {
                                col.Item().Text($"{promo.PromoCode} - {promo.PromoName} ({promo.AppliedAmount:N2} บาท)");
                            }
                        }

                        if (isCancelled)
                        {
                            col.Item().PaddingTop(15)
                                .Text($"เหตุผลการยกเลิก: {cancelAudit?.NewValue ?? "-"}")
                                .FontColor(Colors.Red.Darken1);
                        }

                        col.Item().PaddingTop(35).AlignCenter().Text("ขอบคุณที่ใช้บริการ StayMate").Italic();
                    });

                    page.Footer().AlignCenter()
                        .Text("StayMate - Official Receipt").FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            }).GeneratePdf();

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Receipt_{vm.ReceiptNo}.pdf\"";
            return File(pdfBytes, "application/pdf");
        }

        private static IContainer ReceiptCell(IContainer container) =>
            container.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6);

        private static void ReceiptSummary(ColumnDescriptor col, string label, decimal amount)
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Text(label);
                row.ConstantItem(130).AlignRight().Text($"{amount:N2} บาท");
            });
        }

        // JSON API: ออกใบเสร็จใหม่สำหรับ Payment ที่ Approved แล้วแต่ยังไม่มีใบเสร็จ
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateReceiptAjax(uint paymentId)
        {
            var userId = CurrentUserId();
            if (userId == null)
                return Unauthorized(new { success = false, message = "กรุณาเข้าสู่ระบบใหม่" });

            var payment = await _db.Payments.Include(p => p.Booking).FirstOrDefaultAsync(p => p.PaymentId == paymentId);
            if (payment == null)
                return NotFound(new { success = false, message = "ไม่พบรายการชำระเงินที่เลือก" });

            if (payment.Status != "approved")
                return BadRequest(new { success = false, message = "สามารถออกใบเสร็จได้เฉพาะรายการชำระเงินที่ได้รับการอนุมัติ (Approved) แล้วเท่านั้น" });

            // [เพิ่ม] Separation of Duties เช่นเดียวกับการอนุมัติ
            if (payment.Booking.UserId == userId)
                return BadRequest(new { success = false, message = "ไม่สามารถออกใบเสร็จของ Booking ที่ตนเองเป็นผู้จองได้" });

            bool alreadyExists = await _db.Receipts.AnyAsync(r => r.PaymentId == paymentId);
            if (alreadyExists)
                return BadRequest(new { success = false, message = "รายการชำระเงินนี้มีการออกใบเสร็จรับเงินไปแล้ว ไม่สามารถออกซ้ำได้" });

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var now = DateTime.Now;
                var receiptNo = await GenerateReceiptNoAsync(now);

                var receipt = new Receipt
                {
                    ReceiptNo = receiptNo,
                    PaymentId = payment.PaymentId,
                    Amount = payment.Amount, // ดึงยอดจริงจาก Payment ห้ามแก้ไขยอดเอง
                    IssuedBy = userId,
                    IssuedAt = now
                };

                _db.Receipts.Add(receipt);
                await _db.SaveChangesAsync();

                AddAudit(userId, "CREATE_RECEIPT", "receipts", receipt.ReceiptId.ToString(),
                    null,
                    new { receipt.ReceiptNo, receipt.PaymentId, receipt.Amount, IssuedBy = userId, IssuedAt = now });

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return Json(new
                {
                    success = true,
                    message = $"ออกใบเสร็จรับเงินเลขที่ {receiptNo} สำเร็จเรียบร้อยแล้ว",
                    receiptId = receipt.ReceiptId,
                    receiptNo
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Create receipt for payment {PaymentId} failed", paymentId);
                return StatusCode(500, new { success = false, message = "ไม่สามารถสร้างใบเสร็จได้ กรุณาลองใหม่อีกครั้ง" });
            }
        }

        // JSON API: ยกเลิกใบเสร็จพร้อมระบุเหตุผล (ไม่ลบออกจากระบบถาวร)
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelReceiptAjax(uint receiptId, string reason)
        {
            var userId = CurrentUserId();
            if (userId == null)
                return Unauthorized(new { success = false, message = "กรุณาเข้าสู่ระบบใหม่" });

            if (string.IsNullOrWhiteSpace(reason))
                return BadRequest(new { success = false, message = "กรุณาระบุเหตุผลในการยกเลิกใบเสร็จรับเงิน" });

            var receipt = await _db.Receipts
                .Include(r => r.Payment)
                .ThenInclude(p => p.Booking)
                .FirstOrDefaultAsync(r => r.ReceiptId == receiptId);

            if (receipt == null)
                return NotFound(new { success = false, message = "ไม่พบใบเสร็จรับเงินที่ต้องการยกเลิก" });

            bool alreadyCancelled = await _db.AuditLogs.AnyAsync(
                a => a.Action == "CANCEL_RECEIPT" && a.TableName == "receipts" && a.RecordId == receiptId.ToString());

            if (alreadyCancelled)
                return BadRequest(new { success = false, message = "ใบเสร็จรับเงินนี้ถูกยกเลิกไปแล้ว" });

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var now = DateTime.Now;
                var cleanReason = reason.Trim();

                _db.FinancialTransactions.Add(new FinancialTransaction
                {
                    TxnType = "refund",
                    Category = "Receipt Cancellation",
                    Amount = receipt.Amount,
                    PaymentId = receipt.PaymentId,
                    TxnDate = DateOnly.FromDateTime(now),
                    Description = $"Cancelled Receipt {receipt.ReceiptNo} (Booking {receipt.Payment.Booking.BookingCode}): {cleanReason}",
                    RecordedBy = userId,
                    CreatedAt = now
                });

                AddAudit(userId, "CANCEL_RECEIPT", "receipts", receipt.ReceiptId.ToString(),
                    new { Status = "Issued", receipt.ReceiptNo, receipt.Amount },
                    new { Status = "Cancelled", Reason = cleanReason, CancelledBy = userId, CancelledAt = now.ToString("dd/MM/yyyy HH:mm") });

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return Json(new
                {
                    success = true,
                    message = $"ยกเลิกใบเสร็จเลขที่ {receipt.ReceiptNo} และบันทึกประวัติการตรวจสอบเรียบร้อยแล้ว"
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Cancel receipt {ReceiptId} failed", receiptId);
                return StatusCode(500, new { success = false, message = "ไม่สามารถยกเลิกใบเสร็จได้ กรุณาลองใหม่อีกครั้ง" });
            }
        }

        // =====================================================================
        // 5. Financial Reports
        // =====================================================================
        public async Task<IActionResult> Reports(DateTime? from, DateTime? to)
        {
            var vm = await BuildReportAsync(from, to);
            return View(vm);
        }

        public async Task<IActionResult> ExportReport(DateTime? from, DateTime? to, string format = "excel")
        {
            var vm = await BuildReportAsync(from, to);
            var name = $"finance-report_{vm.From:yyyyMMdd}_{vm.To:yyyyMMdd}";

            return format.Equals("pdf", StringComparison.OrdinalIgnoreCase)
                ? File(BuildReportPdf(vm), "application/pdf", name + ".pdf")
                : File(BuildReportExcel(vm),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name + ".xlsx");
        }

        // =====================================================================
        // Helpers: queries
        // =====================================================================
        private IQueryable<PaymentListItem> PaymentListQuery() =>
            _db.Payments.AsNoTracking().Select(p => new PaymentListItem
            {
                PaymentId = p.PaymentId,
                BookingId = p.BookingId,
                BookingCode = p.Booking.BookingCode,
                ContactName = p.Booking.ContactName,
                Amount = p.Amount,
                BookingTotal = p.Booking.TotalAmount,
                Method = p.Method,
                SlipImagePath = p.SlipImagePath,
                PaidAt = p.PaidAt,
                CreatedAt = p.CreatedAt,
                Status = p.Status
            });

        private IQueryable<FinancialTransaction> FilterTransactions(string? keyword, DateTime? from, DateTime? to, string? txnType)
        {
            var q = _db.FinancialTransactions.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(txnType)) q = q.Where(t => t.TxnType == txnType);
            if (from.HasValue) { var f = DateOnly.FromDateTime(from.Value); q = q.Where(t => t.TxnDate >= f); }
            if (to.HasValue) { var e = DateOnly.FromDateTime(to.Value); q = q.Where(t => t.TxnDate <= e); }
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var k = keyword.Trim();
                // [เพิ่ม] ค้นหาด้วย Transaction ID ตาม spec
                bool isUint = uint.TryParse(k, out uint parsedId);
                q = q.Where(t => t.Category.Contains(k)
                              || (t.Description != null && t.Description.Contains(k))
                              || (t.Payment != null && t.Payment.Booking.BookingCode.Contains(k))
                              || (isUint && t.TxnId == parsedId));
            }
            return q;
        }

        private static Expression<Func<FinancialTransaction, TransactionRow>> TransactionRowExpr() =>
            t => new TransactionRow
            {
                TxnId = t.TxnId,
                TxnDate = t.TxnDate,
                CreatedAt = t.CreatedAt,
                TxnType = t.TxnType,
                Category = t.Category,
                Amount = t.Amount,
                Description = t.Description,
                PaymentId = t.PaymentId,
                PaymentStatus = t.Payment != null ? t.Payment.Status : null,
                Method = t.Payment != null ? t.Payment.Method : null,
                BookingId = t.Payment != null ? t.Payment.BookingId : (uint?)null,
                BookingCode = t.Payment != null ? t.Payment.Booking.BookingCode : null,
                RecordedByName = t.RecordedByNavigation != null ? t.RecordedByNavigation.FullName : null
            };

        private static async Task<PagedList<T>> ToPagedAsync<T>(IQueryable<T> q, int page)
        {
            if (page < 1) page = 1;
            var total = await q.CountAsync();
            var items = await q.Skip((page - 1) * PageSize).Take(PageSize).ToListAsync();
            return new PagedList<T> { Items = items, Page = page, PageSize = PageSize, TotalCount = total };
        }

        private async Task<PaymentReviewViewModel?> BuildReviewViewModelAsync(uint id)
        {
            var p = await _db.Payments.AsNoTracking()
                .Include(x => x.Booking)
                .Include(x => x.ReviewedByNavigation)
                .FirstOrDefaultAsync(x => x.PaymentId == id);
            if (p == null) return null;

            var b = p.Booking;
            var due = await GetAmountDueAsync(p);
            var paid = b.TotalAmount - due;
            var userId = CurrentUserId();

            var vm = new PaymentReviewViewModel
            {
                PaymentId = p.PaymentId,
                Amount = p.Amount,
                Method = p.Method,
                SlipImagePath = p.SlipImagePath,
                PaidAt = p.PaidAt,
                CreatedAt = p.CreatedAt,
                Status = p.Status,
                RejectReason = p.RejectReason,
                ReviewedByName = p.ReviewedByNavigation?.FullName,
                ReviewedAt = p.ReviewedAt,

                BookingId = b.BookingId,
                BookingCode = b.BookingCode,
                BookingStatus = b.Status,
                ContactName = b.ContactName,
                ContactEmail = b.ContactEmail,
                ContactPhone = b.ContactPhone,
                CheckInDate = b.CheckInDate,
                CheckOutDate = b.CheckOutDate,
                Nights = b.Nights,
                NumGuests = b.NumGuests,
                RoomAmount = b.RoomAmount,
                DiscountPercent = b.DiscountPercent,
                DiscountAmount = b.DiscountAmount,
                TaxAmount = b.TaxAmount,
                TotalAmount = b.TotalAmount,

                AlreadyPaid = paid,
                AmountDue = due,

                Rooms = await RoomLinesAsync(b.BookingId),
                Promotions = await PromotionLinesAsync(b.BookingId)
            };

            vm.BlockReason = GetBlockReason(p, userId, forApprove: false, due);
            vm.CanReject = vm.BlockReason == null;
            var approveBlock = GetBlockReason(p, userId, forApprove: true, due);
            vm.CanApprove = approveBlock == null;
            vm.BlockReason ??= approveBlock;

            return vm;
        }

        private async Task<ReceiptDetailViewModel?> BuildReceiptDetailAsync(uint id)
        {
            var r = await _db.Receipts.AsNoTracking()
                .Include(x => x.IssuedByNavigation)
                .Include(x => x.Payment).ThenInclude(p => p.Booking)
                .FirstOrDefaultAsync(x => x.ReceiptId == id);
            if (r == null) return null;

            var b = r.Payment.Booking;
            return new ReceiptDetailViewModel
            {
                ReceiptId = r.ReceiptId,
                ReceiptNo = r.ReceiptNo,
                IssuedAt = r.IssuedAt,
                IssuedByName = r.IssuedByNavigation?.FullName,
                PaymentId = r.PaymentId,
                Method = r.Payment.Method,
                PaidAt = r.Payment.PaidAt,
                Amount = r.Amount,
                BookingId = b.BookingId,
                BookingCode = b.BookingCode,
                ContactName = b.ContactName,
                ContactEmail = b.ContactEmail,
                ContactPhone = b.ContactPhone,
                CheckInDate = b.CheckInDate,
                CheckOutDate = b.CheckOutDate,
                Nights = b.Nights,
                RoomAmount = b.RoomAmount,
                DiscountPercent = b.DiscountPercent,
                DiscountAmount = b.DiscountAmount,
                TaxAmount = b.TaxAmount,
                TotalAmount = b.TotalAmount,
                Rooms = await RoomLinesAsync(b.BookingId),
                Promotions = await PromotionLinesAsync(b.BookingId)
            };
        }

        private Task<List<BookingRoomLine>> RoomLinesAsync(uint bookingId) =>
            _db.BookingRooms.AsNoTracking()
                .Where(x => x.BookingId == bookingId)
                .Select(x => new BookingRoomLine
                {
                    RoomTypeName = x.RoomType.TypeName,
                    RoomNumber = x.Room != null ? x.Room.RoomNumber : null,
                    PricePerNight = x.PricePerNight,
                    Nights = x.Nights,
                    ExtraBedQty = x.ExtraBedQty,
                    ExtraBedAmount = x.ExtraBedAmount,
                    LineTotal = x.LineTotal
                }).ToListAsync();

        private Task<List<BookingPromotionLine>> PromotionLinesAsync(uint bookingId) =>
            _db.BookingPromotions.AsNoTracking()
                .Where(x => x.BookingId == bookingId)
                .Select(x => new BookingPromotionLine
                {
                    PromoCode = x.Promotion.PromoCode,
                    PromoName = x.Promotion.PromoName,
                    StackRole = x.StackRole,
                    AppliedPercent = x.AppliedPercent,
                    AppliedAmount = x.AppliedAmount
                }).ToListAsync();

        // ยอดที่ต้องชำระ = ยอดรวม booking - ยอดที่ approve ไปแล้ว (ไม่นับ payment นี้)
        private async Task<decimal> GetAmountDueAsync(Payment p)
        {
            var paid = await _db.Payments
                .Where(x => x.BookingId == p.BookingId && x.Status == "approved" && x.PaymentId != p.PaymentId)
                .SumAsync(x => (decimal?)x.Amount) ?? 0m;
            return p.Booking.TotalAmount - paid;
        }

        // คืนค่า null ถ้าทำรายการได้ / ข้อความเหตุผลถ้าไม่ได้
        private static string? GetBlockReason(Payment p, uint? userId, bool forApprove, decimal due)
        {
            if (p.Status != "pending")
                return "รายการนี้ถูกตรวจสอบไปแล้ว";

            if (!ReviewableBookingStatuses.Contains(p.Booking.Status))
                return $"สถานะ Booking ({p.Booking.Status}) ไม่สามารถตรวจสอบการชำระเงินได้";

            // Separation of Duties
            if (userId.HasValue && p.Booking.UserId == userId)
                return "ตามหลัก Separation of Duties ไม่สามารถอนุมัติ/ปฏิเสธรายการของ Booking ที่ตนเองเป็นผู้จองได้";

            if (!forApprove) return null;

            if ((p.Method == "bank_transfer" || p.Method == "promptpay") && string.IsNullOrWhiteSpace(p.SlipImagePath))
                return "ไม่พบหลักฐานการโอน (Slip) ไม่สามารถอนุมัติได้";

            if (p.Amount != due)
                return $"ยอดที่โอน ({Money(p.Amount)}) ไม่ตรงกับยอดที่ต้องชำระ ({Money(due)}) กรุณาปฏิเสธพร้อมระบุเหตุผล";

            return null;
        }

        // เลขที่ใบเสร็จ: RC + yyyyMMdd + ลำดับ 4 หลักของวัน เช่น RC202611190001
        // [แก้] ใช้เลขล่าสุด + 1 แทนการนับจำนวน (ทนต่อกรณีมีการลบ/ข้ามเลขมากกว่า)
        // unique index uq_receipt_no จะกันซ้ำกรณีออกพร้อมกัน (transaction จะ rollback)
        private async Task<string> GenerateReceiptNoAsync(DateTime now)
        {
            var prefix = $"RC{now:yyyyMMdd}";
            var last = await _db.Receipts
                .Where(r => r.ReceiptNo.StartsWith(prefix))
                .OrderByDescending(r => r.ReceiptNo)
                .Select(r => r.ReceiptNo)
                .FirstOrDefaultAsync();

            var next = 1;
            if (last != null && int.TryParse(last.Substring(prefix.Length), out var n))
                next = n + 1;

            return $"{prefix}{next:D4}";
        }

        private uint? CurrentUserId()
        {
            var v = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return uint.TryParse(v, out var id) ? id : null;
        }

        private void AddAudit(uint? userId, string action, string table, string recordId, object? oldValue, object? newValue)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = action,
                TableName = table,
                RecordId = recordId,
                OldValue = oldValue == null ? null : JsonSerializer.Serialize(oldValue),
                NewValue = newValue == null ? null : JsonSerializer.Serialize(newValue),
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                CreatedAt = DateTime.Now
            });
        }

        // =====================================================================
        // Helpers: reports
        // =====================================================================
        private static DailyIncomeRow BuildDaily(DateOnly date, IEnumerable<(string Type, decimal Amount)> rows)
        {
            var list = rows.ToList();
            return new DailyIncomeRow
            {
                Date = date,
                Income = list.Where(x => x.Type == "income").Sum(x => x.Amount),
                Refund = list.Where(x => x.Type == "refund").Sum(x => x.Amount),
                Expense = list.Where(x => x.Type == "expense").Sum(x => x.Amount)
            };
        }

        private static WeeklyIncomeRow BuildWeekly(DateOnly weekStart, IEnumerable<(string Type, decimal Amount)> rows)
        {
            var list = rows.ToList();
            return new WeeklyIncomeRow
            {
                WeekStart = weekStart,
                Income = list.Where(x => x.Type == "income").Sum(x => x.Amount),
                Refund = list.Where(x => x.Type == "refund").Sum(x => x.Amount),
                Expense = list.Where(x => x.Type == "expense").Sum(x => x.Amount)
            };
        }

        private static MonthlyIncomeRow BuildMonthly(int year, int month, IEnumerable<(string Type, decimal Amount)> rows)
        {
            var list = rows.ToList();
            return new MonthlyIncomeRow
            {
                Year = year,
                Month = month,
                Income = list.Where(x => x.Type == "income").Sum(x => x.Amount),
                Refund = list.Where(x => x.Type == "refund").Sum(x => x.Amount),
                Expense = list.Where(x => x.Type == "expense").Sum(x => x.Amount)
            };
        }

        private static List<BreakdownItem> BuildBreakdown(IEnumerable<(string Category, decimal Amount)> rows) =>
            rows.GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "อื่นๆ" : x.Category)
                .Select(g => new { Label = g.Key, Amount = g.Sum(x => x.Amount) })
                .OrderByDescending(x => x.Amount)
                .Select((x, i) => new BreakdownItem
                {
                    Label = x.Label,
                    Amount = x.Amount,
                    Color = ChartPalette[i % ChartPalette.Length]
                })
                .ToList();

        private async Task<ReportsViewModel> BuildReportAsync(DateTime? from, DateTime? to)
        {
            var today = DateTime.Now.Date;
            var f = (from ?? new DateTime(today.Year, today.Month, 1)).Date;
            var t = (to ?? today).Date;
            if (t < f) (f, t) = (t, f);

            var fromD = DateOnly.FromDateTime(f);
            var toD = DateOnly.FromDateTime(t);
            var fromDt = f;
            var toDt = t.AddDays(1);

            var vm = new ReportsViewModel { From = f, To = t };

            var txnQuery = _db.FinancialTransactions.AsNoTracking()
                .Where(x => x.TxnDate >= fromD && x.TxnDate <= toD);

            var raw = await txnQuery.Select(x => new { x.TxnDate, x.TxnType, x.Amount, x.Category }).ToListAsync();

            vm.TotalIncome = raw.Where(x => x.TxnType == "income").Sum(x => x.Amount);
            vm.TotalRefund = raw.Where(x => x.TxnType == "refund").Sum(x => x.Amount);
            vm.TotalExpense = raw.Where(x => x.TxnType == "expense").Sum(x => x.Amount);

            // รายวัน
            vm.Daily = raw.GroupBy(x => x.TxnDate).OrderBy(g => g.Key)
                .Select(g => BuildDaily(g.Key, g.Select(x => (x.TxnType, x.Amount)))).ToList();

            // [เพิ่ม] รายสัปดาห์ (เริ่มวันจันทร์)
            vm.Weekly = raw.GroupBy(x => x.TxnDate.AddDays(-(((int)x.TxnDate.DayOfWeek + 6) % 7)))
                .OrderBy(g => g.Key)
                .Select(g => BuildWeekly(g.Key, g.Select(x => (x.TxnType, x.Amount)))).ToList();

            // รายเดือน
            vm.Monthly = raw.GroupBy(x => new { x.TxnDate.Year, x.TxnDate.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => BuildMonthly(g.Key.Year, g.Key.Month, g.Select(x => (x.TxnType, x.Amount)))).ToList();

            // [เพิ่ม] สัดส่วนรายรับ / รายจ่ายตามหมวด (Donut)
            vm.RevenueBreakdown = BuildBreakdown(raw.Where(x => x.TxnType == "income").Select(x => (x.Category, x.Amount)));
            vm.ExpenseBreakdown = BuildBreakdown(raw.Where(x => x.TxnType == "expense").Select(x => (x.Category, x.Amount)));

            // รายรับตามประเภทห้อง (booking ที่มี payment approved ในช่วงวันที่)
            vm.RoomTypes = await _db.BookingRooms.AsNoTracking()
                .Where(br => br.Booking.Payments.Any(p => p.Status == "approved"
                                                           && p.ReviewedAt >= fromDt && p.ReviewedAt < toDt))
                .GroupBy(br => br.RoomType.TypeName)
                .Select(g => new RoomTypeRevenueRow
                {
                    RoomTypeName = g.Key,
                    Rooms = g.Count(),
                    Nights = g.Sum(x => (int)x.Nights),
                    Revenue = g.Sum(x => x.LineTotal)
                })
                .OrderByDescending(x => x.Revenue)
                .ToListAsync();

            // ส่วนลดจากโปรโมชั่น
            vm.Promotions = await _db.BookingPromotions.AsNoTracking()
                .Where(bp => bp.Booking.Payments.Any(p => p.Status == "approved"
                                                           && p.ReviewedAt >= fromDt && p.ReviewedAt < toDt))
                .GroupBy(bp => new { bp.Promotion.PromoCode, bp.Promotion.PromoName })
                .Select(g => new PromotionDiscountRow
                {
                    PromoCode = g.Key.PromoCode,
                    PromoName = g.Key.PromoName,
                    UsedCount = g.Count(),
                    TotalDiscount = g.Sum(x => x.AppliedAmount)
                })
                .OrderByDescending(x => x.TotalDiscount)
                .ToListAsync();
            vm.TotalDiscount = vm.Promotions.Sum(x => x.TotalDiscount);

            // รายงานธุรกรรม (จำกัด 2,000 แถวเพื่อป้องกัน export ใหญ่เกินไป)
            vm.Transactions = await txnQuery
                .OrderBy(x => x.TxnDate).ThenBy(x => x.CreatedAt)
                .Select(TransactionRowExpr())
                .Take(2000)
                .ToListAsync();

            return vm;
        }

        // ---------------------------------------------------------------
        // Export: Excel
        // ---------------------------------------------------------------
        private static byte[] BuildReportExcel(ReportsViewModel vm)
        {
            using var wb = new XLWorkbook();

            var s = wb.Worksheets.Add("Summary");
            s.Cell(1, 1).Value = "StayMate - Financial Report";
            s.Cell(2, 1).Value = $"Period: {vm.From:dd/MM/yyyy} - {vm.To:dd/MM/yyyy}";
            WriteRows(s, 4, new[] { "Item", "Amount" }, new[]
            {
                new object[] { "Total Income", vm.TotalIncome },
                new object[] { "Total Refund", vm.TotalRefund },
                new object[] { "Net Income", vm.NetIncome },
                new object[] { "Total Expense", vm.TotalExpense },
                new object[] { "Net Profit", vm.NetProfit },
                new object[] { "Promotion Discount", vm.TotalDiscount }
            });

            WriteRows(wb.Worksheets.Add("Daily"), 1,
                new[] { "Date", "Income", "Refund", "Expense", "Net" },
                vm.Daily.Select(d => new object[] { d.Date.ToString("yyyy-MM-dd"), d.Income, d.Refund, d.Expense, d.Net }));

            WriteRows(wb.Worksheets.Add("Monthly"), 1,
                new[] { "Month", "Income", "Refund", "Expense", "Net" },
                vm.Monthly.Select(m => new object[] { m.Label, m.Income, m.Refund, m.Expense, m.Net }));

            WriteRows(wb.Worksheets.Add("RoomTypes"), 1,
                new[] { "Room Type", "Rooms", "Nights", "Revenue" },
                vm.RoomTypes.Select(r => new object[] { r.RoomTypeName, r.Rooms, r.Nights, r.Revenue }));

            WriteRows(wb.Worksheets.Add("Promotions"), 1,
                new[] { "Code", "Name", "Used", "Total Discount" },
                vm.Promotions.Select(p => new object[] { p.PromoCode, p.PromoName, p.UsedCount, p.TotalDiscount }));

            WriteRows(wb.Worksheets.Add("Transactions"), 1,
                new[] { "Date", "Type", "Category", "Amount", "Booking", "Description", "Recorded By" },
                vm.Transactions.Select(t => new object[]
                {
                    t.TxnDate.ToString("yyyy-MM-dd"), t.TxnType, t.Category, t.Amount,
                    t.BookingCode ?? "", t.Description ?? "", t.RecordedByName ?? ""
                }));

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static void WriteRows(IXLWorksheet ws, int startRow, string[] headers, IEnumerable<object[]> rows)
        {
            for (var c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(startRow, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            }

            var r = startRow + 1;
            foreach (var row in rows)
            {
                for (var c = 0; c < row.Length; c++)
                {
                    var cell = ws.Cell(r, c + 1);
                    switch (row[c])
                    {
                        case decimal d: cell.Value = d; cell.Style.NumberFormat.Format = "#,##0.00"; break;
                        case int i: cell.Value = i; break;
                        default: cell.Value = row[c]?.ToString() ?? ""; break;
                    }
                }
                r++;
            }
            ws.Columns().AdjustToContents();
        }

        // ---------------------------------------------------------------
        // Export: PDF
        // ---------------------------------------------------------------
        private static byte[] BuildReportPdf(ReportsViewModel vm)
        {
            return Document.Create(doc => doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Tahoma")); // ใช้ฟอนต์ที่รองรับภาษาไทย

                page.Header().Column(c =>
                {
                    c.Item().Text("StayMate - รายงานการเงิน").FontSize(16).Bold();
                    c.Item().Text($"ช่วงวันที่ {vm.From:dd/MM/yyyy} - {vm.To:dd/MM/yyyy}");
                });

                page.Content().PaddingVertical(8).Column(col =>
                {
                    col.Spacing(6);

                    AddTable(col, "สรุปภาพรวม", new[] { "รายการ", "จำนวนเงิน (บาท)" }, new[]
                    {
                        new[] { "รายรับรวม", Money(vm.TotalIncome) },
                        new[] { "คืนเงิน", Money(vm.TotalRefund) },
                        new[] { "รายรับสุทธิ", Money(vm.NetIncome) },
                        new[] { "รายจ่าย", Money(vm.TotalExpense) },
                        new[] { "ผลประกอบการสุทธิ", Money(vm.NetProfit) },
                        new[] { "ส่วนลดจากโปรโมชั่น", Money(vm.TotalDiscount) }
                    });

                    AddTable(col, "รายรับรายวัน", new[] { "วันที่", "รายรับ", "คืนเงิน", "รายจ่าย", "สุทธิ" },
                        vm.Daily.Select(d => new[] { d.Date.ToString("dd/MM/yyyy"), Money(d.Income), Money(d.Refund), Money(d.Expense), Money(d.Net) }));

                    AddTable(col, "รายรับรายเดือน", new[] { "เดือน", "รายรับ", "คืนเงิน", "รายจ่าย", "สุทธิ" },
                        vm.Monthly.Select(m => new[] { m.Label, Money(m.Income), Money(m.Refund), Money(m.Expense), Money(m.Net) }));

                    AddTable(col, "รายรับตามประเภทห้อง", new[] { "ประเภทห้อง", "จำนวนห้อง", "จำนวนคืน", "รายรับ" },
                        vm.RoomTypes.Select(r => new[] { r.RoomTypeName, r.Rooms.ToString(), r.Nights.ToString(), Money(r.Revenue) }));

                    AddTable(col, "ส่วนลดจากโปรโมชั่น", new[] { "รหัส", "ชื่อ", "ใช้ (ครั้ง)", "ส่วนลดรวม" },
                        vm.Promotions.Select(p => new[] { p.PromoCode, p.PromoName, p.UsedCount.ToString(), Money(p.TotalDiscount) }));

                    AddTable(col, "รายงานธุรกรรม", new[] { "วันที่", "ประเภท", "หมวด", "Booking", "จำนวนเงิน" },
                        vm.Transactions.Select(t => new[] { t.TxnDate.ToString("dd/MM/yyyy"), t.TxnType, t.Category, t.BookingCode ?? "-", Money(t.Amount) }));
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("หน้า ");
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            })).GeneratePdf();
        }

        private static void AddTable(ColumnDescriptor col, string title, string[] headers, IEnumerable<string[]> rows)
        {
            col.Item().PaddingTop(8).Text(title).Bold().FontSize(11);
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c => { foreach (var _ in headers) c.RelativeColumn(); });

                t.Header(h =>
                {
                    foreach (var x in headers)
                        h.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text(x).Bold();
                });

                foreach (var r in rows)
                    foreach (var cell in r)
                        t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(3).Text(cell);
            });
        }

        // ==========================================
        // 7. PROFILE (โปรไฟล์ของเจ้าหน้าที่การเงิน)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var model = await LoadProfileAsync();
            if (model == null)
                return RedirectToAction("Login", "Account");

            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(string? fullName, string? phone)
        {
            var userId = CurrentUserId();
            if (userId == null)
                return RedirectToAction("Login", "Account");

            if (string.IsNullOrWhiteSpace(fullName))
            {
                TempData["ErrorMessage"] = "กรุณากรอกชื่อ-นามสกุล";
                return RedirectToAction(nameof(Profile));
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value);
            if (user == null)
                return RedirectToAction("Login", "Account");

            user.FullName = fullName.Trim();
            user.Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
            user.UpdatedAt = DateTime.Now;

            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = "บันทึกข้อมูลโปรไฟล์เรียบร้อยแล้ว";
            return RedirectToAction(nameof(Profile));
        }

        private async Task<FinanceProfileViewModel?> LoadProfileAsync()
        {
            var userId = CurrentUserId();
            if (userId == null)
                return null;

            return await _db.Users
                .AsNoTracking()
                .Where(u => u.UserId == userId.Value)
                .Select(u => new FinanceProfileViewModel
                {
                    UserId = u.UserId,
                    Username = u.Username,
                    FullName = u.FullName,
                    Email = u.Email,
                    Phone = u.Phone,
                    IsActive = u.IsActive ?? false,
                    CreatedAt = u.CreatedAt,
                    RoleName = u.UserRoles.Select(ur => ur.Role.RoleName).FirstOrDefault() ?? ""
                })
                .FirstOrDefaultAsync();
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(string? oldPassword, string? newPassword, string? confirmPassword)
        {
            var userId = CurrentUserId();
            if (userId == null)
                return RedirectToAction("Login", "Account");

            string? error = null;

            if (string.IsNullOrWhiteSpace(oldPassword) ||
                string.IsNullOrWhiteSpace(newPassword) ||
                string.IsNullOrWhiteSpace(confirmPassword))
            {
                error = "กรุณากรอกรหัสผ่านให้ครบถ้วนทุกช่อง";
            }
            else if (newPassword.Length < 6)
            {
                error = "รหัสผ่านใหม่ต้องมีอย่างน้อย 6 ตัวอักษร";
            }
            else if (newPassword != confirmPassword)
            {
                error = "รหัสผ่านใหม่และการยืนยันรหัสผ่านไม่ตรงกัน";
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value);
            if (user == null)
                return RedirectToAction("Login", "Account");

            var hasher = new PasswordHasher<User>();

            if (error == null)
            {
                var verify = hasher.VerifyHashedPassword(user, user.PasswordHash, oldPassword!);
                if (verify == PasswordVerificationResult.Failed)
                    error = "รหัสผ่านเดิมไม่ถูกต้อง";
            }

            if (error != null)
            {
                TempData["ErrorMessage"] = error;
                TempData["OpenPasswordModal"] = true;
                return RedirectToAction(nameof(Profile));
            }

            user.PasswordHash = hasher.HashPassword(user, newPassword!);
            user.UpdatedAt = DateTime.Now;

            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = "เปลี่ยนรหัสผ่านเรียบร้อยแล้ว";
            return RedirectToAction(nameof(Profile));
        }

        private static string Money(decimal v) => v.ToString("N2");
    }
}