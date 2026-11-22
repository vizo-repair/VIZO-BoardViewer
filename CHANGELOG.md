# Changelog

All notable changes to **VIZO BoardViewer** are documented in this file.

## [1.1.0] - 2026-11-22

### Added
- **19-Class Component Taxonomy & Offline Catalog**:
  - Added canonical 19-class `ComponentKind` system (`PowerIc`, `Ic`, `Transistor`, `Inductor`, `Transformer`, `Diode`, `Optocoupler`, `Fuse`, `Connector`, `Switch`, `Battery`, `Crystal`, `Led`, `Jumper`, `Capacitor`, `Resistor`, `TestPoint`, `Mechanical`, `Unknown`).
  - Embedded `boardview_prefix_catalog.json` (5,528 real `Prefix|PinsBucket` signatures from 1,175 boards) with 0 ms offline lookup in `BoardviewPrefixCatalog`.
  - Expanded pin-aware `ComponentRoleDeducer` (Asus `.fz` / Gigabyte `.tvw` composite prefixes, optocouplers, mechanical parts, strict passives) and automatic offline normalization after parsing.
- **Engineering Board Rendering & Highlighting**:
  - 19-class color-coded component outlines, fills, and RefDes watermarks (`ComponentKind.js`, `WireframeOverlayRenderer.js`).
  - Adaptive contact pads with pitch-aware sizing (`O(N log N)` min-pitch calculation), 35th-percentile reference SMD sizing, `Signal / GND / NC` color coding, and inner pin/net labels on zoom.
  - Gold square **Pin 1** indicators with corner key dots on zoom.
  - **Net Dimming** when a net is selected, keeping connected components bright while dimming unrelated elements.
  - Full **Hover Highlight** for the hovered component body and all of its pads.
- **Upgraded Net Inspector & Hover HUD**:
  - Real copper trace rendering (`tracks`) differentiated by active side, opposite side (dashed), and inner layers.
  - **Ratsnest** airwires (star from clicked pin or Prim's MST from search) for non-GND nets without track data.
  - **X-Ray Ghost** dashed purple rings and labels for opposite-side net nodes.
  - Redesigned **Hover HUD** with component class dot, formatted role name, compact BOM summary (`value / package / partNumber / description`), color-coded net badges (`GND`, `POWER`, `SIGNAL`, `NC`), and neighbor pins rendered cleanly above net overlays.
  - `ComponentKind`-powered Spotlight quick filters (`ICs & PWMs`, `Connectors`).
- **Canvas Navigation & UX**:
  - On-canvas zoom controls (`−`, live `%` readout, `+`, `Fit`) with double-click / double-tap reset and glide cancellation (`CameraNavigator.cancelGlide()`).
  - Net Inspector HUD occlusion awareness in `ViewportFocus.js` so fly-to framing stays centered above the bottom HUD.
  - Explicit `400 Bad Request` handling for unsupported formats (including native Cadence Allegro `.brd` projects).

## [1.0.1] - 2026-11-16

### Changed
- Hide console window on Windows (`WinExe`) and auto-exit when the browser tab closes.

## [1.0.0] - 2026-11-16

### Added
- Initial standalone open-source release of VIZO BoardViewer with `.fz`, `.bdv`, `.tvw`, `.xzz`, `.cad`, `.brd`, `.brd2`, `.bvr`, `.bvr3`, `.cst`, `.asc`, and `.xml` format support.
