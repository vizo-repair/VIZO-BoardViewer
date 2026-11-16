// ============================================================================
// FILE: Features/Boardview/ParseBoardview/Formats/CstPcbParser.cs
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using VIZO.BoardViewer.Boardview.ParseBoardview.Services;

namespace VIZO.BoardViewer.Boardview.ParseBoardview.Formats;

public class CstPcbParser : IBoardviewFormatParser
{
    private readonly ComponentRoleDeducer _roleDeducer;
    private readonly QuadrantAnchorScorer _anchorScorer;

    public string FormatName => "CST (.cst)";

    public CstPcbParser(ComponentRoleDeducer roleDeducer, QuadrantAnchorScorer anchorScorer)
    {
        _roleDeducer = roleDeducer;
        _anchorScorer = anchorScorer;
    }

    public bool CanParse(ReadOnlySpan<byte> headerBytes, string fileName)
    {
        return fileName.EndsWith(".cst", StringComparison.OrdinalIgnoreCase);
    }

    public BoardviewNetlistDto? Parse(byte[] fileBytes, string fileName, Action<string>? onLog = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (fileBytes.Length < 4) return null;

        var parts = new List<CstPart>();
        var nets = new List<string>();
        var pins = new List<CstPin>();

        int p = 0;

        try
        {
            int numParts = ReadUShort(fileBytes, ref p);
            p += 4;
            int sectionNameLength = ReadShort(fileBytes, ref p);
            ReadString(fileBytes, ref p, sectionNameLength);

            for (int i = 0; i < numParts; i++)
            {
                if ((i & 1023) == 0) ct.ThrowIfCancellationRequested();
                int nameLen = ReadByte(fileBytes, ref p);
                string partName = ReadString(fileBytes, ref p, nameLen);

                p += 4;
                int layer = ReadByte(fileBytes, ref p);

                string side = "BOTH";
                if (layer == 0x0C) side = "TOP";
                else if (layer == 0x01) side = "BOTTOM";

                parts.Add(new CstPart { Name = partName, Side = side });
                p += 6;
            }

            p -= 2;
            int numNets = ReadUShort(fileBytes, ref p);
            for (int i = 0; i < numNets; i++)
            {
                if ((i & 1023) == 0) ct.ThrowIfCancellationRequested();
                int netLen = ReadByte(fileBytes, ref p);
                string netName = ReadString(fileBytes, ref p, netLen);
                nets.Add(netName);
            }

            // Search for CPad section
            byte[] cpadSig = Encoding.ASCII.GetBytes("CPad");
            int cpadFound = IndexOf(fileBytes, cpadSig, p, ct);

            if (cpadFound != -1)
            {
                p = cpadFound - 8;
                int numPins = ReadUShort(fileBytes, ref p);
                p += 10;

                for (int i = 0; i < numPins; i++)
                {
                    if ((i & 255) == 0) ct.ThrowIfCancellationRequested();
                    short partId = ReadShort(fileBytes, ref p);
                    int partIdx = partId >= 0 ? partId : -1;

                    short probe = ReadShort(fileBytes, ref p);
                    short netId = ReadShort(fileBytes, ref p);

                    string netName = (netId >= 0 && netId < nets.Count) ? nets[netId] : "UNCONNECTED";

                    // Fix Bug #22: Read as unsigned ushort to avoid negative values > 32767
                    ushort posX = ReadUShort(fileBytes, ref p);
                    ushort posY = ReadUShort(fileBytes, ref p);

                    string side = "TOP";
                    if (partIdx >= 0 && partIdx < parts.Count)
                    {
                        side = parts[partIdx].Side;
                        if (side == "BOTH") side = "TOP";
                    }

                    short shape = ReadShort(fileBytes, ref p);

                    pins.Add(new CstPin
                    {
                        PartIndex = partIdx,
                        Probe = probe,
                        Net = netName,
                        X = posX,
                        Y = posY,
                        Side = side
                    });

                    p += 4;
                }
            }
        }
        catch (Exception)
        {
            return null;
        }

        var componentsMap = new Dictionary<string, BoardviewComponentDto>(StringComparer.OrdinalIgnoreCase);
        var netsMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        var pinsByPart = pins.GroupBy(pin => pin.PartIndex).ToDictionary(g => g.Key, g => g.ToList());

        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var compPinsMap = new Dictionary<string, BoardviewPinDto>(StringComparer.OrdinalIgnoreCase);

            if (pinsByPart.TryGetValue(i, out var partPins))
            {
                int pinIdx = 1;
                foreach (var pin in partPins)
                {
                    string pinName = pinIdx.ToString();
                    compPinsMap[pinName] = new BoardviewPinDto(
                        Name: pinName,
                        Net: pin.Net,
                        X: pin.X,
                        Y: pin.Y,
                        Layer: pin.Side
                    );

                    if (pin.Net != "UNCONNECTED")
                    {
                        if (!netsMap.ContainsKey(pin.Net)) netsMap[pin.Net] = new List<string>();
                        netsMap[pin.Net].Add($"{part.Name}.{pinName}");
                    }
                    pinIdx++;
                }
            }

            double minX = compPinsMap.Count > 0 ? compPinsMap.Values.Min(pin => pin.X) : 0;
            double minY = compPinsMap.Count > 0 ? compPinsMap.Values.Min(pin => pin.Y) : 0;
            double maxX = compPinsMap.Count > 0 ? compPinsMap.Values.Max(pin => pin.X) : 0;
            double maxY = compPinsMap.Count > 0 ? compPinsMap.Values.Max(pin => pin.Y) : 0;

            componentsMap[part.Name] = new BoardviewComponentDto(
                X: minX + (maxX - minX) / 2,
                Y: minY + (maxY - minY) / 2,
                MinX: minX, MinY: minY, MaxX: maxX, MaxY: maxY,
                Width: Math.Max(0.8, maxX - minX), Height: Math.Max(0.8, maxY - minY),
                Side: part.Side == "BOTH" ? "TOP" : part.Side,
                PinsCount: compPinsMap.Count,
                Type: _roleDeducer.DeduceRole(part.Name, "SMD"),
                Pins: compPinsMap
            );
        }

        double bMinX = pins.Count > 0 ? pins.Min(p => p.X) - 20 : 0;
        double bMinY = pins.Count > 0 ? pins.Min(p => p.Y) - 20 : 0;
        double bMaxX = pins.Count > 0 ? pins.Max(p => p.X) + 20 : 0;
        double bMaxY = pins.Count > 0 ? pins.Max(p => p.Y) + 20 : 0;

        var outline = new List<BoardviewPointDto>
        {
            new BoardviewPointDto(bMinX, bMinY),
            new BoardviewPointDto(bMaxX, bMinY),
            new BoardviewPointDto(bMaxX, bMaxY),
            new BoardviewPointDto(bMinX, bMaxY),
            new BoardviewPointDto(bMinX, bMinY)
        };

        var bounds = new BoardviewBoundsDto(
            MinX: bMinX, MinY: bMinY, MaxX: bMaxX, MaxY: bMaxY,
            Width: bMaxX - bMinX, Height: bMaxY - bMinY,
            Unit: "MIL",
            Outline: outline
        );

        var anchorCandidates = _anchorScorer.SelectAnchorCandidates(bounds, componentsMap);

        var stats = new BoardviewStatsDto(
            TotalComponents: componentsMap.Count,
            TotalNets: netsMap.Count,
            TotalBridges: 0,
            TotalVias: 0,
            TotalTestPoints: 0
        );

        return new BoardviewNetlistDto(
            Board: Path.GetFileNameWithoutExtension(fileName),
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

    private static short ReadShort(byte[] buf, ref int p)
    {
        if (p + 2 > buf.Length) throw new InvalidDataException("CST: unexpected EOF");
        short val = BitConverter.ToInt16(buf, p);
        p += 2;
        return val;
    }

    private static ushort ReadUShort(byte[] buf, ref int p)
    {
        if (p + 2 > buf.Length) throw new InvalidDataException("CST: unexpected EOF");
        ushort val = BitConverter.ToUInt16(buf, p);
        p += 2;
        return val;
    }

    private static int ReadByte(byte[] buf, ref int p)
    {
        if (p + 1 > buf.Length) throw new InvalidDataException("CST: unexpected EOF");
        int val = buf[p];
        p += 1;
        return val;
    }

    private static string ReadString(byte[] buf, ref int p, int len)
    {
        if (len < 0 || p + len > buf.Length) throw new InvalidDataException("CST: unexpected EOF");
        string val = Encoding.ASCII.GetString(buf, p, len);
        p += len;
        return val;
    }

    private static int IndexOf(byte[] array, byte[] pattern, int startOffset, CancellationToken ct = default)
    {
        for (int i = startOffset; i <= array.Length - pattern.Length; i++)
        {
            if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (array[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
    }

    private class CstPart
    {
        public string Name = "";
        public string Side = "TOP";
    }

    private class CstPin
    {
        public int PartIndex;
        public short Probe;
        public string Net = "";
        public double X;
        public double Y;
        public string Side = "TOP";
    }
}