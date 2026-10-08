namespace AcxiomCRM.ViewModels;

public sealed class ConversionReportViewModel
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int TotalLeads { get; set; }
    public int ConvertedLeads { get; set; }
    public int NotConvertedLeads { get; set; }
    public double ConversionRate { get; set; }
    public IReadOnlyList<ConversionReportRow> BySource { get; set; } = [];
    public IReadOnlyList<ConversionReportRow> ByOwner { get; set; } = [];
}

public sealed record ConversionReportRow(string Name, int Total, int Converted)
{
    public double Rate => Total == 0 ? 0 : (double)Converted / Total * 100;
}
