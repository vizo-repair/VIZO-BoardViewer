// ============================================================================
// FILE: Features/Boardview/ParseBoardview/Formats/BvrPcbParser.cs
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using VIZO.BoardViewer.Boardview.ParseBoardview.Services;
using System.Text;

namespace VIZO.BoardViewer.Boardview.ParseBoardview.Formats;

public class BvrPcbParser : IBoardviewFormatParser
{
    private readonly ComponentRoleDeducer _roleDeducer;
    private readonly QuadrantAnchorScorer _anchorScorer;

    public string FormatName => "BoardViewer v1 (.bvr)";

    public BvrPcbParser(ComponentRoleDeducer roleDeducer, QuadrantAnchorScorer anchorScorer)
    {
        _roleDeducer = roleDeducer;
        _anchorScorer = anchorScorer;
    }

    public bool CanParse(ReadOnlySpan<byte> headerBytes, string fileName)
    {
        string headerStr = Encoding.ASCII.GetString(headerBytes);
        return headerStr.Contains("BVRAW_FORMAT_1");
    }

    public BoardviewNetlistDto? Parse(byte[] fileBytes, string fileName, Action<string>? onLog = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string content = Encoding.UTF8.GetString(fileBytes);
        
        var format = new List<BoardviewPointDto>();
        var partsMap = new Dictionary<string, BvrPart>();
        var testPoints = new List<BoardviewTestPointDto>();

        int currentBlock = 0;
        int skipLines = 0;

        using (var reader = new StringReader(content))
        {
            string? line;
            int lineCount = 0;
            while ((line = reader.ReadLine()) != null)
            {
                if ((++lineCount & 4095) == 0) ct.ThrowIfCancellationRequested();
                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("<<Layout>>"))
                {
                    currentBlock = 1;
                    skipLines = 1;
                    continue;
                }
                else if (line.StartsWith("<<Pin>>"))
                {
                    currentBlock = 2;
                    skipLines = 1;
                    continue;
                }
                else if (line.StartsWith("<<Nail>>"))
                {
                    currentBlock = 3;
                    skipLines = 1;
                    continue;
                }

                if (skipLines > 0)
                {
                    skipLines--;
                    continue;
                }

                string[] fields = line.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);

                switch (currentBlock)
                {
                    case 1: // Layout
                        if (fields.Length >= 2 && double.TryParse(fields[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double fx) && double.TryParse(fields[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double fy))
                        {
                            format.Add(new BoardviewPointDto(Math.Truncate(fx * 1000), Math.Truncate(fy * 1000)));
                        }
                        break;
                    case 2: // Pin
                        if (fields.Length >= 8)
                        {
                            string partName = fields[0];
                            string loc = fields[1];
                            string pinName = fields[3];

                            if (double.TryParse(fields[4], NumberStyles.Any, CultureInfo.InvariantCulture, out double px) && double.TryParse(fields[5], NumberStyles.Any, CultureInfo.InvariantCulture, out double py))
                            {
                                string net = fields[7];

                                if (!partsMap.TryGetValue(partName, out var part))
                                {
                                    part = new BvrPart
                                    {
                                        Name = partName,
                                        Side = loc == "(T)" ? "TOP" : "BOTTOM",
                                        Pins = new Dictionary<string, BoardviewPinDto>()
                                    };
                                    partsMap[partName] = part;
                                }

                                part.Pins[pinName] = new BoardviewPinDto(
                                    Name: pinName,
                                    Net: net,
                                    X: Math.Truncate(px * 1000),
                                    Y: Math.Truncate(py * 1000),
                                    Layer: part.Side
                                );
                            }
                        }
                        break;
                    case 3: // Nail
                        if (fields.Length >= 7)
                        {
                            if (double.TryParse(fields[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double nx) && double.TryParse(fields[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double ny))
                            {
                                string nloc = fields[5];
                                string nnet = fields[6];

                                testPoints.Add(new BoardviewTestPointDto(
                                    Nail: $"TP_{testPoints.Count + 1}",
                                    Net: nnet,
                                    Side: nloc == "(T)" ? "TOP" : "BOTTOM",
                                    X: Math.Truncate(nx * 1000),
                                    Y: Math.Truncate(ny * 1000),
                                    IsVia: false,
                                    PointType: "TESTPAD"
                                ));
                            }
                        }
                        break;
                }
            }
        }

        if (currentBlock == 0) return null;

        var componentsMap = new Dictionary<string, BoardviewComponentDto>();
        var netsMap = new Dictionary<string, List<string>>();

        foreach (var kvp in partsMap)
        {
            var p = kvp.Value;
            
            foreach (var pin in p.Pins.Values)
            {
                if (!string.IsNullOrEmpty(pin.Net) && pin.Net != "UNCONNECTED")
                {
                    if (!netsMap.ContainsKey(pin.Net)) netsMap[pin.Net] = new List<string>();
                    netsMap[pin.Net].Add($"{p.Name}-{pin.Name}");
                }
            }

            double minX = p.Pins.Count > 0 ? p.Pins.Values.Min(pin => pin.X) : 0;
            double minY = p.Pins.Count > 0 ? p.Pins.Values.Min(pin => pin.Y) : 0;
            double maxX = p.Pins.Count > 0 ? p.Pins.Values.Max(pin => pin.X) : 0;
            double maxY = p.Pins.Count > 0 ? p.Pins.Values.Max(pin => pin.Y) : 0;

            componentsMap[p.Name] = new BoardviewComponentDto(
                X: minX + (maxX - minX) / 2,
                Y: minY + (maxY - minY) / 2,
                MinX: minX, MinY: minY, MaxX: maxX, MaxY: maxY,
                Width: maxX - minX, Height: maxY - minY,
                Side: p.Side,
                PinsCount: p.Pins.Count,
                Type: _roleDeducer.DeduceRole(p.Name, "SMD"),
                Pins: p.Pins
            );
        }

        double bMinX = format.Count > 0 ? format.Min(p => p.X) : 0;
        double bMinY = format.Count > 0 ? format.Min(p => p.Y) : 0;
        double bMaxX = format.Count > 0 ? format.Max(p => p.X) : 0;
        double bMaxY = format.Count > 0 ? format.Max(p => p.Y) : 0;

        var bounds = new BoardviewBoundsDto(
            MinX: bMinX, MinY: bMinY, MaxX: bMaxX, MaxY: bMaxY,
            Width: bMaxX - bMinX, Height: bMaxY - bMinY,
            Unit: "MIL",
            Outline: format.Count > 0 ? format : null
        );

        var stats = new BoardviewStatsDto(
            TotalComponents: componentsMap.Count,
            TotalNets: netsMap.Count,
            TotalBridges: 0,
            TotalVias: 0,
            TotalTestPoints: testPoints.Count
        );

        var anchorCandidates = _anchorScorer.SelectAnchorCandidates(bounds, componentsMap);

        return new BoardviewNetlistDto(
            Board: fileName,
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

    private class BvrPart
    {
        public string Name = "";
        public string Side = "TOP";
        public Dictionary<string, BoardviewPinDto> Pins = new Dictionary<string, BoardviewPinDto>();
    }
}
