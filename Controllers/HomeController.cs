using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayMate_Kavinvee_0576.Models;
using StayMate_Kavinvee_0576.Models.ViewModels;
using Microsoft.AspNetCore.Mvc.Filters;

namespace StayMate_Kavinvee_0576.Controllers;

public class HomeController : Controller
{
    private readonly StayMateDbContext _db;

    public HomeController(StayMateDbContext db)
    {
        _db = db;
    }

    private static readonly string[] GradClasses =
        { "sm-grad-blue", "sm-grad-violet", "sm-grad-cyan", "sm-grad-indigo", "sm-grad-gold" };

    private static readonly string[] PromoIcons =
        { "fa-gift", "fa-people-group", "fa-moon", "fa-laptop-code", "fa-champagne-glasses" };

    // =====================================================================
    // Helpers ใช้ร่วมกันทุกหน้า
    // =====================================================================

    /// <summary>โปรโมชั่นที่เปิดใช้งานและอยู่ในช่วงวันที่วันนี้</summary>
    private async Task<List<Promotion>> GetActivePromotionRowsAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        return await _db.Promotions
            .AsNoTracking()
            .Where(p => p.IsActive == true
                     && (p.ValidFrom == null || p.ValidFrom <= today)
                     && (p.ValidTo == null || p.ValidTo >= today))
            .OrderBy(p => p.PromotionId)
            .ToListAsync();
    }

    /// <summary>
    /// แปลง Promotion จากฐานข้อมูลเป็นข้อมูลการ์ดสำหรับแสดงผล
    /// </summary>
    private static List<PromoCardItem> MapPromoCards(
        List<Promotion> rows,
        Dictionary<byte, string> roomTypeNames)
    {
        return rows.Select((p, i) =>
        {
            // หมวดหมู่สำหรับใช้กรองโปรโมชั่น
            var cats = new List<string>();

            if (p.DiscountType == "free_extra_bed" ||
                (p.DiscountPercent ?? 0) >= 15)
            {
                cats.Add("popular");
            }

            if ((p.MinNights ?? 0) >= 3 ||
                p.DiscountType == "percent_extra_nights")
            {
                cats.Add("stay-long");
            }

            if ((p.MinGuests ?? 0) >= 3 ||
                p.DiscountType == "free_extra_bed")
            {
                cats.Add("group-pack");
            }

            if (cats.Count == 0)
            {
                cats.Add("popular");
            }

            // Badge ที่แสดงบนการ์ด
            var badge = p.DiscountType switch
            {
                "free_extra_bed" => "GROUP VALUE",
                "percent_extra_nights" => "STAY LONGER",
                _ => (p.DiscountPercent ?? 0) >= 15
                    ? "HOT DEAL"
                    : "SPECIAL OFFER"
            };

            // รายละเอียดสิทธิประโยชน์จากเงื่อนไขโปรโมชั่น
            var perks = new List<PromoPerk>();

            switch (p.DiscountType)
            {
                case "free_extra_bed":
                    perks.Add(new PromoPerk
                    {
                        Icon = "fa-bed",
                        Text = "ฟรีค่าเตียงเสริม"
                    });
                    break;

                case "percent_room":
                    perks.Add(new PromoPerk
                    {
                        Icon = "fa-percent",
                        Text = $"ลดค่าห้อง {(p.DiscountPercentMax ?? p.DiscountPercent):0.##}%"
                    });
                    break;

                case "percent_extra_nights":
                    perks.Add(new PromoPerk
                    {
                        Icon = "fa-percent",
                        Text = $"ลดคืนที่เพิ่ม {p.DiscountPercent:0.##}%"
                    });
                    break;
            }

            if (p.MinNights is > 0)
            {
                perks.Add(new PromoPerk
                {
                    Icon = "fa-moon",
                    Text = $"พักขั้นต่ำ {p.MinNights} คืน"
                });
            }

            if (p.MinGuests is > 0)
            {
                perks.Add(new PromoPerk
                {
                    Icon = "fa-user-group",
                    Text = $"ตั้งแต่ {p.MinGuests} ท่านขึ้นไป"
                });
            }

            if (p.MaxDiscountNights is > 0)
            {
                perks.Add(new PromoPerk
                {
                    Icon = "fa-calendar-check",
                    Text = $"ลดสูงสุด {p.MaxDiscountNights} คืน"
                });
            }

            if (p.RequiresHoliday)
            {
                perks.Add(new PromoPerk
                {
                    Icon = "fa-calendar-star",
                    Text = "ใช้ได้ช่วงวันหยุด/เทศกาล"
                });
            }

            // ชื่อประเภทห้องที่ร่วมรายการ
            string? typeName = null;

            if (p.ApplicableRoomTypeId.HasValue &&
                roomTypeNames.TryGetValue(p.ApplicableRoomTypeId.Value, out var foundTypeName))
            {
                typeName = foundTypeName;
            }

            return new PromoCardItem
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
                ValidFrom = p.ValidFrom,
                ValidTo = p.ValidTo,

                ApplicableRoomTypeId = p.ApplicableRoomTypeId,
                ApplicableRoomTypeName = typeName,
                RequiresHoliday = p.RequiresHoliday,

                GradClass = GradClasses[i % GradClasses.Length],
                IconClass = PromoIcons[i % PromoIcons.Length],
                Category = string.Join(' ', cats),
                BadgeText = badge,
                Perks = perks,

                Meta1 = p.MinNights is > 0
                    ? $"ขั้นต่ำ {p.MinNights} คืน"
                    : p.MinGuests is > 0
                        ? $"ตั้งแต่ {p.MinGuests} ท่านขึ้นไป"
                        : "ไม่มีขั้นต่ำ",

                Meta2 = p.DiscountType switch
                {
                    "free_extra_bed" => "ฟรีค่าเตียงเสริม",
                    "percent_room" =>
                        $"ลดสูงสุด {(p.DiscountPercentMax ?? p.DiscountPercent):0.##}%",
                    "percent_extra_nights" =>
                        $"ลดคืนที่เพิ่ม {p.DiscountPercent:0.##}%",
                    _ => ""
                }
            };
        }).ToList();
    }

    /// <summary>
    /// ประเภทห้องทั้งหมด พร้อมจำนวนห้องว่าง ข้อมูลเสริม และโปรโมชั่นที่ใช้ได้
    /// </summary>
    private async Task<List<RoomCardItem>> BuildRoomCardsAsync(
        List<Promotion> promoRows)
    {
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
                rt.AllowExtraBed,
                rt.ExtraBedPrice,
                Available = rt.Rooms.Count(r =>
                    r.Status == "available" &&
                    r.IsActive == true)
            })
            .ToListAsync();

        var order = 0;

        return roomTypes.Select(rt =>
        {
            order++;

            var ex = RoomExtrasCatalog.Find(rt.TypeCode, rt.TypeName);
            var fallback = RoomExtrasCatalog.Fallback(rt.TypeName);
            var gallery = ex?.Gallery ?? new[] { fallback, fallback, fallback };

            var cats = new List<string>();

            if (rt.MinGuests <= 2)
            {
                cats.Add("couple");
            }

            if (rt.AllowExtraBed)
            {
                cats.Add("extrabed");
            }

            if (rt.MaxGuests >= 4)
            {
                cats.Add("group");
            }

            return new RoomCardItem
            {
                RoomTypeId = rt.RoomTypeId,
                TypeCode = rt.TypeCode,
                TypeName = rt.TypeName,
                Description = rt.Description,
                BedType = rt.BedType,
                MinGuests = rt.MinGuests,
                MaxGuests = rt.MaxGuests,
                Price = rt.BasePrice,
                AllowExtraBed = rt.AllowExtraBed,
                ExtraBedPrice = rt.ExtraBedPrice,
                AvailableRooms = rt.Available,

                ImageUrl = gallery[0],
                Gallery = gallery,
                SizeText = ex?.SizeText ?? "",
                SizeSqm = ex?.SizeSqm ?? 0,
                BadgeText = ex?.Badge ?? "",
                BadgeClass = ex?.BadgeClass ?? "",
                Code = ex?.Code ?? rt.TypeCode,
                ViewText = ex?.View ?? "-",
                Tagline = ex?.Tagline ?? rt.Description ?? "",
                IsFeatured = ex?.Featured ?? false,
                Highlights = ex?.Highlights ?? new[] { rt.BedType ?? "" },

                Category = string.Join(' ', cats),
                Order = order,

                PromoNames = promoRows
                    .Where(p =>
                        p.ApplicableRoomTypeId == null ||
                        p.ApplicableRoomTypeId == rt.RoomTypeId)
                    .Select(p => p.PromoName)
                    .Take(3)
                    .ToList()
            };
        }).ToList();
    }

    // =====================================================================
    // Index
    // =====================================================================
    public async Task<IActionResult> Index()
    {
        var promoRows = await GetActivePromotionRowsAsync();
        var rooms = await BuildRoomCardsAsync(promoRows);

        var roomTypeNames = await _db.RoomTypes
            .AsNoTracking()
            .ToDictionaryAsync(rt => rt.RoomTypeId, rt => rt.TypeName);

        var promos = MapPromoCards(promoRows, roomTypeNames);

        var ratings = await _db.Reviews
            .AsNoTracking()
            .Select(r => (int)r.Rating)
            .ToListAsync();

        return View(new HomeIndexViewModel
        {
            Rooms = rooms,
            Promotions = promos,
            ReviewCount = ratings.Count,
            AverageRating = ratings.Count > 0
                ? ratings.Average()
                : null
        });
    }

    // =====================================================================
    // Rooms: รายการประเภทห้องทั้งหมด
    // =====================================================================
    public async Task<IActionResult> Rooms()
    {
        var promoRows = await GetActivePromotionRowsAsync();
        var rooms = await BuildRoomCardsAsync(promoRows);

        return View(rooms);
    }

    // =====================================================================
    // RoomDetails: /Home/RoomDetails/3 หรือ ?type=deluxe
    // =====================================================================
    public async Task<IActionResult> RoomDetails(int? id, string? type = null)
    {
        var promoRows = await GetActivePromotionRowsAsync();
        var rooms = await BuildRoomCardsAsync(promoRows);

        if (rooms.Count == 0)
        {
            return RedirectToAction(nameof(Rooms));
        }

        RoomCardItem? room = null;

        if (id.HasValue)
        {
            room = rooms.FirstOrDefault(r => r.RoomTypeId == id.Value);

            if (room == null)
            {
                return NotFound();
            }
        }
        else if (!string.IsNullOrWhiteSpace(type))
        {
            room = rooms.FirstOrDefault(r =>
                r.TypeCode.Contains(type, StringComparison.OrdinalIgnoreCase) ||
                r.TypeName.Contains(type, StringComparison.OrdinalIgnoreCase));
        }

        if (room == null)
        {
            return RedirectToAction(nameof(Rooms));
        }

        var rid = room.RoomTypeId;

        // รีวิวของการจองที่มีห้องประเภทนี้
        var reviewQuery = _db.Reviews
            .AsNoTracking()
            .Where(r => r.Booking.BookingRooms.Any(br => br.RoomTypeId == rid));

        var ratings = await reviewQuery
            .Select(r => (int)r.Rating)
            .ToListAsync();

        var reviews = await reviewQuery
            .OrderByDescending(r => r.CreatedAt)
            .Take(6)
            .Select(r => new ReviewItem
            {
                UserName = r.User != null
                    ? r.User.FullName
                    : "ผู้เข้าพัก",
                Rating = r.Rating,
                Comment = r.Comment,
                CreatedAt = r.CreatedAt
            })
            .ToListAsync();

        // โปรโมชั่นที่ใช้กับห้องนี้ได้ ส่งให้ JavaScript คำนวณราคาจำลอง
        var simPromos = promoRows
            .Where(p =>
                p.ApplicableRoomTypeId == null ||
                p.ApplicableRoomTypeId == rid)
            .Select(p => new SimPromoItem
            {
                Name = p.PromoName,
                Type = p.DiscountType,
                Percent = p.DiscountPercent ?? 0m,
                MinNights = p.MinNights ?? 0,
                MaxDiscountNights = p.MaxDiscountNights ?? 0,
                MinGuests = p.MinGuests ?? 0
            })
            .ToList();

        return View(new RoomDetailsViewModel
        {
            Room = room,
            AllRooms = rooms,
            Reviews = reviews,
            ReviewCount = ratings.Count,
            AverageRating = ratings.Count > 0
                ? ratings.Average()
                : null,
            Promos = simPromos
        });
    }

    // =====================================================================
    // Promotions
    // =====================================================================
    public async Task<IActionResult> Promotions()
    {
        var promoRows = await GetActivePromotionRowsAsync();

        var roomTypeNames = await _db.RoomTypes
            .AsNoTracking()
            .ToDictionaryAsync(rt => rt.RoomTypeId, rt => rt.TypeName);

        var promos = MapPromoCards(promoRows, roomTypeNames);

        return View(promos);
    }

    // =====================================================================
    // Actions อื่น ๆ
    // =====================================================================
    public IActionResult Facilities() => View();

    public async Task<IActionResult> About()
{
    var ratings = await _db.Reviews
        .AsNoTracking()
        .Select(r => (int)r.Rating)
        .ToListAsync();

    ViewBag.AvgRating = ratings.Count > 0 ? ratings.Average() : (double?)null;
    ViewBag.ReviewCount = ratings.Count;

    ViewBag.RoomTypeCount = await _db.RoomTypes.CountAsync();
    ViewBag.RoomCount = await _db.Rooms.CountAsync(r => r.IsActive == true);

    return View();
}

    public IActionResult Contact() => View();

    public IActionResult FAQ() => View();

    public IActionResult Privacy() => View();

    [ResponseCache(
        Duration = 0,
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }

                public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
            {
                ViewBag.Hotel = await _db.HotelSettings.AsNoTracking().FirstOrDefaultAsync()
                                ?? HotelSetting.Default();
                await next();
            }
}