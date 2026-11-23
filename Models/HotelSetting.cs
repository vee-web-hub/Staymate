namespace StayMate_Kavinvee_0576.Models;

public partial class HotelSetting
{
    public byte SettingId { get; set; }
    public string HotelName { get; set; } = null!;
    public string? LogoPath { get; set; }
    public string Address { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string Email { get; set; } = null!;
    public TimeOnly CheckInTime { get; set; }
    public TimeOnly CheckOutTime { get; set; }
    public DateTime UpdatedAt { get; set; }
    public uint? UpdatedBy { get; set; }

    // ค่าสำรองกรณีตารางยังไม่มีข้อมูล
    public static HotelSetting Default() => new()
    {
        SettingId = 1,
        HotelName = "StayMate Hotel",
        LogoPath = "/images/Logo/LogoStayMate.jpg",
        Address = "888 อาคารสเตย์เมท ทาวเวอร์ ถนนสุขุมวิท แขวงคลองเตยเหนือ เขตวัฒนา กรุงเทพมหานคร 10110",
        Phone = "02-888-9999",
        Email = "booking@staymate-hotel.com",
        CheckInTime = new TimeOnly(14, 0),
        CheckOutTime = new TimeOnly(12, 0)
    };
}