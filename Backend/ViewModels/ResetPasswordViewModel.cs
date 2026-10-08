using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.ViewModels;

public sealed class ResetPasswordViewModel
{
    [Required]
    public string UserId { get; set; } = "";

    [Required]
    public string Token { get; set; } = "";

    [Required, DataType(DataType.Password), MinLength(8)]
    public string Password { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(Password))]
    public string ConfirmPassword { get; set; } = "";
}
