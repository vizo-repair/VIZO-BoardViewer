# VIZO BoardViewer

Standalone, free, open-source **Boardview viewer** built with **ASP.NET Core (.NET 10)** and **HTML5 Canvas**.

- **Try Online:** [VIZO BoardViewer Online](https://vizo.repair/en/boardview-online) — instant browser-based viewer with no installation required.
- **Main Project:** Part of the [VIZO Repair Platform](https://vizo.repair/en) — schematics, boardviews, and repair diagnostics ecosystem.

---

## Quick Start (No .NET Required)

Download a single-file build for your OS from [**GitHub Releases**](../../releases):

| Platform | File | How to Run |
| --- | --- | --- |
| **Windows (x64)** | `VIZO.BoardViewer-win-x64.exe` | Double-click the `.exe` — it starts a local server and opens your default browser automatically. |
| **macOS (Apple Silicon)** | `VIZO.BoardViewer-osx-arm64.tar.gz` | Extract and run `./VIZO.BoardViewer` (on first launch, right-click → **Open** if Gatekeeper prompts). |
| **Linux (x64)** | `VIZO.BoardViewer-linux-x64.tar.gz` | Extract, `chmod +x VIZO.BoardViewer`, and run `./VIZO.BoardViewer`. |

### Optional CLI Flags

- `--lan` — bind to `0.0.0.0` so the viewer can be opened from other devices on your local Wi-Fi network (e.g., tablet or phone).
- `--no-browser` — start the server without automatically opening a browser window.
- `--urls http://127.0.0.1:5000` — specify a fixed port instead of an automatic free port.

---

## Supported Formats

`.fz` · `.bdv` · `.tvw` · `.xzz` · `.cad` · `.brd` · `.brd2` · `.bvr` · `.bvr3` · `.cst` · `.asc` (GenCAD) · `.xml` (Eagle)

---

## Features

- **Zero dependencies** — single self-contained executable with embedded frontend assets.
- **Fast HTML5 Canvas rendering** — smooth pan, zoom, and high-DPI board inspection.
- **Dual-side & net inspection** — flip sides, highlight nets, and locate components/pins.
- **Desktop & touch support** — mouse/keyboard shortcuts plus pinch-zoom and touch gestures.

---

## Running from Source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```bash
dotnet run
```

### Building a Single-File Executable

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Replace `win-x64` with `osx-arm64` or `linux-x64` as needed.

---

Built by [**VIZO**](https://vizo.repair/en).
