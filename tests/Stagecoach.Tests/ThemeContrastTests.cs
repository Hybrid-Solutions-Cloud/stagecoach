using System.Globalization;
using System.Text.RegularExpressions;

namespace Stagecoach.Tests;

/// <summary>
/// Fluent resolves hover, pressed, and disabled colours from resource keys rather than from the
/// control's own Background. Styling the control therefore only covers the resting state, and the
/// theme keeps the rest — which rendered selected navigation items as white text on a light hover
/// background, so the left menu appeared to vanish under the pointer.
/// These assert the overrides exist and that no state pairs a light background with light text.
/// </summary>
public sealed class ThemeContrastTests
{
    private static string AppStyles
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Stagecoach.sln")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            return File.ReadAllText(
                Path.Combine(directory.FullName, "src", "Stagecoach.App", "App.axaml"));
        }
    }

    [Theory]
    [InlineData("TabItemHeaderBackgroundUnselected")]
    [InlineData("TabItemHeaderBackgroundUnselectedPointerOver")]
    [InlineData("TabItemHeaderBackgroundSelected")]
    [InlineData("TabItemHeaderBackgroundSelectedPointerOver")]
    [InlineData("TabItemHeaderBackgroundSelectedPressed")]
    [InlineData("TabItemHeaderForegroundUnselected")]
    [InlineData("TabItemHeaderForegroundSelected")]
    [InlineData("ButtonBackgroundPointerOver")]
    [InlineData("ButtonBackgroundDisabled")]
    [InlineData("ButtonForegroundPointerOver")]
    [InlineData("ButtonForegroundDisabled")]
    public void ThemeStateBrushIsOverridden(string key) =>
        Assert.True(TryReadBrush(key, out _), $"App.axaml does not override '{key}'.");

    [Theory]
    // Every state the pointer can put the left navigation into must stay legible.
    [InlineData("TabItemHeaderBackgroundSelected", "TabItemHeaderForegroundSelected")]
    [InlineData("TabItemHeaderBackgroundSelectedPointerOver", "TabItemHeaderForegroundSelected")]
    [InlineData("TabItemHeaderBackgroundSelectedPressed", "TabItemHeaderForegroundSelected")]
    [InlineData("TabItemHeaderBackgroundUnselected", "TabItemHeaderForegroundUnselected")]
    [InlineData("TabItemHeaderBackgroundUnselectedPointerOver", "TabItemHeaderForegroundUnselected")]
    [InlineData("TabItemHeaderBackgroundUnselectedPressed", "TabItemHeaderForegroundUnselected")]
    [InlineData("ButtonBackgroundPointerOver", "ButtonForegroundPointerOver")]
    [InlineData("ButtonBackgroundPressed", "ButtonForegroundPressed")]
    [InlineData("ButtonBackgroundDisabled", "ButtonForegroundDisabled")]
    public void ForegroundStaysReadableAgainstItsBackground(string backgroundKey, string foregroundKey)
    {
        Assert.True(TryReadBrush(backgroundKey, out var background), backgroundKey);
        Assert.True(TryReadBrush(foregroundKey, out var foreground), foregroundKey);

        var contrast = ContrastRatio(background, foreground);
        Assert.True(
            contrast >= 4.5,
            $"'{foregroundKey}' on '{backgroundKey}' has contrast {contrast:0.00}:1, below 4.5:1.");
    }

    private static bool TryReadBrush(string key, out (double R, double G, double B) colour)
    {
        var pattern =
            "<SolidColorBrush\\s+x:Key=\"" + Regex.Escape(key) +
            "\"\\s*>\\s*#([0-9A-Fa-f]{6})\\s*</SolidColorBrush>";
        var match = Regex.Match(AppStyles, pattern);
        if (!match.Success)
        {
            colour = default;
            return false;
        }

        var hex = match.Groups[1].Value;
        colour = (
            int.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d,
            int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d,
            int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d);
        return true;
    }

    // WCAG 2.1 relative luminance and contrast ratio.
    private static double ContrastRatio((double R, double G, double B) a, (double R, double G, double B) b)
    {
        var first = Luminance(a);
        var second = Luminance(b);
        var lighter = Math.Max(first, second);
        var darker = Math.Min(first, second);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance((double R, double G, double B) colour) =>
        (0.2126 * Channel(colour.R)) + (0.7152 * Channel(colour.G)) + (0.0722 * Channel(colour.B));

    private static double Channel(double value) =>
        value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
}
