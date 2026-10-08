using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.Models;

public class Lead
{
    public int LeadId { get; set; }
    [StringLength(30)] public string LeadCode { get; set; } = "";
    [Required, StringLength(100)] public string LeadName { get; set; } = "";
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, RegularExpression(@"^[6-9]\d{9}$")] public string Phone { get; set; } = "";
    [Required, StringLength(150)] public string CompanyName { get; set; } = "";
    [Required] public string Source { get; set; } = "Website";
    [Required] public string Status { get; set; } = "New";
    [Required, StringLength(30)] public string Priority { get; set; } = "Normal";
    [StringLength(500)] public string? Notes { get; set; }
    [Range(0, 1000000000)] public decimal ExpectedValue { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }
    public string? AssignedTo { get; set; }
}