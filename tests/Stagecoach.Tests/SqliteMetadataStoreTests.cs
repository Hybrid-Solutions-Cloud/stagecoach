using Stagecoach.Core.Models;
using Stagecoach.Infrastructure.Storage;
using Xunit;

namespace Stagecoach.Tests;

public class SqliteMetadataStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteMetadataStore _store;

    public SqliteMetadataStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"stagecoach_test_{Guid.NewGuid():N}.db");
        _store = new SqliteMetadataStore(_dbPath);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public async Task InitializeAndSaveMachines_PersistsSuccessfully()
    {
        await _store.InitializeAsync();

        var machine1 = new StagecoachMachine
        {
            Id = "/subscriptions/sub1/resourceGroups/rg1/providers/Microsoft.Compute/virtualMachines/vm-app01",
            Name = "vm-app01",
            ResourceGroup = "rg1",
            SubscriptionId = "sub1",
            TenantId = "ten1",
            Location = "eastus",
            Kind = TargetKind.AzureVM,
            OsName = "Windows Server 2022",
            PowerState = "VM running",
            DomainType = DomainType.ActiveDirectory,
            DomainName = "CORP.CONTOSO.COM"
        };

        await _store.SaveMachinesAsync(new[] { machine1 });

        var all = await _store.GetAllMachinesAsync();
        Assert.Single(all);
        Assert.Equal("vm-app01", all[0].Name);
        Assert.Equal(DomainType.ActiveDirectory, all[0].DomainType);
    }

    [Fact]
    public async Task SetFavorite_TogglesStateCorrectly()
    {
        await _store.InitializeAsync();

        var machine = new StagecoachMachine
        {
            Id = "machine-123",
            Name = "vm-db01",
            ResourceGroup = "rg-prod",
            SubscriptionId = "sub-1",
            TenantId = "ten-1",
            Location = "eastus",
            Kind = TargetKind.ArcServer,
            OsName = "Windows Server 2025"
        };

        await _store.SaveMachinesAsync(new[] { machine });
        await _store.SetFavoriteAsync("machine-123", true);

        var all = await _store.GetAllMachinesAsync();
        Assert.Single(all);
        Assert.True(all[0].IsFavorite);
    }
}
