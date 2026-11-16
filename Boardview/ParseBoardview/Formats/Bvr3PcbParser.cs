// ============================================================================
// FILE: Features/Boardview/ParseBoardview/Formats/Bvr3PcbParser.cs (VIZO 2.0 + Metadata)
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization; 
using VIZO.BoardViewer.Boardview.ParseBoardview.Services;
using System.Text;

namespace VIZO.BoardViewer.Boardview.ParseBoardview.Formats;

public class Bvr3PcbParser : IBoardviewFormatParser
{
    private readonly ComponentRoleDeducer _roleDeducer;
    private readonly QuadrantAnchorScorer _anchorScorer;

    public string FormatName => "BoardViewer v3 (.bvr)";

    public Bvr3PcbParser(ComponentRoleDeducer roleDeducer, QuadrantAnchorScorer anchorScorer)
    {
        _roleDeducer = roleDeducer;
        _anchorScorer = anchorScorer;
    }

    public bool CanParse(ReadOnlySpan<byte> headerBytes, string fileName)
    {
        string headerStr = Encoding.ASCII.GetString(headerBytes);
        return headerStr.Contains("BVRAW_FORMAT_3");
    }

    public BoardviewNetlistDto? Parse(byte[] fileBytes, string fileName, Action<string>? onLog = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string content = Encoding.UTF8.GetString(fileBytes);

        var parts = new List<Bvr3Part>();
        var format = new List<BoardviewPointDto>();
        var outlineSegments = new List<Tuple<BoardviewPointDto, BoardviewPointDto>>();

        Bvr3Part currentPart = new Bvr3Part();
        Bvr3Pin currentPin = new Bvr3Pin();
        bool hasParts = false;

        using (var reader = new StringReader(content))
        {
            string? line;
            int lineCount = 0;
            while ((line = reader.ReadLine()) != null)
            {
                if ((++lineCount & 4095) == 0) ct.ThrowIfCancellationRequested();
                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("PART_NAME "))
                {
                    currentPart.Name = line.Substring(10).Trim();
                }
                else if (line.StartsWith("PART_SIDE "))
                {
                    string side = line.Substring(10).Trim();
                    if (side == "T") currentPart.Side = "TOP";
                    else if (side == "B") currentPart.Side = "BOTTOM";
                    else currentPart.Side = "BOTH";
                }
                else if (line.StartsWith("PART_ORIGIN "))
                {
                    var partsArr = line.Substring(12).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (partsArr.Length >= 2 && double.TryParse(partsArr[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double px) && double.TryParse(partsArr[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double py))
                    {
                        currentPart.OriginX = Math.Truncate(px);
                        currentPart.OriginY = Math.Truncate(py);
                    }
                }
                else if (line.StartsWith("PART_MOUNT "))
                {
                    string mount = line.Substring(11).Trim();
                    currentPart.Type = mount == "SMD" ? "SMD" : "TH";
                    currentPart.Package = mount;
                }
                else if (line.StartsWith("PIN_NUMBER "))
                {
                    currentPin.Number = line.Substring(11).Trim();
                }
                else if (line.StartsWith("PIN_NAME "))
                {
                    currentPin.Name = line.Substring(9).Trim();
                }
                else if (line.StartsWith("PIN_SIDE "))
                {
                    string side = line.Substring(9).Trim();
                    if (side == "T") currentPin.Side = "TOP";
                    else if (side == "B") currentPin.Side = "BOTTOM";
                    else currentPin.Side = "BOTH";
                }
                else if (line.StartsWith("PIN_ORIGIN "))
                {
                    var partsArr = line.Substring(11).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (partsArr.Length >= 2 && double.TryParse(partsArr[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double px) && double.TryParse(partsArr[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double py))
                    {
                        currentPin.OriginX = Math.Truncate(px) + currentPart.OriginX;
                        currentPin.OriginY = Math.Truncate(py) + currentPart.OriginY;
                    }
                }
                else if (line.StartsWith("PIN_NET "))
                {
                    currentPin.Net = line.Substring(8).Trim();
                }
                else if (line == "PIN_END")
                {
                    currentPart.Pins.Add(currentPin);
                    currentPin = new Bvr3Pin();
                }
                else if (line == "PART_END")
                {
                    parts.Add(currentPart);
                    currentPart = new Bvr3Part();
                    hasParts = true;
                }
                else if (line.StartsWith("OUTLINE_POINTS "))
                {
                    var partsArr = line.Substring(15).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < partsArr.Length - 1; i += 2)
                    {
                        if (double.TryParse(partsArr[i], NumberStyles.Any, CultureInfo.InvariantCulture, out double px) && double.TryParse(partsArr[i + 1], NumberStyles.Any, CultureInfo.InvariantCulture, out double py))
                        {
                            format.Add(new BoardviewPointDto(Math.Truncate(px), Math.Truncate(py)));
                        }
                    }
                }
                else if (line.StartsWith("OUTLINE_SEGMENTED "))
                {
                    var partsArr = line.Substring(18).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < partsArr.Length - 3; i += 4)
                    {
                        if (double.TryParse(partsArr[i], NumberStyles.Any, CultureInfo.InvariantCulture, out double p1x) && double.TryParse(partsArr[i + 1], NumberStyles.Any, CultureInfo.InvariantCulture, out double p1y) &&
                            double.TryParse(partsArr[i + 2], NumberStyles.Any, CultureInfo.InvariantCulture, out double p2x) && double.TryParse(partsArr[i + 3], NumberStyles.Any, CultureInfo.InvariantCulture, out double p2y))
                        {
                            outlineSegments.Add(new Tuple<BoardviewPointDto, BoardviewPointDto>(
                                new BoardviewPointDto(Math.Truncate(p1x), Math.Truncate(p1y)),
                                new BoardviewPointDto(Math.Truncate(p2x), Math.Truncate(p2y))
                            ));
                        }
                    }
                }
            }
        }

        if (!hasParts && format.Count == 0 && outlineSegments.Count == 0) return null;

        if (outlineSegments.Count > 0)
        {
            var currentSegment = outlineSegments[0];
            outlineSegments.RemoveAt(0);
            format.Add(currentSegment.Item1);
            format.Add(currentSegment.Item2);

            var startPoint = currentSegment.Item1;
            var endPoint = currentSegment.Item2;

            while ((startPoint.X != endPoint.X || startPoint.Y != endPoint.Y) && outlineSegments.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                bool matchFound = false;
                for (int i = 0; i < outlineSegments.Count; i++)
                {
                    var seg = outlineSegments[i];
                    if (Math.Abs(endPoint.X - seg.Item1.X) < 1e-6 && Math.Abs(endPoint.Y - seg.Item1.Y) < 1e-6)
                    {
                        format.Add(seg.Item2);
                        endPoint = seg.Item2;
                        outlineSegments.RemoveAt(i);
                        matchFound = true;
                        break;
                    }
                    else if (Math.Abs(endPoint.X - seg.Item2.X) < 1e-6 && Math.Abs(endPoint.Y - seg.Item2.Y) < 1e-6)
                    {
                        format.Add(seg.Item1);
                        endPoint = seg.Item1;
                        outlineSegments.RemoveAt(i);
                        matchFound = true;
                        break;
                    }
                }

                if (!matchFound)
                {
                    int nearestIdx = 0;
                    double minDis = double.MaxValue;
                    bool useFirst = true;

                    for (int i = 0; i < outlineSegments.Count; i++)
                    {
                        var seg = outlineSegments[i];
                        double d1 = Math.Abs(endPoint.X - seg.Item1.X) + Math.Abs(endPoint.Y - seg.Item1.Y);
                        double d2 = Math.Abs(endPoint.X - seg.Item2.X) + Math.Abs(endPoint.Y - seg.Item2.Y);

                        if (d1 < minDis) { minDis = d1; nearestIdx = i; useFirst = true; }
                        if (d2 < minDis) { minDis = d2; nearestIdx = i; useFirst = false; }
                    }

                    double dStart = Math.Abs(endPoint.X - startPoint.X) + Math.Abs(endPoint.Y - startPoint.Y);
                    if (dStart <= minDis)
                    {
                        format.Add(startPoint);
                        endPoint = startPoint;
                        break;
                    }

                    var nearestSeg = outlineSegments[nearestIdx];
                    if (useFirst)
                    {
                        format.Add(nearestSeg.Item1);
                        format.Add(nearestSeg.Item2);
                        endPoint = nearestSeg.Item2;
                    }
                    else
                    {
                        format.Add(nearestSeg.Item2);
                        format.Add(nearestSeg.Item1);
                        endPoint = nearestSeg.Item1;
                    }
                    outlineSegments.RemoveAt(nearestIdx);
                }
            }
        }

        var componentsMap = new Dictionary<string, BoardviewComponentDto>();
        var netsMap = new Dictionary<string, List<string>>();

        foreach (var p in parts)
        {
            var compPinsMap = new Dictionary<string, BoardviewPinDto>();
            foreach (var pin in p.Pins)
            {
                string pinKey = string.IsNullOrEmpty(pin.Number) ? pin.Name : pin.Number;
                if (string.IsNullOrEmpty(pinKey)) continue;

                string net = pin.Net;
                if (string.IsNullOrEmpty(net)) net = "UNCONNECTED";

                string side = pin.Side == "BOTH" ? p.Side : pin.Side;
                if (side == "BOTH") side = "TOP";

                compPinsMap[pinKey] = new BoardviewPinDto(
                    Name: pinKey,
                    Net: net,
                    X: pin.OriginX,
                    Y: pin.OriginY,
                    Layer: side
                );

                if (net != "UNCONNECTED")
                {
                    if (!netsMap.ContainsKey(net)) netsMap[net] = new List<string>();
                    netsMap[net].Add($"{p.Name}-{pinKey}");
                }
            }

            double minX = compPinsMap.Count > 0 ? compPinsMap.Values.Min(pin => pin.X) : p.OriginX;
            double minY = compPinsMap.Count > 0 ? compPinsMap.Values.Min(pin => pin.Y) : p.OriginY;
            double maxX = compPinsMap.Count > 0 ? compPinsMap.Values.Max(pin => pin.X) : p.OriginX;
            double maxY = compPinsMap.Count > 0 ? compPinsMap.Values.Max(pin => pin.Y) : p.OriginY;

            componentsMap[p.Name] = new BoardviewComponentDto(
                X: minX + (maxX - minX) / 2,
                Y: minY + (maxY - minY) / 2,
                MinX: minX, MinY: minY, MaxX: maxX, MaxY: maxY,
                Width: maxX - minX, Height: maxY - minY,
                Side: p.Side == "BOTH" ? "TOP" : p.Side,
                PinsCount: compPinsMap.Count,
                Type: _roleDeducer.DeduceRole(p.Name, p.Type),
                Pins: compPinsMap,
                Description: p.Package,
                Package: p.Package
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
            TotalTestPoints: 0
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
            TestPoints: new List<BoardviewTestPointDto>()
        );
    }

    private class Bvr3Part
    {
        public string Name = "";
        public string Side = "TOP";
        public double OriginX;
        public double OriginY;
        public string Type = "SMD";
        public string? Package = null;
        public List<Bvr3Pin> Pins = new List<Bvr3Pin>();
    }

    private class Bvr3Pin
    {
        public string Number = "";
        public string Name = "";
        public string Side = "TOP";
        public double OriginX;
        public double OriginY;
        public string Net = "UNCONNECTED";
    }
}