// Models/ViewModels/HomeIndexViewModel.cs
namespace StayMate_Kavinvee_0576.Models.ViewModels;

// ===================== หน้า Index =====================
public class HomeIndexViewModel
{
    public List<RoomCardItem> Rooms { get; set; } = new();
    public List<PromoCardItem> Promotions { get; set; } = new();
    public double? AverageRating { get; set; }
    public int ReviewCount { get; set; }
}

// ใช้ร่วมกันทั้ง Index / Rooms / RoomDetails
public class RoomCardItem
{
    // ---- จาก DB ----
    public byte RoomTypeId { get; set; }
    public string TypeCode { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string? Description { get; set; }
    public string? BedType { get; set; }
    public byte MinGuests { get; set; }
    public byte MaxGuests { get; set; }
    public decimal Price { get; set; }
    public bool AllowExtraBed { get; set; }
    public decimal ExtraBedPrice { get; set; }
    public int AvailableRooms { get; set; }

    // ---- ค่าที่ไม่มีใน DB (มาจาก RoomExtrasCatalog) ----
    public string ImageUrl { get; set; } = "";
    public string[] Gallery { get; set; } = Array.Empty<string>();
    public string SizeText { get; set; } = "";
    public int SizeSqm { get; set; }
    public string BadgeText { get; set; } = "";
    public string BadgeClass { get; set; } = "";
    public string Code { get; set; } = "";
    public string ViewText { get; set; } = "";
    public string Tagline { get; set; } = "";
    public bool IsFeatured { get; set; }
    public string[] Highlights { get; set; } = Array.Empty<string>();

    // ---- คำนวณจากข้อมูล DB ----
    public string Category { get; set; } = ""; // couple / extrabed / group (ใช้กรองหน้า Rooms)
    public int Order { get; set; }
    public List<string> PromoNames { get; set; } = new(); // โปรโมชั่นที่ใช้กับห้องนี้ได้
}

public class PromoCardItem
{
    public uint PromotionId { get; set; }
    public string PromoCode { get; set; } = "";
    public string PromoName { get; set; } = "";
    public string? Description { get; set; }
    public string DiscountType { get; set; } = "";
    public decimal? DiscountPercent { get; set; }
    public decimal? DiscountPercentMax { get; set; }
    public byte? MinGuests { get; set; }
    public ushort? MinNights { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }

    public string GradClass { get; set; } = "sm-grad-blue";
    public string IconClass { get; set; } = "fa-gift";
    public string Meta1 { get; set; } = "";
    public string Meta2 { get; set; } = "";

    // ---- สำหรับหน้า Promotions ----
    public byte? ApplicableRoomTypeId { get; set; }
    public string? ApplicableRoomTypeName { get; set; }
    public bool RequiresHoliday { get; set; }
    public string Category { get; set; } = ""; // popular / stay-long / group-pack
    public string BadgeText { get; set; } = "";
    public List<PromoPerk> Perks { get; set; } = new();
}

public class PromoPerk
{
    public string Icon { get; set; } = "";
    public string Text { get; set; } = "";
}

// ===================== หน้า RoomDetails =====================
public class RoomDetailsViewModel
{
    public RoomCardItem Room { get; set; } = new();
    public List<RoomCardItem> AllRooms { get; set; } = new();
    public List<ReviewItem> Reviews { get; set; } = new();
    public double? AverageRating { get; set; }
    public int ReviewCount { get; set; }
    public List<SimPromoItem> Promos { get; set; } = new();
}

public class ReviewItem
{
    public string UserName { get; set; } = "ผู้เข้าพัก";
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ข้อมูลโปรโมชั่นที่ส่งให้ JS คำนวณราคาจำลอง
public class SimPromoItem
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public decimal Percent { get; set; }
    public int MinNights { get; set; }
    public int MaxDiscountNights { get; set; }
    public int MinGuests { get; set; }
}