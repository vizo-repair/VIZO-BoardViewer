// ============================================================================
// FILE: Features/Boardview/ParseBoardview/Formats/Brd2PcbParser.cs
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using VIZO.BoardViewer.Boardview.ParseBoardview.Services;
using System.Linq;
using System.Text;

namespace VIZO.BoardViewer.Boardview.ParseBoardview.Formats;

public class Brd2PcbParser : IBoardviewFormatParser
{
    private readonly ComponentRoleDeducer _roleDeducer;
    private readonly QuadrantAnchorScorer _anchorScorer;

    public string FormatName => "BRD2 (.brd)";

    public Brd2PcbParser(ComponentRoleDeducer roleDeducer, QuadrantAnchorScorer anchorScorer)
    {
        _roleDeducer = roleDeducer;
        _anchorScorer = anchorScorer;
    }

    public bool CanParse(ReadOnlySpan<byte> headerBytes, string fileName)
    {
        string headerStr = Encoding.ASCII.GetString(headerBytes);
        return headerStr.Contains("BRDOUT:") && headerStr.Contains("NETS:");
    }

    public BoardviewNetlistDto? Parse(byte[] fileBytes, string fileName, Action<string>? onLog = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string content = Encoding.UTF8.GetString(fileBytes);
        
        var format = new List<BoardviewPointDto>();
        var netsDict = new Dictionary<int, string>();
        var parts = new List<Brd2Part>();
        var pins = new List<Brd2Pin>();
        var nails = new List<BoardviewTestPointDto>();

        double maxY = 0;
        int currentBlock = 0;

        using (var reader = new StringReader(content))
        {
            string? line;
            int lineCount = 0;
            while ((line = reader.ReadLine()) != null)
            {
                if ((++lineCount & 4095) == 0) ct.ThrowIfCancellationRequested();
                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("BRDOUT:"))
                {
                    currentBlock = 1;
                    var f = line.Substring(7).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (f.Length >= 3 && double.TryParse(f[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double mx) && double.TryParse(f[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double my))
                    {
                        maxY = my;
                    }
                    continue;
                }
                if (line.StartsWith("NETS:")) { currentBlock = 2; continue; }
                if (line.StartsWith("PARTS:")) { currentBlock = 3; continue; }
                if (line.StartsWith("PINS:")) { currentBlock = 4; continue; }
                if (line.StartsWith("NAILS:")) { currentBlock = 5; continue; }

                string[] fields = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                switch (currentBlock)
                {
                    case 1: // Format
                        if (fields.Length >= 2 && double.TryParse(fields[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double fx) && double.TryParse(fields[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double fy))
                        {
                            format.Add(new BoardviewPointDto(fx, fy));
                        }
                        break;

                    case 2: // Nets
                        if (fields.Length >= 2 && int.TryParse(fields[0], out int netId))
                        {
                            netsDict[netId] = string.Join(" ", fields.Skip(1));
                        }
                        break;

                    case 3: // PARTS
                        if (fields.Length >= 7)
                        {
                            string name = fields[0];
                            if (int.TryParse(fields[5], out int startOfPins) && int.TryParse(fields[6], out int sideInt))
                            {
                                string side = "BOTH";
                                if (sideInt == 1) side = "TOP";
                                else if (sideInt == 2) side = "BOTTOM";
                                
                                parts.Add(new Brd2Part
                                {
                                    Name = name,
                                    StartOfPins = startOfPins,
                                    Side = side
                                });
                            }
                        }
                        break;

                    case 4: // PINS
                        if (fields.Length >= 4)
                        {
                            if (double.TryParse(fields[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double px) && double.TryParse(fields[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double py) &&
                                int.TryParse(fields[2], out int pNetId) && int.TryParse(fields[3], out int pSideInt))
                            {
                                string pSide = "BOTH";
                                if (pSideInt == 1) pSide = "TOP";
                                else if (pSideInt == 2) pSide = "BOTTOM";

                                string netName = "UNCONNECTED";
                                if (netsDict.TryGetValue(pNetId, out var dictNet)) netName = dictNet;

                                pins.Add(new Brd2Pin
                                {
                                    X = px,
                                    Y = py,
                                    NetId = pNetId,
                                    NetName = netName,
                                    Side = pSide
                                });
                            }
                        }
                        break;

                    case 5: // NAILS
                        if (fields.Length >= 5)
                        {
                            if (int.TryParse(fields[0], out int probe) &&
                                double.TryParse(fields[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double nx) && double.TryParse(fields[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double ny) &&
                                int.TryParse(fields[3], out int nNetId) && int.TryParse(fields[4], out int nSideInt))
                            {
                                string netName = "UNCONNECTED";
                                if (netsDict.TryGetValue(nNetId, out var dictNet)) netName = dictNet;

                                string side = nSideInt == 1 ? "TOP" : "BOTTOM";
                                if (side == "BOTTOM") ny = maxY - ny; // Mirror Y for bottom nails

                                nails.Add(new BoardviewTestPointDto(
                                    Nail: probe.ToString(),
                                    Net: netName,
                                    Side: side,
                                    X: nx,
                                    Y: ny,
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

        // Assign pins to parts
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            
            int endOfPins = pins.Count;
            if (i < parts.Count - 1)
            {
                endOfPins = parts[i + 1].StartOfPins;
            }

            var compPinsMap = new Dictionary<string, BoardviewPinDto>();
            int pinIdx = 1;

            bool isDip = true;

            for (int cpi = part.StartOfPins; cpi < endOfPins && cpi < pins.Count; cpi++)
            {
                var pin = pins[cpi];
                
                double py = pin.Y;
                if (pin.Side != "TOP") py = maxY - py;

                if ((pin.Side == "TOP" && part.Side == "TOP") || (pin.Side == "BOTTOM" && part.Side == "BOTTOM"))
                {
                    isDip = false;
                }

                string pinName = pinIdx.ToString();
                compPinsMap[pinName] = new BoardviewPinDto(
                    Name: pinName,
                    Net: pin.NetName,
                    X: pin.X,
                    Y: py,
                    Layer: pin.Side == "BOTH" ? part.Side : pin.Side
                );

                if (pin.NetName != "UNCONNECTED")
                {
                    if (!netsMap.ContainsKey(pin.NetName)) netsMap[pin.NetName] = new List<string>();
                    netsMap[pin.NetName].Add($"{part.Name}.{pinName}");
                }

                pinIdx++;
            }

            string compSide = part.Side;
            if (isDip && compPinsMap.Count > 0)
            {
                compSide = "BOTH";
            }

            double minX = compPinsMap.Count > 0 ? compPinsMap.Values.Min(p => p.X) : 0;
            double minY = compPinsMap.Count > 0 ? compPinsMap.Values.Min(p => p.Y) : 0;
            double maxX = compPinsMap.Count > 0 ? compPinsMap.Values.Max(p => p.X) : 0;
            double maxY_c = compPinsMap.Count > 0 ? compPinsMap.Values.Max(p => p.Y) : 0;

            componentsMap[part.Name] = new BoardviewComponentDto(
                X: minX + (maxX - minX) / 2,
                Y: minY + (maxY_c - minY) / 2,
                MinX: minX, MinY: minY, MaxX: maxX, MaxY: maxY_c,
                Width: maxX - minX, Height: maxY_c - minY,
                Side: compSide == "BOTH" ? "TOP" : compSide,
                PinsCount: compPinsMap.Count,
                Type: _roleDeducer.DeduceRole(part.Name, isDip ? "TH" : "SMD"),
                Pins: compPinsMap
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
            TotalTestPoints: nails.Count
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
            TestPoints: nails
        );
    }

    private class Brd2Part
    {
        public string Name = "";
        public int StartOfPins;
        public string Side = "TOP";
    }

    private class Brd2Pin
    {
        public double X;
        public double Y;
        public int NetId;
        public string NetName = "UNCONNECTED";
        public string Side = "TOP";
    }
}
