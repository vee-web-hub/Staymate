namespace StayMate_Kavinvee_0576.Models.ViewModels;

public class CustomerViewModel
{
    // สถิติการจองของสมาชิก
    public int TotalBookings { get; set; }
    public int UpcomingBookings { get; set; }   // confirmed
    public int PendingBookings { get; set; }    // pending_payment + pending_approval
    public int CompletedBookings { get; set; }  // checked_out

    public List<CustomerBookingItem> LatestBookings { get; set; } = new();
    public List<CustomerRoomItem> Rooms { get; set; } = new();
    public List<CustomerPromoItem> Promotions { get; set; } = new();
    public List<CustomerReviewItem> Reviews { get; set; } = new();

    public double? AverageRating { get; set; }
    public int ReviewCount { get; set; }
}

public class CustomerBookingItem
{
    public uint BookingId { get; set; }
    public string BookingCode { get; set; } = "";
    public string Status { get; set; } = "";
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public int Nights { get; set; }
    public int Guests { get; set; }
    public int RoomCount { get; set; }
    public decimal TotalAmount { get; set; }
    public string RoomTypeName { get; set; } = "-";
    public string ImageUrl { get; set; } = "";
    public string? PromoName { get; set; }
}

public class CustomerRoomItem
{
    public byte RoomTypeId { get; set; }
    public string TypeName { get; set; } = "";
    public string? Description { get; set; }
    public byte MinGuests { get; set; }
    public byte MaxGuests { get; set; }
    public decimal Price { get; set; }
    public int AvailableRooms { get; set; }
    public string ImageUrl { get; set; } = "";
    public string SizeText { get; set; } = "";
    public string BadgeText { get; set; } = "";
    public string BadgeClass { get; set; } = "customer-home-badge-cyan";
    public string[] Highlights { get; set; } = Array.Empty<string>();
}

public class CustomerPromoItem
{
    public string PromoCode { get; set; } = "";
    public string PromoName { get; set; } = "";
    public string? Description { get; set; }
    public string BenefitText { get; set; } = "";
    public string ConditionText { get; set; } = "";
    public string GradClass { get; set; } = "customer-home-grad-blue";
    public string IconClass { get; set; } = "fa-gift";
    public DateOnly? ValidTo { get; set; }
}

public class CustomerReviewItem
{
    public string UserName { get; set; } = "ผู้เข้าพัก";
    public string RoomTypeName { get; set; } = "-";
    public int Rating { get; set; }
    public string? Comment { get; set; }
}