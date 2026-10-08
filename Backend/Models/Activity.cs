using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.Models;

public class Activity
{
    public int ActivityId { get; set; }
    [Required] public string ActivityType { get; set; } = "Call";
    [Required, StringLength(150)] public string Subject { get; set; } = "";
    [StringLength(500)] public string? Description { get; set; }
    public DateTime ActivityDate { get; set; } = DateTime.Now;
    public int? CustomerId { get; set; }
    public int? LeadId { get; set; }
    public int? OpportunityId { get; set; }
    public string? AssignedTo { get; set; }
    public string Status { get; set; } = "Open";
}