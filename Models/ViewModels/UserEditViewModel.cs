using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace StayMate_Kavinvee_0576.Models.ViewModels;

public class UserEditViewModel
{
    public uint UserId { get; set; }

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

    [Required(ErrorMessage = "กรุณาเลือก Role")]
    [Display(Name = "Role")]
    public string RoleCode { get; set; } = "";

    [Display(Name = "เปิดใช้งานบัญชี")]
    public bool IsActive { get; set; } = true;

    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password ต้องยาวอย่างน้อย 6 ตัวอักษร")]
    [DataType(DataType.Password)]
    [Display(Name = "New Password (เว้นว่างถ้าไม่เปลี่ยน)")]
    public string? NewPassword { get; set; }

    [ValidateNever]
    public List<SelectListItem> Roles { get; set; } = new();
}
