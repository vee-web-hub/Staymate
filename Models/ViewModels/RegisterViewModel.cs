using System.ComponentModel.DataAnnotations;

namespace StayMate_Kavinvee_0576.Models.ViewModels;

public class RegisterViewModel
{
    [Required(ErrorMessage = "กรุณากรอก Username")]
    [StringLength(50, MinimumLength = 4, ErrorMessage = "Username ต้องยาว 4-50 ตัวอักษร")]
    [RegularExpression(@"^[a-zA-Z0-9_.]+$", ErrorMessage = "ใช้ได้เฉพาะ a-z, 0-9, _ และ .")]
    [Display(Name = "Username")]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "กรุณากรอกชื่อ-นามสกุล")]
    [StringLength(150)]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "กรุณากรอก Email")]
    [EmailAddress(ErrorMessage = "รูปแบบ Email ไม่ถูกต้อง")]
    [StringLength(120)]
    [Display(Name = "Email")]
    public string Email { get; set; } = "";

    [Phone(ErrorMessage = "รูปแบบเบอร์โทรไม่ถูกต้อง")]
    [StringLength(20)]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [Required(ErrorMessage = "กรุณากรอก Password")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password ต้องยาวอย่างน้อย 6 ตัวอักษร")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "กรุณายืนยัน Password")]
    [Compare("Password", ErrorMessage = "Password ไม่ตรงกัน")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = "";
}
