using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ClaudeUsage;

/// <summary>
/// Runs when Claude Code invokes the exe as (part of) its status line command.
/// Claude Code sends a JSON payload on stdin whose documented <c>rate_limits</c> field holds
/// the 5-hour and 7-day usage (https://code.claude.com/docs/en/statusline).
///
///   claude-usage-bridge.exe | your-statusline-cmd   -> saves usage, passes stdin through unchanged
///   claude-usage-bridge.exe --print                 -> saves usage, prints a short status line itself
///
/// It must never break the user's status line, so every failure is swallowed and stdin is
/// always forwarded byte for byte.
/// </summary>
internal static class StatusLineBridge
{
    public static int Run(string[] args)
    {
        byte[] input;
        try
        {
            using var stdin = Console.OpenStandardInput();
            using var ms = new MemoryStream();
            stdin.CopyTo(ms);
            input = ms.ToArray();
        }
        catch
        {
            return 0;
        }

        var print = Array.Exists(args, a => a is "--print" or "-p");
        string? summary = null;
        JsonDocument? doc = null;

        try
        {
            doc = JsonDocument.Parse(input);
            if (doc.RootElement.TryGetProperty("rate_limits", out var rl) && rl.ValueKind == JsonValueKind.Object)
            {
                summary = Summarize(rl);
                try
                {
                    Save(rl);
                }
                catch
                {
                    // Never let a locked or unwritable file affect the status line.
                }
            }
        }
        catch
        {
            // Not JSON or no rate limits (API-key users, before the first response): nothing to save.
        }
        finally
        {
            doc?.Dispose();
        }

        try
        {
            using var stdout = Console.OpenStandardOutput();
            var output = print ? Encoding.UTF8.GetBytes((summary ?? string.Empty) + "\n") : input;
            stdout.Write(output, 0, output.Length);
            stdout.Flush();
        }
        catch
        {
        }

        return 0;
    }

    private static void Save(JsonElement incoming)
    {
        var path = AppInfo.DataFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Several Claude Code sessions can be open at once, and an idle one re-renders its status
        // line with the numbers it last saw. Merge so an older snapshot never overwrites a newer one.
        JsonDocument? existingDoc = null;
        try
        {
            if (File.Exists(path))
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                existingDoc = JsonDocument.Parse(fs);
            }
        }
        catch
        {
            existingDoc = null;
        }

        try
        {
            JsonElement existing = default;
            existingDoc?.RootElement.TryGetProperty("rate_limits", out existing);

            using var buffer = new MemoryStream();
            using (var w = new Utf8JsonWriter(buffer))
            {
                w.WriteStartObject();
                w.WriteNumber("updated", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                w.WritePropertyName("rate_limits");
                w.WriteStartObject();
                foreach (var prop in incoming.EnumerateObject())
                {
                    w.WritePropertyName(prop.Name);
                    PickNewer(prop.Value, existing, prop.Name).WriteTo(w);
                }

                w.WriteEndObject();
                w.WriteEndObject();
            }

            // Write-then-rename so the extension never reads a half-written file.
            var tmp = path + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";
            try
            {
                File.WriteAllBytes(tmp, buffer.ToArray());
                File.Move(tmp, path, overwrite: true);
            }
            finally
            {
                try
                {
                    File.Delete(tmp);
                }
                catch
                {
                }
            }
        }
        finally
        {
            existingDoc?.Dispose();
        }
    }

    /// <summary>
    /// Within one window usage only goes up, and a later window has a later <c>resets_at</c>.
    /// So the saved value wins if it belongs to a later window, or to the same window with more usage.
    /// </summary>
    private static JsonElement PickNewer(JsonElement incoming, JsonElement existingLimits, string name)
    {
        if (existingLimits.ValueKind != JsonValueKind.Object ||
            !existingLimits.TryGetProperty(name, out var old) || old.ValueKind != JsonValueKind.Object ||
            incoming.ValueKind != JsonValueKind.Object)
        {
            return incoming;
        }

        var inReset = Number(incoming, "resets_at");
        var oldReset = Number(old, "resets_at");
        if (inReset is null || oldReset is null)
        {
            return incoming;
        }

        // resets_at can jitter by a second between responses; treat that as the same window.
        if (oldReset > inReset + 60)
        {
            return old;
        }

        if (Math.Abs(oldReset.Value - inReset.Value) <= 60 &&
            (Number(old, "used_percentage") ?? 0) > (Number(incoming, "used_percentage") ?? 0))
        {
            return old;
        }

        return incoming;
    }

    private static double? Number(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static string Summarize(JsonElement rl)
    {
        static string Part(JsonElement rl, string key, string label) =>
            rl.TryGetProperty(key, out var w) && w.ValueKind == JsonValueKind.Object &&
            w.TryGetProperty("used_percentage", out var p) && p.ValueKind == JsonValueKind.Number
                ? $"{label} {Math.Round(p.GetDouble()).ToString(CultureInfo.InvariantCulture)}%"
                : string.Empty;

        var five = Part(rl, "five_hour", "5h");
        var week = Part(rl, "seven_day", "7d");
        return five.Length > 0 && week.Length > 0 ? $"{five} | {week}" : five + week;
    }
}
