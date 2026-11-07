using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsage;

internal enum Severity
{
    Normal,
    Warning,
    Critical,
}

/// <summary>
/// Ring-gauge icons for the Dock. The ring fills with usage and turns amber at 80% and red at 95%,
/// so you can read the state from the corner of your eye. Rendered as small SVG files (rounded to
/// 5% steps) in the data directory, with separate light/dark theme variants.
/// </summary>
internal static class GaugeIcons
{
    public const int WarningAt = 80;
    public const int CriticalAt = 95;

    private static readonly ConcurrentDictionary<string, IconInfo> Cache = new();
    private static readonly string Dir = Path.Combine(Path.GetDirectoryName(AppInfo.DataFile)!, "icons");

    public static Severity SeverityOf(double percent) =>
        percent >= CriticalAt ? Severity.Critical : percent >= WarningAt ? Severity.Warning : Severity.Normal;

    /// <summary>Accent color used for bars in the details flyout.</summary>
    public static string AccentColor(Severity s) => s switch
    {
        Severity.Critical => "rgb(229,72,77)",
        Severity.Warning => "rgb(227,160,8)",
        _ => "rgb(217,119,87)",
    };

    public static IconInfo For(double percent)
    {
        var step = (int)Math.Clamp(Math.Round(percent / 5) * 5, 0, 100);
        var severity = SeverityOf(percent);
        var key = $"{step}-{severity}";
        return Cache.GetOrAdd(key, _ =>
        {
            try
            {
                var light = Write(key + "-light", step, severity, dark: false);
                var dark = Write(key + "-dark", step, severity, dark: true);
                return new IconInfo(new IconData(light), new IconData(dark));
            }
            catch
            {
                return new IconInfo(AppInfo.Glyph(severity == Severity.Normal ? 0xE9D9 : 0xE7BA));
            }
        });
    }

    private static string Write(string name, int percent, Severity severity, bool dark)
    {
        Directory.CreateDirectory(Dir);
        var path = Path.Combine(Dir, $"gauge-{name}.svg");
        if (File.Exists(path))
        {
            return path;
        }

        var fg = dark ? "rgb(255,255,255)" : "rgb(26,26,26)";
        var fill = severity switch
        {
            Severity.Critical => "rgb(229,72,77)",
            Severity.Warning => "rgb(234,170,20)",
            _ => fg,
        };

        const double r = 6;
        var circumference = 2 * Math.PI * r;
        var dash = circumference * percent / 100;
        var inv = CultureInfo.InvariantCulture;

        var svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16">
              <circle cx="8" cy="8" r="{r.ToString(inv)}" fill="none" stroke="{fg}" stroke-opacity="0.28" stroke-width="2.6"/>
              <circle cx="8" cy="8" r="{r.ToString(inv)}" fill="none" stroke="{fill}" stroke-width="2.6" stroke-linecap="{(percent is > 0 and < 100 ? "round" : "butt")}"
                stroke-dasharray="{dash.ToString("F2", inv)} {circumference.ToString("F2", inv)}" transform="rotate(-90 8 8)"/>
            </svg>
            """;
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, svg);
        File.Move(tmp, path, overwrite: true);
        return path;
    }
}
