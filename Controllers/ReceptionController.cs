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

        public async Task<IActionResult> RoomCalendar()
        {
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

            // การจองที่ยืนยันแล้วและกำหนดห้องแล้ว (Reserved)
            var reservedStays = await _context.Bookings
                .AsNoTracking()
                .Where(b => b.Status == "confirmed")
                .SelectMany(b => b.BookingRooms
                    .Where(br => br.RoomId != null)
                    .Select(br => new ReceptionStayViewModel
                    {
                        BookingId = b.BookingId,
                        BookingCode = b.BookingCode,
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

            // กำลังเข้าพักจริง (Occupied)
            var activeStays = await _context.StayRecords
                .AsNoTracking()
                .Where(s => s.CheckedOutAt == null)
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
        // ==========================================

        public async Task<IActionResult> BookingManagement(string? search)
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
                    b.TotalAmount,
                    b.Status,
                    b.CreatedAt,
                    RoomNumbers = b.BookingRooms.Where(br => br.Room != null).Select(br => br.Room!.RoomNumber).ToList(),
                    RoomTypes = b.BookingRooms.Select(br => br.RoomType.TypeName).Distinct().ToList(),
                    PaymentStatus = b.Payments.OrderByDescending(p => p.CreatedAt).Select(p => p.Status).FirstOrDefault() ?? "pending",
                    PaymentSubmittedAt = b.Payments.OrderByDescending(p => p.CreatedAt).Select(p => (DateTime?)p.CreatedAt).FirstOrDefault(),
                    CheckedInAt = b.StayRecords.OrderByDescending(s => s.CheckedInAt).Select(s => (DateTime?)s.CheckedInAt).FirstOrDefault(),
                    CheckedOutAt = b.StayRecords.OrderByDescending(s => s.CheckedInAt).Select(s => s.CheckedOutAt).FirstOrDefault()
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

            bool isPaymentApproved = booking.PaymentStatus == "approved"
                                     || booking.PaymentStatus == "paid"
                                     || booking.Status == "confirmed";
            bool canCheckIn = booking.Status == "confirmed" && isPaymentApproved && booking.CheckedInAt == null;
            bool canCheckOut = booking.Status == "checked_in"
                               || (booking.CheckedInAt != null && booking.CheckedOutAt == null);

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
                    paymentStatus = isPaymentApproved ? "approved" : booking.PaymentStatus,
                    paymentLabel = isPaymentApproved ? "Approved" : "Pending",
                    bookingStatus = booking.Status,
                    bookingLabel = booking.Status,
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
        public async Task<IActionResult> ProcessCheckInFromBooking(uint bookingId)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);

            var booking = await _context.Bookings
                .Include(b => b.BookingRooms)
                    .ThenInclude(br => br.Room)
                .Include(b => b.StayRecords)
                .FirstOrDefaultAsync(b => b.BookingId == bookingId);

            if (booking == null)
            {
                TempData["ErrorMessage"] = "ไม่พบข้อมูลการจองในระบบ";
                return RedirectToAction(nameof(BookingManagement));
            }

            // ป้องกัน Check-in ซ้ำ
            if (booking.Status == "checked_in" || booking.StayRecords.Any(s => s.CheckedOutAt == null))
            {
                TempData["ErrorMessage"] = $"การจอง {booking.BookingCode} ได้ดำเนินการ Check-in ไปแล้ว";
                return RedirectToAction(nameof(BookingManagement), new { search = booking.BookingCode });
            }

            // ต้องผ่านการอนุมัติการชำระเงินจากฝ่ายการเงินแล้วเท่านั้น
            if (booking.Status != "confirmed")
            {
                TempData["ErrorMessage"] = "ไม่สามารถ Check-in ได้ เนื่องจากการจองยังไม่ได้รับการอนุมัติการชำระเงินจากฝ่ายการเงิน";
                return RedirectToAction(nameof(BookingManagement), new { search = booking.BookingCode });
            }

            // ห้าม Check-in ก่อนวันเข้าพัก
            if (booking.CheckInDate > today)
            {
                TempData["ErrorMessage"] = $"ยังไม่ถึงวันเข้าพักของการจอง {booking.BookingCode}";
                return RedirectToAction(nameof(BookingManagement), new { search = booking.BookingCode });
            }

            var assignedRoom = booking.BookingRooms.FirstOrDefault(br => br.Room != null)?.Room;
            if (assignedRoom == null || assignedRoom.Status != "available")
            {
                TempData["ErrorMessage"] = "กรุณากำหนดห้องพักที่มีสถานะว่าง (Available) ก่อนดำเนินการ Check-in";
                return RedirectToAction(nameof(CheckIn));
            }

            assignedRoom.Status = "occupied";
            booking.Status = "checked_in";

            _context.StayRecords.Add(new StayRecord
            {
                BookingId = booking.BookingId,
                RoomId = assignedRoom.RoomId,
                CheckedInAt = DateTime.Now,
                ActualGuests = booking.NumGuests
            });

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"ดำเนินการ Check-in รหัส {booking.BookingCode} (ห้อง {assignedRoom.RoomNumber}) สำเร็จ";
            return RedirectToAction(nameof(BookingManagement), new { search = booking.BookingCode });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessCheckOutFromBooking(uint bookingId)
        {
            var stay = await _context.StayRecords
                .Include(s => s.Booking)
                .Include(s => s.Room)
                .FirstOrDefaultAsync(s => s.BookingId == bookingId && s.CheckedOutAt == null);

            if (stay == null)
            {
                TempData["ErrorMessage"] = "ไม่พบรายการกำลังเข้าพัก หรือดำเนินการ Check-out ไปแล้ว";
                return RedirectToAction(nameof(BookingManagement));
            }

            stay.CheckedOutAt = DateTime.Now;
            stay.Room.Status = "needs_cleaning";

            // เปลี่ยนสถานะการจองเป็น checked_out เมื่อไม่เหลือห้องที่ยังพักอยู่
            var hasOtherOpenStays = await _context.StayRecords
                .AnyAsync(s => s.BookingId == stay.BookingId
                               && s.StayId != stay.StayId
                               && s.CheckedOutAt == null);
            if (!hasOtherOpenStays)
            {
                stay.Booking.Status = "checked_out";
            }

            _context.HousekeepingTasks.Add(new HousekeepingTask
            {
                RoomId = stay.RoomId,
                BookingId = stay.BookingId,
                Status = "pending",
                Note = $"ทำความสะอาดหลังลูกค้า Check-out ({stay.Booking.BookingCode})",
                CreatedAt = DateTime.Now
            });

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Check-out ห้อง {stay.Room.RoomNumber} สำเร็จ เปลี่ยนสถานะห้องเป็น Dirty และส่งงานไปยังแผนกแม่บ้านเรียบร้อยแล้ว";
            return RedirectToAction(nameof(BookingManagement), new { search = stay.Booking.BookingCode });
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

            var model = new ReceptionIndexViewModel
            {
                CheckInList = checkInList,

                AvailableRooms = await _context.Rooms
                    .CountAsync(r => r.IsActive == true &&
                                     r.Status == "available"),

                TodayCheckIns = await _context.Bookings
                    .CountAsync(b => b.CheckInDate == today &&
                                     b.Status == "confirmed"),

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

            // ดึงข้อมูล StayRecords ทั้งที่รอ Check-out และที่ Check-out สำเร็จแล้ว (ล่าสุด 100 รายการ)
            var staysQuery = _context.StayRecords
                .AsNoTracking()
                .OrderBy(s => s.CheckedOutAt != null) // ให้รายการที่ยังไม่ Check-out ขึ้นก่อน
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
                });

            var allStays = await staysQuery.ToListAsync();

            // กรองข้อมูลหากส่งมาจากฟอร์มค้นหา
            if (!string.IsNullOrWhiteSpace(guestName))
            {
                var kw = guestName.Trim();
                allStays = allStays.Where(x => x.GuestName.Contains(kw, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(bookingCode))
            {
                var kw = bookingCode.Trim();
                allStays = allStays.Where(x => x.BookingCode.Contains(kw, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(roomNumber))
            {
                var kw = roomNumber.Trim();
                allStays = allStays.Where(x => x.RoomNumber.Contains(kw, StringComparison.OrdinalIgnoreCase)).ToList();
            }

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

    // 1. ตรวจสอบข้อมูลนำเข้าเบื้องต้น (Validation)
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

    // 2. ดึงข้อมูล User จากฐานข้อมูล
    var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value);
    if (user == null)
    {
        return RedirectToAction("Login", "Account");
    }

    // ประกาศ Hasher ไว้ที่ Scope ระดับบนสุดของเมธอดเพื่อให้เรียกใช้ได้ทั่วถึง
    var hasher = new PasswordHasher<User>();

    // 3. ตรวจสอบรหัสผ่านเดิม (เฉพาะกรณีที่ไม่มีข้อผิดพลาดก่อนหน้า)
    if (error == null)
    {
        var verify = hasher.VerifyHashedPassword(user, user.PasswordHash, oldPassword!);
        if (verify == PasswordVerificationResult.Failed)
        {
            error = "รหัสผ่านเดิมไม่ถูกต้อง";
        }
    }

    // 4. หากมีข้อผิดพลาด ให้แจ้งเตือนกลับไปยังหน้า Profile
    if (error != null)
    {
        TempData["ErrorMessage"] = error;
        TempData["OpenPasswordModal"] = true; // ส่งค่าเพื่อสั่งให้ View เปิดหน้าต่าง Modal รหัสผ่านค้างไว้
        return RedirectToAction(nameof(Profile));
    }

    // 5. ทำการแฮชรหัสผ่านใหม่และบันทึกข้อมูล
    user.PasswordHash = hasher.HashPassword(user, newPassword!);
    user.UpdatedAt = DateTime.Now;

    await _context.SaveChangesAsync();

    TempData["SuccessMessage"] = "เปลี่ยนรหัสผ่านเรียบร้อยแล้ว";
    return RedirectToAction(nameof(Profile));
}
    }
}