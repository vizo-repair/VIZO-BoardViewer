// ============================================================================
// FILE: Features/Boardview/ParseBoardview/Formats/BrdPcbParser.cs
// ============================================================================
using System;
using System.Globalization;
using VIZO.BoardViewer.Boardview.ParseBoardview.Services;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;

namespace VIZO.BoardViewer.Boardview.ParseBoardview.Formats;

public class BrdPcbParser : IBoardviewFormatParser
{
    private readonly ILogger<BrdPcbParser> _logger;
    private readonly ComponentRoleDeducer _roleDeducer;
    private readonly QuadrantAnchorScorer _anchorScorer;

    public string FormatName => "Standard BRD (.brd)";

    private static readonly byte[] EncodedSignature = { 0x23, 0xE2, 0x63, 0x28 };

    public BrdPcbParser(ILogger<BrdPcbParser> logger, ComponentRoleDeducer roleDeducer, QuadrantAnchorScorer anchorScorer)
    {
        _logger = logger;
        _roleDeducer = roleDeducer;
        _anchorScorer = anchorScorer;
    }

    public bool CanParse(ReadOnlySpan<byte> headerBytes, string fileName)
    {
        if (headerBytes.Length >= 4 && headerBytes.Slice(0, 4).SequenceEqual(EncodedSignature))
            return true;

        string headerStr = Encoding.ASCII.GetString(headerBytes);
        return headerStr.Contains("str_length:") || headerStr.Contains("var_data:");
    }

    public BoardviewNetlistDto? Parse(byte[] fileBytes, string fileName, Action<string>? onLog = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (fileBytes.Length >= 4 && fileBytes.AsSpan(0, 4).SequenceEqual(EncodedSignature))
        {
            for (int i = 0; i < fileBytes.Length; i++)
            {
                if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
                byte x = fileBytes[i];
                if (x != '\r' && x != '\n' && x != 0)
                {
                    int c = x;
                    fileBytes[i] = (byte)(~(((c >> 6) & 3) | (c << 2)));
                }
            }
        }

        string content = Encoding.UTF8.GetString(fileBytes);
        int currentBlock = 0;

        var format = new List<BoardviewPointDto>();
        
        // Temporarily store parts, pins, nails
        var parts = new List<BrdPart>();
        var pins = new List<BrdPin>();
        var nails = new List<BrdNail>();

        using (var reader = new StringReader(content))
        {
            string? line;
            int lineCount = 0;
            while ((line = reader.ReadLine()) != null)
            {
                if (++lineCount > 2_000_000)
                    throw new InvalidDataException("BRD: too many lines");
                if ((lineCount & 4095) == 0) ct.ThrowIfCancellationRequested();
                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (line.Equals("str_length:", StringComparison.OrdinalIgnoreCase)) { currentBlock = 1; continue; }
                if (line.Equals("var_data:", StringComparison.OrdinalIgnoreCase)) { currentBlock = 2; continue; }
                if (line.Equals("Format:", StringComparison.OrdinalIgnoreCase) || line.Equals("format:", StringComparison.OrdinalIgnoreCase)) { currentBlock = 3; continue; }
                if (line.Equals("Parts:", StringComparison.OrdinalIgnoreCase) || line.Equals("Pins1:", StringComparison.OrdinalIgnoreCase)) { currentBlock = 4; continue; }
                if (line.Equals("Pins:", StringComparison.OrdinalIgnoreCase) || line.Equals("Pins2:", StringComparison.OrdinalIgnoreCase)) { currentBlock = 5; continue; }
                if (line.Equals("Nails:", StringComparison.OrdinalIgnoreCase)) { currentBlock = 6; continue; }

                string[] fields = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                switch (currentBlock)
                {
                    case 2: // var_data (skipping since we parse dynamically)
                        break;
                    case 3: // Format
                        if (fields.Length >= 2 && double.TryParse(fields[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double fx) && double.TryParse(fields[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double fy))
                        {
                            format.Add(new BoardviewPointDto(fx, fy));
                        }
                        break;
                    case 4: // Parts
                        if (fields.Length >= 3)
                        {
                            string name = fields[0];
                            if (uint.TryParse(fields[1], out uint tmp))
                            {
                                string partType = (tmp & 0xc) != 0 ? "SMD" : "TH";
                                string side = "BOTH";
                                if (tmp == 1 || (4 <= tmp && tmp < 8)) side = "TOP";
                                else if (tmp == 2 || tmp >= 8) side = "BOTTOM";
                                
                                parts.Add(new BrdPart { Name = name, Type = partType, Side = side });
                            }
                        }
                        break;
                    case 5: // Pins
                        if (fields.Length >= 5)
                        {
                            if (double.TryParse(fields[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double px) && double.TryParse(fields[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double py) &&
                                int.TryParse(fields[2], out int probe) && int.TryParse(fields[3], out int partId))
                            {
                                string net = string.Join(" ", fields.Skip(4)); // Net name can contain spaces if parsing weirdly
                                pins.Add(new BrdPin { X = px, Y = py, Probe = probe, PartId = partId, Net = net });
                            }
                        }
                        break;
                    case 6: // Nails
                        if (fields.Length >= 5)
                        {
                            if (int.TryParse(fields[0], out int probe) && double.TryParse(fields[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double nx) && double.TryParse(fields[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double ny) && int.TryParse(fields[3], out int nside))
                            {
                                string net = string.Join(" ", fields.Skip(4));
                                nails.Add(new BrdNail { Probe = probe, X = nx, Y = ny, Side = nside == 1 ? "TOP" : "BOTTOM", Net = net });
                            }
                        }
                        break;
                }
            }
        }

        if (currentBlock == 0) return null; // Didn't parse anything

        var nailsToNets = new Dictionary<int, string>();
        foreach (var nail in nails)
        {
            nailsToNets[nail.Probe] = nail.Net;
        }

        var componentsMap = new Dictionary<string, BoardviewComponentDto>();
        var netsMap = new Dictionary<string, List<string>>();

        // Group pins by part
        var pinsByPart = pins.GroupBy(p => p.PartId).ToDictionary(g => g.Key, g => g.ToList());

        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            int partId1Based = i + 1;
            
            var compPinsMap = new Dictionary<string, BoardviewPinDto>();
            
            if (pinsByPart.TryGetValue(partId1Based, out var partPins))
            {
                int pinIdx = 1;
                foreach (var pin in partPins)
                {
                    string net = pin.Net;
                    if (string.IsNullOrEmpty(net) || net == "UNCONNECTED")
                    {
                        if (nailsToNets.TryGetValue(pin.Probe, out string? nailNet))
                        {
                            net = nailNet;
                        }
                        else
                        {
                            net = "UNCONNECTED";
                        }
                    }

                    string pinName = pinIdx.ToString();
                    compPinsMap[pinName] = new BoardviewPinDto(
                        Name: pinName,
                        Net: net,
                        X: pin.X,
                        Y: pin.Y,
                        Layer: part.Side == "BOTH" ? "TOP" : part.Side // Default layer
                    );
                    
                    if (net != "UNCONNECTED")
                    {
                        if (!netsMap.ContainsKey(net)) netsMap[net] = new List<string>();
                        netsMap[net].Add($"{part.Name}.{pinName}");
                    }
                    pinIdx++;
                }
            }

            double minX = compPinsMap.Count > 0 ? compPinsMap.Values.Min(p => p.X) : 0;
            double minY = compPinsMap.Count > 0 ? compPinsMap.Values.Min(p => p.Y) : 0;
            double maxX = compPinsMap.Count > 0 ? compPinsMap.Values.Max(p => p.X) : 0;
            double maxY = compPinsMap.Count > 0 ? compPinsMap.Values.Max(p => p.Y) : 0;

            componentsMap[part.Name] = new BoardviewComponentDto(
                X: minX + (maxX - minX) / 2,
                Y: minY + (maxY - minY) / 2,
                MinX: minX, MinY: minY, MaxX: maxX, MaxY: maxY,
                Width: maxX - minX, Height: maxY - minY,
                Side: part.Side,
                PinsCount: compPinsMap.Count,
                Type: _roleDeducer.DeduceRole(part.Name, part.Type),
                Pins: compPinsMap
            );
        }

        var testPointsMap = nails.Select(n => new BoardviewTestPointDto(
            Nail: n.Probe.ToString(),
            Net: n.Net,
            Side: n.Side,
            X: n.X,
            Y: n.Y,
            IsVia: false,
            PointType: "TESTPAD"
        )).ToList();

        // Calculate board bounds
        double bMinX = format.Count > 0 ? format.Min(p => p.X) : 0;
        double bMinY = format.Count > 0 ? format.Min(p => p.Y) : 0;
        double bMaxX = format.Count > 0 ? format.Max(p => p.X) : 0;
        double bMaxY = format.Count > 0 ? format.Max(p => p.Y) : 0;

        var bounds = new BoardviewBoundsDto(
            MinX: bMinX, MinY: bMinY, MaxX: bMaxX, MaxY: bMaxY,
            Width: bMaxX - bMinX, Height: bMaxY - bMinY,
            Unit: "MIL", // standard BRD usually uses thou/mil or some arbitrary unit, OBV translates directly.
            Outline: format.Count > 0 ? format : null
        );

        var stats = new BoardviewStatsDto(
            TotalComponents: componentsMap.Count,
            TotalNets: netsMap.Count,
            TotalBridges: 0,
            TotalVias: 0,
            TotalTestPoints: testPointsMap.Count
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
            TestPoints: testPointsMap
        );
    }

    private class BrdPart { public string Name = ""; public string Type = ""; public string Side = ""; }
    private class BrdPin { public double X; public double Y; public int Probe; public int PartId; public string Net = ""; }
    private class BrdNail { public int Probe; public double X; public double Y; public string Side = ""; public string Net = ""; }
}
