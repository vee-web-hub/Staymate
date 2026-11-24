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
    // รูปภาพและขนาดห้องคงเดิม ส่วนข้อความเสริม (ป้าย, ทิวทัศน์, สโลแกน, จุดเด่น) เขียนใหม่ให้สอดคล้องกับธีม Futuristic Boutique Luxury
    private static readonly RoomExtra[] All =
    {
        new("standard",
            new[] { "https://images.unsplash.com/photo-1631049307264-da0ec9d70304?auto=format&fit=crop&w=1200&q=85", UnsplashHorizon, UnsplashAmbient },
            "28 ตร.ม.", 28,
            Badge: "Smart Essential",
            BadgeClass: "customer-home-badge-cyan",
            Code: "STD",
            View: "วิวเมืองยามค่ำคืน",
            Tagline: "เรียบหรู คุ้มค่า พร้อมทุกฟังก์ชันที่จำเป็น",
            Highlights: new[]
            {
                "เครื่องนอนพรีเมียม นุ่มสบายตลอดคืน",
                "ไฟ Ambient LED ปรับอารมณ์ได้",
                "Wi-Fi 6 ความเร็วสูงทั่วห้อง"
            }),

        new("superior",
            new[] { "https://images.unsplash.com/photo-1611892440504-42a792e24d32?auto=format&fit=crop&w=1200&q=85", UnsplashAmbient, UnsplashHorizon },
            "32 ตร.ม.", 32,
            Badge: "Urban Comfort",
            BadgeClass: "customer-home-badge-blue",
            Code: "SUP",
            View: "วิวสวนและแนวสกายไลน์",
            Tagline: "พื้นที่กว้างขึ้น ตกแต่งอบอุ่น เหมาะกับการพักผ่อนยาว",
            Highlights: new[]
            {
                "พื้นที่นั่งเล่นและโต๊ะทำงานในห้อง",
                "สมาร์ททีวีพร้อมระบบสตรีมมิ่ง",
                "ห้องน้ำฝักบัวสไตล์เรนฟอเรสต์"
            }),

        new("deluxe",
            new[] { "https://images.unsplash.com/photo-1590490360182-c33d57733427?auto=format&fit=crop&w=1200&q=85", UnsplashAmbient, UnsplashHorizon },
            "38 ตร.ม.", 38,
            Badge: "Guest Favorite",
            BadgeClass: "customer-home-badge-violet",
            Code: "DLX",
            View: "วิว Skyline พาโนรามา",
            Tagline: "ห้องยอดนิยมที่สุด ดีไซน์ล้ำสมัยและพักสบายระดับพรีเมียม",
            Highlights: new[]
            {
                "หน้าต่างบานใหญ่ รับแสงธรรมชาติเต็มที่",
                "เครื่องชงกาแฟและมินิบาร์พรีเมียม",
                "ห้องน้ำหินอ่อนพร้อมอ่างอาบน้ำ"
            },
            Featured: true),

        new("family",
            new[] { "https://images.unsplash.com/photo-1582719478250-c89cae4dc85b?auto=format&fit=crop&w=1200&q=85", UnsplashHorizon, "https://images.unsplash.com/photo-1566665797739-1674de7a421a?auto=format&fit=crop&w=1200&q=85" },
            "60 ตร.ม.", 60,
            Badge: "Family & Gang",
            BadgeClass: "customer-home-badge-gold",
            Code: "FAM",
            View: "วิวเมืองและสระว่ายน้ำ",
            Tagline: "กว้างขวางสำหรับครอบครัวและกลุ่มเพื่อน พักด้วยกันได้อย่างสบาย",
            Highlights: new[]
            {
                "เตียงใหญ่หลายเตียง รองรับกลุ่มใหญ่",
                "โซนนั่งเล่นแยกสัดส่วนชัดเจน",
                "เหมาะกับโปรโมชั่น Family & Gang"
            }),
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