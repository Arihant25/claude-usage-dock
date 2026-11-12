using System;
using System.IO;

namespace ClaudeUsage;

/// <summary>Identifiers and well-known paths shared by the extension and the status line bridge.</summary>
internal static class AppInfo
{
    public const string DisplayName = "Usage Dock";

    public const string ProviderId = "claudeusagedock";
    /// <summary>
    /// Dock band ID. Command Palette resolves pinned bands by this ID alone, ignoring the provider,
    /// so the Store and sideloaded builds use different IDs. Otherwise a pin left over from one
    /// build makes Command Palette treat the other build's band as already pinned and hide it
    /// from the Dock's Edit list.
    /// </summary>
    public static string BandId => s_bandId.Value;

    private static readonly Lazy<string> s_bandId = new(() => IsStoreBuild() ? "claudeusagedock.band" : "claudeusagedock.sideloaded.band");
    public const string FiveHourItemId = "claudeusagedock.fivehour";
    public const string WeeklyItemId = "claudeusagedock.weekly";
    public const string DetailsId = "claudeusagedock.details";
    public const string ConnectId = "claudeusagedock.connect";
    public const string DisconnectId = "claudeusagedock.disconnect";

    /// <summary>App execution alias declared in Package.appxmanifest.</summary>
    public const string BridgeAlias = "claude-usage-bridge.exe";

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// Where the bridge drops the latest rate-limit snapshot. Deliberately outside AppData so
    /// MSIX file-system virtualization never hides it from tools outside the package.
    /// </summary>
    public static string DataFile => Path.Combine(Home, ".claude-usage-dock", "usage.json");

    /// <summary>Claude Code's user settings, honoring CLAUDE_CONFIG_DIR.</summary>
    public static string ClaudeSettingsFile
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (string.IsNullOrWhiteSpace(dir))
            {
                dir = Path.Combine(Home, ".claude");
            }

            return Path.Combine(dir, "settings.json");
        }
    }

    public static string Glyph(int codepoint) => ((char)codepoint).ToString();

    private static bool IsStoreBuild()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.Store;
        }
        catch
        {
            return false; // not running with package identity
        }
    }
}

