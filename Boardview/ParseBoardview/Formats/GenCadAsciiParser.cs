// ============================================================================
// FILE: Features/Boardview/ParseBoardview/Formats/GenCadAsciiParser.cs
// ============================================================================
namespace VIZO.BoardViewer.Boardview.ParseBoardview.Formats;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using VIZO.BoardViewer.Boardview.ParseBoardview.Services;

public class GenCadAsciiParser : IBoardviewFormatParser
{
    private readonly ComponentRoleDeducer _roleDeducer;
    private readonly QuadrantAnchorScorer _anchorScorer;

    public GenCadAsciiParser(ComponentRoleDeducer roleDeducer, QuadrantAnchorScorer anchorScorer)
    {
        _roleDeducer = roleDeducer;
        _anchorScorer = anchorScorer;
    }

    public string FormatName => "GenCAD 1.4 / Fabmaster ASCII";

    public bool CanParse(ReadOnlySpan<byte> headerBytes, string fileName)
    {
        if (headerBytes.Length < 4) return false;
        string preview = Encoding.Latin1.GetString(headerBytes[..Math.Min(headerBytes.Length, 4096)]);

        return preview.Contains("GENCAD", StringComparison.OrdinalIgnoreCase) ||
               preview.Contains("$HEADER", StringComparison.OrdinalIgnoreCase) ||
               preview.Contains("$SHAPES", StringComparison.OrdinalIgnoreCase) ||
               preview.Contains("$COMPONENTS", StringComparison.OrdinalIgnoreCase) ||
               preview.Contains("$SIGNALS", StringComparison.OrdinalIgnoreCase) ||
               preview.Contains("$PINS", StringComparison.OrdinalIgnoreCase) ||
               preview.Contains("<<", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".asc", StringComparison.OrdinalIgnoreCase);
    }

    public BoardviewNetlistDto? Parse(byte[] fileBytes, string fileName, Action<string>? onLog = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string rawText = Encoding.Latin1.GetString(fileBytes).TrimStart('\uFEFF');

        if (rawText.Contains("<<", StringComparison.OrdinalIgnoreCase))
        {
            onLog?.Invoke("📄 [GenCadParser] Parsing Fabmaster ASCII format (<<...>>).");
            return ParseAsciiText(rawText, fileName, onLog, ct);
        }

        onLog?.Invoke("📄 [GenCadParser] Parsing GenCAD 1.4 format (OpenBoardView GenCADFile)...");
        return ParseGenCad14(rawText, fileName, onLog, ct);
    }

    // =========================================================================
    // 1. ORIGINAL COMPAL BDV / FABMASTER ASCII PARSER (<<...>>)
    // =========================================================================
    public BoardviewNetlistDto ParseAsciiText(string text, string fileName, Action<string>? onLog = null, CancellationToken ct = default)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        var outlinePoints = new List<BoardviewPointDto>();
        var testPoints = new List<BoardviewTestPointDto>();
        var components = new Dictionary<string, ComponentBuilder>(StringComparer.OrdinalIgnoreCase);
        var nets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        using var reader = new StringReader(text);
        string? line;
        string currentSection = "";
        string currentPartName = "";
        string currentPartSide = "TOP";
        string? currentPartDesc = null;

        var ignoredHeaderTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Pins", "Nails", "Format", "T/B", "Grid", "Layer", "Net", "Name", "Header" };

        int lineCount = 0;
        while ((line = reader.ReadLine()) != null)
        {
            if ((++lineCount & 4095) == 0) ct.ThrowIfCancellationRequested();
            string trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            if (trimmed.StartsWith("<<", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(">>", StringComparison.OrdinalIgnoreCase))
            {
                currentSection = trimmed.ToLowerInvariant();
                continue;
            }

            if (currentSection == "<<format.asc>>")
            {
                var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 &&
                    double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double bx) &&
                    double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double by))
                {
                    minX = Math.Min(minX, bx); maxX = Math.Max(maxX, bx);
                    minY = Math.Min(minY, by); maxY = Math.Max(maxY, by);
                    outlinePoints.Add(new BoardviewPointDto(Math.Round(bx, 4), Math.Round(by, 4)));
                }
            }
            else if (currentSection == "<<nails.asc>>")
            {
                var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 7 && parts[0].StartsWith("$"))
                {
                    if (double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double nx) &&
                        double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double ny))
                    {
                        string side = parts.Contains("(B)") ? "BOTTOM" : "TOP";
                        string netName = "";
                        int netIdx = -1;

                        for (int i = 0; i < parts.Length; i++)
                        {
                            if (parts[i].StartsWith("#") && i + 1 < parts.Length)
                            {
                                netName = parts[i + 1];
                                netIdx = i + 1;
                                break;
                            }
                        }

                        if (!string.IsNullOrEmpty(netName))
                        {
                            bool isVia = trimmed.Contains("VIA", StringComparison.OrdinalIgnoreCase);
                            string? probeLabel = (netIdx >= 0 && netIdx + 1 < parts.Length) ? parts.Skip(netIdx + 1).FirstOrDefault(p => p != "V" && p != "VIA" && p != "PIN" && p != ".") : null;

                            testPoints.Add(new BoardviewTestPointDto(
                                Nail: parts[0],
                                Net: netName,
                                Side: side,
                                X: Math.Round(nx, 4),
                                Y: Math.Round(ny, 4),
                                IsVia: isVia,
                                ProbeLabel: probeLabel,
                                PointType: isVia ? "VIA" : (trimmed.Contains("PIN", StringComparison.OrdinalIgnoreCase) ? "PIN" : "TEST_POINT")
                            ));
                        }
                    }
                }
            }
            else if (currentSection == "<<pins.asc>>")
            {
                if (trimmed.StartsWith("Part ", StringComparison.OrdinalIgnoreCase))
                {
                    var pParts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (pParts.Length >= 2 && !ignoredHeaderTokens.Contains(pParts[1].Trim()))
                    {
                        currentPartName = pParts[1].Trim();
                        currentPartSide = (pParts.Length >= 3 && pParts[2].Contains("(B)")) ? "BOTTOM" : "TOP";
                        currentPartDesc = pParts.Length >= 4 ? string.Join(" ", pParts.Skip(3)).Trim() : null;

                        if (!components.ContainsKey(currentPartName))
                        {
                            components[currentPartName] = new ComponentBuilder
                            {
                                RefDes = currentPartName,
                                Side = currentPartSide,
                                Type = _roleDeducer.DeduceRole(currentPartName, ""),
                                Description = currentPartDesc,
                                Value = currentPartDesc
                            };
                        }
                        else
                        {
                            components[currentPartName].Side = currentPartSide;
                            if (!string.IsNullOrEmpty(currentPartDesc))
                            {
                                components[currentPartName].Description = currentPartDesc;
                                components[currentPartName].Value = currentPartDesc;
                            }
                        }
                    }
                    else currentPartName = "";
                }
                else if (!string.IsNullOrEmpty(currentPartName) && components.TryGetValue(currentPartName, out var comp))
                {
                    var pinParts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (pinParts.Length >= 6 &&
                        double.TryParse(pinParts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double px) &&
                        double.TryParse(pinParts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out double py))
                    {
                        string effectivePinKey = !string.IsNullOrWhiteSpace(pinParts[1]) ? pinParts[1] : pinParts[0];
                        string pinLayer = pinParts[4] switch { "0" => "BOTH", "2" => "BOTTOM", _ => "TOP" };

                        comp.Pins.Add(new PinCoord(effectivePinKey, pinParts[1], pinParts[5], px, py, pinLayer));

                        minX = Math.Min(minX, px); maxX = Math.Max(maxX, px);
                        minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);

                        if (!nets.ContainsKey(pinParts[5])) nets[pinParts[5]] = new List<string>();
                        nets[pinParts[5]].Add($"{currentPartName}.{effectivePinKey}");
                    }
                }
            }
        }

        

        var finalizedComponents = new Dictionary<string, BoardviewComponentDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in components.Where(c => c.Value.Pins.Count > 0 && !ignoredHeaderTokens.Contains(c.Key)))
        {
            var c = kvp.Value;

            double cMinX = c.Pins.Min(p => p.X);
            double cMaxX = c.Pins.Max(p => p.X);
            double cMinY = c.Pins.Min(p => p.Y);
            double cMaxY = c.Pins.Max(p => p.Y);

            double realCenterX = (cMinX + cMaxX) / 2.0;
            double realCenterY = (cMinY + cMaxY) / 2.0;

            double cWidth = Math.Max(0.045, Math.Round(cMaxX - cMinX, 4));
            double cHeight = Math.Max(0.045, Math.Round(cMaxY - cMinY, 4));

            if (Math.Abs(cMaxX - cMinX) < 0.02) cWidth = 0.06;
            if (Math.Abs(cMaxY - cMinY) < 0.02) cHeight = 0.06;

            var pinsDict = c.Pins.GroupBy(p => p.PinNum, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => new BoardviewPinDto(g.First().PinName, g.First().Net, Math.Round(g.First().X, 4), Math.Round(g.First().Y, 4), g.First().Layer),
                    StringComparer.OrdinalIgnoreCase
                );

            finalizedComponents[kvp.Key] = new BoardviewComponentDto(
                X: Math.Round(realCenterX, 4),
                Y: Math.Round(realCenterY, 4),
                MinX: Math.Round(realCenterX - (cWidth / 2.0), 4),
                MinY: Math.Round(realCenterY - (cHeight / 2.0), 4),
                MaxX: Math.Round(realCenterX + (cWidth / 2.0), 4),
                MaxY: Math.Round(realCenterY + (cHeight / 2.0), 4),
                Width: cWidth,
                Height: cHeight,
                Side: c.Side,
                PinsCount: c.Pins.Count,
                Type: c.Type,
                Pins: pinsDict,
                Description: c.Description,
                Value: c.Value
            );
        }

        var bounds = new BoardviewBoundsDto(
            minX != double.MaxValue ? Math.Round(minX, 4) : 0, minY != double.MaxValue ? Math.Round(minY, 4) : 0,
            maxX != double.MinValue ? Math.Round(maxX, 4) : 0, maxY != double.MinValue ? Math.Round(maxY, 4) : 0,
            maxX != double.MinValue && minX != double.MaxValue ? Math.Round(maxX - minX, 4) : 0,
            maxY != double.MinValue && minY != double.MaxValue ? Math.Round(maxY - minY, 4) : 0, "INCH", outlinePoints.Count > 0 ? outlinePoints : null);

        var bridges = ExtractBridges(components);
        var stats = new BoardviewStatsDto(finalizedComponents.Count, nets.Count, bridges.Count, testPoints.Count(tp => tp.IsVia), testPoints.Count);
        var anchorCandidates = _anchorScorer.SelectAnchorCandidates(bounds, finalizedComponents);

        return new BoardviewNetlistDto(Path.GetFileNameWithoutExtension(fileName), "GenCAD / Fabmaster BDV", bounds, stats, finalizedComponents, anchorCandidates, nets, bridges, testPoints);
    }

    private static List<BoardviewBridgeDto> ExtractBridges(Dictionary<string, ComponentBuilder> components)
    {
        var bridges = new List<BoardviewBridgeDto>();
        foreach (var c in components.Values)
        {
            if (c.Pins.Count == 2 && IsValidBridge(c.Pins[0].Net, c.Pins[1].Net))
            {
                bridges.Add(new BoardviewBridgeDto(c.RefDes, c.Type, c.Pins[0].Net, c.Pins[1].Net, c.Pins[0].PinNum, c.Pins[1].PinNum, c.Side));
            }
        }
        return bridges;
    }

    private static bool IsValidBridge(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && !a.Equals(b, StringComparison.OrdinalIgnoreCase) && !a.StartsWith("NC_");

    // =========================================================================
    // 2. FULL GENCAD 1.4 PARSER FOR CAMCAD / TERADYNE ($HEADER / $SHAPES)
    // =========================================================================
    private BoardviewNetlistDto? ParseGenCad14(string text, string fileName, Action<string>? onLog, CancellationToken ct)
    {
        var shapes = new Dictionary<string, ShapeDef>(StringComparer.OrdinalIgnoreCase);
        var components = new List<ComponentDef>();
        var signalsCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var outlineSegments = new List<(BoardviewPointDto P1, BoardviewPointDto P2)>();
        var vias = new List<BoardviewTestPointDto>();

        string unitMode = "THOU";
        double userUnitVal = 1000.0;

        string currentSection = "";
        ShapeDef? curShape = null;
        ComponentDef? curComp = null;
        string curSignal = "";
        string curRoute = "";

        using var reader = new StringReader(text);
        string? line;
        int lineCount = 0;

        while ((line = reader.ReadLine()) != null)
        {
            if ((++lineCount & 8191) == 0) ct.ThrowIfCancellationRequested();

            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;

            if (trimmed.StartsWith('$'))
            {
                string sec = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0].ToUpperInvariant();
                if (sec.StartsWith("$END"))
                {
                    if (curComp != null) { components.Add(curComp); curComp = null; }
                    currentSection = "";
                    curShape = null;
                    curSignal = "";
                    curRoute = "";
                    continue;
                }

                if (curComp != null) { components.Add(curComp); curComp = null; }
                currentSection = sec;
                curShape = null;
                curSignal = "";
                curRoute = "";
                continue;
            }

            string[] tokens = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            string cmd = tokens[0].ToUpperInvariant();

            if (currentSection == "$HEADER")
            {
                if (cmd == "UNITS" && tokens.Length >= 2)
                {
                    unitMode = tokens[1].ToUpperInvariant().Trim('"');
                    if (unitMode == "USER" && tokens.Length >= 3 && double.TryParse(tokens[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double uVal))
                    {
                        userUnitVal = uVal;
                    }
                }
            }
            else if (currentSection == "$BOARD")
            {
                if (cmd == "LINE" && tokens.Length >= 5)
                {
                    if (double.TryParse(tokens[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double x1) &&
                        double.TryParse(tokens[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double y1) &&
                        double.TryParse(tokens[3], NumberStyles.Any, CultureInfo.InvariantCulture, out double x2) &&
                        double.TryParse(tokens[4], NumberStyles.Any, CultureInfo.InvariantCulture, out double y2))
                    {
                        var p1 = new BoardviewPointDto(UnitToMil(x1, unitMode, userUnitVal), UnitToMil(y1, unitMode, userUnitVal));
                        var p2 = new BoardviewPointDto(UnitToMil(x2, unitMode, userUnitVal), UnitToMil(y2, unitMode, userUnitVal));
                        outlineSegments.Add((p1, p2));
                    }
                }
                else if (cmd == "RECTANGLE" && tokens.Length >= 5)
                {
                    if (double.TryParse(tokens[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double rx) &&
                        double.TryParse(tokens[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double ry) &&
                        double.TryParse(tokens[3], NumberStyles.Any, CultureInfo.InvariantCulture, out double rw) &&
                        double.TryParse(tokens[4], NumberStyles.Any, CultureInfo.InvariantCulture, out double rh))
                    {
                        double x = UnitToMil(rx, unitMode, userUnitVal);
                        double y = UnitToMil(ry, unitMode, userUnitVal);
                        double w = UnitToMil(rw, unitMode, userUnitVal);
                        double h = UnitToMil(rh, unitMode, userUnitVal);

                        var p1 = new BoardviewPointDto(x, y);
                        var p2 = new BoardviewPointDto(x, y + h);
                        var p3 = new BoardviewPointDto(x + w, y + h);
                        var p4 = new BoardviewPointDto(x + w, y);

                        outlineSegments.Add((p1, p2));
                        outlineSegments.Add((p2, p3));
                        outlineSegments.Add((p3, p4));
                        outlineSegments.Add((p4, p1));
                    }
                }
            }
            else if (currentSection == "$SHAPES")
            {
                if (cmd == "SHAPE" && tokens.Length >= 2)
                {
                    string shapeName = CleanToken(tokens[1]);
                    curShape = new ShapeDef { Name = shapeName };
                    shapes[shapeName] = curShape;
                }
                else if (curShape != null && cmd == "PIN" && tokens.Length >= 5)
                {
                    string pinName = CleanToken(tokens[1]);
                    string padName = CleanToken(tokens[2]);

                    if (double.TryParse(tokens[3], NumberStyles.Any, CultureInfo.InvariantCulture, out double rx) &&
                        double.TryParse(tokens[4], NumberStyles.Any, CultureInfo.InvariantCulture, out double ry))
                    {
                        curShape.Pins.Add(new ShapePinDef
                        {
                            Name = pinName,
                            PadName = padName,
                            RelX = UnitToMil(rx, unitMode, userUnitVal),
                            RelY = UnitToMil(ry, unitMode, userUnitVal)
                        });
                    }
                }
            }
            else if (currentSection == "$COMPONENTS")
            {
                if (cmd == "COMPONENT" && tokens.Length >= 2)
                {
                    if (curComp != null) components.Add(curComp);
                    curComp = new ComponentDef { Name = CleanToken(tokens[1]) };

                    if (tokens.Length >= 6)
                    {
                        curComp.ShapeName = CleanToken(tokens[2]);
                        if (double.TryParse(tokens[3], NumberStyles.Any, CultureInfo.InvariantCulture, out double px) &&
                            double.TryParse(tokens[4], NumberStyles.Any, CultureInfo.InvariantCulture, out double py))
                        {
                            curComp.X = UnitToMil(px, unitMode, userUnitVal);
                            curComp.Y = UnitToMil(py, unitMode, userUnitVal);
                        }
                        curComp.Side = tokens[5].ToUpperInvariant().Contains("BOT") ? "BOTTOM" : "TOP";
                        if (tokens.Length >= 7 && double.TryParse(tokens[6], NumberStyles.Any, CultureInfo.InvariantCulture, out double r))
                            curComp.Rotation = r;
                    }
                }
                else if (curComp != null)
                {
                    if (cmd == "PLACE" && tokens.Length >= 3)
                    {
                        if (double.TryParse(tokens[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double px) &&
                            double.TryParse(tokens[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double py))
                        {
                            curComp.X = UnitToMil(px, unitMode, userUnitVal);
                            curComp.Y = UnitToMil(py, unitMode, userUnitVal);
                        }
                    }
                    else if (cmd == "LAYER" && tokens.Length >= 2)
                    {
                        curComp.Side = tokens[1].ToUpperInvariant().Contains("BOT") ? "BOTTOM" : "TOP";
                    }
                    else if (cmd == "ROTATION" && tokens.Length >= 2)
                    {
                        if (double.TryParse(tokens[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double r))
                            curComp.Rotation = r;
                    }
                    else if (cmd == "SHAPE" && tokens.Length >= 2)
                    {
                        curComp.ShapeName = CleanToken(tokens[1]);
                        for (int t = 2; t < tokens.Length; t++)
                        {
                            string opt = tokens[t].ToUpperInvariant();
                            if (opt == "MIRRORX") curComp.MirrorX = true;
                            else if (opt == "MIRRORY") curComp.MirrorY = true;
                            else if (opt == "FLIP") curComp.Flip = true;
                        }
                    }
                    else if (cmd == "DEVICE" && tokens.Length >= 2)
                    {
                        curComp.Device = CleanToken(string.Join(" ", tokens.Skip(1)));
                    }
                    else if (cmd == "MIRRORX") curComp.MirrorX = true;
                    else if (cmd == "MIRRORY") curComp.MirrorY = true;
                    else if (cmd == "FLIP") curComp.Flip = true;
                }
            }
            else if (currentSection == "$SIGNALS")
            {
                if (cmd == "SIGNAL" && tokens.Length >= 2)
                {
                    curSignal = CleanToken(tokens[1]);
                    if (curSignal.StartsWith('/')) curSignal = curSignal.Substring(1);
                    if (string.IsNullOrEmpty(curSignal) || curSignal.Equals("NC", StringComparison.OrdinalIgnoreCase))
                        curSignal = "UNCONNECTED";
                }
                else if (cmd == "NODE" && tokens.Length >= 3 && !string.IsNullOrEmpty(curSignal) && curSignal != "UNCONNECTED")
                {
                    string cName = CleanToken(tokens[1]);
                    string pName = CleanToken(tokens[2]);

                    string key = $"{cName}.{pName}";
                    signalsCache[key] = curSignal;

                    if (!nets.TryGetValue(curSignal, out var netList))
                    {
                        netList = new List<string>();
                        nets[curSignal] = netList;
                    }
                    netList.Add(key);
                }
            }
            else if (currentSection is "$ROUTES" or "$TRACKS")
            {
                if (cmd == "ROUTE" && tokens.Length >= 2)
                {
                    curRoute = CleanToken(tokens[1]);
                    if (curRoute.StartsWith('/')) curRoute = curRoute.Substring(1);
                }
                else if (cmd == "VIA" && tokens.Length >= 3)
                {
                    if (double.TryParse(tokens[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double vx) &&
                        double.TryParse(tokens[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double vy))
                    {
                        vias.Add(new BoardviewTestPointDto(
                            Nail: $"VIA_{vias.Count + 1}",
                            Net: !string.IsNullOrEmpty(curRoute) ? curRoute : "UNCONNECTED",
                            Side: "TOP",
                            X: Math.Round(UnitToMil(vx, unitMode, userUnitVal), 4),
                            Y: Math.Round(UnitToMil(vy, unitMode, userUnitVal), 4),
                            IsVia: true,
                            PointType: "VIA"
                        ));
                    }
                }
            }
        }

        if (curComp != null) components.Add(curComp);
        if (components.Count == 0) return null;

        var componentsMap = new Dictionary<string, BoardviewComponentDto>(StringComparer.OrdinalIgnoreCase);
        var seenShapePlacements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int totalPins = 0;

        foreach (var comp in components)
        {
            string dupKey = $"{comp.Side}_{Math.Round(comp.X, 2)}_{Math.Round(comp.Y, 2)}_{comp.ShapeName}";
            if (seenShapePlacements.Contains(dupKey) && !string.IsNullOrEmpty(comp.ShapeName))
                continue;
            seenShapePlacements.Add(dupKey);

            var compPinsMap = new Dictionary<string, BoardviewPinDto>(StringComparer.OrdinalIgnoreCase);

            double rotRad = comp.Rotation * (Math.PI / 180.0);
            int mirrorXSign = comp.MirrorX ? -1 : 1;
            int mirrorYSign = comp.MirrorY ? -1 : 1;
            if (mirrorXSign * mirrorYSign == -1)
            {
                rotRad = Math.PI - rotRad;
            }
            double cos = Math.Cos(rotRad);
            double sin = Math.Sin(rotRad);

            if (!string.IsNullOrEmpty(comp.ShapeName) && shapes.TryGetValue(comp.ShapeName, out var shapeDef))
            {
                foreach (var spin in shapeDef.Pins)
                {
                    double absX = comp.X + mirrorXSign * (spin.RelX * cos - spin.RelY * sin);
                    double absY = comp.Y + mirrorYSign * (spin.RelX * sin + spin.RelY * cos);

                    string pinSide = comp.Side;
                    if (comp.Flip)
                    {
                        pinSide = (pinSide == "TOP") ? "BOTTOM" : "TOP";
                    }

                    string netKey = $"{comp.Name}.{spin.Name}";
                    string net = signalsCache.TryGetValue(netKey, out var sNet) ? sNet : "UNCONNECTED";

                    compPinsMap[spin.Name] = new BoardviewPinDto(
                        Name: spin.Name,
                        Net: net,
                        X: Math.Round(absX, 4),
                        Y: Math.Round(absY, 4),
                        Layer: pinSide
                    );
                    totalPins++;
                }
            }

            if (compPinsMap.Count == 0)
            {
                compPinsMap["1"] = new BoardviewPinDto(
                    Name: "1",
                    Net: "UNCONNECTED",
                    X: Math.Round(comp.X, 4),
                    Y: Math.Round(comp.Y, 4),
                    Layer: comp.Side
                );
            }

            double minX = compPinsMap.Values.Min(p => p.X);
            double minY = compPinsMap.Values.Min(p => p.Y);
            double maxX = compPinsMap.Values.Max(p => p.X);
            double maxY = compPinsMap.Values.Max(p => p.Y);

            double width = Math.Max(0.8, Math.Round(maxX - minX, 4));
            double height = Math.Max(0.8, Math.Round(maxY - minY, 4));
            double centerX = Math.Round((minX + maxX) / 2.0, 4);
            double centerY = Math.Round((minY + maxY) / 2.0, 4);

            string uName = comp.Name.ToUpperInvariant();
            string uShape = comp.ShapeName.ToUpperInvariant();
            bool isTh = uShape.Contains("DIP") || uShape.Contains("TH") || uShape.Contains("HOLE") ||
                        uName.StartsWith("DIMM") || uName.StartsWith("PCI") || uName.StartsWith("J") || uName.StartsWith("CN");

            componentsMap[comp.Name] = new BoardviewComponentDto(
                X: centerX,
                Y: centerY,
                MinX: Math.Round(minX, 4),
                MinY: Math.Round(minY, 4),
                MaxX: Math.Round(maxX, 4),
                MaxY: Math.Round(maxY, 4),
                Width: width,
                Height: height,
                Side: isTh ? "BOTH" : comp.Side,
                PinsCount: compPinsMap.Count,
                Type: _roleDeducer.DeduceRole(comp.Name, isTh ? "TH" : "SMD"),
                Pins: compPinsMap,
                Description: comp.Device,
                Value: comp.Device,
                Package: comp.ShapeName
            );
        }

        var allPins = componentsMap.Values.SelectMany(c => c.Pins.Values).ToList();
        double pinMinX = allPins.Count > 0 ? allPins.Min(p => p.X) : 0;
        double pinMinY = allPins.Count > 0 ? allPins.Min(p => p.Y) : 0;
        double pinMaxX = allPins.Count > 0 ? allPins.Max(p => p.X) : 1000;
        double pinMaxY = allPins.Count > 0 ? allPins.Max(p => p.Y) : 1000;

        if (componentsMap.Count > 0)
        {
            pinMinX = Math.Min(pinMinX, componentsMap.Values.Min(c => c.X));
            pinMinY = Math.Min(pinMinY, componentsMap.Values.Min(c => c.Y));
            pinMaxX = Math.Max(pinMaxX, componentsMap.Values.Max(c => c.X));
            pinMaxY = Math.Max(pinMaxY, componentsMap.Values.Max(c => c.Y));
        }

        const double Margin = 40.0;
        double bMinX = pinMinX - Margin;
        double bMinY = pinMinY - Margin;
        double bMaxX = pinMaxX + Margin;
        double bMaxY = pinMaxY + Margin;

        var stitchedOutline = StitchOutlinePerimeter(outlineSegments, bMinX, bMinY, bMaxX, bMaxY, ct);

        var bounds = new BoardviewBoundsDto(
            MinX: Math.Round(bMinX, 4),
            MinY: Math.Round(bMinY, 4),
            MaxX: Math.Round(bMaxX, 4),
            MaxY: Math.Round(bMaxY, 4),
            Width: Math.Round(bMaxX - bMinX, 4),
            Height: Math.Round(bMaxY - bMinY, 4),
            Unit: "MIL",
            Outline: stitchedOutline
        );

        var anchorCandidates = _anchorScorer.SelectAnchorCandidates(bounds, componentsMap);

        var stats = new BoardviewStatsDto(
            TotalComponents: componentsMap.Count,
            TotalNets: nets.Count,
            TotalBridges: 0,
            TotalVias: vias.Count,
            TotalTestPoints: 0
        );

        onLog?.Invoke($"✅ [GenCadParser] Done! Size: {bounds.Width:F0}x{bounds.Height:F0} mil ({componentsMap.Count} parts, {totalPins} pins).");

        return new BoardviewNetlistDto(
            Board: Path.GetFileNameWithoutExtension(fileName),
            Format: FormatName,
            Bounds: bounds,
            Stats: stats,
            Components: componentsMap,
            AnchorCandidates: anchorCandidates,
            Nets: nets,
            Bridges: new List<BoardviewBridgeDto>(),
            TestPoints: vias
        );
    }

    private static List<BoardviewPointDto> StitchOutlinePerimeter(
        List<(BoardviewPointDto P1, BoardviewPointDto P2)> segments,
        double bMinX, double bMinY, double bMaxX, double bMaxY,
        CancellationToken ct = default)
    {
        double boardW = bMaxX - bMinX;
        double boardH = bMaxY - bMinY;

        if (segments.Count >= 3)
        {
            var remaining = new List<(BoardviewPointDto P1, BoardviewPointDto P2)>(segments);
            var stitched = new List<BoardviewPointDto>();

            var cur = remaining[0];
            remaining.RemoveAt(0);
            stitched.Add(cur.P1);
            stitched.Add(cur.P2);

            var startPt = cur.P1;
            var endPt = cur.P2;

            while (remaining.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                bool found = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    var seg = remaining[i];
                    if (Hypot(endPt.X - seg.P1.X, endPt.Y - seg.P1.Y) < 2.0)
                    {
                        stitched.Add(seg.P2);
                        endPt = seg.P2;
                        remaining.RemoveAt(i);
                        found = true;
                        break;
                    }
                    else if (Hypot(endPt.X - seg.P2.X, endPt.Y - seg.P2.Y) < 2.0)
                    {
                        stitched.Add(seg.P1);
                        endPt = seg.P1;
                        remaining.RemoveAt(i);
                        found = true;
                        break;
                    }
                }

                if (!found) break;
            }

            if (stitched.Count >= 3)
            {
                double outW = stitched.Max(p => p.X) - stitched.Min(p => p.X);
                double outH = stitched.Max(p => p.Y) - stitched.Min(p => p.Y);
                if (outW >= boardW * 0.70 && outH >= boardH * 0.70)
                {
                    return stitched;
                }
            }
        }

        return new List<BoardviewPointDto>
        {
            new(bMinX, bMinY), new(bMaxX, bMinY), new(bMaxX, bMaxY), new(bMinX, bMaxY), new(bMinX, bMinY)
        };
    }

    private static double Hypot(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);

    private static double UnitToMil(double val, string unit, double userUnit)
    {
        return unit switch
        {
            "INCH" => val * 1000.0,
            "MM" => val * (100.0 / 2.54),
            "MM100" => val * (1.0 / 2.54),
            "USER" => (userUnit * val) / 1000.0,
            "USERCM" => userUnit * val * (10.0 / 2.54),
            "USERMM" => userUnit * val * (1.0 / 2.54),
            _ => val
        };
    }

    private static string CleanToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return "";
        return token.Trim().Trim('"');
    }

    private class ShapeDef { public string Name { get; set; } = ""; public List<ShapePinDef> Pins { get; set; } = []; }
    private class ShapePinDef { public string Name { get; set; } = ""; public string PadName { get; set; } = ""; public double RelX { get; set; } public double RelY { get; set; } }
    private class ComponentDef { public string Name { get; set; } = ""; public string ShapeName { get; set; } = ""; public string Device { get; set; } = ""; public double X { get; set; } public double Y { get; set; } public double Rotation { get; set; } public string Side { get; set; } = "TOP"; public bool MirrorX { get; set; } public bool MirrorY { get; set; } public bool Flip { get; set; } }
    private class ComponentBuilder { public string RefDes { get; set; } = ""; public string Side { get; set; } = "TOP"; public string Type { get; set; } = "IC"; public string? Description { get; set; } public string? Value { get; set; } public List<PinCoord> Pins { get; set; } = []; }
    private record PinCoord(string PinNum, string PinName, string Net, double X, double Y, string Layer);
}