using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.Models;

public class Opportunity
{
    public int OpportunityId { get; set; }
    [Required, StringLength(150)] public string OpportunityName { get; set; } = "";
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? LeadId { get; set; }
    [Range(0.01, 1000000000, ErrorMessage="Amount must be greater than 0.")] public decimal Amount { get; set; }
    [Range(0, 100)] public int Probability { get; set; }
    [Required] public string Stage { get; set; } = "Qualification";
    [StringLength(100)] public string? Source { get; set; }
    public DateTime ExpectedCloseDate { get; set; } = DateTime.Today.AddDays(30);
    public string Status { get; set; } = "Open";
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }
    public string? AssignedTo { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
    public decimal WeightedValue => Amount * Probability / 100m;
}