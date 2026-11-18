namespace StayMate_Kavinvee_0576.ViewModels
{
    public class FinanceProfileViewModel
    {
        public uint UserId { get; set; }

        public string? Username { get; set; }

        public string? FullName { get; set; }

        public string? Email { get; set; }

        public string? Phone { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public string RoleName { get; set; } = "";
    }
}