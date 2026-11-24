using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity; // 2. เพิ่ม using บนสุดของไฟล์
using StayMate_Kavinvee_0576.Models;
using StayMate_Kavinvee_0576.Models.ViewModels;

namespace StayMate_Kavinvee_0576.Controllers
{
    [Authorize(Roles = "CUSTOMER")]
    public class CustomerController : Controller
    {
        private readonly StayMateDbContext _db;
        // 1. เพิ่ม field ต่อจาก _db
        private readonly PasswordHasher<User> _hasher = new();

        public CustomerController(StayMateDbContext db)
        {
            _db = db;
        }

        // ==========================================
        // Helpers
        // ==========================================

        /// <summary>คืนค่า UserId ของผู้ใช้ที่ Login อยู่ (และยัง active) หรือ null</summary>
        private async Task<uint?> GetCurrentUserId()
        {
            var idText = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!uint.TryParse(idText, out var userId))
                return null;

            var exists = await _db.Users
                .AsNoTracking()
                .AnyAsync(u => u.UserId == userId && u.IsActive == true);

            return exists ? userId : null;
        }

        private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

        /// <summary>โปรโมชั่นที่เปิดใช้งานและอยู่ในช่วงเวลาที่ใช้ได้</summary>
        private async Task<List<Promotion>> GetActivePromotionsAsync()
        {
            var today = Today;

            return await _db.Promotions
                .AsNoTracking()
                .Where(p => p.IsActive == true
                         && (p.ValidFrom == null || p.ValidFrom <= today)
                         && (p.ValidTo == null || p.ValidTo >= today))
                .OrderBy(p => p.PromotionId)
                .ToListAsync();
        }

        private static JsonResult Fail(string message) =>
            new(new { success = false, message });

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
            var promoRows = await GetActivePromotionsAsync();

            var promos = promoRows.Select((p, i) =>
            {
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
            var reviewCount = await _db.Reviews.AsNoTracking().CountAsync();
            double? averageRating = reviewCount > 0
                ? await _db.Reviews.AsNoTracking().AverageAsync(r => (double)r.Rating)
                : null;

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
                ReviewCount = reviewCount,
                AverageRating = averageRating
            });
        }

        // ==========================================
        // 2. ROOM AVAILABILITY
        // ==========================================
        public async Task<IActionResult> RoomAvailability()
        {
            if (await GetCurrentUserId() == null)
                return RedirectToAction("Login", "Account");

            var roomTypes = await _db.RoomTypes
                .AsNoTracking()
                .Include(rt => rt.Rooms)
                .OrderBy(rt => rt.BasePrice)
                .ToListAsync();

            ViewBag.ActivePromotions = await GetActivePromotionsAsync();

            return View(roomTypes);
        }

        /// <summary>ตรวจสอบจำนวนห้องว่างตามช่วงวันที่เช็คอิน-เช็คเอาท์จริง</summary>
        [HttpGet]
        public async Task<IActionResult> AvailabilityByDate(string? checkIn, string? checkOut)
        {
            if (await GetCurrentUserId() == null)
                return Fail("กรุณาเข้าสู่ระบบใหม่อีกครั้ง");

            if (!TryParseDate(checkIn, out var inDate) || !TryParseDate(checkOut, out var outDate))
                return Fail("รูปแบบวันที่ไม่ถูกต้อง");
            if (inDate < Today || outDate <= inDate)
                return Fail("ช่วงวันที่ไม่ถูกต้อง");

            var typeIds = await _db.RoomTypes.AsNoTracking().Select(rt => rt.RoomTypeId).ToListAsync();
            var available = new Dictionary<string, int>();

            foreach (var id in typeIds)
                available[id.ToString()] = await CountAvailableRoomsAsync(id, inDate, outDate);

            return Json(new { success = true, available });
        }

        // ==========================================
        // 3. ROOMS / ROOM DETAILS
        // ==========================================
        public async Task<IActionResult> Rooms()
        {
            if (await GetCurrentUserId() == null)
                return RedirectToAction("Login", "Account");

            var firstId = await _db.RoomTypes
                .AsNoTracking()
                .OrderBy(rt => rt.RoomTypeId)
                .Select(rt => (byte?)rt.RoomTypeId)
                .FirstOrDefaultAsync();

            return firstId.HasValue
                ? RedirectToAction(nameof(RoomDetails), new { id = firstId.Value })
                : RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> RoomDetails(byte id)
        {
            if (await GetCurrentUserId() == null)
                return RedirectToAction("Login", "Account");

            var roomTypes = await _db.RoomTypes
                .AsNoTracking()
                .OrderBy(rt => rt.RoomTypeId)
                .ToListAsync();

            ViewBag.SelectedRoomTypeId = id;

            return View(roomTypes);
        }

        // ==========================================
        // 4. PROMOTIONS
        // ==========================================
        public async Task<IActionResult> Promotions()
        {
            if (await GetCurrentUserId() == null)
                return RedirectToAction("Login", "Account");

            var today = Today;

            var promotionsList = await _db.Promotions
                .AsNoTracking()
                .Include(p => p.ApplicableRoomType)
                .Where(p => p.IsActive == true && (p.ValidTo == null || p.ValidTo >= today))
                .OrderBy(p => p.PromotionId)
                .ToListAsync();

            return View(promotionsList);
        }

        // ==========================================
        // 5. CHECKOUT & PAYMENT
        // ==========================================
        public async Task<IActionResult> Checkout()
        {
            var userId = await GetCurrentUserId();

            if (userId == null)
                return RedirectToAction("Login", "Account");

            ViewBag.RoomTypes = await _db.RoomTypes
                .AsNoTracking()
                .OrderBy(rt => rt.RoomTypeId)
                .ToListAsync();

            ViewBag.ActivePromotions = await GetActivePromotionsAsync();

            ViewBag.CurrentUser = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == userId.Value);

            return View();
        }

        // ==========================================
        // 5.1 BOOKING: คำนวณราคา (Quote) และบันทึกการจอง (SubmitBooking)
        // ==========================================
        private const int MaxStayNights = 30;
        private const long MaxSlipBytes = 5 * 1024 * 1024;
        private const decimal OnTopPercent = 5m;
        private const decimal MaxTotalDiscountPercent = 20m;

        private sealed record AppliedPromo(Promotion Promo, string Role, decimal Percent, decimal Amount);

        private static readonly IReadOnlyList<string> HoldingStatuses =
            new[] { "pending_approval", "confirmed", "checked_in" };

        private static readonly Regex PhoneRegex = new(@"^[0-9+\-\s]{8,20}$");

        // ==========================================
        // ผลการคำนวณราคา
        // ==========================================
        private sealed class BookingQuote
        {
            public RoomType Room { get; init; } = null!;
            public int Nights { get; init; }
            public decimal RoomAmount { get; init; }
            public decimal AvgPricePerNight { get; init; }
            public List<AppliedPromo> Applied { get; init; } = new();
            public bool DiscountCapped { get; init; }
            public decimal Discount { get; init; }
            public decimal DiscountPercent { get; init; }
            public decimal Total { get; init; }
            public int Available { get; init; }
        }

        /// <summary>จำนวนห้องที่ว่างจริงของประเภทห้องนั้นในช่วงวันที่เลือก (นับตามประเภทห้อง)</summary>
        private async Task<int> CountAvailableRoomsAsync(byte roomTypeId, DateOnly checkIn, DateOnly checkOut)
        {
            var totalRooms = await _db.Rooms.CountAsync(r =>
                r.RoomTypeId == roomTypeId && r.IsActive == true && r.Status != "maintenance");

            var bookedRooms = await _db.BookingRooms.CountAsync(br =>
                br.RoomTypeId == roomTypeId
                && HoldingStatuses.Contains(br.Booking.Status)
                && br.Booking.CheckInDate < checkOut
                && br.Booking.CheckOutDate > checkIn);

            return Math.Max(0, totalRooms - bookedRooms);
        }

        /// <summary>โปรหลัก = ส่วนลดสูงสุด, โปรอื่นที่เข้าเงื่อนไข On-top รายการละ 5%, รวมไม่เกิน 20% ของค่าห้อง</summary>
        private static (List<AppliedPromo> Applied, decimal Discount, bool Capped) ApplyPromotions(
            List<Promotion> promos, RoomType room, int guests, List<decimal> nightly, bool stayHasHoliday)
        {
            var nights = nightly.Count;
            var roomAmount = nightly.Sum();
            var eligible = new List<(Promotion P, decimal Standalone)>();

            foreach (var p in promos)
            {
                if (p.ApplicableRoomTypeId.HasValue && p.ApplicableRoomTypeId.Value != room.RoomTypeId) continue;
                if (guests < (p.MinGuests ?? 1) || nights < (p.MinNights ?? 1)) continue;
                if (p.RequiresHoliday && !stayHasHoliday) continue;

                var pct = p.DiscountPercent ?? 0m;
                decimal discount = 0m;

                switch (p.DiscountType)
                {
                    case "percent_room":
                        discount = roomAmount * pct / 100m;
                        break;
                    case "percent_extra_nights":
                        var extra = Math.Max(0, nights - (p.MinNights ?? 1));
                        if (p.MaxDiscountNights is > 0) extra = Math.Min(extra, p.MaxDiscountNights.Value);
                        discount = nightly.Skip(nights - extra).Sum() * pct / 100m;
                        break;
                    case "free_extra_bed":
                        break; // ยังไม่มีมูลค่าเป็นเงินในใบจอง
                }

                eligible.Add((p, Math.Round(Math.Min(discount, roomAmount), 2)));
            }

            if (eligible.Count == 0) return (new List<AppliedPromo>(), 0m, false);

            var ordered = eligible.OrderByDescending(e => e.Standalone).ThenBy(e => e.P.PromotionId).ToList();
            var remaining = Math.Round(roomAmount * MaxTotalDiscountPercent / 100m, 2);
            var applied = new List<AppliedPromo>();
            var capped = false;

            for (var i = 0; i < ordered.Count; i++)
            {
                var wanted = i == 0 ? ordered[i].Standalone : Math.Round(roomAmount * OnTopPercent / 100m, 2);
                var amount = Math.Min(wanted, remaining);
                if (amount < wanted) capped = true;
                remaining -= amount;

                var percent = roomAmount > 0m ? Math.Round(amount / roomAmount * 100m, 2) : 0m;
                applied.Add(new AppliedPromo(ordered[i].P, i == 0 ? "base" : "on_top", percent, amount));
            }

            return (applied, applied.Sum(a => a.Amount), capped);
        }

        /// <summary>คำนวณราคาและตรวจห้องว่าง (ใช้ร่วมกันทั้งหน้า Checkout และตอนบันทึกจริง)</summary>
        private async Task<(BookingQuote? Quote, string? Error)> BuildQuoteAsync(
            byte roomTypeId, DateOnly checkIn, DateOnly checkOut, int guests, string? promoCode)
        {
            if (checkIn < Today)
                return (null, "วันเช็คอินต้องเป็นวันนี้หรือหลังจากนี้");
            if (checkOut <= checkIn)
                return (null, "วันเช็คเอาท์ต้องอยู่หลังวันเช็คอินอย่างน้อย 1 วัน");

            var nights = checkOut.DayNumber - checkIn.DayNumber;
            if (nights > MaxStayNights)
                return (null, $"เข้าพักได้ไม่เกิน {MaxStayNights} คืนต่อการจอง");

            var room = await _db.RoomTypes.AsNoTracking().FirstOrDefaultAsync(rt => rt.RoomTypeId == roomTypeId);
            if (room == null)
                return (null, "ไม่พบประเภทห้องพักที่เลือก");

            if (guests < room.MinGuests || guests > room.MaxGuests)
                return (null, $"ห้อง {room.TypeName} รองรับผู้เข้าพัก {room.MinGuests}–{room.MaxGuests} ท่าน");

            var lastNight = checkOut.AddDays(-1);
            var periods = await _db.RoomRatePeriods
                .AsNoTracking()
                .Where(p => p.RoomTypeId == roomTypeId && p.StartDate <= lastNight && p.EndDate >= checkIn)
                .ToListAsync();

            var nightly = new List<decimal>();
            for (var d = checkIn; d < checkOut; d = d.AddDays(1))
            {
                var period = periods
                    .Where(p => p.StartDate <= d && p.EndDate >= d)
                    .OrderByDescending(p => p.RateId)
                    .FirstOrDefault();
                nightly.Add(period?.PricePerNight ?? room.BasePrice);
            }

            var roomAmount = nightly.Sum();

            var promos = await GetActivePromotionsAsync();
            var stayHasHoliday = promos.Any(p => p.RequiresHoliday)
                && await _db.Holidays.AnyAsync(h => h.StartDate <= lastNight && h.EndDate >= checkIn);

            var (applied, discount, capped) = ApplyPromotions(promos, room, guests, nightly, stayHasHoliday);
            var effectivePercent = roomAmount > 0m ? Math.Round(discount / roomAmount * 100m, 2) : 0m;

            var quote = new BookingQuote
            {
                Room = room,
                Nights = nights,
                RoomAmount = roomAmount,
                AvgPricePerNight = Math.Round(roomAmount / nights, 2),
                Applied = applied,
                DiscountCapped = capped,
                Discount = discount,
                DiscountPercent = effectivePercent,
                Total = roomAmount - discount,
                Available = await CountAvailableRoomsAsync(roomTypeId, checkIn, checkOut)
            };

            return (quote, null);
        }

        private static bool TryParseDate(string? text, out DateOnly date) =>
            DateOnly.TryParseExact(text, "yyyy-MM-dd", out date);

        private static string NewBookingCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var suffix = new string(Enumerable.Range(0, 5)
                .Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
            return $"BK{DateTime.Now:yyMMdd}{suffix}";
        }

        /// <summary>ตรวจชนิดไฟล์จากไบต์แรกของไฟล์จริง</summary>
        private static async Task<string?> DetectImageExtensionAsync(IFormFile file)
        {
            var header = new byte[8];
            await using var stream = file.OpenReadStream();
            var read = await stream.ReadAsync(header.AsMemory(0, header.Length));
            if (read < 8) return null;

           // ตรวจสอบ JPEG
    if (header[0] == 0xFF &&
        header[1] == 0xD8 &&
        header[2] == 0xFF)
    {
        return ".jpg";
    }

    // ตรวจสอบ PNG
    if (header[0] == 0x89 &&
        header[1] == 0x50 &&
        header[2] == 0x4E &&
        header[3] == 0x47)
    {
        return ".png";
    }

            return null;
        }

        // ==========================================
        // QUOTE: คำนวณราคาให้หน้า Checkout
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Quote(byte roomTypeId, string? checkIn, string? checkOut, int guests, string? promoCode)
        {
            if (await GetCurrentUserId() == null)
                return Fail("กรุณาเข้าสู่ระบบใหม่อีกครั้ง");

            if (!TryParseDate(checkIn, out var inDate) || !TryParseDate(checkOut, out var outDate))
                return Fail("รูปแบบวันที่ไม่ถูกต้อง");

            var (quote, error) = await BuildQuoteAsync(roomTypeId, inDate, outDate, guests, promoCode);
            if (quote == null)
                return Fail(error ?? "ไม่สามารถคำนวณราคาได้");

            return Json(new
            {
                success = true,
                nights = quote.Nights,
                roomAmount = quote.RoomAmount,
                discount = quote.Discount,
                discountPercent = quote.DiscountPercent,
                discountCapped = quote.DiscountCapped,
                total = quote.Total,
                available = quote.Available,
                promoName = string.Join(" + ", quote.Applied.Select(a => a.Promo.PromoName)),
                promos = quote.Applied.Select(a => new
                {
                    name = a.Promo.PromoName,
                    role = a.Role,
                    percent = a.Percent,
                    amount = a.Amount
                })
            });
        }

        // ==========================================
        // SUBMIT BOOKING
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(6 * 1024 * 1024)]
        public async Task<IActionResult> SubmitBooking(
            byte roomTypeId, string? checkIn, string? checkOut, int guests,
            string? fullName, string? email, string? phone, string? specialRequest,
            string? promoCode, decimal netTotal, IFormFile? slipFile)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Fail("กรุณาเข้าสู่ระบบใหม่อีกครั้ง");

            // ---------- 1) ตรวจข้อมูลที่ส่งมา ----------
            if (!TryParseDate(checkIn, out var inDate) || !TryParseDate(checkOut, out var outDate))
                return Fail("รูปแบบวันที่ไม่ถูกต้อง");

            var name = fullName?.Trim() ?? "";
            var mail = email?.Trim() ?? "";
            var tel = phone?.Trim() ?? "";
            var request = string.IsNullOrWhiteSpace(specialRequest) ? null : specialRequest.Trim();

            if (name.Length == 0 || mail.Length == 0 || tel.Length == 0)
                return Fail("กรุณากรอกชื่อ อีเมล และเบอร์โทรศัพท์ให้ครบถ้วน");
            if (name.Length > 150)
                return Fail("ชื่อ-นามสกุลต้องไม่เกิน 150 ตัวอักษร");
            if (mail.Length > 120 || !new EmailAddressAttribute().IsValid(mail))
                return Fail("รูปแบบอีเมลไม่ถูกต้อง");
            if (!PhoneRegex.IsMatch(tel))
                return Fail("รูปแบบเบอร์โทรศัพท์ไม่ถูกต้อง");
            if (request != null && request.Length > 500)
                return Fail("คำขอเพิ่มเติมต้องไม่เกิน 500 ตัวอักษร");

            if (slipFile == null || slipFile.Length == 0)
                return Fail("กรุณาแนบสลิปการโอนเงิน");
            if (slipFile.Length > MaxSlipBytes)
                return Fail("ไฟล์สลิปต้องมีขนาดไม่เกิน 5MB");

            var slipExt = await DetectImageExtensionAsync(slipFile);
            if (slipExt == null)
                return Fail("รองรับเฉพาะไฟล์รูปภาพ JPEG หรือ PNG");

            // ---------- 2) ล็อกประเภทห้อง แล้วคำนวณราคา/ตรวจห้องว่างใหม่ในธุรกรรมเดียว ----------
            string? savedSlipFullPath = null;

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT room_type_id FROM room_types WHERE room_type_id = {roomTypeId} FOR UPDATE");

                var (quote, error) = await BuildQuoteAsync(roomTypeId, inDate, outDate, guests, promoCode);
                if (quote == null)
                    return Fail(error ?? "ไม่สามารถทำรายการจองได้");

                if (quote.Available <= 0)
                    return Fail($"ขออภัย ห้อง {quote.Room.TypeName} เต็มในช่วงวันที่เลือก กรุณาเลือกวันที่หรือประเภทห้องอื่น");

                if (Math.Abs(netTotal - quote.Total) > 0.01m)
                    return Json(new
                    {
                        success = false,
                        refreshQuote = true,
                        message = $"ราคามีการเปลี่ยนแปลง ยอดชำระล่าสุดคือ ฿{quote.Total:N0} กรุณาตรวจสอบยอดก่อนยืนยันอีกครั้ง"
                    });

                // ---------- 3) สร้างรหัสการจอง ----------
                string bookingCode;
                var attempts = 0;
                do
                {
                    bookingCode = NewBookingCode();
                    attempts++;
                } while (await _db.Bookings.AnyAsync(b => b.BookingCode == bookingCode) && attempts < 5);

                // ---------- 4) บันทึกไฟล์สลิป ----------
                var env = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
                var slipDir = Path.Combine(env.WebRootPath, "uploads", "slips");
                Directory.CreateDirectory(slipDir);

                var slipFileName = $"{bookingCode}_{Guid.NewGuid():N}{slipExt}";
                savedSlipFullPath = Path.Combine(slipDir, slipFileName);
                await using (var fs = System.IO.File.Create(savedSlipFullPath))
                {
                    await slipFile.CopyToAsync(fs);
                }

                // ---------- 5) สร้างรายการจอง ----------
                var now = DateTime.Now;

                var booking = new Booking
                {
                    BookingCode = bookingCode,
                    QrCode = bookingCode, // ให้ Reception สแกนแล้วได้ Booking ID / Code ทันที
                    UserId = userId.Value,
                    ContactName = name,
                    ContactEmail = mail,
                    ContactPhone = tel,
                    CheckInDate = inDate,
                    CheckOutDate = outDate,
                    Nights = (ushort)quote.Nights,
                    NumGuests = (byte)guests,
                    RoomAmount = quote.RoomAmount,
                    DiscountPercent = quote.DiscountPercent,
                    DiscountAmount = quote.Discount,
                    TaxAmount = 0m,
                    TotalAmount = quote.Total,
                    Status = "pending_approval",
                    SpecialRequest = request,
                    CreatedAt = now
                };

                booking.BookingRooms.Add(new BookingRoom
                {
                    RoomTypeId = quote.Room.RoomTypeId,
                    PricePerNight = quote.AvgPricePerNight,
                    Nights = (ushort)quote.Nights,
                    ExtraBedQty = 0,
                    ExtraBedAmount = 0m,
                    LineTotal = quote.RoomAmount
                });

                foreach (var a in quote.Applied)
                {
                    booking.BookingPromotions.Add(new BookingPromotion
                    {
                        PromotionId = a.Promo.PromotionId,
                        StackRole = a.Role,
                        AppliedPercent = a.Percent,
                        AppliedAmount = a.Amount,
                        AppliedAt = now
                    });
                }

                booking.Payments.Add(new Payment
                {
                    Amount = quote.Total,
                    Method = "bank_transfer",
                    SlipImagePath = $"/uploads/slips/{slipFileName}",
                    Status = "pending",
                    CreatedAt = now
                });

                _db.Bookings.Add(booking);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return Json(new
                {
                    success = true,
                    bookingId = booking.BookingId,
                    bookingCode = booking.BookingCode,
                    netTotal = booking.TotalAmount
                });
            }
            catch (Exception)
            {
                if (savedSlipFullPath != null && System.IO.File.Exists(savedSlipFullPath))
                    System.IO.File.Delete(savedSlipFullPath);

                return Fail("ไม่สามารถบันทึกการจองได้ กรุณาลองใหม่อีกครั้ง");
            }
        }

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
                .AsSplitQuery()
                .Include(u => u.Bookings)
                    .ThenInclude(b => b.BookingRooms)
                        .ThenInclude(br => br.RoomType)
                .Include(u => u.Bookings)
                    .ThenInclude(b => b.BookingRooms)
                        .ThenInclude(br => br.Room)
                .Include(u => u.Bookings)
                    .ThenInclude(b => b.BookingPromotions)
                        .ThenInclude(bp => bp.Promotion)
                .Include(u => u.Bookings)
                    .ThenInclude(b => b.Payments)
                .FirstOrDefaultAsync(u => u.UserId == userId.Value);

            if (user == null)
                return RedirectToAction("Login", "Account");

            return View(user);
        }

        public async Task<IActionResult> BookingDetails(uint id)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return RedirectToAction("Login", "Account");

            var user = await _db.Users
                .AsNoTracking()
                .AsSplitQuery()
                .Include(u => u.Bookings.Where(b => b.BookingId == id))
                    .ThenInclude(b => b.BookingRooms)
                        .ThenInclude(br => br.RoomType)
                .Include(u => u.Bookings.Where(b => b.BookingId == id))
                    .ThenInclude(b => b.BookingRooms)
                        .ThenInclude(br => br.Room)
                .Include(u => u.Bookings.Where(b => b.BookingId == id))
                    .ThenInclude(b => b.BookingPromotions)
                        .ThenInclude(bp => bp.Promotion)
                .Include(u => u.Bookings.Where(b => b.BookingId == id))
                    .ThenInclude(b => b.Payments)
                .FirstOrDefaultAsync(u => u.UserId == userId.Value);

            if (user == null || !user.Bookings.Any())
                return RedirectToAction(nameof(MyBookings));

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
                .Include(u => u.Bookings)
                    .ThenInclude(b => b.BookingRooms)
                        .ThenInclude(br => br.RoomType)
                .FirstOrDefaultAsync(u => u.UserId == userId.Value);

            if (user == null)
                return RedirectToAction("Login", "Account");

            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(string firstName, string lastName, string? phone, string email)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Fail("กรุณาเข้าสู่ระบบใหม่อีกครั้ง");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value);
            if (user == null)
                return Fail("ไม่พบข้อมูลผู้ใช้งาน");

            var fullName = $"{firstName?.Trim()} {lastName?.Trim()}".Trim();
            var newEmail = email?.Trim() ?? "";
            var newPhone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(newEmail))
                return Fail("กรุณากรอกชื่อและอีเมลให้ครบถ้วน");

            if (fullName.Length > 150)
                return Fail("ชื่อ-นามสกุลต้องไม่เกิน 150 ตัวอักษร");
            if (newEmail.Length > 120 || !new EmailAddressAttribute().IsValid(newEmail))
                return Fail("รูปแบบอีเมลไม่ถูกต้อง");
            if (newPhone != null && newPhone.Length > 20)
                return Fail("เบอร์โทรศัพท์ต้องไม่เกิน 20 ตัวอักษร");

            var emailTaken = await _db.Users.AnyAsync(u =>
                u.Email == newEmail && u.UserId != user.UserId);
            if (emailTaken)
                return Fail("อีเมลนี้ถูกใช้งานแล้ว");

            user.FullName = fullName;
            user.Phone = newPhone;
            user.Email = newEmail;
            user.UpdatedAt = DateTime.Now;

            await _db.SaveChangesAsync();

            return Json(new
            {
                success = true,
                fullName = user.FullName,
                email = user.Email,
                phone = user.Phone ?? "-"
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(string oldPassword, string newPassword)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Fail("กรุณาเข้าสู่ระบบใหม่อีกครั้ง");

            if (string.IsNullOrWhiteSpace(oldPassword))
                return Fail("กรุณากรอกรหัสผ่านเดิม");

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
                return Fail("รหัสผ่านใหม่ต้องมีอย่างน้อย 6 ตัวอักษร");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value);
            if (user == null)
                return Fail("ไม่พบข้อมูลผู้ใช้งาน");

            if (user.PasswordHash != oldPassword)
                return Fail("รหัสผ่านเดิมไม่ถูกต้อง");

            if (oldPassword == newPassword)
                return Fail("รหัสผ่านใหม่ต้องไม่ซ้ำกับรหัสผ่านเดิม");

            user.PasswordHash = newPassword;
            user.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync();

            return Json(new { success = true });
        }

        // ==========================================
        // 8. REVIEW & FEEDBACK
        // ==========================================
        public async Task<IActionResult> Reviews()
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return RedirectToAction("Login", "Account");

            var allReviews = await _db.Reviews
                .AsNoTracking()
                .Include(r => r.User)
                .Include(r => r.Booking)
                    .ThenInclude(b => b.BookingRooms)
                        .ThenInclude(br => br.RoomType)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            ViewBag.UserBookings = await _db.Bookings
                .AsNoTracking()
                .Include(b => b.Review)
                .Include(b => b.BookingRooms)
                    .ThenInclude(br => br.RoomType)
                .Where(b => b.UserId == userId.Value)
                .OrderByDescending(b => b.CheckOutDate)
                .ToListAsync();

            return View(allReviews);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitReview(uint bookingId, byte rating, string? title, string? comment)
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return Fail("กรุณาเข้าสู่ระบบใหม่อีกครั้ง");

            if (rating < 1 || rating > 5)
                return Fail("กรุณาให้คะแนนระหว่าง 1 ถึง 5 ดาว");

            var cleanTitle = title?.Trim();
            var cleanComment = comment?.Trim();

            if (string.IsNullOrWhiteSpace(cleanComment))
                return Fail("กรุณาเขียนความคิดเห็นก่อนส่งรีวิว");

            if (cleanComment.Length > 2000 || (cleanTitle?.Length ?? 0) > 100)
                return Fail("ข้อความรีวิยาวเกินกำหนด");

            var booking = await _db.Bookings
                .Include(b => b.Review)
                .Include(b => b.BookingRooms)
                    .ThenInclude(br => br.RoomType)
                .FirstOrDefaultAsync(b => b.BookingId == bookingId && b.UserId == userId.Value);

            if (booking == null)
                return Fail("ไม่พบรายการจองของคุณ");

            var status = (booking.Status ?? "").ToLowerInvariant();

            if (status != "checked_out" && status != "completed")
                return Fail("สามารถเขียนรีวิวได้เฉพาะรายการจองที่เข้าพักเสร็จสิ้น (Completed) แล้วเท่านั้น");

            if (booking.Review != null)
                return Fail("รายการจองนี้ได้รับการรีวิวไปแล้ว ไม่สามารถส่งรีวิวซ้ำได้");

            var combinedComment = !string.IsNullOrWhiteSpace(cleanTitle)
                ? $"[{cleanTitle}] {cleanComment}"
                : cleanComment;

            var review = new Review
            {
                BookingId = booking.BookingId,
                UserId = userId.Value,
                Rating = rating,
                Comment = combinedComment,
                CreatedAt = DateTime.Now
            };

            _db.Reviews.Add(review);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return Fail("รายการจองนี้ได้รับการรีวิวไปแล้ว ไม่สามารถส่งรีวิวซ้ำได้");
            }

            var fullName = await _db.Users
                .AsNoTracking()
                .Where(u => u.UserId == userId.Value)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync();

            var roomTypeName = booking.BookingRooms
                .Select(br => br.RoomType?.TypeName)
                .FirstOrDefault() ?? "Standard Room";

            return Json(new
            {
                success = true,
                review = new
                {
                    id = review.ReviewId,
                    name = fullName ?? booking.ContactName,
                    roomType = roomTypeName,
                    stayDate = booking.CheckInDate.ToString("dd MMM yyyy"),
                    reviewDate = review.CreatedAt.ToString("yyyy-MM-dd"),
                    reviewDateText = review.CreatedAt.ToString("dd MMM yyyy"),
                    rating = (int)review.Rating,
                    title = !string.IsNullOrWhiteSpace(cleanTitle) ? cleanTitle : "ความประทับใจจากการเข้าพัก",
                    comment = cleanComment
                }
            });
        }
    }
}
