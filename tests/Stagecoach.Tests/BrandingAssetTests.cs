using System.Reflection;

namespace Stagecoach.Tests;

/// <summary>
/// The application shipped for a while with no Win32 icon, so Explorer, the taskbar, and the Start
/// menu all showed the generic default. These guard the assets and the wiring that fixed it.
/// </summary>
public sealed class BrandingAssetTests
{
    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Stagecoach.sln")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            return directory.FullName;
        }
    }

    private static string IconPath =>
        Path.Combine(RepositoryRoot, "src", "Stagecoach.App", "Assets", "stagecoach.ico");

    [Fact]
    public void ApplicationIconIsAMultiResolutionIcoContainingTheSmallWindowsSizes()
    {
        Assert.True(File.Exists(IconPath), $"Missing application icon at {IconPath}");
        var bytes = File.ReadAllBytes(IconPath);

        // ICONDIR: reserved 0, type 1 (icon), then the image count.
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));
        var count = BitConverter.ToUInt16(bytes, 4);
        Assert.True(count >= 5, $"Expected several icon sizes, found {count}.");

        var widths = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + (16 * i);
            var width = bytes[entry] == 0 ? 256 : bytes[entry];
            var length = BitConverter.ToInt32(bytes, entry + 8);
            var offset = BitConverter.ToInt32(bytes, entry + 12);
            Assert.InRange(offset, 6 + (16 * count), bytes.Length);
            Assert.InRange(length, 1, bytes.Length - offset);
            widths.Add(width);
        }

        // Windows picks 16 for the title bar and small list views, 32 for Alt-Tab, and 256 for
        // extra-large Explorer tiles. Losing any of them is a visible regression.
        Assert.Contains(16, widths);
        Assert.Contains(32, widths);
        Assert.Contains(48, widths);
        Assert.Contains(256, widths);
    }

    [Fact]
    public void ApplicationIconIsReferencedByTheProjectSoTheExecutableCarriesIt()
    {
        var project = File.ReadAllText(
            Path.Combine(RepositoryRoot, "src", "Stagecoach.App", "Stagecoach.App.csproj"));
        Assert.Contains("<ApplicationIcon>Assets\\stagecoach.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.Contains("<AvaloniaResource Include=\"Assets\\**\" />", project, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerUsesTheIconForAddRemoveProgramsAndTheShortcut()
    {
        var installer = File.ReadAllText(Path.Combine(RepositoryRoot, "installer", "Package.wxs"));
        Assert.Contains("Id=\"Stagecoach.ico\"", installer, StringComparison.Ordinal);
        Assert.Contains("ARPPRODUCTICON", installer, StringComparison.Ordinal);
        Assert.Contains("Icon=\"Stagecoach.ico\"", installer, StringComparison.Ordinal);

        // ARPPRODUCTICON on its own left DisplayIcon empty in the uninstall key, so the icon is
        // also written explicitly. Verified end to end by installing the MSI and reading the key.
        Assert.Contains("Name=\"DisplayIcon\"", installer, StringComparison.Ordinal);
        Assert.Contains("[INSTALLFOLDER]Stagecoach.App.exe,0", installer, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowIconIsEmbeddedAsAnAvaloniaResource()
    {
        // Loaded through avares:// at runtime, so it has to actually be in the assembly.
        var app = Assembly.Load("Stagecoach.App");
        var resources = app.GetManifestResourceNames();
        Assert.Contains(resources, name => name.Contains("Avalonia", StringComparison.OrdinalIgnoreCase));
    }
}
