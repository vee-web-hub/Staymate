using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models;

public partial class UserRole
{
    public uint UserId { get; set; }

    public byte RoleId { get; set; }

    public DateTime AssignedAt { get; set; }

    public virtual Role Role { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
