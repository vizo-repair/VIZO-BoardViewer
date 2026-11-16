// ============================================================================
// FILE: Features/Boardview/ParseBoardview/Formats/CadPcbParser.cs
// Exact OpenBoardView CAD (.cad) parser with detailed logging
// ============================================================================
namespace VIZO.BoardViewer.Boardview.ParseBoardview.Formats;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using VIZO.BoardViewer.Boardview.ParseBoardview.Services;

public class CadPcbParser : IBoardviewFormatParser
{
    private readonly ComponentRoleDeducer _roleDeducer;
    private readonly QuadrantAnchorScorer _anchorScorer;

    public string FormatName => "CAD (.cad)";

    public CadPcbParser(ComponentRoleDeducer roleDeducer, QuadrantAnchorScorer anchorScorer)
    {
        _roleDeducer = roleDeducer;
        _anchorScorer = anchorScorer;
    }

    public bool CanParse(ReadOnlySpan<byte> headerBytes, string fileName)
    {
        if (headerBytes.Length < 4) return false;
        string headerStr = Encoding.Latin1.GetString(headerBytes);

        // If the file contains GenCAD 1.4 tags, it MUST go to GenCadAsciiParser!
        if (headerStr.Contains("GENCAD", StringComparison.OrdinalIgnoreCase) ||
            headerStr.Contains("$HEADER", StringComparison.OrdinalIgnoreCase) ||
            headerStr.Contains("$SHAPES", StringComparison.OrdinalIgnoreCase) ||
            headerStr.Contains("$COMPONENTS", StringComparison.OrdinalIgnoreCase) ||
            headerStr.Contains("$SIGNALS", StringComparison.OrdinalIgnoreCase) ||
            headerStr.Contains("$PINS", StringComparison.OrdinalIgnoreCase) ||
            headerStr.Contains("<<", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Check classic OpenBoardView CADFile.cpp format
        return headerStr.Contains("C_PIN", StringComparison.OrdinalIgnoreCase) ||
               headerStr.Contains("###Panel Added", StringComparison.OrdinalIgnoreCase) ||
               headerStr.Contains("N_VIA", StringComparison.OrdinalIgnoreCase);
    }

    public BoardviewNetlistDto? Parse(byte[] fileBytes, string fileName, Action<string>? onLog = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (fileBytes == null || fileBytes.Length < 4)
        {
            onLog?.Invoke("❌ [CadPcbParser] File is empty.");
            return null;
        }

        string content = Encoding.Latin1.GetString(fileBytes).TrimStart('\uFEFF');

        var partsMap = new Dictionary<string, CadPart>(StringComparer.OrdinalIgnoreCase);
        var pins = new List<CadPin>();
        var testPoints = new List<BoardviewTestPointDto>();

        string currentNailNet = "UNCONNECTED";
        const double Multiplier = 1000.0;

        int countComp = 0, countPin = 0, countNet = 0, countVia = 0, countSkipped = 0;
        var sampleLines = new List<string>();

        using (var reader = new StringReader(content))
        {
            string? line;
            int lineCount = 0;
            while ((line = reader.ReadLine()) != null)
            {
                if ((++lineCount & 8191) == 0) ct.ThrowIfCancellationRequested();

                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (sampleLines.Count < 5 && !line.StartsWith('#'))
                    sampleLines.Add(line);

                // OpenBoardView strncmp: check command prefix
                int blockType = -1;
                if (line.StartsWith("COMP", StringComparison.OrdinalIgnoreCase)) blockType = 1;
                else if (line.StartsWith("C_PIN", StringComparison.OrdinalIgnoreCase)) blockType = 2;
                else if (line.StartsWith("NET", StringComparison.OrdinalIgnoreCase)) blockType = 3;
                else if (line.StartsWith("N_VIA", StringComparison.OrdinalIgnoreCase)) blockType = 4;

                if (blockType == -1)
                {
                    countSkipped++;
                    continue;
                }

                // Split by any spaces and tabs
                string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 2) continue;

                switch (blockType)
                {
                    case 1: // COMP
                        countComp++;
                        // OpenBoardView: [0] TYPE, [1] part.name, [2..6] meta/xy, [7] loc (1 = Top, 2 = Bottom)
                        string partName = tokens[1].Trim();
                        string loc = tokens.Length >= 8 ? tokens[7].Trim() : "1";
                        partsMap[partName] = new CadPart
                        {
                            Name = partName,
                            Side = (loc == "1") ? "TOP" : "BOTTOM"
                        };
                        break;

                    case 2: // C_PIN
                        // OpenBoardView: [0] TYPE, [1] part-pin, [2] posx, [3] posy, [4..7] unk, [8] net
                        if (tokens.Length >= 4)
                        {
                            string rawPart = tokens[1].Trim();
                            string compRef = rawPart;
                            string pinNumber = "";

                            int dash = rawPart.IndexOf('-');
                            if (dash != -1)
                            {
                                compRef = rawPart.Substring(0, dash).Trim();
                                pinNumber = rawPart.Substring(dash + 1).Trim();
                            }

                            if (double.TryParse(tokens[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double px) &&
                                double.TryParse(tokens[3], NumberStyles.Any, CultureInfo.InvariantCulture, out double py))
                            {
                                countPin++;
                                string net = "UNCONNECTED";
                                if (tokens.Length >= 9)
                                {
                                    net = tokens[8].Trim();
                                }
                                else if (tokens.Length > 4)
                                {
                                    net = tokens[^1].Trim();
                                }

                                if (net.StartsWith('/')) net = net.Substring(1).Trim();
                                if (string.IsNullOrEmpty(net) || net.Equals("NC", StringComparison.OrdinalIgnoreCase))
                                    net = "UNCONNECTED";

                                pins.Add(new CadPin
                                {
                                    PartName = compRef,
                                    PinNumber = pinNumber,
                                    X = px * Multiplier,
                                    Y = py * Multiplier,
                                    Net = net
                                });
                            }
                        }
                        break;

                    case 3: // NET
                        countNet++;
                        string netName = tokens[1].Trim();
                        if (netName.StartsWith('/')) netName = netName.Substring(1).Trim();
                        currentNailNet = string.IsNullOrEmpty(netName) || netName.Equals("NC", StringComparison.OrdinalIgnoreCase)
                            ? "UNCONNECTED"
                            : netName;
                        break;

                    case 4: // N_VIA
                        if (tokens.Length >= 3 &&
                            double.TryParse(tokens[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double vx) &&
                            double.TryParse(tokens[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double vy))
                        {
                            countVia++;
                            string side = (tokens.Length >= 5 && tokens[4].Trim() == "2") ? "BOTTOM" : "TOP";
                            testPoints.Add(new BoardviewTestPointDto(
                                Nail: $"VIA_{testPoints.Count + 1}",
                                Net: currentNailNet,
                                Side: side,
                                X: Math.Round(vx * Multiplier, 4),
                                Y: Math.Round(vy * Multiplier, 4),
                                IsVia: true,
                                PointType: "VIA"
                            ));
                        }
                        break;
                }
            }
        }

        onLog?.Invoke($"📊 [CadPcbParser] Parsed lines: COMP={countComp}, C_PIN={countPin}, NET={countNet}, N_VIA={countVia}, Skipped={countSkipped}");
        if (sampleLines.Count > 0)
        {
            onLog?.Invoke($"📝 [CadPcbParser] Sample lines: {string.Join(" | ", sampleLines.Take(3))}");
        }

        if (partsMap.Count == 0 && pins.Count == 0)
        {
            onLog?.Invoke("❌ [CadPcbParser] No components (COMP) and no pins (C_PIN) found.");
            return null;
        }

        var componentsMap = new Dictionary<string, BoardviewComponentDto>(StringComparer.OrdinalIgnoreCase);
        var netsMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // Group pins by component
        var pinsByPart = pins.GroupBy(p => p.PartName, StringComparer.OrdinalIgnoreCase)
                             .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Add components that appeared only in C_PIN
        foreach (var pKey in pinsByPart.Keys)
        {
            if (!partsMap.ContainsKey(pKey))
                partsMap[pKey] = new CadPart { Name = pKey, Side = "TOP" };
        }

        foreach (var (pName, part) in partsMap)
        {
            var compPinsMap = new Dictionary<string, BoardviewPinDto>(StringComparer.OrdinalIgnoreCase);

            if (pinsByPart.TryGetValue(pName, out var partPins))
            {
                int autoIdx = 1;
                foreach (var pin in partPins)
                {
                    string pNum = !string.IsNullOrEmpty(pin.PinNumber) ? pin.PinNumber : autoIdx.ToString();
                    if (compPinsMap.ContainsKey(pNum))
                        pNum = $"{pNum}_{autoIdx}";

                    compPinsMap[pNum] = new BoardviewPinDto(
                        Name: pNum,
                        Net: pin.Net,
                        X: Math.Round(pin.X, 4),
                        Y: Math.Round(pin.Y, 4),
                        Layer: part.Side
                    );

                    if (pin.Net != "UNCONNECTED")
                    {
                        if (!netsMap.TryGetValue(pin.Net, out var netList))
                        {
                            netList = new List<string>();
                            netsMap[pin.Net] = netList;
                        }
                        netList.Add($"{pName}.{pNum}");
                    }
                    autoIdx++;
                }
            }

            double minX = compPinsMap.Count > 0 ? compPinsMap.Values.Min(p => p.X) : 0;
            double minY = compPinsMap.Count > 0 ? compPinsMap.Values.Min(p => p.Y) : 0;
            double maxX = compPinsMap.Count > 0 ? compPinsMap.Values.Max(p => p.X) : 0;
            double maxY = compPinsMap.Count > 0 ? compPinsMap.Values.Max(p => p.Y) : 0;

            double width = Math.Max(0.8, Math.Round(maxX - minX, 4));
            double height = Math.Max(0.8, Math.Round(maxY - minY, 4));
            double cx = compPinsMap.Count > 0 ? Math.Round((minX + maxX) / 2.0, 4) : 0;
            double cy = compPinsMap.Count > 0 ? Math.Round((minY + maxY) / 2.0, 4) : 0;

            componentsMap[pName] = new BoardviewComponentDto(
                X: cx,
                Y: cy,
                MinX: Math.Round(minX, 4),
                MinY: Math.Round(minY, 4),
                MaxX: Math.Round(maxX, 4),
                MaxY: Math.Round(maxY, 4),
                Width: width,
                Height: height,
                Side: part.Side,
                PinsCount: compPinsMap.Count,
                Type: _roleDeducer.DeduceRole(pName, "SMD"),
                Pins: compPinsMap
            );
        }

        const double OutlineMargin = 20.0;
        double bMinX = (pins.Count > 0 ? pins.Min(p => p.X) : 0) - OutlineMargin;
        double bMinY = (pins.Count > 0 ? pins.Min(p => p.Y) : 0) - OutlineMargin;
        double bMaxX = (pins.Count > 0 ? pins.Max(p => p.X) : 0) + OutlineMargin;
        double bMaxY = (pins.Count > 0 ? pins.Max(p => p.Y) : 0) + OutlineMargin;

        var outline = new List<BoardviewPointDto>
        {
            new(Math.Round(bMinX, 4), Math.Round(bMinY, 4)),
            new(Math.Round(bMaxX, 4), Math.Round(bMinY, 4)),
            new(Math.Round(bMaxX, 4), Math.Round(bMaxY, 4)),
            new(Math.Round(bMinX, 4), Math.Round(bMaxY, 4)),
            new(Math.Round(bMinX, 4), Math.Round(bMinY, 4))
        };

        var bounds = new BoardviewBoundsDto(
            MinX: Math.Round(bMinX, 4),
            MinY: Math.Round(bMinY, 4),
            MaxX: Math.Round(bMaxX, 4),
            MaxY: Math.Round(bMaxY, 4),
            Width: Math.Round(bMaxX - bMinX, 4),
            Height: Math.Round(bMaxY - bMinY, 4),
            Unit: "MIL",
            Outline: outline
        );

        var anchorCandidates = _anchorScorer.SelectAnchorCandidates(bounds, componentsMap);

        var stats = new BoardviewStatsDto(
            TotalComponents: componentsMap.Count,
            TotalNets: netsMap.Count,
            TotalBridges: 0,
            TotalVias: testPoints.Count,
            TotalTestPoints: 0
        );

        onLog?.Invoke($"✅ [CadPcbParser] Successfully assembled: {componentsMap.Count} components, {netsMap.Count} nets, {testPoints.Count} vias.");

        return new BoardviewNetlistDto(
            Board: Path.GetFileNameWithoutExtension(fileName),
            Format: FormatName,
            Bounds: bounds,
            Stats: stats,
            Components: componentsMap,
            AnchorCandidates: anchorCandidates,
            Nets: netsMap,
            Bridges: new List<BoardviewBridgeDto>(),
            TestPoints: testPoints
        );
    }

    private class CadPart
    {
        public string Name { get; set; } = "";
        public string Side { get; set; } = "TOP";
    }

    private class CadPin
    {
        public string PartName { get; set; } = "";
        public string PinNumber { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public string Net { get; set; } = "";
    }
}