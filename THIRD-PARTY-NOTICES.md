# Third-party notices and asset licenses

## Runtime dependencies

Kite has **no third-party runtime packages**. It uses only parts of .NET 10 itself:

| Component | Version | License |
|---|---|---|
| .NET 10 runtime, WPF and Windows Forms (for the tray icon and monitor list) | 10.0 | MIT |

## Test-only packages

These are used only by `tests/Kite.Core.Tests` and are not shipped with the app.

| Package | Version | License |
|---|---|---|
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT |
| xunit | 2.9.3 | Apache-2.0 |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 |

## Assets

| Asset | Source | License |
|---|---|---|
| `src/Kite.App/Assets/kite.ico`, `kite.png` | Original artwork drawn for this project | MIT, same as the code |

## Fonts and symbols

The interface asks Windows for **Segoe UI Variable** and **Segoe Fluent Icons**, falling back to **Segoe UI** and **Segoe MDL2 Assets**. These fonts ship with Windows and are **not included** in this project, so no font license applies to the source or the build output.

App icons shown in results are read from the user's own Windows installation at runtime and are not distributed.
