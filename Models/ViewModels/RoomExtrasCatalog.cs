namespace StayMate_Kavinvee_0576.Models.ViewModels;

public record RoomExtra(
    string Key,
    string[] Gallery,
    string SizeText,
    int SizeSqm,
    string Badge,
    string BadgeClass,
    string Code,
    string View,
    string Tagline,
    string[] Highlights,
    bool Featured = false);

public static class RoomExtrasCatalog
{
    private const string UnsplashHorizon = "https://images.unsplash.com/photo-1618773928121-c32242e63f39?auto=format&fit=crop&w=1200&q=85";
    private const string UnsplashAmbient = "https://images.unsplash.com/photo-1600210492486-724fe5c67fb0?auto=format&fit=crop&w=1200&q=85";

    // Key ต้องตรงกับส่วนหนึ่งของ type_code หรือ type_name ในตาราง room_types
    // ข้อความอธิบายทั้งหมดดึงจากฐานข้อมูล (Description / BedType / จำนวนผู้เข้าพัก)
    // ไฟล์นี้เก็บเฉพาะรูปภาพ และขนาดห้อง (ใช้เรียงลำดับ) เท่านั้น
    private static readonly RoomExtra[] All =
    {
        new("standard",
            new[] { "https://images.unsplash.com/photo-1631049307264-da0ec9d70304?auto=format&fit=crop&w=1200&q=85", UnsplashHorizon, UnsplashAmbient },
            "28 ตร.ม.", 28, "", "", "", "", "", System.Array.Empty<string>()),

        new("superior",
            new[] { "https://images.unsplash.com/photo-1611892440504-42a792e24d32?auto=format&fit=crop&w=1200&q=85", UnsplashAmbient, UnsplashHorizon },
            "32 ตร.ม.", 32, "", "", "", "", "", System.Array.Empty<string>()),

        new("deluxe",
            new[] { "https://images.unsplash.com/photo-1590490360182-c33d57733427?auto=format&fit=crop&w=1200&q=85", UnsplashAmbient, UnsplashHorizon },
            "38 ตร.ม.", 38, "", "", "", "", "", System.Array.Empty<string>(),
            Featured: true),

        new("family",
            new[] { "https://images.unsplash.com/photo-1582719478250-c89cae4dc85b?auto=format&fit=crop&w=1200&q=85", UnsplashHorizon, "https://images.unsplash.com/photo-1566665797739-1674de7a421a?auto=format&fit=crop&w=1200&q=85" },
            "60 ตร.ม.", 60, "", "", "", "", "", System.Array.Empty<string>()),
    };

    public static RoomExtra? Find(string? code, string? name)
    {
        foreach (var e in All)
        {
            if ((code?.Contains(e.Key, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (name?.Contains(e.Key, StringComparison.OrdinalIgnoreCase) ?? false))
                return e;
        }
        return null;
    }

    public static string Fallback(string name) =>
        "https://placehold.co/1200x800/070D1E/00E5FF?text=" + Uri.EscapeDataString(name);
}