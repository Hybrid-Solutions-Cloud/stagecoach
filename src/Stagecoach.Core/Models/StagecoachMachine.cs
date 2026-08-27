namespace Stagecoach.Core.Models;

public class StagecoachMachine
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public TargetKind Kind { get; set; } = TargetKind.AzureVM;
    public string OsType { get; set; } = "Windows";
    public string OsName { get; set; } = string.Empty;
    public string PowerState { get; set; } = "Unknown";
    public string AgentStatus { get; set; } = string.Empty;
    public string DomainName { get; set; } = string.Empty;
    public DomainType DomainType { get; set; } = DomainType.Workgroup;
    public string? BastionHostId { get; set; }
    public string? PublicIpAddress { get; set; }
    public string? PrivateIpAddress { get; set; }
    public bool IsFavorite { get; set; }
    public DateTime? LastConnectedAt { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new();
}
