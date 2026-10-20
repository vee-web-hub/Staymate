using System;
using System.Collections.Generic;

namespace StayMate_Kavinvee_0576.Models;

public partial class Role
{
    public byte RoleId { get; set; }

    public string RoleCode { get; set; } = null!;

    public string RoleName { get; set; } = null!;

    public string? Description { get; set; }

    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
