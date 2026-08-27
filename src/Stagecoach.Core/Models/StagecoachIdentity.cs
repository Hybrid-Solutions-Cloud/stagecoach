namespace Stagecoach.Core.Models;

public class StagecoachIdentity
{
    public string AccountName { get; set; } = string.Empty;
    public List<StagecoachTenant> Tenants { get; set; } = new();
}

public class StagecoachTenant
{
    public string TenantId { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public bool IsSelected { get; set; } = true;
    public List<StagecoachSubscription> Subscriptions { get; set; } = new();
}

public class StagecoachSubscription
{
    public string SubscriptionId { get; set; } = string.Empty;
    public string SubscriptionName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}
