using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeUsage;

/// <summary>
/// Hooks the bridge into Claude Code's <c>statusLine</c> setting without replacing the
/// user's own status line: an existing command <c>X</c> becomes <c>bridge | X</c>.
/// </summary>
internal static class ClaudeSettingsConnector
{
    private const string PipePrefix = AppInfo.BridgeAlias + " | ";
    private const string PrintCommand = AppInfo.BridgeAlias + " --print";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,

        // Keep the user's file readable: don't turn && into && or escape non-ASCII text.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static bool IsConnected()
    {
        try
        {
            return GetCommand(Load(out _)) is { } cmd && cmd.Contains(AppInfo.BridgeAlias, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static string Connect()
    {
        var root = Load(out var existed);
        var current = GetCommand(root);
        var file = AppInfo.ClaudeSettingsFile;

        if (current is not null && current.Contains(AppInfo.BridgeAlias, StringComparison.OrdinalIgnoreCase))
        {
            return $"Already connected ({file}).";
        }

        if (root["statusLine"] is not JsonObject statusLine)
        {
            statusLine = new JsonObject();
            root["statusLine"] = statusLine;
        }

        statusLine["type"] = "command";
        statusLine["command"] = string.IsNullOrWhiteSpace(current) ? PrintCommand : PipePrefix + Group(current);

        Save(root, existed);
        return $"Connected via {file}. Usage appears after your next Claude Code response. Disconnect before uninstalling.";
    }

    public static string Disconnect()
    {
        var root = Load(out var existed);
        var current = GetCommand(root);

        if (current is null || !current.Contains(AppInfo.BridgeAlias, StringComparison.OrdinalIgnoreCase))
        {
            return "Not connected.";
        }

        var statusLine = (JsonObject)root["statusLine"]!;
        if (current.StartsWith(PipePrefix, StringComparison.OrdinalIgnoreCase))
        {
            statusLine["command"] = Ungroup(current[PipePrefix.Length..]);
        }
        else if (current.Trim().Equals(PrintCommand, StringComparison.OrdinalIgnoreCase))
        {
            statusLine.Remove("command");
            statusLine.Remove("type");
            if (statusLine.Count == 0)
            {
                root.Remove("statusLine");
            }
        }
        else
        {
            return $"The status line was edited by hand. Remove {AppInfo.BridgeAlias} from it in {AppInfo.ClaudeSettingsFile}.";
        }

        Save(root, existed);
        return "Disconnected from Claude Code.";
    }

    /// <summary>
    /// <c>|</c> binds tighter than <c>&amp;&amp;</c>, <c>||</c> and <c>;</c> in both bash and cmd, so a compound
    /// command must be grouped or only its first part would receive the JSON. Simple commands are
    /// left alone to keep the setting readable and to stay safe in shells where ( ) means something else.
    /// </summary>
    private static string Group(string command)
    {
        var needsGroup = command.Contains("&&", StringComparison.Ordinal) || command.Contains("||", StringComparison.Ordinal) ||
                         command.Contains(';', StringComparison.Ordinal) || command.Contains('\n', StringComparison.Ordinal) ||
                         (command.Contains('&', StringComparison.Ordinal) && !command.Contains("&&", StringComparison.Ordinal));
        return needsGroup ? $"({command})" : command;
    }

    private static string Ungroup(string command) =>
        command.Length >= 2 && command[0] == '(' && command[^1] == ')' ? command[1..^1] : command;

    private static string? GetCommand(JsonObject root) =>
        root["statusLine"] is JsonObject sl && sl["command"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>The file to actually write: dotfiles users often symlink settings.json.</summary>
    private static string ResolvePath()
    {
        var path = AppInfo.ClaudeSettingsFile;
        try
        {
            var target = new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null)
            {
                return target.FullName;
            }
        }
        catch
        {
        }

        return path;
    }

    private static JsonObject Load(out bool existed)
    {
        var path = ResolvePath();
        existed = File.Exists(path);
        if (!existed)
        {
            return new JsonObject();
        }

        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new JsonObject();
        }

        return JsonNode.Parse(text, documentOptions: ReadOptions) as JsonObject
            ?? throw new InvalidOperationException("settings.json is not a JSON object");
    }

    private static void Save(JsonObject root, bool existed)
    {
        var path = ResolvePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (existed)
        {
            File.Copy(path, path + ".usage-dock.bak", overwrite: true);
        }

        var tmp = path + ".usage-dock.tmp";
        try
        {
            File.WriteAllText(tmp, root.ToJsonString(WriteOptions) + Environment.NewLine);
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
}
