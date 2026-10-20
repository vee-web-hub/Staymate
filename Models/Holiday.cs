using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models;

public partial class Holiday
{
    public uint HolidayId { get; set; }

    public string HolidayName { get; set; } = null!;

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public string? EventNote { get; set; }

    public uint? CreatedBy { get; set; }

    public virtual User? CreatedByNavigation { get; set; }
}
