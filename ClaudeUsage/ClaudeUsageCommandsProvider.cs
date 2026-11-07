using System;
using System.Globalization;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsage;

/// <summary>Formatting shared by the Dock labels and the details flyout.</summary>
internal static class UsageText
{
    /// <summary>After this long without an update, values are shown as approximate (~).</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);

    public static bool HasReset(UsageWindow w) => w.ResetsAt is { } r && r <= DateTimeOffset.Now;

    /// <summary>Once a window has reset, usage is 0 even if Claude Code hasn't reported it yet.</summary>
    public static double EffectivePercent(UsageWindow w) => HasReset(w) ? 0 : Math.Round(w.Percent);

    public static bool IsStale(UsageSnapshot snap)
    {
        if (DateTimeOffset.Now - snap.UpdatedAt < StaleAfter)
        {
            return false;
        }

        // A window that has fully reset since the last update is exactly known (0%), so only
        // windows that are still running can be out of date.
        return (snap.FiveHour is { } f && !HasReset(f)) || (snap.Weekly is { } w && !HasReset(w));
    }

    public static string ResetLine(UsageWindow w)
    {
        if (w.ResetsAt is not { } at)
        {
            return string.Empty;
        }

        if (HasReset(w))
        {
            return "Window has reset";
        }

        var local = at.ToLocalTime();
        var left = local - DateTimeOffset.Now;
        var when = local.Date == DateTime.Today
            ? local.ToString("t", CultureInfo.CurrentCulture)
            : local.ToString("ddd ", CultureInfo.CurrentCulture) + local.ToString("t", CultureInfo.CurrentCulture);
        return $"Resets {when} · in {Duration(left)}";
    }

    public static string Duration(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h" :
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" :
        $"{Math.Max(1, t.Minutes)}m";
}

internal sealed partial class ConnectCommand : InvokableCommand
{
    private readonly bool _connect;

    public ConnectCommand(bool connect)
    {
        _connect = connect;
        Id = connect ? AppInfo.ConnectId : AppInfo.DisconnectId;
        Name = connect ? "Connect" : "Disconnect";
        Icon = new IconInfo(AppInfo.Glyph(connect ? 0xE71B : 0xE8BB));
    }

    public override ICommandResult Invoke()
    {
        string message;
        try
        {
            message = _connect ? ClaudeSettingsConnector.Connect() : ClaudeSettingsConnector.Disconnect();
        }
        catch (Exception ex)
        {
            message = $"Could not update Claude Code settings: {ex.Message}";
        }

        return CommandResult.ShowToast(message);
    }
}

/// <summary>One Dock button: ring gauge + "5h 82%". Clicking opens the details flyout.</summary>
internal sealed partial class UsageItem : ListItem
{
    private readonly UsageService _service;
    private readonly bool _weekly;

    public UsageItem(UsageService service, bool weekly)
        : base(new UsageDetailsPage(service, weekly ? AppInfo.WeeklyItemId : AppInfo.FiveHourItemId))
    {
        _service = service;
        _weekly = weekly;
        Update();
        service.Changed += Update;
    }

    private void Update()
    {
        var label = _weekly ? "7d" : "5h";
        var snap = _service.Latest;
        var w = _weekly ? snap?.Weekly : snap?.FiveHour;

        if (w is null)
        {
            var setup = snap is null && !ClaudeSettingsConnector.IsConnected();
            Title = setup && !_weekly ? "Set up" : $"{label} --";
            Subtitle = setup ? "Connect to Claude Code" : "Waiting for Claude Code";
            Icon = new IconInfo(AppInfo.Glyph(setup ? 0xE7BA : 0xE9D9));
            return;
        }

        var pct = UsageText.EffectivePercent(w);
        var approx = UsageText.IsStale(snap!) && !UsageText.HasReset(w) ? "~" : string.Empty;
        Title = $"{label} {approx}{pct.ToString(CultureInfo.CurrentCulture)}%";
        Subtitle = UsageText.ResetLine(w);
        Icon = GaugeIcons.For(pct);
    }
}

public sealed partial class ClaudeUsageCommandsProvider : CommandProvider
{
    private readonly UsageService _service = new();
    private readonly ICommandItem[] _commands;
    private readonly ICommandItem _band;

    public ClaudeUsageCommandsProvider()
    {
        Id = AppInfo.ProviderId;
        DisplayName = AppInfo.DisplayName;
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        _band = new WrappedDockItem(
            [new UsageItem(_service, weekly: false), new UsageItem(_service, weekly: true)],
            AppInfo.BandId,
            AppInfo.DisplayName);

        _commands = [
            new CommandItem(new UsageDetailsPage(_service, AppInfo.DetailsId))
            {
                Title = "Claude Code usage",
                Subtitle = "5-hour and weekly limits",
            },
            new CommandItem(new ConnectCommand(connect: true))
            {
                Title = "Connect Usage Dock to Claude Code",
                Subtitle = "Adds the usage bridge to Claude Code's status line (keeps your existing one)",
            },
            new CommandItem(new ConnectCommand(connect: false))
            {
                Title = "Disconnect Usage Dock from Claude Code",
                Subtitle = "Restores your previous Claude Code status line",
            },
        ];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override ICommandItem[]? GetDockBands() => [_band];
}
