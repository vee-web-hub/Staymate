using System.ComponentModel.DataAnnotations;
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

            // ---------- 5) รีวิว (นับ/เฉลี่ยใน DB ไม่ต้องโหลดทั้งหมด) ----------
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

            // ประเภทห้องพร้อมห้องย่อย เพื่อนับจำนวนห้องว่างจริง
            var roomTypes = await _db.RoomTypes
                .AsNoTracking()
                .Include(rt => rt.Rooms)
                .OrderBy(rt => rt.BasePrice)
                .ToListAsync();

            ViewBag.ActivePromotions = await GetActivePromotionsAsync();

            return View(roomTypes);
        }

        // ==========================================
        // 3. ROOMS / ROOM DETAILS
        // ==========================================
        public async Task<IActionResult> Rooms()
        {
            if (await GetCurrentUserId() == null)
                return RedirectToAction("Login", "Account");

            // ไปยังประเภทห้องแรกที่มีอยู่จริง (ไม่ hardcode id = 1)
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

            // ส่ง id ที่เลือกให้ View ใช้เปิดแท็บ/ห้องที่ต้องการ
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

            // โปรโมชั่นที่เปิดใช้งานและยังไม่หมดอายุ (รวมที่ยังไม่เริ่ม เพื่อโชว์ล่วงหน้า)
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
        // 6. MY BOOKINGS / BOOKING DETAILS
        // ==========================================
        public async Task<IActionResult> MyBookings()
        {
            var userId = await GetCurrentUserId();
            if (userId == null)
                return RedirectToAction("Login", "Account");

            var user = await _db.Users
                .AsNoTracking()
                .AsSplitQuery() // หลีกเลี่ยง cartesian explosion จาก Include หลายชั้น
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

            // ป้องกัน IDOR: ดึงเฉพาะการจองของผู้ใช้ปัจจุบันเท่านั้น
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

            // ขนาดตามคอลัมน์ในฐานข้อมูล
            if (fullName.Length > 150)
                return Fail("ชื่อ-นามสกุลต้องไม่เกิน 150 ตัวอักษร");
            if (newEmail.Length > 120 || !new EmailAddressAttribute().IsValid(newEmail))
                return Fail("รูปแบบอีเมลไม่ถูกต้อง");
            if (newPhone != null && newPhone.Length > 20)
                return Fail("เบอร์โทรศัพท์ต้องไม่เกิน 20 ตัวอักษร");

            // อีเมลต้องไม่ซ้ำกับผู้ใช้อื่น (unique index uq_users_email)
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

            // TODO: ปัจจุบันระบบเก็บรหัสผ่านแบบ plain text ให้ตรงกับหน้า Login เดิม
            //       ควรเปลี่ยนเป็น hash (PasswordHasher<User> / BCrypt) ทั้ง Register, Login และที่นี่
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

            // 1) รีวิวทั้งหมดในระบบ
            var allReviews = await _db.Reviews
                .AsNoTracking()
                .Include(r => r.User)
                .Include(r => r.Booking)
                    .ThenInclude(b => b.BookingRooms)
                        .ThenInclude(br => br.RoomType)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            // 2) การจองของลูกค้าปัจจุบัน สำหรับเลือกเขียนรีวิว
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
                return Fail("ข้อความรีวิวยาวเกินกำหนด");

            // กรองด้วย UserId เสมอ เพื่อป้องกัน IDOR
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
                // กรณีกดส่งซ้ำพร้อมกัน ชน unique index uq_review_booking
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