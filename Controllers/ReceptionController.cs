using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayMate_Kavinvee_0576.Models;
using StayMate_Kavinvee_0576.ViewModels;
using Microsoft.AspNetCore.Identity;

namespace StayMate_Kavinvee_0576.Controllers
{
    [Authorize(Roles = "FRONT_ADMIN")]
    public class ReceptionController : Controller
    {
        private readonly StayMateDbContext _context;

        public ReceptionController(StayMateDbContext context)
        {
            _context = context;
        }

        // ==========================================
        // HELPERS: ประวัติการเปลี่ยนสถานะห้อง + Audit Log (ตามสเปกข้อ 9.7)
        // ------------------------------------------
        // หมายเหตุ: ชื่อ DbSet/Property ด้านล่างเป็นการสมมติจากชื่อ Model ในสเปก
        // (RoomStatusHistory.cs, AuditLog.cs) หากชื่อจริงต่างกัน แก้เฉพาะ 2 เมธอดนี้ที่เดียว
        // ==========================================

        private void LogRoomStatus(Room room, string oldStatus, string newStatus, string? note)
        {
            _context.RoomStatusHistories.Add(new RoomStatusHistory
            {
                RoomId = room.RoomId,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                ChangedBy = GetCurrentUserId(),
                ChangedAt = DateTime.Now,
                Note = note
            });
        }

        private void LogAudit(string action, string entityType, uint entityId, string? detail)
        {
            _context.AuditLogs.Add(new AuditLog
            {
                UserId = GetCurrentUserId(),
                TableName = entityType,
                Action = $"{action}:{entityType}#{entityId}",
                CreatedAt = DateTime.Now
            });
        }

        private static string NormalizePayment(string? status)
        {
            var s = (status ?? "").Trim().ToLowerInvariant();
            return s switch
            {
                "approved" or "paid" or "verified" => "approved",
                "rejected" or "failed" => "rejected",
                _ => "pending"
            };
        }

        private static bool IsApprovedPayment(string? status) => NormalizePayment(status) == "approved";

        private static bool IsRoomStatus(string? status, string expected) =>
            string.Equals(status?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

        // ==========================================
        // 1. DASHBOARD
        // ==========================================

        public async Task<IActionResult> Index()
        {
            var today = DateOnly.FromDateTime(DateTime.Today);

            var model = new ReceptionIndexViewModel
            {
                TotalRooms = await _context.Rooms
                    .CountAsync(r => r.IsActive == true),

                AvailableRooms = await _context.Rooms
                    .CountAsync(r => r.IsActive == true &&
                                     r.Status == "available"),

                OccupiedRooms = await _context.Rooms
                    .CountAsync(r => r.IsActive == true &&
                                     r.Status == "occupied"),

                NeedsCleaningRooms = await _context.Rooms
                    .CountAsync(r => r.IsActive == true &&
                                     r.Status == "needs_cleaning"),

                MaintenanceRooms = await _context.Rooms
                    .CountAsync(r => r.IsActive == true &&
                                     r.Status == "maintenance"),

                TodayCheckIns = await _context.Bookings
                    .CountAsync(b => b.CheckInDate == today &&
                                     b.Status == "confirmed"),

                TodayCheckOuts = await _context.StayRecords
                    .CountAsync(s => s.CheckedOutAt == null &&
                                     s.Booking.CheckOutDate == today),

                PendingBookings = await _context.Bookings
                    .CountAsync(b => b.Status == "pending_approval"),

                ConfirmedBookings = await _context.Bookings
                    .CountAsync(b => b.Status == "confirmed")
            };

            model.RecentBookings = await _context.Bookings
                .AsNoTracking()
                .OrderByDescending(b => b.CreatedAt)
                .Take(10)
                .Select(b => new ReceptionBookingViewModel
                {
                    BookingId = b.BookingId,
                    BookingCode = b.BookingCode,
                    ContactName = b.ContactName,
                    ContactPhone = b.ContactPhone,
                    CheckInDate = b.CheckInDate,
                    CheckOutDate = b.CheckOutDate,
                    Nights = b.Nights,
                    NumGuests = b.NumGuests,
                    TotalAmount = b.TotalAmount,
                    Status = b.Status
                })
                .ToListAsync();

            // Today's Check-in (เฉพาะ confirmed = ผ่านการอนุมัติชำระเงินแล้ว)
            model.CheckInList = await _context.Bookings
                .AsNoTracking()
                .Where(b => b.Status == "confirmed" && b.CheckInDate == today)
                .SelectMany(b => b.BookingRooms.Select(br => new ReceptionStayViewModel
                {
                    BookingId = b.BookingId,
                    BookingCode = b.BookingCode,
                    GuestName = b.ContactName,
                    GuestPhone = b.ContactPhone,
                    RoomId = br.RoomId ?? 0,
                    RoomNumber = br.Room != null ? br.Room.RoomNumber : "ยังไม่กำหนดห้อง",
                    RoomTypeName = br.RoomType.TypeName,
                    CheckInDate = b.CheckInDate,
                    CheckOutDate = b.CheckOutDate,
                    NumGuests = b.NumGuests
                }))
                .ToListAsync();

            // Today's Check-out
            model.CheckOutList = await _context.StayRecords
                .AsNoTracking()
                .Where(s => s.CheckedOutAt == null && s.Booking.CheckOutDate == today)
                .OrderBy(s => s.CheckedInAt)
                .Select(s => new ReceptionStayViewModel
                {
                    StayId = s.StayId,
                    BookingId = s.BookingId,
                    BookingCode = s.Booking.BookingCode,
                    GuestName = s.Booking.ContactName,
                    GuestPhone = s.Booking.ContactPhone,
                    RoomId = s.RoomId,
                    RoomNumber = s.Room.RoomNumber,
                    RoomTypeName = s.Room.RoomType.TypeName,
                    CheckInDate = s.Booking.CheckInDate,
                    CheckOutDate = s.Booking.CheckOutDate,
                    CheckedInAt = s.CheckedInAt,
                    NumGuests = s.Booking.NumGuests,
                    ActualGuests = s.ActualGuests
                })
                .ToListAsync();

            // Room Status Overview
            model.Rooms = await _context.Rooms
                .AsNoTracking()
                .Where(r => r.IsActive == true)
                .OrderBy(r => r.FloorNo)
                .ThenBy(r => r.RoomNumber)
                .Select(r => new ReceptionRoomViewModel
                {
                    RoomId = r.RoomId,
                    RoomNumber = r.RoomNumber,
                    FloorNo = r.FloorNo,
                    RoomTypeName = r.RoomType.TypeName,
                    Status = r.Status,
                    IsActive = r.IsActive
                })
                .ToListAsync();

            return View(model);
        }

        // ==========================================
        // 2. ROOM CALENDAR (ห้องพัก + Booking Bar)
        // ==========================================

        public async Task<IActionResult> Calendar(string? view, DateOnly? startDate)
        {
            var daysCount = (view ?? "week").ToLowerInvariant() switch { "day" => 1, "month" => 30, _ => 7 };
            var start = startDate ?? DateOnly.FromDateTime(DateTime.Today);
            var end = start.AddDays(daysCount - 1);

            var rooms = await _context.Rooms
                .AsNoTracking()
                .Where(r => r.IsActive == true)
                .OrderBy(r => r.FloorNo)
                .ThenBy(r => r.RoomNumber)
                .Select(r => new ReceptionRoomViewModel
                {
                    RoomId = r.RoomId,
                    RoomNumber = r.RoomNumber,
                    FloorNo = r.FloorNo,
                    RoomTypeName = r.RoomType.TypeName,
                    Status = r.Status,
                    IsActive = r.IsActive
                })
                .ToListAsync();

            // การจองที่ยืนยันแล้วและกำหนดห้องแล้ว (Reserved) ที่ซ้อนทับช่วงวันที่เลือก
            var reservedStays = await _context.Bookings
                .AsNoTracking()
                .Where(b => b.Status == "confirmed" &&
                            b.CheckInDate <= end &&
                            b.CheckOutDate > start)
                .SelectMany(b => b.BookingRooms
                    .Where(br => br.RoomId != null)
                    .Select(br => new ReceptionStayViewModel
                    {
                        BookingId = b.BookingId,
                        BookingCode = b.BookingCode,
                        BookingStatus = b.Status,
                        PaymentStatus = b.Payments
                            .OrderByDescending(p => p.CreatedAt)
                            .Select(p => p.Status)
                            .FirstOrDefault() ?? "pending",
                        GuestName = b.ContactName,
                        GuestPhone = b.ContactPhone,
                        RoomId = br.RoomId ?? 0,
                        RoomNumber = br.Room != null ? br.Room.RoomNumber : "",
                        RoomTypeName = br.RoomType.TypeName,
                        CheckInDate = b.CheckInDate,
                        CheckOutDate = b.CheckOutDate,
                        NumGuests = b.NumGuests
                    }))
                .ToListAsync();

            // กำลังเข้าพักจริง (Occupied) แสดงเสมอจนกว่าจะ Check-out
            var activeStays = await _context.StayRecords
                .AsNoTracking()
                .Where(s => s.CheckedOutAt == null)
                .Select(s => new ReceptionStayViewModel
                {
                    StayId = s.StayId,
                    BookingId = s.BookingId,
                    BookingCode = s.Booking.BookingCode,
                    BookingStatus = s.Booking.Status,
                    PaymentStatus = "approved",
                    GuestName = s.Booking.ContactName,
                    GuestPhone = s.Booking.ContactPhone,
                    RoomId = s.RoomId,
                    RoomNumber = s.Room.RoomNumber,
                    RoomTypeName = s.Room.RoomType.TypeName,
                    CheckInDate = s.Booking.CheckInDate,
                    CheckOutDate = s.Booking.CheckOutDate,
                    CheckedInAt = s.CheckedInAt,
                    NumGuests = s.Booking.NumGuests,
                    ActualGuests = s.ActualGuests
                })
                .ToListAsync();

            ViewBag.CalendarStart = start;
            ViewBag.CalendarEnd = end;
            ViewBag.CalendarView = (view ?? "week").ToLowerInvariant();

            var model = new ReceptionIndexViewModel
            {
                TotalRooms = rooms.Count,
                AvailableRooms = rooms.Count(r => r.Status == "available"),
                OccupiedRooms = rooms.Count(r => r.Status == "occupied"),
                NeedsCleaningRooms = rooms.Count(r => r.Status == "needs_cleaning"),
                MaintenanceRooms = rooms.Count(r => r.Status == "maintenance"),
                ConfirmedBookings = reservedStays.Count,
                Rooms = rooms,
                CheckInList = reservedStays,
                CheckOutList = activeStays
            };

            return View("Calendar", model);
        }

        // ==========================================
        // 3. BOOKING MANAGEMENT
        // route ให้ตรงกับที่ JS เรียก: /Reception/Bookings
        // ==========================================

        [HttpGet("Reception/Bookings")]
        public async Task<IActionResult> Bookings(string? search)
        {
            var query = _context.Bookings
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var keyword = search.Trim();
                query = query.Where(b =>
                    b.BookingCode.Contains(keyword) ||
                    b.ContactName.Contains(keyword) ||
                    b.ContactPhone.Contains(keyword) ||
                    b.BookingRooms.Any(br => br.Room != null && br.Room.RoomNumber.Contains(keyword)));
            }

            var bookings = await query
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new ReceptionBookingViewModel
                {
                    BookingId = b.BookingId,
                    BookingCode = b.BookingCode,
                    ContactName = b.ContactName,
                    ContactPhone = b.ContactPhone,
                    ContactEmail = b.ContactEmail,
                    CheckInDate = b.CheckInDate,
                    CheckOutDate = b.CheckOutDate,
                    Nights = b.Nights,
                    NumGuests = b.NumGuests,
                    DiscountAmount = b.DiscountAmount,
                    TaxAmount = b.TaxAmount,
                    TotalAmount = b.TotalAmount,
                    Status = b.Status,
                    CreatedAt = b.CreatedAt,
                    GuestCount = b.BookingGuests.Count(),
                    RoomNumbers = b.BookingRooms
                        .Where(br => br.Room != null)
                        .Select(br => br.Room!.RoomNumber)
                        .ToList(),
                    RoomTypes = b.BookingRooms
                        .Select(br => br.RoomType.TypeName)
                        .Distinct()
                        .ToList(),
                    PaymentStatus = b.Payments
                        .OrderByDescending(p => p.CreatedAt)
                        .Select(p => p.Status)
                        .FirstOrDefault() ?? "pending",
                    PaymentSubmittedAt = b.Payments
                        .OrderByDescending(p => p.CreatedAt)
                        .Select(p => (DateTime?)p.CreatedAt)
                        .FirstOrDefault(),
                    CheckedInAt = b.StayRecords
                        .OrderByDescending(s => s.CheckedInAt)
                        .Select(s => (DateTime?)s.CheckedInAt)
                        .FirstOrDefault(),
                    CheckedOutAt = b.StayRecords
                        .OrderByDescending(s => s.CheckedInAt)
                        .Select(s => s.CheckedOutAt)
                        .FirstOrDefault()
                })
                .ToListAsync();

            var model = new ReceptionIndexViewModel
            {
                SearchTerm = search,
                Bookings = bookings,
                ConfirmedBookings = bookings.Count(b => b.Status == "confirmed"),
                PendingBookings = bookings.Count(b => b.Status == "pending_approval")
            };

            return View("Bookings", model);
        }

        // ==========================================
        // 3.1 VERIFY BOOKING QR (ตรวจที่ Backend เสมอ)
        // ==========================================

        [HttpGet]
        public async Task<IActionResult> VerifyBookingQr(string? qrPayload)
        {
            if (string.IsNullOrWhiteSpace(qrPayload))
            {
                return Json(new { success = false, message = "QR Code ไม่ถูกต้อง หรือไม่มีรหัสอ้างอิง" });
            }

            var today = DateOnly.FromDateTime(DateTime.Today);

            // ใช้เฉพาะรหัสอ้างอิง ไม่เชื่อชื่อหรือยอดเงินที่อาจแนบมาใน QR
            var code = qrPayload.Trim();
            if (code.Contains('|'))
            {
                code = code.Split('|')[0].Trim();
            }

            var booking = await _context.Bookings
                .AsNoTracking()
                .Where(b => b.BookingCode == code)
                .Select(b => new
                {
                    b.BookingId,
                    b.BookingCode,
                    b.ContactName,
                    b.ContactPhone,
                    b.ContactEmail,
                    b.CheckInDate,
                    b.CheckOutDate,
                    b.Nights,
                    b.NumGuests,
                    b.DiscountAmount,
                    b.TaxAmount,
                    b.TotalAmount,
                    b.Status,
                    b.CreatedAt,
                    RoomNumbers = b.BookingRooms.Where(br => br.Room != null).Select(br => br.Room!.RoomNumber).ToList(),
                    RoomTypes = b.BookingRooms.Select(br => br.RoomType.TypeName).Distinct().ToList(),
                    PaymentStatus = b.Payments.OrderByDescending(p => p.CreatedAt).Select(p => p.Status).FirstOrDefault() ?? "pending",
                    PaymentSubmittedAt = b.Payments.OrderByDescending(p => p.CreatedAt).Select(p => (DateTime?)p.CreatedAt).FirstOrDefault(),
                    CheckedInAt = b.StayRecords.OrderByDescending(s => s.CheckedInAt).Select(s => (DateTime?)s.CheckedInAt).FirstOrDefault(),
                    CheckedOutAt = b.StayRecords.OrderByDescending(s => s.CheckedInAt).Select(s => s.CheckedOutAt).FirstOrDefault(),
                    HasOpenStay = b.StayRecords.Any(s => s.CheckedOutAt == null)
                })
                .FirstOrDefaultAsync();

            if (booking == null)
            {
                return Json(new { success = false, message = $"ไม่พบรหัสการจอง '{code}' ในฐานข้อมูล" });
            }

            if (booking.Status == "cancelled")
            {
                return Json(new { success = false, message = $"การจองรหัส {booking.BookingCode} ถูกยกเลิกแล้ว" });
            }

            // ต้องเป็น Approved จาก Finance เท่านั้น (ไม่ใช้สถานะ booking แทน)
            bool isPaymentApproved = IsApprovedPayment(booking.PaymentStatus);

            bool dateAllowed = booking.CheckInDate <= today && booking.CheckOutDate > today;
            bool canCheckIn = booking.Status == "confirmed"
                              && isPaymentApproved
                              && dateAllowed
                              && !booking.HasOpenStay;
            bool canCheckOut = booking.HasOpenStay;

            var payNorm = NormalizePayment(booking.PaymentStatus);
            var bookNorm = booking.Status switch
            {
                "checked_out" or "completed" => "checked_out",
                "pending_approval" => "pending",
                var s => s
            };
            var bookLabel = bookNorm switch
            {
                "confirmed" => "Confirmed",
                "checked_in" => "Checked-in",
                "checked_out" => "Checked-out",
                "cancelled" => "Cancelled",
                _ => "Pending"
            };

            return Json(new
            {
                success = true,
                data = new
                {
                    bookingId = booking.BookingId,
                    bookingCode = booking.BookingCode,
                    guestName = booking.ContactName,
                    guestPhone = booking.ContactPhone,
                    guestEmail = booking.ContactEmail,
                    roomNumbers = booking.RoomNumbers.Any() ? string.Join(", ", booking.RoomNumbers) : "รอกำหนดห้อง",
                    roomTypes = booking.RoomTypes.Any() ? string.Join(", ", booking.RoomTypes) : "-",
                    checkInDisplay = booking.CheckInDate.ToString("dd/MM/yyyy"),
                    checkOutDisplay = booking.CheckOutDate.ToString("dd/MM/yyyy"),
                    nights = booking.Nights,
                    guests = booking.NumGuests,
                    totalAmount = booking.TotalAmount.ToString("N2"),
                    discountAmount = booking.DiscountAmount.ToString("N2"),
                    taxAmount = booking.TaxAmount.ToString("N2"),
                    paymentStatus = payNorm,
                    paymentLabel = char.ToUpper(payNorm[0]) + payNorm[1..],
                    bookingStatus = bookNorm,
                    bookingLabel = bookLabel,
                    createdAt = booking.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
                    paymentSubmittedAt = booking.PaymentSubmittedAt?.ToString("dd/MM/yyyy HH:mm") ?? "Waiting",
                    paymentApprovedAt = isPaymentApproved ? "อนุมัติแล้ว" : "Waiting",
                    checkedInAt = booking.CheckedInAt?.ToString("dd/MM/yyyy HH:mm") ?? "Waiting",
                    checkedOutAt = booking.CheckedOutAt?.ToString("dd/MM/yyyy HH:mm") ?? "Waiting",
                    canCheckIn,
                    canCheckOut
                }
            });
        }

        // ==========================================
        // 3.2 PROCESS CHECK-IN / CHECK-OUT
        // ==========================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessCheckInFromBooking(uint bookingId, uint? roomId)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);

            var booking = await _context.Bookings
                .Include(b => b.BookingRooms).ThenInclude(br => br.Room)
                .Include(b => b.BookingRooms).ThenInclude(br => br.RoomType)
                .Include(b => b.StayRecords)
                .Include(b => b.Payments)
                .FirstOrDefaultAsync(b => b.BookingId == bookingId);

            if (booking == null)
            {
                TempData["ErrorMessage"] = "ไม่พบข้อมูลการจองในระบบ";
                return RedirectToAction(nameof(Bookings));
            }

            IActionResult Fail(string msg)
            {
                TempData["ErrorMessage"] = msg;
                return RedirectToAction(nameof(CheckIn), new { bookingCode = booking.BookingCode });
            }

            // 2. ต้องไม่ถูกยกเลิก
            if (booking.Status == "cancelled")
                return Fail($"การจอง {booking.BookingCode} ถูกยกเลิกแล้ว ไม่สามารถ Check-in ได้");

            // 6. ต้องไม่ Check-in ซ้ำ
            if (booking.Status == "checked_in" || booking.StayRecords.Any(s => s.CheckedOutAt == null))
                return Fail($"การจอง {booking.BookingCode} ได้ดำเนินการ Check-in ไปแล้ว");

            if (booking.Status == "checked_out")
                return Fail($"การจอง {booking.BookingCode} ได้ Check-out ไปแล้ว");

            // 4. Payment ต้อง Approved (ตรวจจากตาราง Payment)
            var latestPayment = booking.Payments.OrderByDescending(p => p.CreatedAt).FirstOrDefault();
            if (latestPayment == null || !IsApprovedPayment(latestPayment.Status) || booking.Status != "confirmed")
                return Fail("ไม่สามารถ Check-in ได้ เนื่องจากการจองยังไม่ได้รับการอนุมัติการชำระเงินจากฝ่ายการเงิน");

            // 3. วันที่
            if (booking.CheckInDate > today)
                return Fail($"ยังไม่ถึงวันเข้าพักของการจอง {booking.BookingCode}");

            if (booking.CheckOutDate <= today)
                return Fail($"การจอง {booking.BookingCode} เลยกำหนดวันออกแล้ว ไม่สามารถ Check-in ได้");

            if (!booking.BookingRooms.Any())
                return Fail("การจองนี้ยังไม่มีห้องพัก");

            // กำหนดห้องให้การจองที่ยังไม่ระบุห้อง (ต้องเป็นประเภทเดียวกับที่จอง)
            bool assignedNow = false;
            if (roomId.HasValue)
            {
                var pick = await _context.Rooms
                    .Include(r => r.RoomType)
                    .FirstOrDefaultAsync(r => r.RoomId == roomId.Value && r.IsActive == true);

                if (pick == null)
                    return Fail("ไม่พบห้องพักที่เลือก");

                if (!IsRoomStatus(pick.Status, "available"))
                    return Fail($"ห้อง {pick.RoomNumber} ไม่อยู่ในสถานะ Available (สถานะ: {pick.Status})");

                if (booking.BookingRooms.Any(br => br.RoomId == pick.RoomId))
                    return Fail($"ห้อง {pick.RoomNumber} ถูกกำหนดให้การจองนี้แล้ว");

                var slot = booking.BookingRooms.FirstOrDefault(br =>
                    br.RoomId == null && br.RoomType.TypeName == pick.RoomType.TypeName);

                if (slot == null)
                    return Fail($"ไม่มีรายการห้องประเภท {pick.RoomType.TypeName} ที่รอกำหนดห้องในการจองนี้");

                slot.RoomId = pick.RoomId;
                slot.Room = pick;
                assignedNow = true;

                LogAudit("ASSIGN_ROOM", "Booking", booking.BookingId, $"กำหนดห้อง {pick.RoomNumber} ให้ {booking.BookingCode}");
            }

            // ยังมีห้องที่ไม่ได้กำหนด
            if (booking.BookingRooms.Any(br => br.Room == null))
            {
                if (assignedNow) await _context.SaveChangesAsync();
                return Fail(assignedNow
                    ? "กำหนดห้องให้แล้ว แต่ยังเหลือห้องที่ยังไม่กำหนด กรุณากำหนดห้องที่เหลือก่อน Check-in"
                    : "กรุณากำหนดห้องพักให้ครบทุกห้องก่อนดำเนินการ Check-in");
            }

            var rooms = booking.BookingRooms
                .Select(br => br.Room!)
                .GroupBy(r => r.RoomId)
                .Select(g => g.First())
                .ToList();

            var maintenanceRoom = rooms.FirstOrDefault(r => IsRoomStatus(r.Status, "maintenance"));
            if (maintenanceRoom != null)
                return Fail($"ห้อง {maintenanceRoom.RoomNumber} อยู่ในสถานะ Maintenance ไม่สามารถ Check-in ได้");

            var notReadyRoom = rooms.FirstOrDefault(r => !IsRoomStatus(r.Status, "available"));
            if (notReadyRoom != null)
                return Fail($"ห้อง {notReadyRoom.RoomNumber} ยังไม่พร้อมใช้งาน (สถานะ: {notReadyRoom.Status})");

            // ผ่านทุกเงื่อนไข
            var now = DateTime.Now;
            booking.Status = "checked_in";

            foreach (var room in rooms)
            {
                var oldStatus = room.Status;
                room.Status = "occupied";

                _context.StayRecords.Add(new StayRecord
                {
                    BookingId = booking.BookingId,
                    RoomId = room.RoomId,
                    CheckedInAt = now,
                    ActualGuests = booking.NumGuests
                });

                LogRoomStatus(room, oldStatus, "occupied", $"Check-in ({booking.BookingCode})");
            }

            LogAudit("CHECK_IN", "Booking", booking.BookingId,
                $"Check-in {booking.BookingCode} ห้อง {string.Join(", ", rooms.Select(r => r.RoomNumber))}");

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] =
                $"ดำเนินการ Check-in รหัส {booking.BookingCode} (ห้อง {string.Join(", ", rooms.Select(r => r.RoomNumber))}) สำเร็จ";
            
            return RedirectToAction(nameof(Bookings));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessCheckOutFromBooking(uint bookingId)
        {
            var now = DateTime.Now;

            // ค้นหารายการ StayRecord ที่กำลังเข้าพัก (ยังไม่ได้ Check-out)
            var openStays = await _context.StayRecords
                .Include(s => s.Booking)
                .Include(s => s.Room)
                .Where(s => s.BookingId == bookingId && s.CheckedOutAt == null)
                .ToListAsync();

            if (!openStays.Any())
            {
                TempData["ErrorMessage"] = "ไม่พบรายการกำลังเข้าพัก หรือดำเนินการ Check-out ไปแล้ว";
                return RedirectToAction(nameof(CheckOut));
            }

            var booking = openStays.First().Booking;
            
            // ตั้งเวลาจำกัด (Cutoff) เป็น 12:00 น. ของวัน CheckOutDate
            var checkOutCutoff = booking.CheckOutDate.ToDateTime(new TimeOnly(12, 0));

            // แก้ไข: ห้ามกด Check-out ก่อนถึงเวลา 12:00 น. ของวัน CheckOutDate
            if (now < checkOutCutoff)
            {
                TempData["ErrorMessage"] =
                    $"ยังไม่ถึงกำหนด Check-out ของการจอง {booking.BookingCode} (กดได้ตั้งแต่ {checkOutCutoff:dd/MM/yyyy HH:mm} น.)";
                return RedirectToAction(nameof(CheckOut));
            }

            // ทำการบันทึกเวลาออกและปิดห้องพัก
            foreach (var stay in openStays)
            {
                stay.CheckedOutAt = now;

                var oldStatus = stay.Room.Status;
                stay.Room.Status = "needs_cleaning"; // เปลี่ยนเป็นสถานะทำความสะอาด
                LogRoomStatus(stay.Room, oldStatus, "needs_cleaning", $"Check-out ({booking.BookingCode})");

                _context.HousekeepingTasks.Add(new HousekeepingTask
                {
                    RoomId = stay.RoomId,
                    BookingId = stay.BookingId,
                    Status = "pending",
                    Note = $"ทำความสะอาดหลังลูกค้า Check-out ({booking.BookingCode})",
                    CreatedAt = now
                });
            }

            booking.Status = "checked_out";

            LogAudit("CHECK_OUT", "Booking", booking.BookingId,
                $"Check-out {booking.BookingCode} ห้อง {string.Join(", ", openStays.Select(s => s.Room.RoomNumber))}");

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] =
                $"Check-out ห้อง {string.Join(", ", openStays.Select(s => s.Room.RoomNumber))} สำเร็จ เปลี่ยนสถานะห้องเป็น Cleaning และส่งงานไปยังแผนกแม่บ้านเรียบร้อยแล้ว";
            
            return RedirectToAction(nameof(Bookings));
        }

        // ==========================================
        // 4. CHECK-IN
        // ==========================================

        public async Task<IActionResult> CheckIn(
            string? bookingCode,
            string? guestName,
            string? roomNumber)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);

            var query = _context.Bookings
                .AsNoTracking()
                .Where(b => (b.Status == "confirmed" || b.Status == "checked_in") &&
                            b.CheckInDate <= today &&
                            b.CheckOutDate > today);

            if (!string.IsNullOrWhiteSpace(bookingCode))
            {
                var code = bookingCode.Trim();
                query = query.Where(b => b.BookingCode.Contains(code));
            }

            if (!string.IsNullOrWhiteSpace(guestName))
            {
                var name = guestName.Trim();
                query = query.Where(b => b.ContactName.Contains(name));
            }

            var checkInList = await query
                .OrderBy(b => b.CheckInDate)
                .SelectMany(b => b.BookingRooms.Select(br =>
                    new ReceptionStayViewModel
                    {
                        BookingId = b.BookingId,
                        BookingCode = b.BookingCode,
                        BookingStatus = b.Status,
                        PaymentStatus = b.Payments
                            .OrderByDescending(p => p.CreatedAt)
                            .Select(p => p.Status)
                            .FirstOrDefault() ?? "pending",
                        GuestName = b.ContactName,
                        GuestPhone = b.ContactPhone,
                        RoomId = br.RoomId ?? 0,
                        RoomNumber = br.Room != null
                            ? br.Room.RoomNumber : "ยังไม่กำหนดห้อง",
                        RoomStatus = br.Room != null
                            ? br.Room.Status : "",
                        RoomTypeName = br.RoomType.TypeName,
                        CheckInDate = b.CheckInDate,
                        CheckOutDate = b.CheckOutDate,
                        NumGuests = b.NumGuests
                    }))
                .ToListAsync();

            // ค้นหาด้วยหมายเลขห้อง (กรองในหน่วยความจำ)
            if (!string.IsNullOrWhiteSpace(roomNumber))
            {
                var room = roomNumber.Trim();
                checkInList = checkInList
                    .Where(x => x.RoomNumber.Contains(room, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var availableRooms = await _context.Rooms
                .AsNoTracking()
                .Where(r => r.IsActive == true && r.Status == "available")
                .OrderBy(r => r.FloorNo)
                .ThenBy(r => r.RoomNumber)
                .Select(r => new ReceptionRoomViewModel
                {
                    RoomId = r.RoomId,
                    RoomNumber = r.RoomNumber,
                    FloorNo = r.FloorNo,
                    RoomTypeName = r.RoomType.TypeName,
                    Status = r.Status,
                    IsActive = r.IsActive
                })
                .ToListAsync();

            var model = new ReceptionIndexViewModel
            {
                CheckInList = checkInList,
                Rooms = availableRooms,
                AvailableRooms = availableRooms.Count,
                TodayCheckIns = await _context.Bookings
                    .CountAsync(b => b.CheckInDate == today && b.Status == "confirmed"),
                CompletedCheckInsCount = await _context.StayRecords
                    .CountAsync(s => s.CheckedOutAt == null)
            };

            return View(model);
        }

        // ==========================================
        // 5. CHECK-OUT
        // ==========================================

        public async Task<IActionResult> CheckOut(string? guestName, string? bookingCode, string? roomNumber, string? statusFilter)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);

            // กรองที่ฐานข้อมูลก่อน แล้วค่อย Take(100) เพื่อไม่ให้ค้นหาพลาดข้อมูลเก่า
            var baseQuery = _context.StayRecords.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(guestName))
            {
                var kw = guestName.Trim();
                baseQuery = baseQuery.Where(s => s.Booking.ContactName.Contains(kw));
            }
            if (!string.IsNullOrWhiteSpace(bookingCode))
            {
                var kw = bookingCode.Trim();
                baseQuery = baseQuery.Where(s => s.Booking.BookingCode.Contains(kw));
            }
            if (!string.IsNullOrWhiteSpace(roomNumber))
            {
                var kw = roomNumber.Trim();
                baseQuery = baseQuery.Where(s => s.Room.RoomNumber.Contains(kw));
            }

            var allStays = await baseQuery
                .OrderBy(s => s.CheckedOutAt != null) // รายการที่ยังไม่ Check-out ขึ้นก่อน
                .ThenBy(s => s.Booking.CheckOutDate)
                .ThenByDescending(s => s.CheckedInAt)
                .Take(100)
                .Select(s => new ReceptionStayViewModel
                {
                    StayId = s.StayId,
                    BookingId = s.BookingId,
                    BookingCode = s.Booking.BookingCode,
                    GuestName = s.Booking.ContactName,
                    GuestPhone = s.Booking.ContactPhone,
                    RoomId = s.RoomId,
                    RoomNumber = s.Room.RoomNumber,
                    RoomTypeName = s.Room.RoomType.TypeName,
                    CheckInDate = s.Booking.CheckInDate,
                    CheckOutDate = s.Booking.CheckOutDate,
                    CheckedInAt = s.CheckedInAt,
                    CheckedOutAt = s.CheckedOutAt,
                    NumGuests = s.Booking.NumGuests,
                    ActualGuests = s.ActualGuests
                })
                .ToListAsync();

            var model = new ReceptionIndexViewModel
            {
                // Card 1: จำนวนห้องที่กำลังมีผู้เข้าพัก
                OccupiedRooms = await _context.Rooms.CountAsync(r => r.IsActive == true && r.Status == "occupied"),

                // Card 2: รายการที่ต้อง Check-out วันนี้
                TodayCheckOuts = await _context.StayRecords.CountAsync(s => s.CheckedOutAt == null && s.Booking.CheckOutDate == today),

                CheckOutList = allStays
            };

            return View(model);
        }

        // ==========================================
        // 6. HOUSEKEEPING REQUESTS (แจ้งงานแม่บ้าน)
        // ==========================================

        public async Task<IActionResult> HousekeepingRequests()
        {
            var model = new ReceptionIndexViewModel
            {
                // ใช้เป็นตัวเลือกห้องในฟอร์มแจ้งงาน
                Rooms = await _context.Rooms
                    .AsNoTracking()
                    .Where(r => r.IsActive == true)
                    .OrderBy(r => r.FloorNo)
                    .ThenBy(r => r.RoomNumber)
                    .Select(r => new ReceptionRoomViewModel
                    {
                        RoomId = r.RoomId,
                        RoomNumber = r.RoomNumber,
                        FloorNo = r.FloorNo,
                        RoomTypeName = r.RoomType.TypeName,
                        Status = r.Status,
                        IsActive = r.IsActive
                    })
                    .ToListAsync(),

                HousekeepingRequests = await _context.HousekeepingTasks
                    .AsNoTracking()
                    .OrderByDescending(t => t.CreatedAt)
                    .Select(t => new ReceptionTaskViewModel
                    {
                        TaskId = t.TaskId,
                        RoomId = t.RoomId,
                        RoomNumber = t.Room.RoomNumber,
                        BookingId = t.BookingId,
                        BookingCode = t.Booking != null
                            ? t.Booking.BookingCode : null,
                        Status = t.Status,
                        Note = t.Note,
                        CreatedAt = t.CreatedAt,
                        StartedAt = t.StartedAt,
                        CompletedAt = t.CompletedAt,
                        AssignedToName = t.AssignedToNavigation != null
                            ? t.AssignedToNavigation.FullName : null
                    })
                    .ToListAsync()
            };

            return View("HousekeepingRequests", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateHousekeepingRequest(uint roomId, string? note)
        {
            var room = await _context.Rooms
                .FirstOrDefaultAsync(r => r.RoomId == roomId && r.IsActive == true);

            if (room == null)
            {
                TempData["ErrorMessage"] = "ไม่พบห้องพักที่เลือก";
                return RedirectToAction(nameof(HousekeepingRequests));
            }

            _context.HousekeepingTasks.Add(new HousekeepingTask
            {
                RoomId = room.RoomId,
                Status = "pending",
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                CreatedAt = DateTime.Now
            });

            LogAudit("CREATE_HOUSEKEEPING_REQUEST", "Room", room.RoomId,
                $"แจ้งงานแม่บ้านห้อง {room.RoomNumber}");

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"แจ้งงานแม่บ้านสำหรับห้อง {room.RoomNumber} เรียบร้อยแล้ว";
            return RedirectToAction(nameof(HousekeepingRequests));
        }

        // ==========================================
        // 7. PROFILE (โปรไฟล์ของฉัน)
        // ==========================================

        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var model = await LoadProfileAsync();
            if (model == null)
            {
                return RedirectToAction("Login", "Account");
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(string? fullName, string? phone)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return RedirectToAction("Login", "Account");
            }

            if (string.IsNullOrWhiteSpace(fullName))
            {
                TempData["ErrorMessage"] = "กรุณากรอกชื่อ-นามสกุล";
                return RedirectToAction(nameof(Profile));
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            user.FullName = fullName.Trim();
            user.Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
            user.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "บันทึกข้อมูลโปรไฟล์เรียบร้อยแล้ว";
            return RedirectToAction(nameof(Profile));
        }

        private uint? GetCurrentUserId()
        {
            var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return uint.TryParse(value, out var id) ? id : null;
        }

        private async Task<ReceptionProfileViewModel?> LoadProfileAsync()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return null;
            }

            return await _context.Users
                .AsNoTracking()
                .Where(u => u.UserId == userId.Value)
                .Select(u => new ReceptionProfileViewModel
                {
                    UserId = u.UserId,
                    Username = u.Username,
                    FullName = u.FullName,
                    Email = u.Email,
                    Phone = u.Phone,
                    IsActive = u.IsActive ?? false,
                    CreatedAt = u.CreatedAt,
                    RoleName = u.UserRoles
                        .Select(ur => ur.Role.RoleName)
                        .FirstOrDefault() ?? ""
                })
                .FirstOrDefaultAsync();
        }

        // ==========================================
        // 7.1 CHANGE PASSWORD (เปลี่ยนรหัสผ่านพนักงานต้อนรับ)
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(string? oldPassword, string? newPassword, string? confirmPassword)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return RedirectToAction("Login", "Account");
            }

            string? error = null;
            if (string.IsNullOrWhiteSpace(oldPassword) || string.IsNullOrWhiteSpace(newPassword) || string.IsNullOrWhiteSpace(confirmPassword))
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

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var hasher = new PasswordHasher<User>();

            if (error == null)
            {
                var verify = hasher.VerifyHashedPassword(user, user.PasswordHash, oldPassword!);
                if (verify == PasswordVerificationResult.Failed)
                {
                    error = "รหัสผ่านเดิมไม่ถูกต้อง";
                }
            }

            if (error != null)
            {
                TempData["ErrorMessage"] = error;
                TempData["OpenPasswordModal"] = true;
                return RedirectToAction(nameof(Profile));
            }

            user.PasswordHash = hasher.HashPassword(user, newPassword!);
            user.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "เปลี่ยนรหัสผ่านเรียบร้อยแล้ว";
            return RedirectToAction(nameof(Profile));
        }
    }
}
