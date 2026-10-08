namespace AcxiomCRM.ViewModels;
public class DashboardViewModel
{
    public int TotalCustomers { get; set; }
    public int TotalLeads { get; set; }
    public int OpenLeads { get; set; }
    public int TotalOpportunities { get; set; }
    public int OpenOpportunities { get; set; }
    public int WonOpportunities { get; set; }
    public int LostOpportunities { get; set; }
    public int PendingFollowUps { get; set; }
    public int OverdueFollowUps { get; set; }
    public int TotalUsers { get; set; }
    public decimal PipelineValue { get; set; }
    public decimal WeightedPipeline { get; set; }
    public Dictionary<string, int> LeadStatuses { get; set; } = new();
    public Dictionary<string, decimal> OpportunityStages { get; set; } = new();
    public Dictionary<string, decimal> MonthlySales { get; set; } = new();
    public string Range { get; set; } = "month";
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
