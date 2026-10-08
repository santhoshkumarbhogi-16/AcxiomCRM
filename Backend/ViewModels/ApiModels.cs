using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.ViewModels;

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed class CustomerRequest
{
    [Required, StringLength(100)] public string CustomerName { get; set; } = "";
    [Required, EmailAddress, StringLength(150)] public string Email { get; set; } = "";
    [Required, RegularExpression(@"^[6-9]\d{9}$")] public string Phone { get; set; } = "";
    [Required, StringLength(150)] public string CompanyName { get; set; } = "";
    [StringLength(250)] public string? Address { get; set; }
    [StringLength(80)] public string? City { get; set; }
    [StringLength(80)] public string? State { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
    [Required, RegularExpression("^(Active|Inactive)$")] public string Status { get; set; } = "Active";
    public string? AssignedTo { get; set; }
}

public sealed record CustomerResponse(
    int CustomerId, string CustomerCode, string CustomerName, string Email, string Phone,
    string CompanyName, string? Address, string? City, string? State, string Status,
    DateTime CreatedDate, string? AssignedTo);

public sealed class LeadRequest
{
    [Required, StringLength(100)] public string LeadName { get; set; } = "";
    [Required, EmailAddress, StringLength(150)] public string Email { get; set; } = "";
    [Required, RegularExpression(@"^[6-9]\d{9}$")] public string Phone { get; set; } = "";
    [Required, StringLength(150)] public string CompanyName { get; set; } = "";
    [Required, RegularExpression("^(Website|Referral|Advertisement|Event|Other)$")] public string Source { get; set; } = "Website";
    [Required, RegularExpression("^(New|Contacted|Qualified|Unqualified|Converted|Lost)$")] public string Status { get; set; } = "New";
    [Required, RegularExpression("^(Low|Normal|High)$")] public string Priority { get; set; } = "Normal";
    [Range(0, 1000000000)] public decimal ExpectedValue { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
    public string? AssignedTo { get; set; }
}

public sealed record LeadResponse(
    int LeadId, string LeadCode, string LeadName, string Email, string Phone,
    string CompanyName, string Source, string Status, string Priority,
    decimal ExpectedValue, DateTime CreatedDate, string? AssignedTo);

public sealed class OpportunityRequest
{
    [Required, StringLength(150)] public string OpportunityName { get; set; } = "";
    public int? CustomerId { get; set; }
    public int? LeadId { get; set; }
    [Range(typeof(decimal), "0.01", "1000000000")] public decimal Amount { get; set; }
    [Range(0, 100)] public int Probability { get; set; }
    [Required, RegularExpression("^(Qualification|Proposal|Negotiation|Won|Lost)$")] public string Stage { get; set; } = "Qualification";
    [DataType(DataType.Date)] public DateTime ExpectedCloseDate { get; set; } = DateTime.Today.AddDays(30);
    [Required, RegularExpression("^(Open|Closed)$")] public string Status { get; set; } = "Open";
    [StringLength(100)] public string? Source { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
    public string? AssignedTo { get; set; }
}

public sealed record OpportunityResponse(
    int OpportunityId, string OpportunityName, int? CustomerId, int? LeadId,
    decimal Amount, int Probability, string Stage, DateTime ExpectedCloseDate,
    string Status, decimal WeightedValue, string? Source, string? AssignedTo);

public sealed class FollowUpRequest
{
    public int? CustomerId { get; set; }
    public int? LeadId { get; set; }
    public int? OpportunityId { get; set; }
    [Required, StringLength(150)] public string Subject { get; set; } = "";
    [Required, RegularExpression("^(Call|Meeting|Email|Task)$")] public string FollowUpType { get; set; } = "Call";
    [DataType(DataType.Date)] public DateTime FollowUpDate { get; set; } = DateTime.Today;
    [Required, RegularExpression("^(Planned|Completed|Missed|Cancelled)$")] public string Status { get; set; } = "Planned";
    [StringLength(200)] public string? Remarks { get; set; }
    public string? AssignedTo { get; set; }
}

public sealed record FollowUpResponse(
    int FollowUpId, int? CustomerId, int? LeadId, int? OpportunityId,
    string Subject, string FollowUpType, DateTime FollowUpDate, string Status,
    string? Remarks, string? AssignedTo);
