namespace StayMate_Kavinvee_0576.Models.ViewModels;

public class UserListItem
{
    public uint UserId { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public bool IsActive { get; set; }
    public string? RoleCode { get; set; }
    public string? RoleName { get; set; }
}
