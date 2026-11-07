# Privacy Policy

_Last updated: November 7, 2026_

Usage Dock for Claude Code does not collect, store, transmit or share any personal information.

## What the extension reads and writes

All of this stays on your PC:

- **Claude Code status line data.** Claude Code passes a JSON payload to your status line command. The bridge (`claude-usage-bridge.exe`) reads the `rate_limits` fields from it (usage percentages and reset times) and writes them to `%USERPROFILE%\.claude-usage-dock\usage.json`. Everything else in the payload is passed through to your own status line command unchanged and is not saved.
- **Claude Code settings.** When you choose Connect or Disconnect, the extension edits the `statusLine` entry in your Claude Code `settings.json` and saves a backup next to it as `settings.json.usage-dock.bak`.
- **Icons.** Gauge images are generated into `%USERPROFILE%\.claude-usage-dock\icons\`.

## What it does not do

- It makes no network requests. The package doesn't declare internet access.
- It never reads your Claude credentials, conversations or code.
- It has no analytics, telemetry, ads or third-party services.

The "Open usage on claude.ai" button opens that page in your default browser. What happens there is covered by Anthropic's privacy policy.

## Removing your data

Run **Disconnect Usage Dock from Claude Code** (or `claude-usage-bridge.exe --disconnect`), uninstall the app, then delete the `%USERPROFILE%\.claude-usage-dock` folder.

## Contact

Open an issue at https://github.com/Arihant25/claude-usage-dock/issues.
