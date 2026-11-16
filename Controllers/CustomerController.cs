using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayMate_Kavinvee_0576.Models;
using StayMate_Kavinvee_0576.Models.ViewModels;

namespace StayMate_Kavinvee_0576.Controllers
{
    [Authorize(Roles = "CUSTOMER")]
    public class CustomerController : Controller
    {
        private readonly StayMateDbContext _db;

        public CustomerController(StayMateDbContext db)
        {
            _db = db;
        }

        // ==========================================
        // ตรวจสอบผู้ใช้งานที่ Login
        // ==========================================
        private async Task<uint?> GetCurrentUserId()
        {
            var idText = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!uint.TryParse(idText, out var userId))
                return null;

            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u =>
                    u.UserId == userId &&
                    u.IsActive == true);

            if (user == null)
                return null;

            return userId;
        }

        // ==========================================
        // 1. HOME
        // ==========================================
        private static readonly string[] PromoGrads =
            { "customer-home-grad-blue", "customer-home-grad-violet", "customer-home-grad-cyan",
              "customer-home-grad-indigo", "customer-home-grad-gold" };

        private static readonly string[] PromoIcons =
            { "fa-gift", "fa-people-group", "fa-moon", "fa-laptop-code", "fa-champagne-glasses" };

        private static readonly string[] RoomBadgeClasses =
            { "customer-home-badge-cyan", "customer-home-badge-blue",
              "customer-home-badge-violet", "customer-home-badge-gold" };

        private static string RoomImage(string? code, string? name)
        {
            var ex = RoomExtrasCatalog.Find(code ?? "", name ?? "");
            return ex?.Gallery?.FirstOrDefault() ?? RoomExtrasCatalog.Fallback(name ?? "");
        }

        public async Task<IActionResult> Index()
        {
            var userId = await GetCurrentUserId();

            if (userId == null)
                return RedirectToAction("Login", "Account");

            var uid = userId.Value;
            var today = DateOnly.FromDateTime(DateTime.Today);

            // ---------- 1) สถิติการจอง ----------
            var statusCounts = await _db.Bookings
                .AsNoTracking()
                .Where(b => b.UserId == uid)
                .GroupBy(b => b.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            int CountOf(params string[] s) =>
                statusCounts.Where(x => s.Contains(x.Status)).Sum(x => x.Count);

            // ---------- 2) การจองล่าสุด 2 รายการ ----------
            var latestRaw = await _db.Bookings
                .AsNoTracking()
                .Where(b => b.UserId == uid)
                .OrderByDescending(b => b.CreatedAt)
                .Take(2)
                .Select(b => new
                {
                    b.BookingId,
                    b.BookingCode,
                    b.Status,
                    b.CheckInDate,
                    b.CheckOutDate,
                    b.Nights,
                    b.NumGuests,
                    b.TotalAmount,
                    RoomCount = b.BookingRooms.Count(),
                    TypeCode = b.BookingRooms.Select(br => br.RoomType.TypeCode).FirstOrDefault(),
                    TypeName = b.BookingRooms.Select(br => br.RoomType.TypeName).FirstOrDefault(),
                    PromoName = b.BookingPromotions.Select(bp => bp.Promotion.PromoName).FirstOrDefault()
                })
                .ToListAsync();

            var latest = latestRaw.Select(b => new CustomerBookingItem
            {
                BookingId = b.BookingId,
                BookingCode = b.BookingCode,
                Status = b.Status,
                CheckIn = b.CheckInDate,
                CheckOut = b.CheckOutDate,
                Nights = b.Nights,
                Guests = b.NumGuests,
                RoomCount = b.RoomCount,
                TotalAmount = b.TotalAmount,
                RoomTypeName = b.TypeName ?? "-",
                ImageUrl = RoomImage(b.TypeCode, b.TypeName),
                PromoName = b.PromoName
            }).ToList();

            // ---------- 3) ประเภทห้อง ----------
            var roomTypes = await _db.RoomTypes
                .AsNoTracking()
                .OrderBy(rt => rt.BasePrice)
                .Select(rt => new
                {
                    rt.RoomTypeId,
                    rt.TypeCode,
                    rt.TypeName,
                    rt.Description,
                    rt.BedType,
                    rt.MinGuests,
                    rt.MaxGuests,
                    rt.BasePrice,
                    Available = rt.Rooms.Count(r => r.Status == "available" && r.IsActive == true)
                })
                .ToListAsync();

            var rooms = roomTypes.Select((rt, i) =>
            {
                var ex = RoomExtrasCatalog.Find(rt.TypeCode, rt.TypeName);
                return new CustomerRoomItem
                {
                    RoomTypeId = rt.RoomTypeId,
                    TypeName = rt.TypeName,
                    Description = rt.Description,
                    MinGuests = rt.MinGuests,
                    MaxGuests = rt.MaxGuests,
                    Price = rt.BasePrice,
                    AvailableRooms = rt.Available,
                    ImageUrl = RoomImage(rt.TypeCode, rt.TypeName),
                    SizeText = ex?.SizeText ?? "",
                    BadgeText = ex?.Badge ?? "",
                    BadgeClass = RoomBadgeClasses[i % RoomBadgeClasses.Length],
                    Highlights = (ex?.Highlights ?? new[] { rt.BedType ?? "" })
                        .Where(h => !string.IsNullOrWhiteSpace(h))
                        .Take(3)
                        .ToArray()
                };
            }).ToList();

            var roomTypeNames = roomTypes.ToDictionary(rt => rt.RoomTypeId, rt => rt.TypeName);

            // ---------- 4) โปรโมชั่นที่เปิดใช้งาน ----------
            var promoRows = await _db.Promotions
                .AsNoTracking()
                .Where(p => p.IsActive == true
                         && (p.ValidFrom == null || p.ValidFrom <= today)
                         && (p.ValidTo == null || p.ValidTo >= today))
                .OrderBy(p => p.PromotionId)
                .ToListAsync();

            var promos = promoRows.Select((p, i) =>
            {
                // สิทธิประโยชน์
                var benefits = new List<string>();
                switch (p.DiscountType)
                {
                    case "free_extra_bed":
                        benefits.Add("ฟรีค่าเตียงเสริม");
                        break;
                    case "percent_room":
                        benefits.Add(p.DiscountPercentMax.HasValue && p.DiscountPercentMax != p.DiscountPercent
                            ? $"ลดค่าห้อง {p.DiscountPercent:0.##}–{p.DiscountPercentMax:0.##}%"
                            : $"ลดค่าห้อง {p.DiscountPercent:0.##}%");
                        break;
                    case "percent_extra_nights":
                        benefits.Add($"ลด {p.DiscountPercent:0.##}% ในคืนที่เพิ่ม"
                            + (p.MaxDiscountNights is > 0 ? $" (สูงสุด {p.MaxDiscountNights} คืน)" : ""));
                        break;
                }
                if (p.VoucherMinValue.HasValue && p.VoucherMaxValue.HasValue)
                    benefits.Add($"E-Voucher ฿{p.VoucherMinValue:N0}–฿{p.VoucherMaxValue:N0}");

                // เงื่อนไข
                var conds = new List<string>();
                if (p.MinGuests is > 0) conds.Add($"ผู้เข้าพักตั้งแต่ {p.MinGuests} ท่านขึ้นไป");
                if (p.MinNights is > 0) conds.Add($"พักขั้นต่ำ {p.MinNights} คืน");
                if (p.RequiresHoliday) conds.Add("ช่วงวันหยุด/เทศกาลที่กำหนด");
                if (p.ApplicableRoomTypeId.HasValue &&
                    roomTypeNames.TryGetValue(p.ApplicableRoomTypeId.Value, out var rtName))
                    conds.Add($"เฉพาะห้อง {rtName}");

                return new CustomerPromoItem
                {
                    PromoCode = p.PromoCode,
                    PromoName = p.PromoName,
                    Description = p.Description,
                    BenefitText = benefits.Count > 0 ? string.Join(" + ", benefits) : "-",
                    ConditionText = conds.Count > 0 ? string.Join(" · ", conds) : "ไม่มีเงื่อนไขขั้นต่ำ",
                    GradClass = PromoGrads[i % PromoGrads.Length],
                    IconClass = PromoIcons[i % PromoIcons.Length],
                    ValidTo = p.ValidTo
                };
            }).ToList();

            // ---------- 5) รีวิว ----------
            var ratings = await _db.Reviews
                .AsNoTracking()
                .Select(r => (int)r.Rating)
                .ToListAsync();

            var reviews = await _db.Reviews
                .AsNoTracking()
                .OrderByDescending(r => r.CreatedAt)
                .Take(3)
                .Select(r => new CustomerReviewItem
                {
                    UserName = r.User != null ? r.User.FullName : "ผู้เข้าพัก",
                    RoomTypeName = r.Booking.BookingRooms
                        .Select(br => br.RoomType.TypeName)
                        .FirstOrDefault() ?? "-",
                    Rating = r.Rating,
                    Comment = r.Comment
                })
                .ToListAsync();

            return View(new CustomerViewModel
            {
                TotalBookings = statusCounts.Sum(x => x.Count),
                UpcomingBookings = CountOf("confirmed"),
                PendingBookings = CountOf("pending_payment", "pending_approval"),
                CompletedBookings = CountOf("checked_out"),
                LatestBookings = latest,
                Rooms = rooms,
                Promotions = promos,
                Reviews = reviews,
                ReviewCount = ratings.Count,
                AverageRating = ratings.Count > 0 ? ratings.Average() : null
            });
        }

        // ==========================================
        // 2. ROOM AVAILABILITY
        // ==========================================
        public async Task<IActionResult> RoomAvailability()
        {
            if (await GetCurrentUserId() == null)
                return RedirectToAction("Login", "Account");

            return View();
        }

        // ==========================================
        // 3. ROOMS / ROOM DETAILS (All-In-One RoomDetails)
        // ==========================================
        public async Task<IActionResult> Rooms()
        {
            if (await GetCurrentUserId() == null)
                return RedirectToAction("Login", "Account");

            // เมื่อลูกค้ากดเมนู "ห้องพัก" ให้เด้งไปหน้า RoomDetails (ห้องที่ 1)
            return RedirectToAction("RoomDetails", new { id = 1 });
        }

        public async Task<IActionResult> RoomDetails(uint id)
        {
            if (await GetCurrentUserId() == null)
                return RedirectToAction("Login", "Account");

            // RoomDetails.cshtml ใช้ Pure Mock เก็บข้อมูลทั้ง 4 ห้องในที่เดียว
            return View();
        }

        // ==========================================
        // 4. PROMOTIONS
        // ==========================================
        public async Task<IActionResult> Promotions()
        {
            if (await GetCurrentUserId() == null)
                return RedirectToAction("Login", "Account");

            return View();
        }

        // ==========================================
        // 5. CHECKOUT & PAYMENT
        // ==========================================
        public async Task<IActionResult> Checkout()
        {
            if (await GetCurrentUserId() == null)
                return RedirectToAction("Login", "Account");

            return View();
        }

        // ==========================================
        // 6. MY BOOKINGS / BOOKING DETAILS
        // ==========================================
       // ==========================================
// 6. MY BOOKINGS / BOOKING DETAILS
// ==========================================
public async Task<IActionResult> MyBookings()
{
    var userId = await GetCurrentUserId();
    if (userId == null)
        return RedirectToAction("Login", "Account");

    var user = await _db.Users
        .AsNoTracking()
        .Include(u => u.Bookings)
        .FirstOrDefaultAsync(u => u.UserId == userId.Value);

    return View(user);
}

public async Task<IActionResult> BookingDetails(uint id)
{
    var userId = await GetCurrentUserId();
    if (userId == null)
        return RedirectToAction("Login", "Account");

    // ป้องกัน IDOR: ดึงเฉพาะการจองที่เป็นของ userId ปัจจุบันเท่านั้น
    var user = await _db.Users
        .AsNoTracking()
        .Include(u => u.Bookings)
        .FirstOrDefaultAsync(u => u.UserId == userId.Value);

    if (user == null)
        return NotFound();

    return View(user);
}


        // ==========================================
        // 7. PROFILE
        // ==========================================
        public async Task<IActionResult> Profile()
        {
            var userId = await GetCurrentUserId();

            if (userId == null)
                return RedirectToAction("Login", "Account");

            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == userId.Value);

            return View(user);
        }

        // ==========================================
        // 8. REVIEW & FEEDBACK
        // ==========================================
        public async Task<IActionResult> Reviews()
        {
            if (await GetCurrentUserId() == null)
                return RedirectToAction("Login", "Account");

            return View();
        }
    }
}