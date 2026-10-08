using AcxiomCRM.Models;

namespace AcxiomCRM.ViewModels;

public sealed class CustomerDetailsViewModel
{
    public required Customer Customer { get; init; }
    public required IReadOnlyList<Activity> Activities { get; init; }
    public required IReadOnlyList<FollowUp> FollowUps { get; init; }
    public required IReadOnlyList<Opportunity> Opportunities { get; init; }
}
