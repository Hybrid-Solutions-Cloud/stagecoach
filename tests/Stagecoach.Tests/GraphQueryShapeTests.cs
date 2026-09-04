using System.Reflection;
using Stagecoach.Infrastructure.Azure;

namespace Stagecoach.Tests;

/// <summary>
/// On Windows the Azure CLI is az.cmd, so the command line is re-parsed by cmd.exe and a newline
/// inside an argument truncates it there. A multi-line query silently lost its where clause, and
/// Resource Graph returned every resource in scope rather than the ten types wanted.
/// </summary>
public sealed class GraphQueryShapeTests
{
    private static string SingleLineQuery =>
        (string)typeof(ResourceGraphDiscoveryService)
            .GetField("SingleLineQuery", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    [Fact]
    public void QueryPassedToTheCliHasNoLineBreaks()
    {
        Assert.DoesNotContain('\n', SingleLineQuery);
        Assert.DoesNotContain('\r', SingleLineQuery);
    }

    [Fact]
    public void FlatteningKeepsTheFilterAndTheProjection()
    {
        // Losing either of these is the difference between ten resource types and the whole estate.
        Assert.Contains("| where type in~ (", SingleLineQuery, StringComparison.Ordinal);
        Assert.Contains("microsoft.compute/virtualmachines", SingleLineQuery, StringComparison.Ordinal);
        Assert.Contains("microsoft.hybridcompute/machines", SingleLineQuery, StringComparison.Ordinal);
        Assert.Contains("| project id, name, type", SingleLineQuery, StringComparison.Ordinal);
    }

    [Fact]
    public void ClausesStaySeparatedSoTheQueryRemainsValid()
    {
        Assert.DoesNotContain("Resources| where", SingleLineQuery, StringComparison.Ordinal);
        Assert.DoesNotContain(")| project", SingleLineQuery, StringComparison.Ordinal);
    }
}
