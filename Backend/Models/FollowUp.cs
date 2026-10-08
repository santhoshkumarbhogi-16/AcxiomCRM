using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.Models;

public class FollowUp
{
    public int FollowUpId { get; set; }
    public int? CustomerId { get; set; }
    public int? LeadId { get; set; }
    public int? OpportunityId { get; set; }
    [DataType(DataType.Date)] public DateTime FollowUpDate { get; set; } = DateTime.Today;
    [Required, StringLength(150)] public string Subject { get; set; } = "";
    [Required] public string FollowUpType { get; set; } = "Call";
    [StringLength(200)] public string? Remarks { get; set; }
    [Required] public string Status { get; set; } = "Planned";
    public string? AssignedTo { get; set; }
}