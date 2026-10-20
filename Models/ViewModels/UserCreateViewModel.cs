using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace StayMate_Kavinvee_0576.Models.ViewModels;

// หน้า Add User (ผู้ดูแลเลือก Role ได้)
public class UserCreateViewModel : RegisterViewModel
{
    [Required(ErrorMessage = "กรุณาเลือก Role")]
    [Display(Name = "Role")]
    public string RoleCode { get; set; } = "";

    [ValidateNever]
    public List<SelectListItem> Roles { get; set; } = new();
}
