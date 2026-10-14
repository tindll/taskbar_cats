# taskbar-cats

Pixel cats that live on your Windows taskbar, one cat per AI coding session. Busy sessions chase mice and
play; idle ones nap. Click a cat to jump to its conversation.

## Install

1. Download `TaskbarCats.App-win-Setup.exe` from the [latest release](https://github.com/tindll/taskbar_cats/releases/latest).
2. Run it. No admin rights needed. Windows SmartScreen may warn because the installer is not code-signed yet:
   click **More info**, then **Run anyway**.
3. The cats start at once, on the taskbar, one per open Claude Code conversation. It installs for your user only
   (`%LOCALAPPDATA%\TaskbarCats.App`), adds a Start-menu shortcut and turns on Start with Windows
   (tray icon, **Start with Windows** toggles it).
4. Using Codex, Copilot, Cursor or Gemini? Tray icon, **Coding tools**, **Install hooks**. Codex also needs a
   one-time `/hooks` trust inside Codex.

Portable instead: download `TaskbarCats-portable-needs-dotnet10.exe` from the same release and run it (needs the
.NET 10 Desktop Runtime; no Start-menu entry, no auto-update, Start with Windows stays off until you tick it).

Build from source (.NET 10 SDK, Windows 11):

```
git clone https://github.com/tindll/taskbar_cats.git
cd taskbar_cats
dotnet build
dotnet run --project src/TaskbarCats
```

## Features

- One cat per running session, on every monitor; cats come and go with your sessions.
- Moods from what the conversation is doing: reading, editing, running commands, waiting for you.
- Click a cat to focus its VS Code, Cursor or terminal window; right-click for its menu.
- Pick cats up by the scruff and throw them across monitors; they land on their feet.
- Cats wander between monitors on their own, and cats from the same project play tag.
- Hover a cat for a card: project, model, what it is doing, context fill. Optional chime when one finishes.
- Toys: a yarn ball you can throw, a teaser wand play mode, birds, mice and the cardboard box.
- Per-project looks saved between runs: ten coats, collar colours, personality, favourite toy, hide a cat.
- Tray icon (notification area) with Teaser wand, Hidden cats, Coding tools, Start with Windows,
  Check for updates, About and Exit.

## Supported tools

- Claude Code (VS Code extension and CLI): sessions and transcripts under `%USERPROFILE%\.claude`
  (or `CLAUDE_CONFIG_DIR`). Nothing to install.
- Codex, GitHub Copilot (CLI and VS Code agent mode), Cursor and Gemini CLI: through hooks that
  taskbar-cats adds to each tool's config (tray icon, **Coding tools**, **Install hooks**).

Updates: right-click the tray icon, **Check for updates**. If you installed hooks, an update rewrites their exe
path to `%LOCALAPPDATA%\TaskbarCats.App\current\TaskbarCats.exe`, which stays valid across updates.

## Uninstall

Settings, Apps, Installed apps, **taskbar-cats**, Uninstall. This removes the app, the Start-menu shortcut,
the Start-with-Windows entry and any coding-tool hooks taskbar-cats installed. Your cat preferences in
`%APPDATA%\TaskbarCats` are kept; delete that folder to remove them too.

## Privacy

taskbar-cats runs entirely on your PC.

**It reads** (read-only): Claude Code's session files `%USERPROFILE%\.claude\sessions\*.json` and transcripts
`%USERPROFILE%\.claude\projects\**\<session>.jsonl`; and, for the other tools, the small event files their hooks
write to `%LOCALAPPDATA%\TaskbarCats\events`. It keeps only tool names, counts, timestamps, the model id, status
flags and the working folder.

**It never reads or keeps** message text, tool inputs or outputs, file contents, file paths or commands.

**It never sends** anything over the network. The only network access is **Check for updates**, and only when you
click it (it asks GitHub for the latest release).

**It writes**: its preferences to `%APPDATA%\TaskbarCats\cats.json`; the hook event files above; the
Start-with-Windows value `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\TaskbarCats`; and, only if you choose
**Install hooks** (or run `TaskbarCats.exe hooks install`), hook entries in `~/.codex/hooks.json`,
`~/.copilot/hooks/taskbar-cats.json`, `~/.cursor/hooks.json` and `~/.gemini/settings.json` (only for tools that
are installed; a `.taskbar-cats.bak` backup is made of an existing file first). Feeding a sardine types one
message into that conversation's own window.

### Hooks

`TaskbarCats.exe hooks install` adds our entries to the config files above and leaves yours alone;
`hooks status` shows which tools have them; `hooks uninstall` removes them (the uninstaller does this for you).

## Development

`dotnet test TaskbarCats.slnx` runs the tests. Releases are built by `.github/workflows/release.yml` when a
`vX.Y.Z` tag matching `<Version>` in `src/TaskbarCats/TaskbarCats.csproj` is pushed.

## Licence

Not yet licensed.
