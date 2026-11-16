// ============================================================================
// FILE: Features/Boardview/ParseBoardview/Formats/FzPcbParser.cs
// ============================================================================
namespace VIZO.BoardViewer.Boardview.ParseBoardview.Formats;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VIZO.BoardViewer.Boardview;
using VIZO.BoardViewer.Boardview.ParseBoardview.Services;

public class FzPcbParser : IBoardviewFormatParser
{
    private readonly ILogger<FzPcbParser> _logger;
    private readonly ComponentRoleDeducer _roleDeducer;
    private readonly QuadrantAnchorScorer _anchorScorer;
    private readonly uint[] _fzKey = new uint[44];

    private static readonly uint[] DefaultFzKey = new uint[]
    {
        0x25d8d248, 0xe1502405, 0x56b5d486, 0x69213fe0,
        0xa22490ec, 0x01fdd9fa, 0x0681955f, 0x0fac202d,
        0xdac9eeb4, 0xf6024aba, 0xcd8b4cc6, 0x9f307c8e,
        0x4ab8fad7, 0x232f967d, 0x5e8666a3, 0xde966d4b,
        0xc64bfb1c, 0xea7fb092, 0x1a751a7e, 0x37e8f0bc,
        0x3359c8f3, 0x969ac22b, 0x610f5804, 0xd99d10e6,
        0xc58d54d6, 0x1f9aea8b, 0x8e388c1a, 0xe4f7d2ed,
        0x3e5da1f6, 0xedfe818a, 0x7252b016, 0xb503a170,
        0xc4128fb6, 0x2c93ceeb, 0x53539a6e, 0xdacf7668,
        0x3ab78e52, 0x8ee9d815, 0x7043f799, 0xc6a05dcf,
        0x727f1da2, 0x0dfd983b, 0x78c53872, 0x00945692
    };

    public string FormatName => "Phoneboard / Flexboardview / Asus (.fz)";

    public FzPcbParser(
        ILogger<FzPcbParser> logger,
        ComponentRoleDeducer roleDeducer,
        QuadrantAnchorScorer anchorScorer,
        IConfiguration configuration)
    {
        _logger = logger;
        _roleDeducer = roleDeducer;
        _anchorScorer = anchorScorer;
        Array.Copy(DefaultFzKey, _fzKey, 44);

        string? keyStr = configuration["Boardview:FzKey"];
        if (!string.IsNullOrWhiteSpace(keyStr))
        {
            var parts = keyStr.Split(new[] { ' ', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 44)
            {
                for (int i = 0; i < 44; i++)
                {
                    string p = parts[i].Trim();
                    if (p.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        _fzKey[i] = Convert.ToUInt32(p.Substring(2), 16);
                    else
                        uint.TryParse(p, out _fzKey[i]);
                }
            }
        }
    }

    public bool CanParse(ReadOnlySpan<byte> headerBytes, string fileName)
    {
        if (fileName.EndsWith(".fz", StringComparison.OrdinalIgnoreCase)) return true;
        if (headerBytes.Length > 6 && headerBytes[4] == 0x78 && headerBytes[5] is 0x01 or 0x9C or 0xDA)
            return true;
        return false;
    }

    private void DecodeRc6(byte[] source, uint[] key, CancellationToken ct)
    {
        const int logw = 5;
        const uint r = 20;

        uint A = 0, B = 0, C = 0, D = 0;
        byte[] ibuf = new byte[16];

        for (int pos = 0; pos < source.Length; ++pos)
        {
            if ((pos & 4095) == 0) ct.ThrowIfCancellationRequested();
            B += key[0];
            D += key[1];
            for (uint i = 1; i <= r; ++i)
            {
                uint t = BitOperations.RotateLeft(B * (2 * B + 1), logw);
                uint u = BitOperations.RotateLeft(D * (2 * D + 1), logw);
                A = BitOperations.RotateLeft(A ^ t, (int)(u & 31)) + key[2 * i];
                C = BitOperations.RotateLeft(C ^ u, (int)(t & 31)) + key[2 * i + 1];

                uint tmp = A;
                A = B; B = C; C = D; D = tmp;
            }
            A += key[2 * r + 2];
            C += key[2 * r + 3];

            byte currentByte = source[pos];
            source[pos] = (byte)(currentByte ^ (A & 0xFF));

            for (int i = 0; i < 15; ++i)
            {
                ibuf[i] = ibuf[i + 1];
            }
            ibuf[15] = currentByte;

            A = (uint)(ibuf[0] | (ibuf[1] << 8) | (ibuf[2] << 16) | (ibuf[3] << 24));
            B = (uint)(ibuf[4] | (ibuf[5] << 8) | (ibuf[6] << 16) | (ibuf[7] << 24));
            C = (uint)(ibuf[8] | (ibuf[9] << 8) | (ibuf[10] << 16) | (ibuf[11] << 24));
            D = (uint)(ibuf[12] | (ibuf[13] << 8) | (ibuf[14] << 16) | (ibuf[15] << 24));
        }
    }

    private byte[]? DecompressZlib(byte[] data, int offset, int length, CancellationToken ct)
    {
        if (offset < 0 || length <= 0 || offset + length > data.Length) return null;

        try
        {
            using var ms = new MemoryStream(data, offset, length);
            using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
            var inflated = SafeBinaryReader.DecompressLimited(zlib, ct: ct);
            if (inflated.Length > 0) return inflated;
        }
        catch (InvalidDataException) { return null; }
        catch (OperationCanceledException) { throw; }
        catch { }

        try
        {
            int skip = (data[offset] == 0x78) ? 2 : 0;
            using var ms = new MemoryStream(data, offset + skip, length - skip);
            using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
            var inflated = SafeBinaryReader.DecompressLimited(deflate, ct: ct);
            if (inflated.Length > 0) return inflated;
        }
        catch (InvalidDataException) { return null; }
        catch (OperationCanceledException) { throw; }
        catch { }

        return null;
    }

    public BoardviewNetlistDto? Parse(byte[] fileBytes, string fileName, Action<string>? onLog = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (fileBytes.Length < 16) return null;

        byte[]? contentBuf = null;
        byte[]? descrBuf = null;

        bool isPlainZlib = fileBytes.Length > 6 && fileBytes[4] == 0x78 && fileBytes[5] is 0x01 or 0x9C or 0xDA;

        if (isPlainZlib)
        {
            try
            {
                (contentBuf, descrBuf) = SplitAndDecompress(fileBytes, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }
        else
        {
            try
            {
                DecodeRc6(fileBytes, _fzKey, ct);
                (contentBuf, descrBuf) = SplitAndDecompress(fileBytes, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }

        if (contentBuf == null) return null;

        string contentText = Encoding.ASCII.GetString(contentBuf);
        var partDescriptions = ParseFzDescriptions(descrBuf, onLog);

        var components = new Dictionary<string, BoardviewComponentDto>(StringComparer.OrdinalIgnoreCase);
        var nets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var testPoints = new List<BoardviewTestPointDto>();

        double multiplier = 1.0;
        int currentBlock = 0; // 1 = Parts, 2 = Pins, 3 = TestVia

        using (var reader = new StringReader(contentText))
        {
            string? line;
            int lineCount = 0;
            while ((line = reader.ReadLine()) != null)
            {
                if ((++lineCount & 4095) == 0) ct.ThrowIfCancellationRequested();
                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (line.Equals("UNIT:millimeters", StringComparison.OrdinalIgnoreCase))
                {
                    multiplier = 25.4;
                    continue;
                }

                if (line.StartsWith("A!"))
                {
                    string header = line[2..].Trim();
                    if (header.StartsWith("REFDES", StringComparison.OrdinalIgnoreCase))
                        currentBlock = 1;
                    else if (header.StartsWith("NET_NAME", StringComparison.OrdinalIgnoreCase))
                        currentBlock = 2;
                    else if (header.StartsWith("TESTVIA", StringComparison.OrdinalIgnoreCase))
                        currentBlock = 3;
                    else
                        currentBlock = -1;
                    continue;
                }

                if (!line.StartsWith("S!")) continue;
                string[] fields = line[2..].Split('!');

                switch (currentBlock)
                {
                    case 1: // Components (REFDES)
                        {
                            if (fields.Length < 1) break;
                            string name = fields[0].Trim();
                            if (string.IsNullOrWhiteSpace(name)) break;

                            string smirror = fields.Length > 3 ? fields[3].Trim() : "";
                            string side = smirror.Equals("YES", StringComparison.OrdinalIgnoreCase) ? "BOTTOM" : "TOP";

                            string uName = name.ToUpperInvariant();
                            bool isTh = uName.StartsWith("DIMM") || uName.StartsWith("PCI") ||
                                        uName.StartsWith("EATX") || uName.StartsWith("AUDIO") ||
                                        uName.StartsWith("HDMI") || uName.StartsWith("USB") ||
                                        uName.StartsWith("LGA") || uName.StartsWith("SOCKET") ||
                                        uName.StartsWith("J") || uName.StartsWith("CN");

                            components[name] = new BoardviewComponentDto(
                                X: 0, Y: 0, MinX: 0, MinY: 0, MaxX: 0, MaxY: 0,
                                Width: 0, Height: 0, Side: isTh ? "BOTH" : side, PinsCount: 0,
                                Type: _roleDeducer.DeduceRole(name, isTh ? "TH" : "SMD"),
                                Pins: new Dictionary<string, BoardviewPinDto>(StringComparer.OrdinalIgnoreCase)
                            );
                        }
                        break;

                    case 2: // Component pins (NET_NAME)
                        {
                            if (fields.Length < 6) break;

                            string net = fields[0].Trim();
                            string part = fields[1].Trim();
                            string snum = fields[2].Trim();
                            string pinName = fields[3].Trim();

                            if (string.IsNullOrEmpty(part)) break;

                            string pinKey = (string.IsNullOrEmpty(snum) || snum == "0") && !string.IsNullOrEmpty(pinName)
                                ? pinName
                                : (string.IsNullOrEmpty(snum) ? "1" : snum);

                            string pxStr = fields[4].Trim().Replace(',', '.');
                            string pyStr = fields[5].Trim().Replace(',', '.');
                            if (!double.TryParse(pxStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double px) ||
                                !double.TryParse(pyStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double py))
                            {
                                break;
                            }

                            double finalX = Math.Round(px * multiplier, 4);
                            double finalY = Math.Round(py * multiplier, 4);

                            string uPart = part.ToUpperInvariant();
                            bool isThroughHole = uPart.StartsWith("DIMM") || uPart.StartsWith("PCI") ||
                                                 uPart.StartsWith("EATX") || uPart.StartsWith("AUDIO") ||
                                                 uPart.StartsWith("HDMI") || uPart.StartsWith("USB") ||
                                                 uPart.StartsWith("LGA") || uPart.StartsWith("SOCKET") ||
                                                 uPart.StartsWith("J") || uPart.StartsWith("CN");

                            if (!components.TryGetValue(part, out var comp))
                            {
                                comp = new BoardviewComponentDto(
                                    X: finalX, Y: finalY, MinX: finalX, MinY: finalY, MaxX: finalX, MaxY: finalY,
                                    Width: 0, Height: 0, Side: isThroughHole ? "BOTH" : "TOP", PinsCount: 0,
                                    Type: _roleDeducer.DeduceRole(part, isThroughHole ? "TH" : "SMD"),
                                    Pins: new Dictionary<string, BoardviewPinDto>(StringComparer.OrdinalIgnoreCase)
                                );
                                components[part] = comp;
                            }

                            string cleanNet = string.IsNullOrEmpty(net) || net.Equals("NC", StringComparison.OrdinalIgnoreCase)
                                ? "UNCONNECTED"
                                : net;

                            string pinLayer = isThroughHole ? "BOTH" : (comp.Side ?? "TOP");

                            comp.Pins[pinKey] = new BoardviewPinDto(
                                Name: !string.IsNullOrEmpty(pinName) ? pinName : pinKey,
                                Net: cleanNet,
                                X: finalX,
                                Y: finalY,
                                Layer: pinLayer
                            );

                            if (cleanNet != "UNCONNECTED")
                            {
                                if (!nets.TryGetValue(cleanNet, out var netPins))
                                {
                                    netPins = new List<string>();
                                    nets[cleanNet] = netPins;
                                }
                                netPins.Add($"{part}.{pinKey}");
                            }
                        }
                        break;

                    case 3: // Test points (TESTVIA)
                        {
                            if (TryReadFzTestVia(fields, testPoints.Count, out string nailNet, out string nailName, out double vx, out double vy, out string nailSide, out bool isVia))
                            {
                                testPoints.Add(new BoardviewTestPointDto(
                                    Nail: nailName,
                                    Net: nailNet,
                                    Side: nailSide,
                                    X: Math.Round(vx * multiplier, 4),
                                    Y: Math.Round(vy * multiplier, 4),
                                    IsVia: isVia,
                                    PointType: isVia ? "VIA" : "TEST_POINT"
                                ));
                            }
                        }
                        break;
                }
            }
        }

        // 1. First determine physical board bounds from electrical nets:
        var allConnectedPins = components.Values
            .SelectMany(c => c.Pins.Values)
            .Where(p => p.Net != "UNCONNECTED")
            .ToList();

        if (allConnectedPins.Count == 0)
        {
            allConnectedPins = components.Values.SelectMany(c => c.Pins.Values).ToList();
        }

        if (allConnectedPins.Count == 0) return null;

        const double OUTLINE_MARGIN = 20.0;
        double boardMinX = allConnectedPins.Min(p => p.X) - OUTLINE_MARGIN;
        double boardMaxX = allConnectedPins.Max(p => p.X) + OUTLINE_MARGIN;
        double boardMinY = allConnectedPins.Min(p => p.Y) - OUTLINE_MARGIN;
        double boardMaxY = allConnectedPins.Max(p => p.Y) + OUTLINE_MARGIN;

        // 2. Finalize components: keep only parts that lie on the board
        // (filter out drawing junk like INDEX6, 0.5H and out-of-bounds heatsinks)
        var finalizedComponents = new Dictionary<string, BoardviewComponentDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var (refDes, comp) in components)
        {
            if (comp.Pins != null && comp.Pins.Count > 0)
            {
                // If all pins of the part lie far outside the board (in the drawing margin), skip it
                double compCenterX = comp.Pins.Values.Average(p => p.X);
                double compCenterY = comp.Pins.Values.Average(p => p.Y);

                if (compCenterX < boardMinX - 80 || compCenterX > boardMaxX + 80 ||
                    compCenterY < boardMinY - 80 || compCenterY > boardMaxY + 80)
                {
                    continue;
                }

                double minX = comp.Pins.Values.Min(p => p.X);
                double minY = comp.Pins.Values.Min(p => p.Y);
                double maxX = comp.Pins.Values.Max(p => p.X);
                double maxY = comp.Pins.Values.Max(p => p.Y);

                double w = Math.Round(maxX - minX, 4);
                double h = Math.Round(maxY - minY, 4);

                if (w < 0.04) w = 0.8;
                if (h < 0.04) h = 0.8;

                string uRef = refDes.ToUpperInvariant();
                bool isTh = comp.Side == "BOTH" ||
                            uRef.StartsWith("DIMM") || uRef.StartsWith("PCI") ||
                            uRef.StartsWith("EATX") || uRef.StartsWith("AUDIO") ||
                            uRef.StartsWith("HDMI") || uRef.StartsWith("USB") ||
                            uRef.StartsWith("LGA") || uRef.StartsWith("SOCKET") ||
                            uRef.StartsWith("J") || uRef.StartsWith("CN");

                partDescriptions.TryGetValue(refDes, out var extraInfo);

                finalizedComponents[refDes] = comp with
                {
                    X = Math.Round((minX + maxX) / 2.0, 4),
                    Y = Math.Round((minY + maxY) / 2.0, 4),
                    MinX = Math.Round(minX, 4),
                    MinY = Math.Round(minY, 4),
                    MaxX = Math.Round(maxX, 4),
                    MaxY = Math.Round(maxY, 4),
                    Width = w,
                    Height = h,
                    Side = isTh ? "BOTH" : (comp.Side ?? "TOP"),
                    PinsCount = comp.Pins.Count,
                    Type = _roleDeducer.DeduceRole(refDes, isTh ? "TH" : comp.Type),
                    Description = !string.IsNullOrEmpty(extraInfo.Desc) ? extraInfo.Desc : comp.Description,
                    Value = !string.IsNullOrEmpty(extraInfo.Desc) ? extraInfo.Desc : comp.Value,
                    PartNumber = !string.IsNullOrEmpty(extraInfo.PartNo) ? extraInfo.PartNo : comp.PartNumber
                };
            }
        }

        // Filter out test points lying outside the board
        testPoints = testPoints.Where(tp =>
            tp.X >= boardMinX - 40 && tp.X <= boardMaxX + 40 &&
            tp.Y >= boardMinY - 40 && tp.Y <= boardMaxY + 40
        ).ToList();

        var outline = new List<BoardviewPointDto>
        {
            new(Math.Round(boardMinX, 4), Math.Round(boardMinY, 4)),
            new(Math.Round(boardMaxX, 4), Math.Round(boardMinY, 4)),
            new(Math.Round(boardMaxX, 4), Math.Round(boardMaxY, 4)),
            new(Math.Round(boardMinX, 4), Math.Round(boardMaxY, 4)),
            new(Math.Round(boardMinX, 4), Math.Round(boardMinY, 4))
        };

        var bounds = new BoardviewBoundsDto(
            MinX: Math.Round(boardMinX, 4),
            MinY: Math.Round(boardMinY, 4),
            MaxX: Math.Round(boardMaxX, 4),
            MaxY: Math.Round(boardMaxY, 4),
            Width: Math.Round(boardMaxX - boardMinX, 4),
            Height: Math.Round(boardMaxY - boardMinY, 4),
            Unit: "MIL",
            Outline: outline
        );

        var anchorCandidates = _anchorScorer.SelectAnchorCandidates(bounds, finalizedComponents);

        var stats = new BoardviewStatsDto(
            TotalComponents: finalizedComponents.Count,
            TotalNets: nets.Count,
            TotalBridges: 0,
            TotalVias: testPoints.Count(t => t.IsVia),
            TotalTestPoints: testPoints.Count(t => !t.IsVia)
        );

        onLog?.Invoke($"✅ [FzParser] Board parsed successfully: {finalizedComponents.Count} components, {nets.Count} nets, {testPoints.Count} test points.");

        return new BoardviewNetlistDto(
            Board: fileName,
            Format: FormatName,
            Bounds: bounds,
            Stats: stats,
            Components: finalizedComponents,
            AnchorCandidates: anchorCandidates,
            Nets: nets,
            Bridges: new List<BoardviewBridgeDto>(),
            TestPoints: testPoints
        );
    }

    internal static bool TryReadFzTestVia(
        string[] fields,
        int fallbackIndex,
        out string net,
        out string name,
        out double x,
        out double y,
        out string side,
        out bool isVia)
    {
        net = "UNCONNECTED";
        name = $"TP_{fallbackIndex + 1}";
        x = 0;
        y = 0;
        side = "TOP";
        isVia = false;
        if (fields == null || fields.Length < 7) return false;

        int locIdx = -1;
        for (int i = fields.Length - 1; i >= 2; i--)
        {
            string token = fields[i].Trim();
            if (token.Equals("T", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                locIdx = i;
                break;
            }
        }
        if (locIdx < 2) return false;

        string xStr = fields[locIdx - 2].Trim().Replace(',', '.');
        string yStr = fields[locIdx - 1].Trim().Replace(',', '.');
        if (!double.TryParse(xStr, NumberStyles.Any, CultureInfo.InvariantCulture, out x) ||
            !double.TryParse(yStr, NumberStyles.Any, CultureInfo.InvariantCulture, out y))
            return false;

        int start = 0;
        string first = fields[0].Trim();
        if (first.Equals("Y", StringComparison.OrdinalIgnoreCase) || first.Equals("N", StringComparison.OrdinalIgnoreCase))
            start = 1;

        if (start < fields.Length)
            net = fields[start].Trim();
        if (start + 1 < locIdx - 2)
        {
            string refdes = fields[start + 1].Trim();
            if (!string.IsNullOrEmpty(refdes)) name = refdes;
        }
        if (string.IsNullOrEmpty(net) || net.Equals("NC", StringComparison.OrdinalIgnoreCase))
            net = "UNCONNECTED";

        side = fields[locIdx].Trim().Equals("T", StringComparison.OrdinalIgnoreCase) ? "TOP" : "BOTTOM";
        isVia = name.Contains("VIA", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith('$')
            || net.Contains("VIA", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static Dictionary<string, (string Desc, string PartNo)> ParseFzDescriptions(byte[]? descrBuf, Action<string>? onLog = null)
    {
        var dict = new Dictionary<string, (string Desc, string PartNo)>(StringComparer.OrdinalIgnoreCase);
        if (descrBuf == null || descrBuf.Length < 4) return dict;

        string text = Encoding.UTF8.GetString(descrBuf);
        using var reader = new StringReader(text);
        string? line;

        int locCol = -1;
        int descCol = -1;
        int partNoCol = -1;

        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;

            // Column separator: in FZ this is always tab '\t' or '!'
            char sep = line.Contains('\t') ? '\t' : (line.Contains('!') ? '!' : ',');
            string[] cols = line.Split(sep);

            // 1. Search for column header row (PARTNUMBER, DESCRIPTION, LOCATION)
            if (locCol == -1)
            {
                for (int i = 0; i < cols.Length; i++)
                {
                    string h = cols[i].Trim().ToUpperInvariant();
                    if (h.Contains("LOCATION") || h.Contains("REFDES") || h.Contains("DESIGNATOR"))
                        locCol = i;
                    else if (h.Contains("DESCRIPTION") || h.Contains("DESC"))
                        descCol = i;
                    else if (h.Contains("PARTNUMBER") || h.Contains("PART_NUMBER") || h.Contains("PARTNO"))
                        partNoCol = i;
                }
                if (locCol != -1) continue; // Header found, proceed to data rows
            }

            // Standard ASUS FZ column order: [0]=PARTNUMBER, [1]=DESCRIPTION, [2]=QTY, [3]=LOCATION
            int effLoc = locCol != -1 ? locCol : (cols.Length >= 4 ? 3 : -1);
            int effDesc = descCol != -1 ? descCol : (cols.Length >= 2 ? 1 : -1);
            int effPart = partNoCol != -1 ? partNoCol : (cols.Length >= 1 ? 0 : -1);

            if (effLoc != -1 && effDesc != -1 && cols.Length > effLoc && cols.Length > effDesc)
            {
                string desc = cols[effDesc].Trim();
                string partNo = effPart != -1 && cols.Length > effPart ? cols[effPart].Trim() : "";
                string locs = cols[effLoc].Trim();

                // LOCATION cell can contain one RefDes (C25) or a comma/space-separated list (C25,C26,C27)
                string[] refList = locs.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
                foreach (var rRaw in refList)
                {
                    string r = rRaw.Trim().ToUpperInvariant();
                    if (!string.IsNullOrEmpty(r) && !dict.ContainsKey(r))
                    {
                        dict[r] = (desc, partNo);
                    }
                }
            }
            else if (cols.Length == 2)
            {
                string r = cols[0].Trim().ToUpperInvariant();
                string d = cols[1].Trim();
                if (r.Length > 0 && d.Length > 0 && char.IsLetter(r[0]) && !dict.ContainsKey(r))
                {
                    dict[r] = (d, "");
                }
            }
        }

        onLog?.Invoke($"📑 [FzParser] BOM specification: parsed {dict.Count} part entries.");
        return dict;
    }
    private (byte[]? Content, byte[]? Descr) SplitAndDecompress(byte[] buf, CancellationToken ct)
    {
        if (buf.Length < 16) return (null, null);

        // The last 4 bytes of the file store the description block size (uint32 LE)
        int descrSize = BitConverter.ToInt32(buf, buf.Length - 4);
        if (descrSize <= 4 || descrSize >= buf.Length - 4)
        {
            var single = DecompressZlib(buf, 4, buf.Length - 4, ct);
            return (single, null);
        }

        // Check both possible offsets of the compressed BOM stream
        int descrOffsetA = buf.Length - descrSize;
        int descrOffsetB = buf.Length - descrSize + 4;

        byte[]? descr = TryDecompressSlice(buf, descrOffsetA, descrSize - 4, ct);
        int chosenOffset = descrOffsetA;

        if (descr == null && descrSize > 8)
        {
            descr = TryDecompressSlice(buf, descrOffsetB, descrSize - 8, ct);
            if (descr != null) chosenOffset = descrOffsetB;
        }

        int contentOffset = 4;
        int contentSize = chosenOffset - contentOffset;
        byte[]? content = null;

        if (contentSize > 0)
        {
            content = DecompressZlib(buf, contentOffset, contentSize, ct);
        }

        if (content == null && chosenOffset != descrOffsetB && descrOffsetB > 4)
        {
            content = DecompressZlib(buf, 4, descrOffsetB - 4, ct);
        }

        return (content, descr);
    }
    private byte[]? TryDecompressSlice(byte[] buf, int offset, int length, CancellationToken ct)
    {
        if (offset < 0 || length <= 0 || offset + length > buf.Length) return null;
        return DecompressZlib(buf, offset, length, ct);
    }
}