using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayMate_Kavinvee_0576.Models;
using StayMate_Kavinvee_0576.Models.ViewModels;

namespace StayMate_Kavinvee_0576.Controllers
{
    /// <summary>
    /// Hotel Manager Portal
    /// - หน้า GET ทุกหน้าเป็น Read-only (AsNoTracking)
    /// - POST ทุกตัวมี [ValidateAntiForgeryToken] และตรวจซ้ำจาก DB ว่าเป็น MANAGER ที่ Active
    /// - รองรับทั้งฟอร์มปกติ (Redirect + TempData) และ AJAX (JSON) จากหัว X-Requested-With
    /// - Separation of Duties: ไม่แตะ Payment / FinancialTransaction / บัญชี OWNER และ MANAGER อื่น
    /// </summary>
    [Authorize(Roles = "MANAGER")]
    public class ManagerController : Controller
    {
        private const string ManagerRoleCode = "MANAGER";
        private const string PaymentApproved = "approved";

        private static readonly string[] RoomStatuses = { "available", "occupied", "needs_cleaning", "maintenance" };
        private static readonly string[] BookingStatuses =
            { "pending_payment", "pending_approval", "confirmed", "checked_in", "checked_out", "cancelled", "rejected" };
        private static readonly string[] DiscountTypes = { "free_extra_bed", "percent_room", "percent_extra_nights" };

        // ใช้ List<string> (ไม่ใช่ array) เพราะ .NET 10 / C# 14 แปลง array.Contains ใน Expression Tree
        // ไปเป็น MemoryExtensions.Contains(ReadOnlySpan<T>) ซึ่ง EF Core ประเมินไม่ได้ (TypeLoadException)
        private static readonly List<string> NonStaffRoleCodes = new() { "CUSTOMER", "GUEST" };

        // [ใหม่] บัญชีที่มี Role เหล่านี้ Manager ห้ามระงับ/เปลี่ยน Role (รักษา Separation of Duties)
        private static readonly List<string> ProtectedStaffRoleCodes = new() { "OWNER", "MANAGER", "ADMIN" };

        // [ใหม่] Role ที่ Manager ห้ามมอบหมายให้ใคร (ยกระดับสิทธิ์ไม่ได้ และไม่ใช่บทบาทพนักงาน)
        private static readonly List<string> UnassignableRoleCodes = new() { "OWNER", "MANAGER", "ADMIN", "CUSTOMER", "GUEST" };

        private const decimal MaxMoney = 99_999_999.99m;

        // [ใหม่] ส่วนลดสูงสุดตาม spec (ส่วนลดรวมไม่เกิน 20% ของค่าห้อง)
        private const decimal MaxDiscountPercent = 20m;

        private readonly StayMateDbContext _db;
        private readonly ILogger<ManagerController> _logger;
        private readonly IWebHostEnvironment _env;
        private readonly PasswordHasher<User> _hasher = new();

        public ManagerController(StayMateDbContext db, ILogger<ManagerController> logger, IWebHostEnvironment env)
        {
            _db = db;
            _logger = logger;
            _env = env;
        }

        // ═════════════════════════ Pages (GET) ═════════════════════════

        // GET: /Manager?startDate=2026-11-01&endDate=2026-11-23
        [HttpGet]
        public async Task<IActionResult> Index(DateOnly? startDate, DateOnly? endDate, CancellationToken ct)
        {
            ViewData["Title"] = "ภาพรวมการดำเนินงาน";
            ViewBag.Hotel = await _db.HotelSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? HotelSetting.Default();
            ViewBag.RoomTypes = await _db.RoomTypes.AsNoTracking().OrderBy(t => t.RoomTypeId).ToListAsync(ct);
            ViewBag.RoomCounts = (await _db.Rooms.AsNoTracking()
                    .Where(r => r.IsActive != false)
                    .GroupBy(r => r.RoomTypeId)
                    .Select(g => new { Id = g.Key, C = g.Count() })
                    .ToListAsync(ct))
                .ToDictionary(x => x.Id, x => x.C);

            return View(await BuildDashboardAsync(startDate, endDate, ct));
        }

        // GET: /Manager/RoomManagement?q=&status=&roomTypeId=&includePast=false
        [HttpGet]
        public async Task<IActionResult> RoomManagement(
            string? q, string? status, byte? roomTypeId, bool includePast, CancellationToken ct)
        {
            ViewData["Title"] = "จัดการห้องพักและราคา";
            return View(await BuildRoomManagementAsync(q, status, roomTypeId, includePast, ct));
        }

        // GET: /Manager/PromotionManagement?q=&status=&discountType=
        [HttpGet]
        public async Task<IActionResult> PromotionManagement(
            string? q, string? status, string? discountType, CancellationToken ct)
        {
            ViewData["Title"] = "จัดการโปรโมชั่น";
            return View(await BuildPromotionManagementAsync(q, status, discountType, ct));
        }

        // GET: /Manager/ReviewManagement?q=&rating=&startDate=&endDate=&page=1
        [HttpGet]
        public async Task<IActionResult> ReviewManagement(
            string? q, byte? rating, DateOnly? startDate, DateOnly? endDate, int page = 1, CancellationToken ct = default)
        {
            ViewData["Title"] = "จัดการรีวิว Feedback";
            return View(await BuildReviewManagementAsync(q, rating, startDate, endDate, page, ct));
        }

        // GET: /Manager/StaffManagement?q=&roleCode=
        [HttpGet]
        public async Task<IActionResult> StaffManagement(string? q, string? roleCode, CancellationToken ct)
        {
            ViewData["Title"] = "จัดการบัญชีพนักงาน";
            return View(await BuildStaffManagementAsync(q, roleCode, ct));
        }

        // GET: /Manager/Profile
        [HttpGet]
        public async Task<IActionResult> Profile(CancellationToken ct)
        {
            ViewData["Title"] = "โปรไฟล์ของฉัน";
            var id = GetCurrentUserId();
            if (id == null) return RedirectToAction("Login", "Account");

            var model = await BuildProfileAsync(id.Value, ct);
            return model == null ? NotFound() : View(model);
        }

        // ═════════════════════════ JSON (GET) ═════════════════════════

        // GET: /Manager/DashboardData?startDate=&endDate=   (ให้ manager.js เรียก)
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> DashboardData(DateOnly? startDate, DateOnly? endDate, CancellationToken ct)
            => Json(await BuildDashboardAsync(startDate, endDate, ct));

        // ═════════════════════════ Rooms (POST) [ใหม่] ═════════════════════════

        // POST: /Manager/SaveRoom  (RoomId ว่าง = เพิ่มห้องใหม่, มีค่า = แก้ไข)
        // ไม่แก้ Status ผ่านที่นี่ เพราะสถานะห้องเป็นงานของ Reception/Housekeeping
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveRoom(ManagerRoomInput input, CancellationToken ct)
        {
            const string back = nameof(RoomManagement);
            try
            {
                var managerId = await GetValidManagerAsync(ct);
                if (managerId == null) return Denied();

                if (!ModelState.IsValid || input == null)
                    return Respond(false, "ข้อมูลที่ส่งมาไม่ถูกต้อง", back);

                var number = CleanText(input.RoomNumber, 10)?.ToUpperInvariant();
                if (number == null || !Regex.IsMatch(number, @"^[A-Z0-9\-]{1,10}$"))
                    return Respond(false, "เลขห้องต้องเป็น A-Z, 0-9 หรือ - ความยาว 1-10 ตัวอักษร", back);

                if (!input.FloorNo.HasValue || input.FloorNo.Value < 1 || input.FloorNo.Value > 99)
                    return Respond(false, "ชั้นต้องอยู่ระหว่าง 1 - 99", back);

                if (!input.RoomTypeId.HasValue)
                    return Respond(false, "กรุณาเลือกประเภทห้อง", back);

                var typeId = input.RoomTypeId.Value;
                var typeName = await _db.RoomTypes.AsNoTracking()
                    .Where(t => t.RoomTypeId == typeId).Select(t => t.TypeName).FirstOrDefaultAsync(ct);
                if (typeName == null) return Respond(false, "ไม่พบประเภทห้องที่เลือก", back);

                var editId = input.RoomId;
                if (await _db.Rooms.AsNoTracking().AnyAsync(r =>
                        r.RoomNumber == number && (editId == null || r.RoomId != editId.Value), ct))
                    return Respond(false, "เลขห้องนี้ถูกใช้งานแล้ว", back);

                if (editId.HasValue)
                {
                    var room = await _db.Rooms.FirstOrDefaultAsync(r => r.RoomId == editId.Value, ct);
                    if (room == null) return Respond(false, "ไม่พบห้องที่ต้องการแก้ไข", back);

                    if (room.RoomTypeId != typeId && room.Status == "occupied")
                        return Respond(false, "ห้องมีผู้เข้าพักอยู่ ไม่สามารถเปลี่ยนประเภทห้องได้", back);

                    var oldValue = new { roomNumber = room.RoomNumber, floor = room.FloorNo, roomTypeId = room.RoomTypeId };

                    room.RoomNumber = number;
                    room.FloorNo = input.FloorNo.Value;
                    room.RoomTypeId = typeId;
                    await _db.SaveChangesAsync(ct);

                    await AuditAsync(managerId, "room_update", "rooms", room.RoomId, oldValue,
                        new { roomNumber = number, floor = input.FloorNo.Value, roomTypeId = typeId }, ct);
                    return Respond(true, "แก้ไขข้อมูลห้องเรียบร้อยแล้ว", back);
                }

                var created = new Room
                {
                    RoomNumber = number,
                    FloorNo = input.FloorNo.Value,
                    RoomTypeId = typeId,
                    Status = "available",
                    IsActive = true
                };
                _db.Rooms.Add(created);
                await _db.SaveChangesAsync(ct);

                await AuditAsync(managerId, "room_create", "rooms", created.RoomId, null,
                    new { roomNumber = number, floor = input.FloorNo.Value, roomType = typeName }, ct);
                return Respond(true, "เพิ่มห้องเรียบร้อยแล้ว", back);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveRoom failed");
                return Respond(false, "เกิดข้อผิดพลาดในการบันทึกห้อง กรุณาลองใหม่อีกครั้ง", back);
            }
        }

        // POST: /Manager/ToggleRoomActive  (เปิด/ปิดห้องให้บริการ แทนการลบ เพราะ booking_rooms/stay_records อ้างอิงอยู่)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleRoomActive(uint roomId, bool isActive, CancellationToken ct)
        {
            const string back = nameof(RoomManagement);
            try
            {
                var managerId = await GetValidManagerAsync(ct);
                if (managerId == null) return Denied();

                var room = await _db.Rooms.FirstOrDefaultAsync(r => r.RoomId == roomId, ct);
                if (room == null) return Respond(false, "ไม่พบห้อง", back);

                if ((room.IsActive != false) == isActive)
                    return Respond(false, isActive ? "ห้องนี้เปิดให้บริการอยู่แล้ว" : "ห้องนี้ปิดให้บริการอยู่แล้ว", back);

                if (!isActive)
                {
                    if (room.Status == "occupied")
                        return Respond(false, "ห้องมีผู้เข้าพักอยู่ ไม่สามารถปิดให้บริการได้", back);

                    var today = DateOnly.FromDateTime(DateTime.Today);
                    var live = new List<string> { "confirmed", "checked_in" };
                    var hasBooking = await _db.BookingRooms.AsNoTracking().AnyAsync(br =>
                        br.RoomId == roomId &&
                        live.Contains(br.Booking.Status) &&
                        br.Booking.CheckOutDate >= today, ct);
                    if (hasBooking)
                        return Respond(false, "ห้องนี้มีการจองที่ยังไม่สิ้นสุด ไม่สามารถปิดให้บริการได้", back);
                }

                room.IsActive = isActive;
                await _db.SaveChangesAsync(ct);

                await AuditAsync(managerId, isActive ? "room_enable" : "room_disable", "rooms", room.RoomId,
                    new { isActive = !isActive }, new { isActive }, ct);
                return Respond(true, isActive ? "เปิดให้บริการห้องแล้ว" : "ปิดให้บริการห้องแล้ว", back);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ToggleRoomActive failed");
                return Respond(false, "เกิดข้อผิดพลาดในการเปลี่ยนสถานะห้อง", back);
            }
        }

        // POST: /Manager/SaveRoomType  (แก้ข้อมูลประเภทห้อง: ชื่อ, เตียง, จำนวนผู้เข้าพักสูงสุด/ต่ำสุด, เตียงเสริม)
        // ราคาไปแก้ที่ UpdateBasePrice
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveRoomType(ManagerRoomTypeInput input, CancellationToken ct)
        {
            const string back = nameof(RoomManagement);
            try
            {
                var managerId = await GetValidManagerAsync(ct);
                if (managerId == null) return Denied();

                if (!ModelState.IsValid || input == null || !input.RoomTypeId.HasValue)
                    return Respond(false, "ข้อมูลที่ส่งมาไม่ถูกต้อง", back);

                var name = CleanText(input.TypeName, 100);
                if (name == null) return Respond(false, "กรุณากรอกชื่อประเภทห้อง", back);

                if (!input.MinGuests.HasValue || !input.MaxGuests.HasValue ||
                    input.MinGuests.Value < 1 || input.MaxGuests.Value < input.MinGuests.Value || input.MaxGuests.Value > 20)
                    return Respond(false, "จำนวนผู้เข้าพักต้องอยู่ระหว่าง 1 - 20 และค่าสูงสุดต้องไม่น้อยกว่าค่าต่ำสุด", back);

                var typeId = input.RoomTypeId.Value;
                var type = await _db.RoomTypes.FirstOrDefaultAsync(t => t.RoomTypeId == typeId, ct);
                if (type == null) return Respond(false, "ไม่พบประเภทห้อง", back);

                var oldValue = new
                {
                    typeName = type.TypeName,
                    bedType = type.BedType,
                    minGuests = type.MinGuests,
                    maxGuests = type.MaxGuests,
                    allowExtraBed = type.AllowExtraBed
                };

                type.TypeName = name;
                type.Description = CleanText(input.Description, 5000);
                type.BedType = CleanText(input.BedType, 100);
                type.MinGuests = input.MinGuests.Value;
                type.MaxGuests = input.MaxGuests.Value;
                type.AllowExtraBed = input.AllowExtraBed;

                await _db.SaveChangesAsync(ct);

                await AuditAsync(managerId, "room_type_update", "room_types", typeId, oldValue, new
                {
                    typeName = name,
                    bedType = type.BedType,
                    minGuests = type.MinGuests,
                    maxGuests = type.MaxGuests,
                    allowExtraBed = type.AllowExtraBed
                }, ct);
                return Respond(true, "แก้ไขข้อมูลประเภทห้องเรียบร้อยแล้ว", back);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveRoomType failed");
                return Respond(false, "เกิดข้อผิดพลาดในการบันทึกประเภทห้อง กรุณาลองใหม่อีกครั้ง", back);
            }
        }

        // ═════════════════════════ Pricing (POST) ═════════════════════════

        // POST: /Manager/SavePricePeriod  (RateId ว่าง = เพิ่มใหม่, มีค่า = แก้ไข)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SavePricePeriod(ManagerPricePeriodInput input, CancellationToken ct)
        {
            const string back = nameof(RoomManagement);
            try
            {
                var managerId = await GetValidManagerAsync(ct);
                if (managerId == null) return Denied();

                if (!ModelState.IsValid || input == null)
                    return Respond(false, "ข้อมูลที่ส่งมาไม่ถูกต้อง", back);
                if (!input.RoomTypeId.HasValue)
                    return Respond(false, "กรุณาเลือกประเภทห้อง", back);
                if (!input.StartDate.HasValue || !input.EndDate.HasValue)
                    return Respond(false, "กรุณาระบุวันที่เริ่มต้นและสิ้นสุด", back);
                if (input.EndDate.Value < input.StartDate.Value)
                    return Respond(false, "วันที่สิ้นสุดต้องไม่ก่อนวันที่เริ่มต้น", back);
                if (!input.PricePerNight.HasValue || input.PricePerNight.Value <= 0 || input.PricePerNight.Value > MaxMoney)
                    return Respond(false, "ราคาต่อคืนต้องมากกว่า 0", back);

                var typeId = input.RoomTypeId.Value;
                var start = input.StartDate.Value;
                var end = input.EndDate.Value;
                var price = Math.Round(input.PricePerNight.Value, 2);
                var name = CleanText(input.PeriodName, 100);

                var typeNames = await _db.RoomTypes.AsNoTracking()
                    .ToDictionaryAsync(t => t.RoomTypeId, t => t.TypeName, ct);
                if (!typeNames.ContainsKey(typeId))
                    return Respond(false, "ไม่พบประเภทห้องที่เลือก", back);

                var editId = input.RateId;
                var overlap = await _db.RoomRatePeriods.AsNoTracking().AnyAsync(p =>
                    p.RoomTypeId == typeId &&
                    p.StartDate <= end && p.EndDate >= start &&
                    (editId == null || p.RateId != editId.Value), ct);
                if (overlap)
                    return Respond(false, "ช่วงวันที่ซ้อนทับกับช่วงราคาอื่นของประเภทห้องเดียวกัน", back);

                var newValue = new
                {
                    roomType = typeNames[typeId],
                    period = name,
                    startDate = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    endDate = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    pricePerNight = price
                };

                if (editId.HasValue)
                {
                    var rate = await _db.RoomRatePeriods.FirstOrDefaultAsync(p => p.RateId == editId.Value, ct);
                    if (rate == null) return Respond(false, "ไม่พบช่วงราคาที่ต้องการแก้ไข", back);

                    var oldValue = new
                    {
                        roomType = typeNames.TryGetValue(rate.RoomTypeId, out var oldTypeName) ? oldTypeName : null,
                        period = rate.PeriodName,
                        startDate = rate.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        endDate = rate.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        pricePerNight = rate.PricePerNight
                    };

                    rate.RoomTypeId = typeId;
                    rate.PeriodName = name;
                    rate.StartDate = start;
                    rate.EndDate = end;
                    rate.PricePerNight = price;
                    await _db.SaveChangesAsync(ct);

                    await AuditAsync(managerId, "price_period_update", "room_rate_periods", rate.RateId, oldValue, newValue, ct);
                    return Respond(true, "แก้ไขช่วงราคาเรียบร้อยแล้ว", back);
                }

                var created = new RoomRatePeriod
                {
                    RoomTypeId = typeId,
                    PeriodName = name,
                    StartDate = start,
                    EndDate = end,
                    PricePerNight = price,
                    CreatedBy = managerId.Value
                };
                _db.RoomRatePeriods.Add(created);
                await _db.SaveChangesAsync(ct);

                await AuditAsync(managerId, "price_period_create", "room_rate_periods", created.RateId, null, newValue, ct);
                return Respond(true, "เพิ่มช่วงราคาเรียบร้อยแล้ว", back);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SavePricePeriod failed");
                return Respond(false, "เกิดข้อผิดพลาดในการบันทึกช่วงราคา กรุณาลองใหม่อีกครั้ง", back);
            }
        }

        // POST: /Manager/DeletePricePeriod  (RoomRatePeriod ไม่ถูกตารางอื่นอ้างอิง FK ลบได้ปลอดภัย / บันทึกประวัติไว้ใน audit_logs)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePricePeriod(uint rateId, CancellationToken ct)
        {
            const string back = nameof(RoomManagement);
            try
            {
                var managerId = await GetValidManagerAsync(ct);
                if (managerId == null) return Denied();

                var rate = await _db.RoomRatePeriods.Include(p => p.RoomType)
                    .FirstOrDefaultAsync(p => p.RateId == rateId, ct);
                if (rate == null) return Respond(false, "ไม่พบช่วงราคาที่ต้องการลบ", back);

                var oldValue = new
                {
                    roomType = rate.RoomType.TypeName,
                    period = rate.PeriodName,
                    startDate = rate.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    endDate = rate.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    pricePerNight = rate.PricePerNight
                };
                var id = rate.RateId;

                _db.RoomRatePeriods.Remove(rate);
                await _db.SaveChangesAsync(ct);

                await AuditAsync(managerId, "price_period_delete", "room_rate_periods", id, oldValue, null, ct);
                return Respond(true, "ลบช่วงราคาเรียบร้อยแล้ว", back);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeletePricePeriod failed");
                return Respond(false, "เกิดข้อผิดพลาดในการลบช่วงราคา", back);
            }
        }

        // POST: /Manager/UpdateBasePrice  (แก้ราคาพื้นฐานของประเภทห้อง)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateBasePrice(byte roomTypeId, decimal? basePrice, decimal? extraBedPrice, CancellationToken ct)
        {
            const string back = nameof(RoomManagement);
            try
            {
                var managerId = await GetValidManagerAsync(ct);
                if (managerId == null) return Denied();

                if (!basePrice.HasValue || basePrice.Value <= 0 || basePrice.Value > MaxMoney)
                    return Respond(false, "ราคาพื้นฐานต้องมากกว่า 0", back);
                if (extraBedPrice.HasValue && (extraBedPrice.Value < 0 || extraBedPrice.Value > MaxMoney))
                    return Respond(false, "ราคาเตียงเสริมไม่ถูกต้อง", back);

                var type = await _db.RoomTypes.FirstOrDefaultAsync(t => t.RoomTypeId == roomTypeId, ct);
                if (type == null) return Respond(false, "ไม่พบประเภทห้อง", back);

                var oldValue = new { roomType = type.TypeName, basePrice = type.BasePrice, extraBedPrice = type.ExtraBedPrice };

                type.BasePrice = Math.Round(basePrice.Value, 2);
                if (extraBedPrice.HasValue) type.ExtraBedPrice = Math.Round(extraBedPrice.Value, 2);

                await _db.SaveChangesAsync(ct);

                await AuditAsync(managerId, "base_price_update", "room_types", type.RoomTypeId, oldValue,
                    new { roomType = type.TypeName, basePrice = type.BasePrice, extraBedPrice = type.ExtraBedPrice }, ct);
                return Respond(true, "อัปเดตราคาพื้นฐานเรียบร้อยแล้ว", back);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateBasePrice failed");
                return Respond(false, "เกิดข้อผิดพลาดในการอัปเดตราคา", back);
            }
        }

        // ═════════════════════════ Promotions (POST) ═════════════════════════

        // POST: /Manager/SavePromotion  (PromotionId ว่าง = เพิ่มใหม่)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SavePromotion(ManagerPromotionInput input, CancellationToken ct)
        {
            const string back = nameof(PromotionManagement);
            try
            {
                var managerId = await GetValidManagerAsync(ct);
                if (managerId == null) return Denied();

                if (!ModelState.IsValid || input == null)
                    return Respond(false, "ข้อมูลที่ส่งมาไม่ถูกต้อง", back);

                var code = (input.PromoCode ?? string.Empty).Trim().ToUpperInvariant();
                if (!Regex.IsMatch(code, @"^[A-Z0-9_\-]{3,30}$"))
                    return Respond(false, "รหัสโปรโมชั่นต้องเป็น A-Z, 0-9, _ หรือ - ความยาว 3-30 ตัวอักษร", back);

                var promoName = CleanText(input.PromoName, 150);
                if (promoName == null) return Respond(false, "กรุณากรอกชื่อโปรโมชั่น", back);

                var type = (input.DiscountType ?? string.Empty).Trim();
                if (!DiscountTypes.Contains(type)) return Respond(false, "ประเภทส่วนลดไม่ถูกต้อง", back);

                // [แก้] จำกัดส่วนลดไม่เกิน MaxDiscountPercent (20%) ตาม spec
                decimal? percent = null, percentMax = null;
                if (type != "free_extra_bed")
                {
                    if (!input.DiscountPercent.HasValue || input.DiscountPercent.Value <= 0 || input.DiscountPercent.Value > MaxDiscountPercent)
                        return Respond(false, $"เปอร์เซ็นต์ส่วนลดต้องอยู่ระหว่าง 0.01 - {MaxDiscountPercent:0}", back);
                    percent = Math.Round(input.DiscountPercent.Value, 2);

                    if (input.DiscountPercentMax.HasValue)
                    {
                        if (input.DiscountPercentMax.Value < percent.Value || input.DiscountPercentMax.Value > MaxDiscountPercent)
                            return Respond(false, $"เปอร์เซ็นต์สูงสุดต้องไม่น้อยกว่าเปอร์เซ็นต์ส่วนลดและไม่เกิน {MaxDiscountPercent:0}", back);
                        percentMax = Math.Round(input.DiscountPercentMax.Value, 2);
                    }
                }

                if (input.ValidFrom.HasValue && input.ValidTo.HasValue && input.ValidTo.Value < input.ValidFrom.Value)
                    return Respond(false, "วันที่สิ้นสุดต้องไม่ก่อนวันที่เริ่มต้น", back);

                if (input.VoucherMinValue is < 0 || input.VoucherMaxValue is < 0 ||
                    input.VoucherMinValue > MaxMoney || input.VoucherMaxValue > MaxMoney)
                    return Respond(false, "มูลค่า Voucher ไม่ถูกต้อง", back);
                if (input.VoucherMinValue.HasValue && input.VoucherMaxValue.HasValue &&
                    input.VoucherMaxValue.Value < input.VoucherMinValue.Value)
                    return Respond(false, "มูลค่า Voucher สูงสุดต้องไม่น้อยกว่าค่าต่ำสุด", back);

                if (input.ApplicableRoomTypeId.HasValue)
                {
                    var rt = input.ApplicableRoomTypeId.Value;
                    if (!await _db.RoomTypes.AsNoTracking().AnyAsync(t => t.RoomTypeId == rt, ct))
                        return Respond(false, "ไม่พบประเภทห้องที่ระบุ", back);
                }

                var editId = input.PromotionId;
                if (await _db.Promotions.AsNoTracking().AnyAsync(p =>
                        p.PromoCode == code && (editId == null || p.PromotionId != editId.Value), ct))
                    return Respond(false, "รหัสโปรโมชั่นนี้ถูกใช้งานแล้ว", back);

                Promotion promo;
                object? oldValue = null;
                if (editId.HasValue)
                {
                    var existing = await _db.Promotions.FirstOrDefaultAsync(p => p.PromotionId == editId.Value, ct);
                    if (existing == null) return Respond(false, "ไม่พบโปรโมชั่นที่ต้องการแก้ไข", back);
                    promo = existing;
                    oldValue = new
                    {
                        code = existing.PromoCode,
                        name = existing.PromoName,
                        percent = existing.DiscountPercent,
                        active = existing.IsActive
                    };
                }
                else
                {
                    promo = new Promotion { CreatedBy = managerId.Value };
                    _db.Promotions.Add(promo);
                }

                promo.PromoCode = code;
                promo.PromoName = promoName;
                promo.Description = CleanText(input.Description, 5000);
                promo.DiscountType = type;
                promo.DiscountPercent = percent;
                promo.DiscountPercentMax = percentMax;
                promo.MinGuests = input.MinGuests;
                promo.MinNights = input.MinNights;
                promo.MaxDiscountNights = input.MaxDiscountNights;
                promo.ApplicableRoomTypeId = input.ApplicableRoomTypeId;
                promo.RequiresHoliday = input.RequiresHoliday;
                promo.VoucherMinValue = input.VoucherMinValue.HasValue ? Math.Round(input.VoucherMinValue.Value, 2) : null;
                promo.VoucherMaxValue = input.VoucherMaxValue.HasValue ? Math.Round(input.VoucherMaxValue.Value, 2) : null;
                promo.IsActive = input.IsActive;
                promo.ValidFrom = input.ValidFrom;
                promo.ValidTo = input.ValidTo;

                await _db.SaveChangesAsync(ct);

                await AuditAsync(managerId, editId.HasValue ? "promotion_update" : "promotion_create", "promotions",
                    promo.PromotionId, oldValue, new { code, name = promoName, percent, active = input.IsActive }, ct);
                return Respond(true, editId.HasValue ? "แก้ไขโปรโมชั่นเรียบร้อยแล้ว" : "เพิ่มโปรโมชั่นเรียบร้อยแล้ว", back);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SavePromotion failed");
                return Respond(false, "เกิดข้อผิดพลาดในการบันทึกโปรโมชั่น กรุณาลองใหม่อีกครั้ง", back);
            }
        }

        // POST: /Manager/TogglePromotion  (ใช้ปิด/เปิดแทนการลบ เพราะ booking_promotions อ้างอิงอยู่)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePromotion(uint promotionId, bool isActive, CancellationToken ct)
        {
            const string back = nameof(PromotionManagement);
            try
            {
                var managerId = await GetValidManagerAsync(ct);
                if (managerId == null) return Denied();

                var promo = await _db.Promotions.FirstOrDefaultAsync(p => p.PromotionId == promotionId, ct);
                if (promo == null) return Respond(false, "ไม่พบโปรโมชั่น", back);

                var was = promo.IsActive;
                promo.IsActive = isActive;
                await _db.SaveChangesAsync(ct);

                await AuditAsync(managerId, isActive ? "promotion_enable" : "promotion_disable", "promotions",
                    promo.PromotionId, new { active = was }, new { active = isActive }, ct);
                return Respond(true, isActive ? "เปิดใช้งานโปรโมชั่นแล้ว" : "ปิดใช้งานโปรโมชั่นแล้ว", back);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TogglePromotion failed");
                return Respond(false, "เกิดข้อผิดพลาดในการเปลี่ยนสถานะโปรโมชั่น", back);
            }
        }

        // ═════════════════════════ Holidays (POST) ═════════════════════════

        // POST: /Manager/SaveHoliday
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveHoliday(ManagerHolidayInput input, CancellationToken ct)
        {
            const string back = nameof(PromotionManagement);
            try
            {
                var managerId = await GetValidManagerAsync(ct);
                if (managerId == null) return Denied();

                if (!ModelState.IsValid || input == null)
                    return Respond(false, "ข้อมูลที่ส่งมาไม่ถูกต้อง", back);

                var name = CleanText(input.HolidayName, 150);
                if (name == null) return Respond(false, "กรุณากรอกชื่อวันหยุด/เทศกาล", back);
                if (!input.StartDate.HasValue || !input.EndDate.HasValue)
                    return Respond(false, "กรุณาระบุวันที่เริ่มต้นและสิ้นสุด", back);
                if (input.EndDate.Value < input.StartDate.Value)
                    return Respond(false, "วันที่สิ้นสุดต้องไม่ก่อนวันที่เริ่มต้น", back);

                var note = CleanText(input.EventNote, 255);

                if (input.HolidayId.HasValue)
                {
                    var h = await _db.Holidays.FirstOrDefaultAsync(x => x.HolidayId == input.HolidayId.Value, ct);
                    if (h == null) return Respond(false, "ไม่พบวันหยุดที่ต้องการแก้ไข", back);

                    h.HolidayName = name;
                    h.StartDate = input.StartDate.Value;
                    h.EndDate = input.EndDate.Value;
                    h.EventNote = note;
                }
                else
                {
                    _db.Holidays.Add(new Holiday
                    {
                        HolidayName = name,
                        StartDate = input.StartDate.Value,
                        EndDate = input.EndDate.Value,
                        EventNote = note,
                        CreatedBy = managerId.Value
                    });
                }

                await _db.SaveChangesAsync(ct);
                return Respond(true, input.HolidayId.HasValue ? "แก้ไขวันหยุดเรียบร้อยแล้ว" : "เพิ่มวันหยุดเรียบร้อยแล้ว", back);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveHoliday failed");
                return Respond(false, "เกิดข้อผิดพลาดในการบันทึกวันหยุด กรุณาลองใหม่อีกครั้ง", back);
            }
        }

        // POST: /Manager/DeleteHoliday
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteHoliday(uint holidayId, CancellationToken ct)
        {
            const string back = nameof(PromotionManagement);
            try
            {
                if (await GetValidManagerAsync(ct) == null) return Denied();

                var h = await _db.Holidays.FirstOrDefaultAsync(x => x.HolidayId == holidayId, ct);
                if (h == null) return Respond(false, "ไม่พบวันหยุดที่ต้องการลบ", back);

                _db.Holidays.Remove(h);
                await _db.SaveChangesAsync(ct);
                return Respond(true, "ลบวันหยุดเรียบร้อยแล้ว", back);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteHoliday failed");
                return Respond(false, "เกิดข้อผิดพลาดในการลบวันหยุด", back);
            }
        }

        // ═════════════════════════ Staff (POST) [ใหม่] ═════════════════════════

        // POST: /Manager/CreateStaff
        // Manager ตั้งรหัสผ่านเริ่มต้นได้ตอนสร้างเท่านั้น (เก็บเป็น Hash) หลังจากนั้นดู/แก้รหัสผ่านพนักงานไม่ได้
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateStaff(ManagerStaffCreateInput input, CancellationToken ct)
        {
            const string back = nameof(StaffManagement);
            try
            {
                var managerId = await GetValidManagerAsync(ct);
                if (managerId == null) return Denied();

                if (!ModelState.IsValid || input == null)
                    return Respond(false, "ข้อมูลที่ส่งมาไม่ถูกต้อง", back);

                var username = (input.Username ?? string.Empty).Trim();
                if (!Regex.IsMatch(username, @"^[A-Za-z0-9_.\-]{4,50}$"))
                    return Respond(false, "ชื่อผู้ใช้ต้องเป็น A-Z, 0-9, _ . หรือ - ความยาว 4-50 ตัวอักษร", back);

                var email = CleanText(input.Email, 120)?.ToLowerInvariant();
                if (email == null || !Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                    return Respond(false, "รูปแบบอีเมลไม่ถูกต้อง", back);

                var fullName = CleanText(input.FullName, 150);
                if (fullName == null) return Respond(false, "กรุณากรอกชื่อ-นามสกุล", back);

                var phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim();
                if (phone != null && !Regex.IsMatch(phone, @"^[0-9+\-\s]{6,20}$"))
                    return Respond(false, "รูปแบบเบอร์โทรศัพท์ไม่ถูกต้อง", back);

                var password = input.Password ?? string.Empty;
                if (password.Length < 8 || password.Length > 100)
                    return Respond(false, "รหัสผ่านเริ่มต้นต้องมี 8-100 ตัวอักษร", back);

                var roleCode = (input.RoleCode ?? string.Empty).Trim().ToUpperInvariant();
                if (roleCode.Length == 0 || UnassignableRoleCodes.Contains(roleCode))
                    return Respond(false, "ไม่สามารถกำหนด Role นี้ได้", back);

                var role = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleCode == roleCode, ct);
                if (role == null) return Respond(false, "ไม่พบ Role ที่เลือก", back);

                if (await _db.Users.AsNoTracking().AnyAsync(u => u.Username == username, ct))
                    return Respond(false, "ชื่อผู้ใช้นี้ถูกใช้งานแล้ว", back);
                if (await _db.Users.AsNoTracking().AnyAsync(u => u.Email == email, ct))
                    return Respond(false, "อีเมลนี้ถูกใช้งานแล้ว", back);

                var user = new User
                {
                    Username = username,
                    Email = email,
                    FullName = fullName,
                    Phone = phone,
                    IsActive = true,
                    PasswordHash = string.Empty
                };
                user.PasswordHash = _hasher.HashPassword(user, password);
                user.UserRoles.Add(new UserRole { RoleId = role.RoleId });

                _db.Users.Add(user);
                await _db.SaveChangesAsync(ct);

                // ไม่บันทึกรหัสผ่านลง audit
                await AuditAsync(managerId, "staff_create", "users", user.UserId, null,
                    new { username, email, role = roleCode }, ct);
                return Respond(true, "เพิ่มบัญชีพนักงานเรียบร้อยแล้ว", back);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateStaff failed");
                return Respond(false, "เกิดข้อผิดพลาดในการเพิ่มบัญชีพนักงาน กรุณาลองใหม่อีกครั้ง", back);
            }
        }

        // POST: /Manager/SetStaffActive  (ระงับ / เปิดใช้งานอีกครั้ง)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetStaffActive(uint userId, bool isActive, CancellationToken ct)
        {
            const string back = nameof(StaffManagement);
            try
            {
                var managerId = await GetValidManagerAsync(ct);
                if (managerId == null) return Denied();

                var (user, error) = await LoadManageableStaffAsync(userId, managerId.Value, ct);
                if (user == null) return Respond(false, error ?? "ไม่พบบัญชีพนักงาน", back);

                if ((user.IsActive == true) == isActive)
                    return Respond(false, isActive ? "บัญชีนี้เปิดใช้งานอยู่แล้ว" : "บัญชีนี้ถูกระงับอยู่แล้ว", back);

                user.IsActive = isActive;
                await _db.SaveChangesAsync(ct);

                await AuditAsync(managerId, isActive ? "staff_activate" : "staff_suspend", "users", user.UserId,
                    new { isActive = !isActive }, new { isActive }, ct);
                return Respond(true, isActive ? "เปิดใช้งานบัญชีพนักงานแล้ว" : "ระงับบัญชีพนักงานแล้ว", back);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SetStaffActive failed");
                return Respond(false, "เกิดข้อผิดพลาดในการเปลี่ยนสถานะบัญชี", back);
            }
        }

        // POST: /Manager/UpdateStaffRole  (เปลี่ยน Role พนักงาน: แทนที่ Role พนักงานเดิมด้วย Role ใหม่ เฉพาะที่ได้รับอนุญาต)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStaffRole(uint userId, string? roleCode, CancellationToken ct)
        {
            const string back = nameof(StaffManagement);
            try
            {
                var managerId = await GetValidManagerAsync(ct);
                if (managerId == null) return Denied();

                var code = (roleCode ?? string.Empty).Trim().ToUpperInvariant();
                if (code.Length == 0 || UnassignableRoleCodes.Contains(code))
                    return Respond(false, "ไม่สามารถกำหนด Role นี้ได้", back);

                var role = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleCode == code, ct);
                if (role == null) return Respond(false, "ไม่พบ Role ที่เลือก", back);

                var (user, error) = await LoadManageableStaffAsync(userId, managerId.Value, ct);
                if (user == null) return Respond(false, error ?? "ไม่พบบัญชีพนักงาน", back);

                var staffRoles = user.UserRoles.Where(ur => !NonStaffRoleCodes.Contains(ur.Role.RoleCode)).ToList();
                var oldCodes = staffRoles.Select(ur => ur.Role.RoleCode).ToList();

                if (staffRoles.Count == 1 && staffRoles[0].RoleId == role.RoleId)
                    return Respond(false, "บัญชีนี้มี Role นี้อยู่แล้ว", back);

                var alreadyHas = staffRoles.Any(ur => ur.RoleId == role.RoleId);
                foreach (var ur in staffRoles.Where(ur => ur.RoleId != role.RoleId))
                    _db.UserRoles.Remove(ur);
                if (!alreadyHas)
                    _db.UserRoles.Add(new UserRole { UserId = user.UserId, RoleId = role.RoleId });

                await _db.SaveChangesAsync(ct);

                await AuditAsync(managerId, "staff_role_change", "users", user.UserId,
                    new { roles = string.Join(",", oldCodes) }, new { roles = code }, ct);
                return Respond(true, "เปลี่ยน Role พนักงานเรียบร้อยแล้ว", back);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateStaffRole failed");
                return Respond(false, "เกิดข้อผิดพลาดในการเปลี่ยน Role", back);
            }
        }

        // ═════════════════════════ Profile & Hotel Settings (POST) ═════════════════════════

        // POST: /Manager/SaveHotelSettings
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(3_000_000)]
        public async Task<IActionResult> SaveHotelSettings(
            string? hotelName, string? address, string? phone, string? email,
            string? checkInTime, string? checkOutTime, IFormFile? logo, CancellationToken ct)
        {
            const string back = nameof(Index);
            try
            {
                var managerId = await GetValidManagerAsync(ct);
                if (managerId == null) return Denied();

                var name = CleanText(hotelName, 150);
                if (name == null) return Respond(false, "กรุณากรอกชื่อโรงแรม", back);

                var addr = CleanText(address, 500);
                if (addr == null) return Respond(false, "กรุณากรอกที่อยู่", back);

                var ph = CleanText(phone, 30);
                if (ph == null || !Regex.IsMatch(ph, @"^[0-9+\-\s()]{6,30}$"))
                    return Respond(false, "รูปแบบเบอร์โทรศัพท์ไม่ถูกต้อง", back);

                var mail = CleanText(email, 120);
                if (mail == null || !Regex.IsMatch(mail, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                    return Respond(false, "รูปแบบอีเมลไม่ถูกต้อง", back);

                if (!TimeOnly.TryParseExact(checkInTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tin) ||
                    !TimeOnly.TryParseExact(checkOutTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tout))
                    return Respond(false, "รูปแบบเวลาเช็คอิน/เช็คเอาท์ไม่ถูกต้อง", back);

                var s = await _db.HotelSettings.FirstOrDefaultAsync(ct);
                if (s == null)
                {
                    s = new HotelSetting { SettingId = 1 };
                    _db.HotelSettings.Add(s);
                }

                if (logo != null && logo.Length > 0)
                {
                    var ext = Path.GetExtension(logo.FileName).ToLowerInvariant();
                    if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
                        return Respond(false, "โลโก้ต้องเป็นไฟล์ .jpg .png หรือ .webp", back);

                    if (logo.Length > 2 * 1024 * 1024)
                        return Respond(false, "ไฟล์โลโก้ต้องไม่เกิน 2 MB", back);

                    if (!logo.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                        return Respond(false, "ไฟล์โลโก้ไม่ใช่รูปภาพ", back);

                    // [ใหม่] ตรวจ magic bytes ว่าเนื้อไฟล์ตรงกับนามสกุลจริง
                    if (!await LooksLikeImageAsync(logo, ext, ct))
                        return Respond(false, "เนื้อไฟล์ไม่ตรงกับชนิดรูปภาพที่ระบุ", back);

                    var dir = Path.Combine(_env.WebRootPath, "images", "Logo");
                    Directory.CreateDirectory(dir);

                    var fileName = $"hotel-logo-{Guid.NewGuid():N}{ext}";
                    await using (var fs = new FileStream(Path.Combine(dir, fileName), FileMode.Create))
                        await logo.CopyToAsync(fs, ct);

                    // ลบโลโก้ที่อัปโหลดไว้ก่อนหน้า (ไม่แตะโลโก้ดั้งเดิม LogoStayMate.jpg)
                    if (s.LogoPath != null && s.LogoPath.StartsWith("/images/Logo/hotel-logo-"))
                    {
                        var old = Path.Combine(_env.WebRootPath, s.LogoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                        if (System.IO.File.Exists(old)) System.IO.File.Delete(old);
                    }

                    s.LogoPath = "/images/Logo/" + fileName;
                }

                s.HotelName = name;
                s.Address = addr;
                s.Phone = ph;
                s.Email = mail;
                s.CheckInTime = tin;
                s.CheckOutTime = tout;
                s.UpdatedAt = DateTime.Now;
                s.UpdatedBy = managerId.Value;

                await _db.SaveChangesAsync(ct);
                return Respond(true, "บันทึกข้อมูลโรงแรมเรียบร้อยแล้ว", back);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveHotelSettings failed");
                return Respond(false, "เกิดข้อผิดพลาดในการบันทึกข้อมูลโรงแรม กรุณาลองใหม่อีกครั้ง", back);
            }
        }

        // POST: /Manager/UpdateProfile  (แก้ได้เฉพาะชื่อและเบอร์โทร)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(string? fullName, string? phone, CancellationToken ct)
        {
            const string back = nameof(Profile);
            try
            {
                var managerId = await GetValidManagerAsync(ct);
                if (managerId == null) return Denied();

                var cleanName = CleanText(fullName, 150);
                if (cleanName == null) return Respond(false, "กรุณากรอกชื่อ", back);

                var cleanPhone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
                if (cleanPhone != null && !Regex.IsMatch(cleanPhone, @"^[0-9+\-\s]{6,20}$"))
                    return Respond(false, "รูปแบบเบอร์โทรศัพท์ไม่ถูกต้อง", back);

                var uid = managerId.Value;
                var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == uid, ct);
                if (user == null) return Denied();

                user.FullName = cleanName;
                user.Phone = cleanPhone;
                await _db.SaveChangesAsync(ct);

                return Respond(true, "บันทึกข้อมูลส่วนตัวเรียบร้อยแล้ว", back);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateProfile failed");
                return Respond(false, "เกิดข้อผิดพลาดในการบันทึกข้อมูล กรุณาลองใหม่อีกครั้ง", back);
            }
        }

        // POST: /Manager/ChangePassword  (ใช้ PasswordHasher<User> เหมือน AccountController)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(
            string? oldPassword, string? newPassword, string? confirmPassword, CancellationToken ct)
        {
            const string back = nameof(Profile);
            try
            {
                var managerId = await GetValidManagerAsync(ct);
                if (managerId == null) return Denied();

                if (string.IsNullOrWhiteSpace(oldPassword) || string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
                {
                    TempData["OpenPasswordModal"] = true;
                    return Respond(false, "กรุณากรอกข้อมูลให้ครบ และรหัสผ่านใหม่ต้องมีอย่างน้อย 6 ตัวอักษร", back);
                }
                if (newPassword != confirmPassword)
                {
                    TempData["OpenPasswordModal"] = true;
                    return Respond(false, "รหัสผ่านใหม่และการยืนยันรหัสผ่านไม่ตรงกัน", back);
                }

                var uid = managerId.Value;
                var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == uid, ct);
                if (user == null) return Denied();

                var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, oldPassword);
                if (verify == PasswordVerificationResult.Failed)
                {
                    TempData["OpenPasswordModal"] = true;
                    return Respond(false, "รหัสผ่านเดิมไม่ถูกต้อง", back);
                }

                user.PasswordHash = _hasher.HashPassword(user, newPassword);
                await _db.SaveChangesAsync(ct);

                return Respond(true, "เปลี่ยนรหัสผ่านเรียบร้อยแล้ว", back);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChangePassword failed");
                TempData["OpenPasswordModal"] = true;
                return Respond(false, "เกิดข้อผิดพลาดในการเปลี่ยนรหัสผ่าน กรุณาลองใหม่อีกครั้ง", back);
            }
        }

        // ═════════════════════════ Builders ═════════════════════════

        private async Task<ManagerDashboardViewModel> BuildDashboardAsync(DateOnly? startDate, DateOnly? endDate, CancellationToken ct)
        {
            var (start, end) = ResolveRange(startDate, endDate);
            var from = start.ToDateTime(TimeOnly.MinValue);
            var toExcl = end.AddDays(1).ToDateTime(TimeOnly.MinValue);
            var today = DateOnly.FromDateTime(DateTime.Today);

            // ห้อง: นับตามสถานะ (เฉพาะห้องที่เปิดใช้งาน) ใน 1 Query
            var roomRows = await _db.Rooms.AsNoTracking()
                .Where(r => r.IsActive != false)
                .GroupBy(r => r.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            int RoomCount(string s) => roomRows.Where(x => x.Status == s).Sum(x => x.Count);
            var totalRooms = roomRows.Sum(x => x.Count);

            // การจอง: นับตามสถานะ 1 Query
            var bookingRows = await _db.Bookings.AsNoTracking()
                .GroupBy(b => b.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            int BookingCount(string s) => bookingRows.Where(x => x.Status == s).Sum(x => x.Count);

            var bookingDates = await _db.Bookings.AsNoTracking()
                .Where(b => b.CreatedAt >= from && b.CreatedAt < toExcl)
                .Select(b => b.CreatedAt)
                .ToListAsync(ct);

            // รายรับจริง: Payment ที่ approved (วันที่ตามกฎเดียวกับ OwnerController)
            var payments = await _db.Payments.AsNoTracking()
                .Where(p => p.Status == PaymentApproved)
                .Select(p => new { Date = p.PaidAt ?? p.ReviewedAt ?? p.CreatedAt, p.Amount })
                .Where(x => x.Date >= from && x.Date < toExcl)
                .ToListAsync(ct);

            var totalRevenue = await _db.Payments.AsNoTracking()
                .Where(p => p.Status == PaymentApproved)
                .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

            var activePromotions = await _db.Promotions.AsNoTracking()
                .CountAsync(p => p.IsActive == true &&
                                 (p.ValidFrom == null || p.ValidFrom <= today) &&
                                 (p.ValidTo == null || p.ValidTo >= today), ct);

            var reviewCount = await _db.Reviews.AsNoTracking().CountAsync(ct);
            var avgRating = reviewCount > 0
                ? await _db.Reviews.AsNoTracking().AverageAsync(r => (double?)r.Rating, ct)
                : null;

            // รายการจองล่าสุด (ไม่ดึงเบอร์โทร/อีเมลลูกค้า)
            var recent = await _db.Bookings.AsNoTracking()
                .OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.BookingId)
                .Take(10)
                .Select(b => new ManagerRecentBookingRow
                {
                    BookingId = b.BookingId,
                    BookingCode = b.BookingCode,
                    ContactName = b.ContactName,
                    CheckInDate = b.CheckInDate,
                    CheckOutDate = b.CheckOutDate,
                    Nights = b.Nights,
                    NumGuests = b.NumGuests,
                    TotalAmount = b.TotalAmount,
                    Status = b.Status,
                    CreatedAt = b.CreatedAt,
                    RoomTypeNames = b.BookingRooms.Select(br => br.RoomType.TypeName).ToList()
                })
                .ToListAsync(ct);

            foreach (var r in recent)
            {
                r.StatusLabel = BookingStatusLabel(r.Status);
                r.RoomTypeNames = r.RoomTypeNames.Distinct().ToList();
            }

            // อนุกรมเวลารายวัน (เติม 0 ให้ครบทุกวัน)
            var revenueByDay = payments
                .GroupBy(x => DateOnly.FromDateTime(x.Date))
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
            var bookingsByDay = bookingDates
                .GroupBy(d => DateOnly.FromDateTime(d))
                .ToDictionary(g => g.Key, g => g.Count());

            var dailyRevenue = new List<ManagerChartPoint>();
            var dailyBookings = new List<ManagerCountPoint>();
            var periodDays = end.DayNumber - start.DayNumber + 1;
            for (var i = 0; i < periodDays; i++)
            {
                var d = start.AddDays(i);
                var key = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var label = d.ToString("dd MMM", CultureInfo.InvariantCulture);
                dailyRevenue.Add(new ManagerChartPoint
                {
                    Key = key,
                    Label = label,
                    Value = revenueByDay.TryGetValue(d, out var rv) ? rv : 0m
                });
                dailyBookings.Add(new ManagerCountPoint
                {
                    Key = key,
                    Label = label,
                    Count = bookingsByDay.TryGetValue(d, out var bc) ? bc : 0
                });
            }

            var occupied = RoomCount("occupied");

            return new ManagerDashboardViewModel
            {
                StartDate = start,
                EndDate = end,
                PeriodDays = periodDays,

                TotalRooms = totalRooms,
                AvailableRooms = RoomCount("available"),
                OccupiedRooms = occupied,
                NeedsCleaningRooms = RoomCount("needs_cleaning"),
                MaintenanceRooms = RoomCount("maintenance"),
                OccupancyRate = totalRooms > 0 ? Math.Round(occupied * 100m / totalRooms, 2) : 0m,

                TotalBookings = bookingRows.Sum(x => x.Count),
                PendingCheckInBookings = BookingCount("confirmed"),
                PendingApprovalBookings = BookingCount("pending_approval"),
                PeriodBookings = bookingDates.Count,

                TotalRevenue = totalRevenue,
                PeriodRevenue = payments.Sum(x => x.Amount),
                PeriodPaymentCount = payments.Count,

                ActivePromotions = activePromotions,
                TotalReviews = reviewCount,
                AverageRating = avgRating.HasValue ? Math.Round((decimal)avgRating.Value, 2) : null,

                RoomsByStatus = RoomStatuses.Select(s => new ManagerCountPoint
                {
                    Key = s,
                    Label = RoomStatusLabel(s),
                    Count = RoomCount(s)
                }).ToList(),
                BookingsByStatus = BookingStatuses.Select(s => new ManagerCountPoint
                {
                    Key = s,
                    Label = BookingStatusLabel(s),
                    Count = BookingCount(s)
                }).ToList(),
                DailyRevenue = dailyRevenue,
                DailyBookings = dailyBookings,
                RecentBookings = recent,
                DataNotes = new List<string>
                {
                    "รายรับ = ผลรวม Payment ที่สถานะ approved (วันที่ = paid_at, ถ้าไม่มีใช้ reviewed_at หรือ created_at) ไม่หักเงินคืน จึงอาจต่างจากรายงานของ Owner",
                    "จำนวนห้องทั้งหมดและสถานะห้องนับเฉพาะห้องที่เปิดใช้งาน (is_active ไม่เป็น false)",
                    "การจองที่รอเข้าพัก = Booking สถานะ confirmed ส่วนจำนวนการจองในช่วงวันที่นับทุกสถานะตามวันที่สร้าง Booking"
                }
            };
        }

        private async Task<ManagerRoomManagementViewModel> BuildRoomManagementAsync(
            string? q, string? status, byte? roomTypeId, bool includePast, CancellationToken ct)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var search = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
            var statusFilter = status != null && RoomStatuses.Contains(status) ? status : null;

            // ราคาปัจจุบันของแต่ละประเภทห้อง
            var currentRates = (await _db.RoomRatePeriods.AsNoTracking()
                    .Where(p => p.StartDate <= today && p.EndDate >= today)
                    .Select(p => new { p.RoomTypeId, p.RateId, p.PricePerNight, p.PeriodName })
                    .ToListAsync(ct))
                .GroupBy(x => x.RoomTypeId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.RateId).First());

            // จำนวนห้องต่อประเภท x สถานะ ใน 1 Query
            var counts = await _db.Rooms.AsNoTracking()
                .Where(r => r.IsActive != false)
                .GroupBy(r => new { r.RoomTypeId, r.Status })
                .Select(g => new { g.Key.RoomTypeId, g.Key.Status, Count = g.Count() })
                .ToListAsync(ct);

            var roomTypes = await _db.RoomTypes.AsNoTracking()
                .OrderBy(t => t.RoomTypeId)
                .Select(t => new ManagerRoomTypeRow
                {
                    RoomTypeId = t.RoomTypeId,
                    TypeCode = t.TypeCode,
                    TypeName = t.TypeName,
                    Description = t.Description,
                    BedType = t.BedType,
                    MinGuests = t.MinGuests,
                    MaxGuests = t.MaxGuests,
                    AllowExtraBed = t.AllowExtraBed,
                    ExtraBedPrice = t.ExtraBedPrice,
                    BasePrice = t.BasePrice
                })
                .ToListAsync(ct);

            foreach (var t in roomTypes)
            {
                int C(string s) => counts.Where(x => x.RoomTypeId == t.RoomTypeId && x.Status == s).Sum(x => x.Count);
                t.RoomCount = counts.Where(x => x.RoomTypeId == t.RoomTypeId).Sum(x => x.Count);
                t.AvailableCount = C("available");
                t.OccupiedCount = C("occupied");
                t.NeedsCleaningCount = C("needs_cleaning");
                t.MaintenanceCount = C("maintenance");
                if (currentRates.TryGetValue(t.RoomTypeId, out var cr))
                {
                    t.CurrentPrice = cr.PricePerNight;
                    t.CurrentPeriodName = cr.PeriodName;
                }
                else
                {
                    t.CurrentPrice = t.BasePrice;
                }
            }

            // รายการห้อง (ค้นหา/กรอง) รวมห้องที่ปิดให้บริการ เพื่อให้เปิดกลับได้
            var roomQuery = _db.Rooms.AsNoTracking().AsQueryable();
            if (roomTypeId.HasValue)
            {
                var tid = roomTypeId.Value;
                roomQuery = roomQuery.Where(r => r.RoomTypeId == tid);
            }
            if (statusFilter != null) roomQuery = roomQuery.Where(r => r.Status == statusFilter);
            if (search != null)
                roomQuery = roomQuery.Where(r => r.RoomNumber.Contains(search) || r.RoomType.TypeName.Contains(search));

            var rooms = await roomQuery
                .OrderBy(r => r.FloorNo).ThenBy(r => r.RoomNumber)
                .Select(r => new ManagerRoomRow
                {
                    RoomId = r.RoomId,
                    RoomNumber = r.RoomNumber,
                    FloorNo = r.FloorNo,
                    RoomTypeId = r.RoomTypeId,
                    RoomTypeName = r.RoomType.TypeName,
                    Status = r.Status,
                    IsActive = r.IsActive != false,
                    BasePrice = r.RoomType.BasePrice
                })
                .ToListAsync(ct);

            foreach (var r in rooms)
            {
                r.StatusLabel = RoomStatusLabel(r.Status);
                r.CurrentPrice = currentRates.TryGetValue(r.RoomTypeId, out var cr) ? cr.PricePerNight : r.BasePrice;
            }

            // ช่วงราคา
            var periodQuery = _db.RoomRatePeriods.AsNoTracking().AsQueryable();
            if (roomTypeId.HasValue)
            {
                var tid = roomTypeId.Value;
                periodQuery = periodQuery.Where(p => p.RoomTypeId == tid);
            }
            if (!includePast) periodQuery = periodQuery.Where(p => p.EndDate >= today);

            var periods = await periodQuery
                .OrderBy(p => p.RoomTypeId).ThenBy(p => p.StartDate)
                .Select(p => new ManagerPricePeriodRow
                {
                    RateId = p.RateId,
                    RoomTypeId = p.RoomTypeId,
                    RoomTypeName = p.RoomType.TypeName,
                    PeriodName = p.PeriodName,
                    StartDate = p.StartDate,
                    EndDate = p.EndDate,
                    PricePerNight = p.PricePerNight,
                    CreatedByName = p.CreatedByNavigation != null ? p.CreatedByNavigation.FullName : null,
                    CreatedAt = p.CreatedAt
                })
                .ToListAsync(ct);

            foreach (var p in periods)
            {
                if (p.EndDate < today) { p.PeriodStatus = "past"; p.PeriodStatusLabel = "สิ้นสุดแล้ว"; }
                else if (p.StartDate > today) { p.PeriodStatus = "upcoming"; p.PeriodStatusLabel = "ยังไม่เริ่ม"; }
                else { p.PeriodStatus = "current"; p.PeriodStatusLabel = "ใช้อยู่ตอนนี้"; }
            }

            // [ใหม่] ประวัติการเปลี่ยนแปลงราคา 30 รายการล่าสุด (จาก audit_logs)
            var priceTables = new List<string> { "room_types", "room_rate_periods" };
            var priceActions = new List<string>
                { "base_price_update", "price_period_create", "price_period_update", "price_period_delete" };
            var logs = await _db.AuditLogs.AsNoTracking()
                .Where(a => priceTables.Contains(a.TableName) && priceActions.Contains(a.Action))
                .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.LogId)
                .Take(30)
                .Select(a => new
                {
                    a.CreatedAt,
                    a.Action,
                    a.OldValue,
                    a.NewValue,
                    UserName = a.User != null ? a.User.FullName : null
                })
                .ToListAsync(ct);

            var history = logs.Select(a => new ManagerPriceHistoryRow
            {
                CreatedAt = a.CreatedAt,
                UserName = a.UserName,
                Action = a.Action,
                ActionLabel = PriceActionLabel(a.Action),
                Summary = DescribeChange(a.OldValue, a.NewValue)
            }).ToList();

            return new ManagerRoomManagementViewModel
            {
                Search = search,
                Status = statusFilter,
                RoomTypeId = roomTypeId,
                IncludePastPeriods = includePast,
                Today = today,
                TotalRooms = counts.Sum(x => x.Count),
                FilteredRoomCount = rooms.Count,
                RoomTypes = roomTypes,
                Rooms = rooms,
                PricePeriods = periods,
                PriceHistory = history,
                StatusOptions = RoomStatuses.Select(s => new ManagerOption { Value = s, Label = RoomStatusLabel(s) }).ToList(),
                RoomTypeOptions = roomTypes.Select(t => new ManagerOption { Value = t.RoomTypeId.ToString(CultureInfo.InvariantCulture), Label = t.TypeName }).ToList(),
                DataNotes = new List<string>
                {
                    "ราคาปัจจุบัน = RoomRatePeriod ที่ครอบคลุมวันนี้ (ถ้าซ้อนกันใช้รายการล่าสุด) ถ้าไม่มีใช้ BasePrice ของประเภทห้อง",
                    "ตารางห้องไม่มีคอลัมน์ราคา ราคามาจากประเภทห้อง (RoomType) ทั้งหมด",
                    "จำนวนห้องในสรุปประเภทห้องนับเฉพาะห้องที่เปิดใช้งาน ส่วนรายการห้องแสดงทั้งหมดรวมห้องที่ปิดให้บริการ",
                    "สถานะห้อง (ว่าง/มีผู้เข้าพัก/รอทำความสะอาด) เปลี่ยนโดย Reception และ Housekeeping Manager ปิด/เปิดให้บริการได้เท่านั้น",
                    "ประวัติราคาแสดง 30 รายการล่าสุดจาก audit_logs"
                }
            };
        }

        private async Task<ManagerPromotionManagementViewModel> BuildPromotionManagementAsync(
            string? q, string? status, string? discountType, CancellationToken ct)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var search = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
            var typeFilter = discountType != null && DiscountTypes.Contains(discountType) ? discountType : null;
            var validStatuses = new[] { "active", "scheduled", "expired", "inactive" };
            var statusFilter = status != null && validStatuses.Contains(status) ? status : null;

            var query = _db.Promotions.AsNoTracking().AsQueryable();
            if (search != null) query = query.Where(p => p.PromoCode.Contains(search) || p.PromoName.Contains(search));
            if (typeFilter != null) query = query.Where(p => p.DiscountType == typeFilter);

            var promos = await query
                .OrderByDescending(p => p.PromotionId)
                .Select(p => new ManagerPromotionRow
                {
                    PromotionId = p.PromotionId,
                    PromoCode = p.PromoCode,
                    PromoName = p.PromoName,
                    Description = p.Description,
                    DiscountType = p.DiscountType,
                    DiscountPercent = p.DiscountPercent,
                    DiscountPercentMax = p.DiscountPercentMax,
                    MinGuests = p.MinGuests,
                    MinNights = p.MinNights,
                    MaxDiscountNights = p.MaxDiscountNights,
                    ApplicableRoomTypeId = p.ApplicableRoomTypeId,
                    ApplicableRoomTypeName = p.ApplicableRoomType != null ? p.ApplicableRoomType.TypeName : null,
                    RequiresHoliday = p.RequiresHoliday,
                    VoucherMinValue = p.VoucherMinValue,
                    VoucherMaxValue = p.VoucherMaxValue,
                    IsActive = p.IsActive == true,
                    ValidFrom = p.ValidFrom,
                    ValidTo = p.ValidTo,
                    UsageCount = p.BookingPromotions.Count(),
                    CreatedAt = p.CreatedAt
                })
                .ToListAsync(ct);

            foreach (var p in promos)
            {
                p.Status = PromotionStatus(p.IsActive, p.ValidFrom, p.ValidTo, today);
                p.StatusLabel = PromotionStatusLabel(p.Status);
                p.DiscountTypeLabel = DiscountTypeLabel(p.DiscountType);
            }

            var activeCount = promos.Count(p => p.Status == "active");
            var scheduledCount = promos.Count(p => p.Status == "scheduled");
            var expiredCount = promos.Count(p => p.Status == "expired");
            var inactiveCount = promos.Count(p => p.Status == "inactive");

            if (statusFilter != null) promos = promos.Where(p => p.Status == statusFilter).ToList();

            var holidays = await _db.Holidays.AsNoTracking()
                .OrderByDescending(h => h.StartDate)
                .Select(h => new ManagerHolidayRow
                {
                    HolidayId = h.HolidayId,
                    HolidayName = h.HolidayName,
                    StartDate = h.StartDate,
                    EndDate = h.EndDate,
                    EventNote = h.EventNote,
                    CreatedByName = h.CreatedByNavigation != null ? h.CreatedByNavigation.FullName : null
                })
                .ToListAsync(ct);

            foreach (var h in holidays)
            {
                h.DayCount = h.EndDate.DayNumber - h.StartDate.DayNumber + 1;
                if (h.EndDate < today) { h.Status = "past"; h.StatusLabel = "ผ่านมาแล้ว"; }
                else if (h.StartDate > today) { h.Status = "upcoming"; h.StatusLabel = "กำลังจะมาถึง"; }
                else { h.Status = "ongoing"; h.StatusLabel = "กำลังดำเนินอยู่"; }
            }

            var roomTypeOptions = await _db.RoomTypes.AsNoTracking()
                .OrderBy(t => t.RoomTypeId)
                .Select(t => new { t.RoomTypeId, t.TypeName })
                .ToListAsync(ct);

            return new ManagerPromotionManagementViewModel
            {
                Search = search,
                Status = statusFilter,
                DiscountType = typeFilter,
                Today = today,
                MaxDiscountPercent = MaxDiscountPercent,
                ActiveCount = activeCount,
                ScheduledCount = scheduledCount,
                ExpiredCount = expiredCount,
                InactiveCount = inactiveCount,
                Promotions = promos,
                Holidays = holidays,
                StatusOptions = validStatuses.Select(s => new ManagerOption { Value = s, Label = PromotionStatusLabel(s) }).ToList(),
                DiscountTypeOptions = DiscountTypes.Select(s => new ManagerOption { Value = s, Label = DiscountTypeLabel(s) }).ToList(),
                RoomTypeOptions = roomTypeOptions.Select(t => new ManagerOption
                {
                    Value = t.RoomTypeId.ToString(CultureInfo.InvariantCulture),
                    Label = t.TypeName
                }).ToList(),
                DataNotes = new List<string>
                {
                    "ตาราง promotions ไม่มีคอลัมน์สถานะ จึงคำนวณจาก is_active และ valid_from/valid_to (ใช้งานอยู่ / รอเริ่ม / หมดอายุ / ปิดใช้งาน)",
                    "ไม่มีการลบโปรโมชั่นเพราะถูกอ้างอิงโดย booking_promotions ให้ปิดใช้งานแทน",
                    $"เปอร์เซ็นต์ส่วนลดต่อโปรโมชั่นตั้งได้ไม่เกิน {MaxDiscountPercent:0}% ส่วนการรวมส่วนลดหลายโปรโมชั่นคำนวณโดยระบบจองของลูกค้า",
                    "สรุปจำนวนตามสถานะคำนวณก่อนกรองสถานะ แต่หลังกรองคำค้นและประเภทส่วนลด"
                }
            };
        }

        private async Task<ManagerReviewManagementViewModel> BuildReviewManagementAsync(
            string? q, byte? rating, DateOnly? startDate, DateOnly? endDate, int page, CancellationToken ct)
        {
            const int pageSize = 20;
            var search = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
            byte? ratingFilter = rating is >= 1 and <= 5 ? rating : null;

            var query = _db.Reviews.AsNoTracking().AsQueryable();
            if (ratingFilter.HasValue)
            {
                var rv = ratingFilter.Value;
                query = query.Where(r => r.Rating == rv);
            }
            if (startDate.HasValue)
            {
                var f = startDate.Value.ToDateTime(TimeOnly.MinValue);
                query = query.Where(r => r.CreatedAt >= f);
            }
            if (endDate.HasValue)
            {
                var t = endDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
                query = query.Where(r => r.CreatedAt < t);
            }
            if (search != null)
            {
                query = query.Where(r =>
                    (r.Comment != null && r.Comment.Contains(search)) ||
                    r.Booking.BookingCode.Contains(search) ||
                    r.Booking.ContactName.Contains(search) ||
                    (r.User != null && r.User.FullName.Contains(search)));
            }

            var dist = await query
                .GroupBy(r => r.Rating)
                .Select(g => new { Rating = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var total = dist.Sum(x => x.Count);
            decimal? avg = total > 0 ? Math.Round(dist.Sum(x => x.Rating * x.Count) / (decimal)total, 2) : null;
            var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
            var currentPage = Math.Clamp(page, 1, totalPages);

            var rows = await query
                .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.ReviewId)
                .Skip((currentPage - 1) * pageSize).Take(pageSize)
                .Select(r => new ManagerReviewRow
                {
                    ReviewId = r.ReviewId,
                    BookingId = r.BookingId,
                    BookingCode = r.Booking.BookingCode,
                    ReviewerName = r.User != null ? r.User.FullName : r.Booking.ContactName,
                    Rating = r.Rating,
                    Comment = r.Comment,
                    CreatedAt = r.CreatedAt
                })
                .ToListAsync(ct);

            return new ManagerReviewManagementViewModel
            {
                Search = search,
                Rating = ratingFilter,
                StartDate = startDate,
                EndDate = endDate,
                Page = currentPage,
                PageSize = pageSize,
                TotalCount = total,
                TotalPages = totalPages,
                AverageRating = avg,
                RatingDistribution = Enumerable.Range(1, 5).Reverse().Select(i => new ManagerCountPoint
                {
                    Key = i.ToString(CultureInfo.InvariantCulture),
                    Label = $"{i} ดาว",
                    Count = dist.Where(x => x.Rating == i).Sum(x => x.Count)
                }).ToList(),
                Reviews = rows,
                DataNotes = new List<string>
                {
                    "ผู้รีวิว = users.full_name ของบัญชีที่รีวิว ถ้าบัญชีถูกลบ (user_id เป็น null) ใช้ชื่อผู้ติดต่อของ Booking แทน",
                    "หน้านี้เป็นการดูรีวิวอย่างเดียว (ค้นหา/กรองตามคะแนนและวันที่) ไม่มีการตอบกลับ ซ่อน หรือลบรีวิว",
                    "ค่าเฉลี่ยและการกระจายคะแนนคำนวณจากรีวิวที่ผ่านตัวกรองปัจจุบัน"
                }
            };
        }

        private async Task<ManagerStaffManagementViewModel> BuildStaffManagementAsync(string? q, string? roleCode, CancellationToken ct)
        {
            var search = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
            var roleFilter = string.IsNullOrWhiteSpace(roleCode) ? null : roleCode.Trim();
            var me = GetCurrentUserId();

            // คัดลอกเป็น List<string> ตัวแปรโลคอล เพื่อให้ EF แปลงเป็น SQL "NOT IN (...)" ได้
            // (หลีกเลี่ยง ReadOnlySpan<string> ใน Expression Tree บน .NET 10)
            var nonStaff = NonStaffRoleCodes.ToList();
            var unassignable = UnassignableRoleCodes.ToList();

            // พนักงาน = ผู้ใช้ที่มี Role อย่างน้อยหนึ่งตัวที่ไม่ใช่ CUSTOMER/GUEST
            var query = _db.Users.AsNoTracking()
                .Where(u => u.UserRoles.Any(ur => !nonStaff.Contains(ur.Role.RoleCode)));

            if (roleFilter != null) query = query.Where(u => u.UserRoles.Any(ur => ur.Role.RoleCode == roleFilter));
            if (search != null)
                query = query.Where(u => u.Username.Contains(search) || u.FullName.Contains(search) || u.Email.Contains(search));

            // ไม่ดึง PasswordHash
            var staff = await query
                .OrderBy(u => u.FullName)
                .Select(u => new ManagerStaffRow
                {
                    UserId = u.UserId,
                    Username = u.Username,
                    FullName = u.FullName,
                    Email = u.Email,
                    Phone = u.Phone,
                    IsActive = u.IsActive == true,
                    CreatedAt = u.CreatedAt,
                    Roles = u.UserRoles
                        .Where(ur => !nonStaff.Contains(ur.Role.RoleCode))
                        .Select(ur => new ManagerStaffRoleItem { RoleCode = ur.Role.RoleCode, RoleName = ur.Role.RoleName })
                        .ToList()
                })
                .ToListAsync(ct);

            foreach (var s in staff)
            {
                s.IsSelf = me.HasValue && s.UserId == me.Value;
                s.CanManage = !s.IsSelf && !s.Roles.Any(r => ProtectedStaffRoleCodes.Contains(r.RoleCode));
            }

            var roleOptions = await _db.Roles.AsNoTracking()
                .Where(r => !nonStaff.Contains(r.RoleCode))
                .OrderBy(r => r.RoleCode)
                .Select(r => new ManagerOption { Value = r.RoleCode, Label = r.RoleName })
                .ToListAsync(ct);

            var assignable = await _db.Roles.AsNoTracking()
                .Where(r => !unassignable.Contains(r.RoleCode))
                .OrderBy(r => r.RoleCode)
                .Select(r => new ManagerOption { Value = r.RoleCode, Label = r.RoleName })
                .ToListAsync(ct);

            return new ManagerStaffManagementViewModel
            {
                Search = search,
                RoleCode = roleFilter,
                TotalStaff = staff.Count,
                ActiveStaff = staff.Count(s => s.IsActive),
                InactiveStaff = staff.Count(s => !s.IsActive),
                Staff = staff,
                RoleOptions = roleOptions,
                AssignableRoleOptions = assignable,
                DataNotes = new List<string>
                {
                    "พนักงาน = ผู้ใช้ที่มี Role อื่นนอกจาก CUSTOMER และ GUEST บทบาทอ่านจาก user_roles และ roles",
                    "Manager เพิ่ม ระงับ เปิดใช้งาน และเปลี่ยน Role ได้เฉพาะ Role พนักงานทั่วไป ไม่สามารถแตะบัญชี Owner/Manager หรือบัญชีตัวเอง",
                    "ไม่แสดงและไม่สามารถแก้รหัสผ่านพนักงานได้ รหัสผ่านเริ่มต้นตั้งได้ตอนสร้างบัญชีเท่านั้น"
                }
            };
        }

        private async Task<ManagerProfileViewModel?> BuildProfileAsync(uint userId, CancellationToken ct)
        {
            // ไม่ดึง PasswordHash
            return await _db.Users.AsNoTracking()
                .Where(u => u.UserId == userId && u.UserRoles.Any(ur => ur.Role.RoleCode == ManagerRoleCode))
                .Select(u => new ManagerProfileViewModel
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

        // ═════════════════════════ Security & response helpers ═════════════════════════

        private uint? GetCurrentUserId()
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return uint.TryParse(raw, out var id) ? id : null;
        }

        /// <summary>ยืนยันจากฐานข้อมูลว่าผู้ใช้ปัจจุบันยังเป็น MANAGER ที่ Active ก่อนอนุญาตให้เขียนข้อมูล</summary>
        private async Task<uint?> GetValidManagerAsync(CancellationToken ct)
        {
            var id = GetCurrentUserId();
            if (id == null) return null;
            var uid = id.Value;

            var ok = await _db.Users.AsNoTracking().AnyAsync(u =>
                u.UserId == uid &&
                u.IsActive == true &&
                u.UserRoles.Any(ur => ur.Role.RoleCode == ManagerRoleCode), ct);

            return ok ? uid : null;
        }

        /// <summary>
        /// [ใหม่] โหลดบัญชีพนักงานที่ Manager มีสิทธิ์จัดการ
        /// ปฏิเสธเมื่อ: เป็นตัวเอง / ไม่ใช่พนักงาน / มี Role OWNER, MANAGER, ADMIN
        /// </summary>
        private async Task<(User? User, string? Error)> LoadManageableStaffAsync(uint userId, uint managerId, CancellationToken ct)
        {
            if (userId == managerId) return (null, "ไม่สามารถดำเนินการกับบัญชีของตัวเองได้");

            var user = await _db.Users
                .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.UserId == userId, ct);
            if (user == null) return (null, "ไม่พบบัญชีผู้ใช้");

            var codes = user.UserRoles.Select(ur => ur.Role.RoleCode).ToList();
            if (codes.Any(c => ProtectedStaffRoleCodes.Contains(c)))
                return (null, "ไม่สามารถแก้ไขบัญชี Owner หรือ Manager ได้");
            if (!codes.Any(c => !NonStaffRoleCodes.Contains(c)))
                return (null, "บัญชีนี้ไม่ใช่บัญชีพนักงาน");

            return (user, null);
        }

        /// <summary>
        /// [ใหม่] บันทึก audit_logs (ไม่ทำให้งานหลักล้มเหลวถ้าบันทึก log ไม่สำเร็จ)
        /// เรียกหลังงานหลัก SaveChanges สำเร็จแล้ว เพื่อให้มี recordId ของแถวใหม่
        /// </summary>
        private async Task AuditAsync(uint? userId, string action, string table, object recordId,
            object? oldValue, object? newValue, CancellationToken ct)
        {
            try
            {
                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = action,
                    TableName = table,
                    RecordId = Convert.ToString(recordId, CultureInfo.InvariantCulture),
                    OldValue = oldValue == null ? null : JsonSerializer.Serialize(oldValue),
                    NewValue = newValue == null ? null : JsonSerializer.Serialize(newValue),
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
                });
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audit log write failed for {Action} on {Table}", action, table);
            }
        }

        private bool IsAjax()
            => string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

        private IActionResult Denied()
            => IsAjax() ? Unauthorized() : RedirectToAction("Login", "Account");

        /// <summary>AJAX → JSON, ฟอร์มปกติ → TempData + Redirect (ใช้คีย์เดียวกับ OwnerController)</summary>
        private IActionResult Respond(bool success, string message, string redirectAction)
        {
            if (IsAjax()) return Json(new { success, message });

            TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
            return RedirectToAction(redirectAction);
        }

        // ═════════════════════════ Pure helpers ═════════════════════════

        private static string? CleanText(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var t = value.Trim();
            return t.Length > maxLength ? t.Substring(0, maxLength) : t;
        }

        /// <summary>[ใหม่] ตรวจ magic bytes ของไฟล์รูป (JPEG / PNG / WEBP)</summary>
        private static async Task<bool> LooksLikeImageAsync(IFormFile file, string ext, CancellationToken ct)
        {
            var buf = new byte[12];
            await using var s = file.OpenReadStream();
            var n = await s.ReadAtLeastAsync(buf, buf.Length, throwOnEndOfStream: false, cancellationToken: ct);
            if (n < buf.Length) return false;

            return ext switch
            {
                ".jpg" or ".jpeg" => buf[0] == 0xFF && buf[1] == 0xD8 && buf[2] == 0xFF,
                ".png" => buf[0] == 0x89 && buf[1] == 0x50 && buf[2] == 0x4E && buf[3] == 0x47,
                ".webp" => buf[0] == 'R' && buf[1] == 'I' && buf[2] == 'F' && buf[3] == 'F' &&
                           buf[8] == 'W' && buf[9] == 'E' && buf[10] == 'B' && buf[11] == 'P',
                _ => false
            };
        }

        private static (DateOnly Start, DateOnly End) ResolveRange(DateOnly? startDate, DateOnly? endDate)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var end = endDate ?? today;
            var start = startDate ?? new DateOnly(end.Year, end.Month, 1);

            if (start > end) (start, end) = (end, start);
            if (end > today) end = today;
            if (start > end) start = end;
            if (end.DayNumber - start.DayNumber > 365) start = end.AddDays(-365);

            return (start, end);
        }

        // ── Audit JSON → ข้อความอ่านง่าย ──

        private static Dictionary<string, string> ParseFlat(string? json)
        {
            var d = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(json)) return d;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return d;
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    d[p.Name] = p.Value.ValueKind switch
                    {
                        JsonValueKind.String => p.Value.GetString() ?? "-",
                        JsonValueKind.Null => "-",
                        _ => p.Value.GetRawText()
                    };
                }
            }
            catch (JsonException) { }
            return d;
        }

        private static string AuditKeyLabel(string key) => key switch
        {
            "roomType" => "ประเภทห้อง",
            "basePrice" => "ราคาพื้นฐาน",
            "extraBedPrice" => "ราคาเตียงเสริม",
            "period" => "ชื่อช่วง",
            "startDate" => "เริ่ม",
            "endDate" => "สิ้นสุด",
            "pricePerNight" => "ราคาต่อคืน",
            _ => key
        };

        private static string DescribeChange(string? oldJson, string? newJson)
        {
            var o = ParseFlat(oldJson);
            var n = ParseFlat(newJson);
            var parts = new List<string>();

            foreach (var key in o.Keys.Union(n.Keys))
            {
                var hasOld = o.TryGetValue(key, out var ov);
                var hasNew = n.TryGetValue(key, out var nv);
                if (hasOld && hasNew)
                {
                    if (ov != nv) parts.Add($"{AuditKeyLabel(key)}: {ov} → {nv}");
                }
                else
                {
                    parts.Add($"{AuditKeyLabel(key)}: {(hasNew ? nv : ov)}");
                }
            }
            return string.Join(" | ", parts);
        }

        private static string PriceActionLabel(string action) => action switch
        {
            "base_price_update" => "แก้ราคาพื้นฐาน",
            "price_period_create" => "เพิ่มช่วงราคา",
            "price_period_update" => "แก้ช่วงราคา",
            "price_period_delete" => "ลบช่วงราคา",
            _ => action
        };

        private static string PromotionStatus(bool isActive, DateOnly? from, DateOnly? to, DateOnly today)
        {
            if (!isActive) return "inactive";
            if (to.HasValue && to.Value < today) return "expired";
            if (from.HasValue && from.Value > today) return "scheduled";
            return "active";
        }

        private static string PromotionStatusLabel(string s) => s switch
        {
            "active" => "ใช้งานอยู่",
            "scheduled" => "รอเริ่ม",
            "expired" => "หมดอายุ",
            "inactive" => "ปิดใช้งาน",
            _ => s
        };

        private static string DiscountTypeLabel(string s) => s switch
        {
            "free_extra_bed" => "เตียงเสริมฟรี",
            "percent_room" => "ลดเปอร์เซ็นต์ค่าห้อง",
            "percent_extra_nights" => "ลดเปอร์เซ็นต์คืนพิเศษ",
            _ => s
        };

        private static string RoomStatusLabel(string s) => s switch
        {
            "available" => "ว่าง",
            "occupied" => "มีผู้เข้าพัก",
            "needs_cleaning" => "รอทำความสะอาด",
            "maintenance" => "ซ่อมบำรุง",
            _ => s
        };

        private static string BookingStatusLabel(string s) => s switch
        {
            "pending_payment" => "รอชำระเงิน",
            "pending_approval" => "รอตรวจสอบการชำระเงิน",
            "confirmed" => "ยืนยันแล้ว (รอเข้าพัก)",
            "checked_in" => "เข้าพักแล้ว",
            "checked_out" => "เช็คเอาท์แล้ว",
            "cancelled" => "ยกเลิก",
            "rejected" => "ถูกปฏิเสธ",
            _ => s
        };
    }
}