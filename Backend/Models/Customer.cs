using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.Models;

public class Customer
{
    public int CustomerId { get; set; }
    [StringLength(30)] public string CustomerCode { get; set; } = "";
    [Required, StringLength(100)] public string CustomerName { get; set; } = "";
    [Required, EmailAddress, StringLength(150)] public string Email { get; set; } = "";
    [Required, RegularExpression(@"^[6-9]\d{9}$", ErrorMessage="Enter a valid 10-digit Indian mobile number.")] public string Phone { get; set; } = "";
    [Required, StringLength(150)] public string CompanyName { get; set; } = "";
    [StringLength(250)] public string? Address { get; set; }
    [StringLength(80)] public string? City { get; set; }
    [StringLength(80)] public string? State { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
    [Required] public string Status { get; set; } = "Active";
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }
    public string? CreatedBy { get; set; }
    public string? AssignedTo { get; set; }
}