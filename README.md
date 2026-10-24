# Kite

A fast, keyboard-first quick launcher for Windows 11. Press **Alt+Space**, type, press **Enter**.

Kite opens apps (including Microsoft Store apps), does quick math, searches the web with short prefixes, opens your own bookmarks, and lets you create aliases like `projects` → your projects folder. It runs quietly in the notification area, needs no account or internet connection, and keeps all data on your PC.

---

## Why this stack

**C# on .NET 10 with WPF.**

- **Native and light.** WPF is part of .NET's Windows desktop runtime. No browser engine is bundled, unlike Electron. Memory use stays low while Kite waits in the tray.
- **Direct Windows integration.** The Win32 functions a launcher needs (`RegisterHotKey`, monitor and DPI information, the shell's app list and icons) are one documented call away.
- **Long-term support.** .NET 10 is the current LTS release.
- **No third-party runtime packages.** The tray icon uses Windows Forms' `NotifyIcon`, which ships with .NET.
- **Testable core.** All search logic lives in `Kite.Core`, a plain .NET library with no Windows dependency, so it can be unit-tested anywhere.

---

## Requirements

| | |
|---|---|
| OS | Windows 11 (Windows 10 version 2004 or later should also work) |
| To build | [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), version 10.0.100 or later |
| To run a published build | Nothing extra. The published build includes the runtime. |

---

## Setup, build, test and run (Windows 11)

Open **PowerShell** or **Windows Terminal** in the folder where you unzipped Kite.

```powershell
# 1. install the .net 10 sdk (skip if `dotnet --list-sdks` already shows 10.0.x)
winget install Microsoft.DotNet.SDK.10

# 2. close and reopen the terminal, then check it
dotnet --list-sdks

# 3. download test packages and build everything
dotnet restore
dotnet build -c Release

# 4. run the tests
dotnet test -c Release

# 5. start kite
dotnet run --project src/Kite.App -c Release
```

Kite starts in the notification area and shows a short "Kite is running" notice. Press **Alt+Space** to open it.

> If Alt+Space is already taken, for example by PowerToys Run, Kite tells you and offers to open settings so you can pick another shortcut.

### Using Visual Studio instead

Open `Kite.sln` in Visual Studio 2026 (or any Visual Studio version that supports .NET 10) with the **.NET desktop development** workload. Set **Kite.App** as the startup project and press F5. Run tests from **Test Explorer**.

---

## Creating a distributable build

The easiest way is the included script. It runs the tests, then builds a single `Kite.exe` that includes the .NET runtime:

```powershell
.\scripts\publish.ps1              # normal 64-bit pc
.\scripts\publish.ps1 -Arch arm64  # arm pc
```

Output:

- `publish\win-x64\Kite.exe`: copy this anywhere and run it.
- `publish\Kite-win-x64.zip`: the same, zipped for sharing.

If PowerShell blocks the script, run it once with `powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1`.

To run the same steps by hand:

```powershell
dotnet publish src/Kite.App/Kite.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o publish/win-x64
```

For a much smaller build that needs the **.NET 10 Desktop Runtime** installed on the target PC:

```powershell
dotnet publish src/Kite.App/Kite.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish/win-x64-small
```

**About SmartScreen:** the exe is not code-signed, so Windows may show "Windows protected your PC" the first time. Choose **More info → Run anyway**. Signing needs a paid certificate and is outside this project.

---

## Using Kite

| Type | What happens | Enter does |
|---|---|---|
| `note`, `notpad`, `vsc` | Finds apps, even with typos or initials | Opens the app |
| `25 * 4`, `(120 + 30) / 5`, `2^8` | Shows the answer instantly | Copies the answer |
| `=sqrt(2)` | Starting with `=` forces calculator mode | Copies the answer |
| `g best pizza` | Google search | Opens your browser |
| `gh wpf launcher` | GitHub search | Opens your browser |
| `ddg`, `yt`, `w` + words | DuckDuckGo, YouTube, Wikipedia | Opens your browser |
| A bookmark name | Your saved bookmarks | Opens the site |
| An alias, like `projects` | Your own shortcuts | Opens the folder, file, app or site |
| `kite`, `reload`, `clear history`, `quit` | Kite's own commands | Runs the command |
| Anything with no match | Offers a web search | Searches only if you choose it |

**Keys:** ↑/↓ move, PageUp/PageDown jump to the first or last result, **Enter** opens, **Esc** closes. Clicking a result also opens it. The launcher hides when you click elsewhere.

**Calculator:** supports `+ - * / % ^`, brackets, decimals, `× ÷`, `pi`, `e`, and `sqrt abs round floor ceil sin cos tan log ln`. It is a small custom parser, so it never runs code. Division by zero and broken expressions show a message instead of an answer.

**Web searches** are only sent when you open the result. Nothing you type is sent anywhere while you type.

### Settings

Right-click the tray icon → **Settings**, click the gear in the launcher, or type `kite`.

- **General:** shortcut, theme (Dark, Light, Match Windows), which monitor, number of results, animations, launch history, start with Windows, open on start, app list status, data folder.
- **Bookmarks:** add, edit and delete named web addresses.
- **Aliases:** a word that opens an app, folder, file or website. Targets are checked when you save and again when you open them. A moved or deleted target shows a clear message instead of failing silently.
- **Web search:** edit prefixes and addresses (use `{query}` where the words go), and choose the engine used when nothing matches.

Closing the settings window leaves Kite running. Use **Quit Kite** in the tray menu to exit. Starting Kite a second time just opens the existing launcher.

### Where your data lives

Everything is in `%AppData%\Kite`:

| File | Contents |
|---|---|
| `settings.json` | All settings |
| `bookmarks.json` | Your bookmarks |
| `aliases.json` | Your aliases |
| `history.json` | What you opened, used to rank results. Clear it in **Settings → General → Clear launch history** or by typing `clear history`. |
| `kite.log` | Errors, to help with troubleshooting |

If a file is missing, Kite uses defaults. If a file is damaged, Kite keeps a copy named like `settings.damaged-20260101-120000.json`, uses defaults, and tells you once.

---

## How apps are found, and Microsoft Store limitations

Kite looks in two places, once at startup, then again when the Start menu changes, when you choose **Reload app list**, or when the list is over 30 minutes old:

1. **Start menu shortcuts** (`.lnk`, `.url` and `.appref-ms`) for your account and for all users. Uninstallers, readmes and similar entries are skipped.
2. **The Windows apps folder** (`shell:AppsFolder`), read through the Windows shell. This is the same list as **Start → All apps**, so it includes Microsoft Store and other packaged apps.

Known limitations:

- **Store apps that are not in All apps can't be found.** Some packaged apps, such as background components and some games, do not list themselves there.
- **Store apps open through Explorer** (`explorer.exe shell:AppsFolder\<app id>`), which is the documented way to start them by ID. On a slow PC this can take a moment longer than starting a normal program.
- **Some packaged apps may show a plain symbol** instead of their real icon if Windows doesn't return one.
- **Duplicate names are merged.** If a Start menu shortcut and an apps folder entry have the same name, the shortcut is kept.
- **Desktop shortcuts and loose `.exe` files are not scanned.** Use an alias for those.
- **Full file search is not included yet.** See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#ideas-for-later) for how to add it as a provider.

---

## Safety

- Text in the search bar is **never run as a command**. There is no shell or command prompt mode.
- Results are opened with the Windows shell directly (`ShellExecute`), never through `cmd.exe` or PowerShell. Store apps are opened by passing the app ID as a single argument to `explorer.exe`.
- Only `http://` and `https://` addresses can be opened from bookmarks, aliases and search engines.
- Kite runs with normal user rights and does not ask for admin.

---

## Project guide

See **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** for a file-by-file guide, how a search flows, the scoring table, and a complete example of adding a new search provider.

```text
Kite.sln
├─ src/Kite.Core/          search engine, ranking, calculator, storage (no Windows code)
├─ src/Kite.App/           WPF app: windows, tray, hotkey, app discovery, icons
├─ tests/Kite.Core.Tests/  xUnit tests
├─ docs/ARCHITECTURE.md    file guide and how to add a search provider
└─ scripts/publish.ps1     builds a distributable exe
```

### Dependency versions

| What | Version |
|---|---|
| .NET SDK | 10.0.100 or later (pinned by `global.json`, rolls forward) |
| Target framework | `net10.0-windows` for the app, `net10.0` for the core library and tests |
| Runtime packages | None besides .NET itself |
| Microsoft.NET.Test.Sdk | 17.14.1 (tests only) |
| xunit | 2.9.3 (tests only) |
| xunit.runner.visualstudio | 3.1.4 (tests only) |

---

## What was tested, and what was not

This project was written in a **Linux** environment without access to Windows, and without access to NuGet. Here is exactly what was checked:

**Passed**

- `Kite.Core` builds with the .NET 10 SDK with no warnings.
- All **103 test cases** (search ranking, calculator parsing, settings persistence and repair, hotkey parsing, alias validation) were compiled and run, and all passed. Because NuGet was unreachable, they were run through a small stand-in for xUnit's `[Fact]`, `[Theory]` and `Assert` rather than the real xUnit runner. The test files themselves are unchanged and use standard xUnit.
- All of `Kite.App`'s C# code was **type-checked** against WPF's official reference API, compiled from the `dotnet/wpf` repository's `release/10.0` branch. A few Windows Forms types (tray icon, screen list) were checked against hand-written stand-ins.
- Every XAML element, attribute, attached property and style setter was checked against the same WPF reference types. All event handlers and named elements were matched to code.
- Every code comment was checked by script to be lowercase.

**Not verified. Please check on Windows**

- A real `dotnet build` of `Kite.App`, which includes WPF's XAML compiler, and a real `dotnet test` run with xUnit.
- Any behaviour at runtime: the global shortcut, tray icon, app discovery, icons, launching, monitor placement and display scaling, the Fluent look of the settings window, animations, and screen reader announcements.
- The published single-file exe.

If anything fails to build or behaves oddly, `%AppData%\Kite\kite.log` usually says why.

---

## Troubleshooting

| Problem | Fix |
|---|---|
| "Alt+Space is already used" | Another tool such as PowerToys Run uses it. Pick another shortcut in Settings, for example `Ctrl+Space` or `Alt+K`, or turn the other tool off. |
| An app is missing | Type `reload`, or choose **Settings → Reload app list**. If it is not in **Start → All apps**, add an alias for it. |
| `global.json` error about the SDK | Install the .NET 10 SDK, then reopen the terminal. |
| Nothing happens when starting Kite again | That is expected. A second copy opens the running launcher instead. |
| Settings look wrong after editing JSON by hand | Delete the file. Kite recreates it with defaults. |

---

## License

MIT. See [LICENSE](LICENSE). Asset and font notes are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
