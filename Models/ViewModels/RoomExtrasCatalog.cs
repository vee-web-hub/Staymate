namespace StayMate_Kavinvee_0576.Models.ViewModels;

/// <summary>
/// ข้อมูลที่ตาราง room_types ไม่มี (รูป, ขนาด ตร.ม., ป้าย, ทิวทัศน์ ฯลฯ)
/// จับคู่กับห้องจาก type_code หรือ type_name โดยหาคำว่า standard / deluxe / family / suite
/// อยากเปลี่ยนรูป ให้แก้ที่ไฟล์นี้ที่เดียว (แนะนำให้ใช้รูปของตัวเอง เช่น "/images/rooms/deluxe-1.jpg")
/// </summary>
public record RoomExtra(
    string Key,
    string[] Gallery,       // รูปที่ 1 = รูปหลักของห้อง, รูปที่ 2-3 = มุมอื่น
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

    private static readonly RoomExtra[] All =
    {
        new("standard",
            new[] { "https://images.unsplash.com/photo-1631049307264-da0ec9d70304?auto=format&fit=crop&w=1200&q=85", UnsplashHorizon, UnsplashAmbient },
            "28 ตร.ม.", 28, "Smart Value", "",
            "TYPE A · URBAN MINIMAL", "Urban Street View",
            "ห้องพักมาตรฐานดีไซน์โมเดิร์น มินิมอล พร้อมระบบไฟอัจฉริยะ",
            new[] { "Smart TV 55\" & Wi-Fi 6", "ฝักบัว Rain Shower แรงดันสูง", "ระบบควบคุมไฟ Ambient ข้างเตียง" }),

        new("deluxe",
            new[] { "https://images.unsplash.com/photo-1590490360182-c33d57733427?auto=format&fit=crop&w=1200&q=85", UnsplashAmbient, UnsplashHorizon },
            "36 ตร.ม.", 36, "Popular · รองรับเตียงเสริม", "sm-badge-cyan",
            "TYPE B · SKYLINE PANORAMA", "High-Floor City Skyline",
            "ห้องพักวิวเมืองมุมสูง รองรับเตียงเสริมและโปรโมชั่นกลุ่ม",
            new[] { "วิวเมืองมุมสูง (City Skyline)", "รองรับ Extra Bed นุ่มพิเศษ", "โซฟาเลานจ์ & มินิบาร์พรีเมียม" },
            Featured: true),

        new("family",
            new[] { "https://images.unsplash.com/photo-1566665797739-1674de7a421a?auto=format&fit=crop&w=1200&q=85", UnsplashAmbient, "https://images.unsplash.com/photo-1631049307264-da0ec9d70304?auto=format&fit=crop&w=1200&q=85" },
            "50 ตร.ม.", 50, "Family & Gang", "sm-badge-violet",
            "TYPE C · SHARED HAPPINESS SPACE", "Panoramic City View",
            "พื้นที่ความสุขขนาดใหญ่สำหรับครอบครัวและแก๊งเพื่อน",
            new[] { "เตียงใหญ่ 2 เตียง (2 King Beds)", "โต๊ะทานอาหารและมุมบอร์ดเกม", "ห้องน้ำแยกโซนเปียก-แห้ง" }),

        new("suite",
            new[] { "https://images.unsplash.com/photo-1582719478250-c89cae4dc85b?auto=format&fit=crop&w=1200&q=85", UnsplashHorizon, "https://images.unsplash.com/photo-1590490360182-c33d57733427?auto=format&fit=crop&w=1200&q=85" },
            "68 ตร.ม.", 68, "Futuristic Luxury", "sm-badge-gold",
            "TYPE D · EXECUTIVE HORIZON SUITE", "180° Horizon Skyline",
            "ห้องสวีทระดับ Flagship อ่างจากุซซี่ชมวิวเมือง และสิทธิ์ Sky Lounge",
            new[] { "อ่างจากุซซี่ชมวิวพาโนรามา", "ระบบเสียง Hi-Res รอบทิศทาง", "สิทธิ์เข้าใช้ Executive Sky Lounge" }),
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