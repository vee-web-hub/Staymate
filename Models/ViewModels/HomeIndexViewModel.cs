namespace StayMate_Kavinvee_0576.Models.ViewModels;

public class HomeIndexViewModel
{
    public List<RoomCardItem> Rooms { get; set; } = new();
    public List<PromoCardItem> Promotions { get; set; } = new();
    public double? AverageRating { get; set; }
    public int ReviewCount { get; set; }
}

public class RoomCardItem
{
    public byte RoomTypeId { get; set; }
    public string TypeCode { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string? Description { get; set; }
    public string? BedType { get; set; }
    public byte MinGuests { get; set; }
    public byte MaxGuests { get; set; }
    public decimal Price { get; set; }
    public bool AllowExtraBed { get; set; }
    public int AvailableRooms { get; set; }

    // ค่าที่ไม่มีใน DB (แมปจาก TypeCode)
    public string ImageUrl { get; set; } = "";
    public string SizeText { get; set; } = "";
    public string BadgeText { get; set; } = "";
    public string BadgeClass { get; set; } = "";
    public string[] Highlights { get; set; } = Array.Empty<string>();
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
    public DateOnly? ValidTo { get; set; }

    // UI
    public string GradClass { get; set; } = "sm-grad-blue";
    public string IconClass { get; set; } = "fa-gift";
    public string Meta1 { get; set; } = "";
    public string Meta2 { get; set; } = "";
}