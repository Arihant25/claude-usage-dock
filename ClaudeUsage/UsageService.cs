using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace ClaudeUsage;

internal sealed record UsageWindow(double Percent, DateTimeOffset? ResetsAt);

internal sealed record UsageSnapshot(UsageWindow? FiveHour, UsageWindow? Weekly, DateTimeOffset UpdatedAt);

/// <summary>
/// Watches the snapshot file written by <see cref="StatusLineBridge"/>.
/// No network access and no credentials: the data comes from Claude Code's own status line input.
/// </summary>
internal sealed partial class UsageService : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly string _path = AppInfo.DataFile;
    private readonly Timer _timer;
    private readonly FileSystemWatcher? _watcher;
    private readonly Lock _lock = new();

    public UsageSnapshot? Latest { get; private set; }

    /// <summary>Raised on file change and on every tick (so countdowns update).</summary>
    public event Action? Changed;

    public UsageService()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(dir);
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(_path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => ScheduleRefresh();
            _watcher.Created += (_, _) => ScheduleRefresh();
            _watcher.Renamed += (_, _) => ScheduleRefresh();
        }
        catch
        {
            // The timer below still polls without the watcher.
        }

        _debounce = new Timer(_ => Refresh(), null, Timeout.Infinite, Timeout.Infinite);
        _timer = new Timer(_ => Refresh(), null, TimeSpan.Zero, TickInterval);
    }

    private readonly Timer _debounce;

    /// <summary>One bridge write raises several watcher events; coalesce them into one refresh.</summary>
    private void ScheduleRefresh()
    {
        try
        {
            _debounce.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Re-reads the file and notifies listeners. Everything runs under one lock so updates are
    /// applied in order and a slow, older read can never overwrite a newer one in the UI.
    /// </summary>
    public void Refresh()
    {
        lock (_lock)
        {
            try
            {
                Latest = Read();
            }
            catch (FileNotFoundException)
            {
                Latest = null;
            }
            catch (DirectoryNotFoundException)
            {
                Latest = null;
            }
            catch
            {
                // Keep the last good snapshot.
            }

            // Handlers push property changes across COM to Command Palette. If the host has gone
            // away that throws, and an unhandled exception on a timer thread would kill the process.
            if (Changed is { } changed)
            {
                foreach (var handler in changed.GetInvocationList())
                {
                    try
                    {
                        ((Action)handler)();
                    }
                    catch
                    {
                    }
                }
            }
        }
    }

    private UsageSnapshot Read()
    {
        using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var doc = JsonDocument.Parse(fs);
        var root = doc.RootElement;
        var updated = root.TryGetProperty("updated", out var u) && u.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(u.GetInt64())
            : DateTimeOffset.Now;
        root.TryGetProperty("rate_limits", out var rl);
        return new UsageSnapshot(ParseWindow(rl, "five_hour"), ParseWindow(rl, "seven_day"), updated);
    }

    private static UsageWindow? ParseWindow(JsonElement rl, string name)
    {
        if (rl.ValueKind != JsonValueKind.Object || !rl.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var pct = w.TryGetProperty("used_percentage", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : 0;
        DateTimeOffset? resets = w.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds((long)r.GetDouble())
            : null;
        return new UsageWindow(pct, resets);
    }

    public void Dispose()
    {
        _timer.Dispose();
        _debounce.Dispose();
        _watcher?.Dispose();
    }
}
