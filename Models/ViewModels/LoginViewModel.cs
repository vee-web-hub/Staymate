using System.ComponentModel.DataAnnotations;

namespace StayMate_Kavinvee_0576.Models.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "กรุณากรอก Username")]
    [Display(Name = "Username")]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "กรุณากรอก Password")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = "";
}
