using System;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ClaudeUsage;

/// <summary>
/// Flyout shown when a Dock item is clicked: both windows with bars, exact reset times,
/// data freshness, and setup help when the bridge isn't connected yet.
/// </summary>
internal sealed partial class UsageDetailsPage : ContentPage
{
    private readonly UsageService _service;
    private readonly DetailsForm _form;

    public UsageDetailsPage(UsageService service, string id)
    {
        _service = service;
        Id = id;
        Name = "Details";
        Title = "Claude Code usage";
        Icon = IconHelpers.FromRelativePath(@"Assets\StoreLogo.png");
        _form = new DetailsForm(service);
        Commands =
        [
            new CommandContextItem(new OpenUrlCommand("https://claude.ai/settings/usage")) { Title = "Open usage on claude.ai" },
            new CommandContextItem(new ConnectCommand(connect: false)) { Title = "Disconnect from Claude Code" },
        ];
        _service.Changed += () => _form.Refresh();
        _form.Refresh();
    }

    public override IContent[] GetContent() => [_form];

    private sealed partial class DetailsForm : FormContent
    {
        private readonly UsageService _service;

        public DetailsForm(UsageService service)
        {
            _service = service;
            TemplateJson = Template;
        }

        public void Refresh() => DataJson = BuildData(_service.Latest).ToJsonString();

        public override ICommandResult SubmitForm(string inputs, string data)
        {
            string? action = null;
            try
            {
                action = JsonNode.Parse(string.IsNullOrEmpty(data) ? "{}" : data)?["action"]?.GetValue<string>();
            }
            catch
            {
            }

            if (action == "connect")
            {
                string message;
                try
                {
                    message = ClaudeSettingsConnector.Connect();
                }
                catch (Exception ex)
                {
                    message = $"Could not update Claude Code settings: {ex.Message}";
                }

                Refresh();
                return CommandResult.ShowToast(message);
            }

            return CommandResult.KeepOpen();
        }
    }

    private static JsonObject BuildData(UsageSnapshot? snap)
    {
        var connected = ClaudeSettingsConnector.IsConnected();
        var hasData = snap is not null && (snap.FiveHour is not null || snap.Weekly is not null);
        var data = new JsonObject
        {
            ["hasData"] = hasData,
            ["needsSetup"] = !hasData && !connected,
            ["waiting"] = !hasData && connected,
            ["fiveHour"] = Window(snap?.FiveHour),
            ["weekly"] = Window(snap?.Weekly),
        };

        if (snap is not null)
        {
            var age = DateTimeOffset.Now - snap.UpdatedAt;
            data["updated"] = $"Updated {Ago(age)} from Claude Code";
            data["stale"] = UsageText.IsStale(snap);
        }

        return data;
    }

    private static JsonObject? Window(UsageWindow? w)
    {
        if (w is null)
        {
            return null;
        }

        var pct = UsageText.EffectivePercent(w);
        return new JsonObject
        {
            ["percent"] = $"{pct.ToString(CultureInfo.CurrentCulture)}%",
            ["bar"] = Bar(pct),
            ["resets"] = UsageText.ResetLine(w),
        };
    }

    private static string Bar(double percent)
    {
        const int width = 1000;
        var filled = Math.Clamp(percent, 0, 100) / 100 * width;
        var color = GaugeIcons.AccentColor(GaugeIcons.SeverityOf(percent));
        var inv = CultureInfo.InvariantCulture;
        var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='8' viewBox='0 0 {width} 8' preserveAspectRatio='none'>" +
                  $"<rect width='{width}' height='8' rx='4' fill='rgb(128,128,128)' fill-opacity='0.25'/>" +
                  (filled > 0 ? $"<rect width='{Math.Max(8, filled).ToString("F1", inv)}' height='8' rx='4' fill='{color}'/>" : string.Empty) +
                  "</svg>";
        return "data:image/svg+xml;utf8," + svg;
    }

    private static string Ago(TimeSpan t) =>
        t.TotalMinutes < 1 ? "just now" :
        t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min ago" :
        t.TotalDays < 1 ? $"{(int)t.TotalHours}h {t.Minutes}m ago" :
        $"{(int)t.TotalDays}d ago";

    private const string Template = """
    {
      "type": "AdaptiveCard",
      "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
      "version": "1.5",
      "body": [
        {
          "type": "Container",
          "$when": "${needsSetup}",
          "items": [
            { "type": "TextBlock", "text": "Not connected to Claude Code", "weight": "bolder", "size": "medium" },
            { "type": "TextBlock", "wrap": true, "isSubtle": true,
              "text": "Usage Dock reads your limits from Claude Code's status line. Connecting adds a small bridge in front of your existing status line command in ~/.claude/settings.json (or $CLAUDE_CONFIG_DIR). Your current status line keeps working. Disconnect before uninstalling." }
          ]
        },
        {
          "type": "Container",
          "$when": "${waiting}",
          "items": [
            { "type": "TextBlock", "text": "Waiting for Claude Code", "weight": "bolder", "size": "medium" },
            { "type": "TextBlock", "wrap": true, "isSubtle": true,
              "text": "Connected. Usage appears after your next Claude Code response. Limits are only reported for Claude Pro and Max subscriptions." }
          ]
        },
        {
          "type": "Container",
          "$when": "${fiveHour != null}",
          "spacing": "medium",
          "items": [
            { "type": "ColumnSet", "columns": [
              { "type": "Column", "width": "stretch", "verticalContentAlignment": "bottom", "items": [
                { "type": "TextBlock", "text": "Current session", "weight": "bolder" },
                { "type": "TextBlock", "text": "5-hour window", "isSubtle": true, "size": "small", "spacing": "none" } ] },
              { "type": "Column", "width": "auto", "items": [
                { "type": "TextBlock", "text": "${fiveHour.percent}", "size": "extraLarge", "weight": "bolder" } ] } ] },
            { "type": "Image", "url": "${fiveHour.bar}", "size": "stretch", "height": "8px", "spacing": "small" },
            { "type": "TextBlock", "text": "${fiveHour.resets}", "isSubtle": true, "size": "small", "spacing": "small" }
          ]
        },
        {
          "type": "Container",
          "$when": "${weekly != null}",
          "spacing": "large",
          "items": [
            { "type": "ColumnSet", "columns": [
              { "type": "Column", "width": "stretch", "verticalContentAlignment": "bottom", "items": [
                { "type": "TextBlock", "text": "Weekly", "weight": "bolder" },
                { "type": "TextBlock", "text": "7-day window, all models", "isSubtle": true, "size": "small", "spacing": "none" } ] },
              { "type": "Column", "width": "auto", "items": [
                { "type": "TextBlock", "text": "${weekly.percent}", "size": "extraLarge", "weight": "bolder" } ] } ] },
            { "type": "Image", "url": "${weekly.bar}", "size": "stretch", "height": "8px", "spacing": "small" },
            { "type": "TextBlock", "text": "${weekly.resets}", "isSubtle": true, "size": "small", "spacing": "small" }
          ]
        },
        {
          "type": "Container",
          "$when": "${stale}",
          "style": "warning",
          "spacing": "medium",
          "items": [
            { "type": "TextBlock", "wrap": true, "size": "small",
              "text": "These numbers may be out of date (shown with ~ in the Dock). Usage from claude.ai or the desktop app counts toward the same limits but only shows up here after your next Claude Code response." }
          ]
        },
        { "type": "TextBlock", "$when": "${updated != null}", "text": "${updated}", "isSubtle": true, "size": "small", "spacing": "medium" }
      ],
      "actions": [
        { "type": "Action.Submit", "$when": "${needsSetup}", "title": "Connect to Claude Code", "data": { "action": "connect" } },
        { "type": "Action.OpenUrl", "$when": "${hasData}", "title": "Open usage on claude.ai", "url": "https://claude.ai/settings/usage" }
      ]
    }
    """;
}
