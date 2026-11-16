// ============================================================================
// FILE: Features/Boardview/ParseBoardview/Formats/XzzPcbParser.cs
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using VIZO.BoardViewer.Boardview.ParseBoardview.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace VIZO.BoardViewer.Boardview.ParseBoardview.Formats;

public class XzzPcbParser : IBoardviewFormatParser
{
    private readonly ILogger<XzzPcbParser> _logger;
    private readonly ComponentRoleDeducer _roleDeducer;
    private readonly QuadrantAnchorScorer _anchorScorer;
    private readonly ulong _xzzKey;
    private const int XZZ_GLOBAL_SCALE = 10000;

    public string FormatName => "XinZhiZao (.tvw, .xzz)";

    public XzzPcbParser(ILogger<XzzPcbParser> logger, IConfiguration configuration, ComponentRoleDeducer roleDeducer, QuadrantAnchorScorer anchorScorer)
    {
        _logger = logger;
        _roleDeducer = roleDeducer;
        _anchorScorer = anchorScorer;
        string? keyStr = configuration["Boardview:XzzKey"];
        if (!string.IsNullOrEmpty(keyStr))
        {
            if (keyStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                _xzzKey = Convert.ToUInt64(keyStr.Substring(2), 16);
            else
                ulong.TryParse(keyStr, out _xzzKey);
        }
    }

    public bool CanParse(ReadOnlySpan<byte> headerBytes, string fileName)
    {
        if (headerBytes.Length < 6) return false;

        string sig = Encoding.ASCII.GetString(headerBytes.Slice(0, 6));
        if (sig == "XZZPCB") return true;

        if (headerBytes.Length > 0x10 && headerBytes[0x10] != 0x00)
        {
            byte xorKey = headerBytes[0x10];
            byte[] xorBuf = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                xorBuf[i] = (byte)(headerBytes[i] ^ xorKey);
            }
            string xorSig = Encoding.ASCII.GetString(xorBuf);
            if (xorSig == "XZZPCB") return true;
        }

        return false;
    }

    private bool CheckKey(ulong key)
    {
        byte[] keyParity = { 1, 1, 1, 1, 1, 1, 1, 0 };
        bool valid = true;
        for (int i = 0; i < 8; i++)
        {
            byte tmp = (byte)((key >> (i * 8)) & 0xff);
            tmp ^= (byte)(tmp >> 4);
            tmp ^= (byte)(tmp >> 2);
            tmp ^= (byte)(tmp >> 1);
            tmp = (byte)((~tmp) & 1);
            valid = valid && (tmp == keyParity[i]);
        }
        return valid;
    }

    public BoardviewNetlistDto? Parse(byte[] fileBytes, string fileName, Action<string>? onLog = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!CheckKey(_xzzKey))
        {
            onLog?.Invoke("Invalid XZZ PCB Key. Please configure 'Boardview:XzzKey' in appsettings.");
            _logger.LogWarning("Invalid XZZ PCB Key configured.");
            return null;
        }

        byte[] buf = fileBytes;
        
        string v6v6 = "v6v6555v6v6";
        byte[] v6v6Bytes = Encoding.ASCII.GetBytes(v6v6);
        int v6v6Found = IndexOf(buf, v6v6Bytes, ct);
        int searchEnd = v6v6Found == -1 ? buf.Length : v6v6Found;

        if (buf.Length >= 0x10 && buf[0x10] != 0x00)
        {
            byte xorKey = buf[0x10];
            for (int i = 0; i < searchEnd; i++)
            {
                if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
                buf[i] ^= xorKey;
            }
        }

        if (buf.Length < 0x28 + 4) return null;

        uint mainDataOffset = ReadUInt32(buf, 0x20);
        uint netDataOffset = ReadUInt32(buf, 0x28);

        uint mainDataStart = mainDataOffset + 0x20;
        uint netDataStart = netDataOffset + 0x20;

        if (buf.Length < mainDataStart + 4 || buf.Length < netDataStart + 4) return null;

        uint mainDataBlocksSize = ReadUInt32(buf, (int)mainDataStart);
        uint netBlockSize = ReadUInt32(buf, (int)netDataStart);

        if (buf.Length < netDataStart + netBlockSize + 4) return null;
        
        var netDict = new Dictionary<uint, string>();
        ParseNetBlock(buf, (int)netDataStart + 4, (int)netBlockSize, netDict, ct);

        var components = new Dictionary<string, BoardviewComponentDto>();
        var pins = new List<BoardviewPinDto>();
        var testPoints = new List<BoardviewTestPointDto>();
        var outline = new List<BoardviewPointDto>();
        var nets = new Dictionary<string, List<string>>();

        ProcessBlocks(buf, (int)mainDataStart, (int)mainDataBlocksSize, netDict, components, testPoints, outline, ct);

        // Compute translation
        double transX = 0, transY = 0;
        if (outline.Count > 0)
        {
            transX = outline.Min(p => p.X);
            transY = outline.Min(p => p.Y);
            
            for (int i = 0; i < outline.Count; i++)
            {
                outline[i] = new BoardviewPointDto(outline[i].X - transX, outline[i].Y - transY);
            }
        }

        // Apply translation to components and rebuild pins
        var translatedComponents = new Dictionary<string, BoardviewComponentDto>();
        foreach (var kvp in components)
        {
            var comp = kvp.Value;
            var translatedPins = new Dictionary<string, BoardviewPinDto>();
            foreach (var pinKvp in comp.Pins)
            {
                var p = pinKvp.Value;
                translatedPins[p.Name] = p with { X = p.X - transX, Y = p.Y - transY };
                
                // Add to nets
                if (!string.IsNullOrEmpty(p.Net) && p.Net != "UNCONNECTED")
                {
                    if (!nets.ContainsKey(p.Net)) nets[p.Net] = new List<string>();
                    nets[p.Net].Add($"{kvp.Key}.{p.Name}");
                }
            }
            
            translatedComponents[kvp.Key] = comp with {
                X = comp.X - transX,
                Y = comp.Y - transY,
                MinX = comp.MinX - transX,
                MinY = comp.MinY - transY,
                MaxX = comp.MaxX - transX,
                MaxY = comp.MaxY - transY,
                Pins = translatedPins
            };
        }

        var translatedTestPoints = new List<BoardviewTestPointDto>();
        foreach (var tp in testPoints)
        {
            translatedTestPoints.Add(tp with { X = tp.X - transX, Y = tp.Y - transY });
        }

        double minX = outline.Count > 0 ? outline.Min(p => p.X) : 0;
        double minY = outline.Count > 0 ? outline.Min(p => p.Y) : 0;
        double maxX = outline.Count > 0 ? outline.Max(p => p.X) : 0;
        double maxY = outline.Count > 0 ? outline.Max(p => p.Y) : 0;

        var bounds = new BoardviewBoundsDto(
            MinX: minX, MinY: minY, MaxX: maxX, MaxY: maxY,
            Width: maxX - minX, Height: maxY - minY, Unit: "MM", Outline: outline
        );

        var stats = new BoardviewStatsDto(
            TotalComponents: translatedComponents.Count,
            TotalNets: nets.Count,
            TotalBridges: 0,
            TotalVias: 0,
            TotalTestPoints: translatedTestPoints.Count
        );

        var anchorCandidates = _anchorScorer.SelectAnchorCandidates(bounds, translatedComponents);

        return new BoardviewNetlistDto(
            Board: fileName,
            Format: FormatName,
            Bounds: bounds,
            Stats: stats,
            Components: translatedComponents,
            AnchorCandidates: anchorCandidates,
            Nets: nets,
            Bridges: new List<BoardviewBridgeDto>(),
            TestPoints: translatedTestPoints
        );
    }

    private void ProcessBlocks(byte[] buf, int mainDataStart, int mainDataBlocksSize, Dictionary<uint, string> netDict, 
        Dictionary<string, BoardviewComponentDto> components, List<BoardviewTestPointDto> testPoints, List<BoardviewPointDto> outline, CancellationToken ct)
    {
        int currentPointer = mainDataStart + 4;
        int endPointer = mainDataStart + 4 + mainDataBlocksSize;
        int blocks = 0;

        while (currentPointer < endPointer && currentPointer + 5 <= buf.Length)
        {
            if ((++blocks & 255) == 0) ct.ThrowIfCancellationRequested();
            byte blockType = buf[currentPointer];
            currentPointer++;
            uint blockSize = ReadUInt32(buf, currentPointer);
            currentPointer += 4;

            if (currentPointer + blockSize > buf.Length) break;

            int blockStart = currentPointer;
            
            switch (blockType)
            {
                case 0x01: // ARC
                    ParseArcBlock(buf, blockStart, outline);
                    break;
                case 0x05: // LINE SEGMENT
                    ParseLineSegmentBlock(buf, blockStart, outline);
                    break;
                case 0x07: // PART/PIN
                    ParsePartBlock(buf, blockStart, (int)blockSize, netDict, components, ct);
                    break;
                case 0x09: // TEST PADS
                    ParseTestPadBlock(buf, blockStart, (int)blockSize, netDict, testPoints);
                    break;
            }

            currentPointer += (int)blockSize;
        }
    }

    private void ParseArcBlock(byte[] buf, int offset, List<BoardviewPointDto> outline)
    {
        if (offset + 28 > buf.Length) return;
        uint layer = ReadUInt32(buf, offset);
        if (layer != 28) return;

        uint x = ReadUInt32(buf, offset + 4);
        uint y = ReadUInt32(buf, offset + 8);
        uint r = ReadUInt32(buf, offset + 12);
        uint angleStart = ReadUInt32(buf, offset + 16);
        uint angleEnd = ReadUInt32(buf, offset + 20);

        double scale = XZZ_GLOBAL_SCALE;
        double cx = x / scale;
        double cy = y / scale;
        double rad = r / scale;
        double aStart = angleStart / scale;
        double aEnd = angleEnd / scale;

        // Simple arc approx
        if (aStart > aEnd) { var t = aStart; aStart = aEnd; aEnd = t; }
        if (aEnd - aStart > 180) aStart += 360;

        int numPoints = 10;
        double startRad = aStart * Math.PI / 180.0;
        double endRad = aEnd * Math.PI / 180.0;
        double step = (endRad - startRad) / (numPoints - 1);

        for (int i = 0; i < numPoints; i++)
        {
            double angle = startRad + i * step;
            outline.Add(new BoardviewPointDto(cx + rad * Math.Cos(angle), cy + rad * Math.Sin(angle)));
        }
    }

    private void ParseLineSegmentBlock(byte[] buf, int offset, List<BoardviewPointDto> outline)
    {
        if (offset + 24 > buf.Length) return;
        uint layer = ReadUInt32(buf, offset);
        if (layer != 28) return;

        uint x1 = ReadUInt32(buf, offset + 4);
        uint y1 = ReadUInt32(buf, offset + 8);
        uint x2 = ReadUInt32(buf, offset + 12);
        uint y2 = ReadUInt32(buf, offset + 16);

        double scale = XZZ_GLOBAL_SCALE;
        outline.Add(new BoardviewPointDto(x1 / scale, y1 / scale));
        outline.Add(new BoardviewPointDto(x2 / scale, y2 / scale));
    }

    private void ParseTestPadBlock(byte[] buf, int offset, int blockSize, Dictionary<uint, string> netDict, List<BoardviewTestPointDto> testPoints)
    {
        int ptr = offset + 4;
        if (ptr + 16 > buf.Length) return;
        
        uint x = ReadUInt32(buf, ptr); ptr += 4;
        uint y = ReadUInt32(buf, ptr); ptr += 4;
        ptr += 8; // inner_diameter + unknown

        uint nameLen = ReadUInt32(buf, ptr); ptr += 4;
        if (ptr + nameLen > buf.Length) return;
        
        string name = Encoding.ASCII.GetString(buf, ptr, (int)nameLen);
        
        int netPtr = offset + blockSize - 4;
        if (netPtr < 0 || netPtr + 4 > buf.Length) return;
        uint netIndex = ReadUInt32(buf, netPtr);

        string net = "";
        if (netDict.TryGetValue(netIndex, out var n) && n != "NC" && n != "UNCONNECTED")
            net = n;

        testPoints.Add(new BoardviewTestPointDto(
            Nail: name,
            Net: net,
            Side: "TOP",
            X: x / (double)XZZ_GLOBAL_SCALE,
            Y: y / (double)XZZ_GLOBAL_SCALE,
            IsVia: false,
            PointType: "TESTPAD"
        ));
    }

    private void ParsePartBlock(byte[] encryptedBuf, int offset, int blockSize, Dictionary<uint, string> netDict, Dictionary<string, BoardviewComponentDto> components, CancellationToken ct)
    {
        byte[] encData = new byte[blockSize];
        Array.Copy(encryptedBuf, offset, encData, 0, blockSize);
        byte[] buf = DesDecrypt(encData, _xzzKey, ct);

        int ptr = 0;
        if (ptr + 4 > buf.Length) return;
        uint partSize = ReadUInt32(buf, ptr); ptr += 4;
        ptr += 18;
        if (ptr + 4 > buf.Length) return;
        uint groupNameSize = ReadUInt32(buf, ptr); ptr += 4;
        if (groupNameSize <= 0 || !SafeBinaryReader.TryAdvance(ref ptr, (int)groupNameSize, buf.Length)) return;

        if (ptr >= buf.Length || buf[ptr] != 0x06) return;
        ptr += 31;
        
        if (ptr + 4 > buf.Length) return;
        uint partNameSize = ReadUInt32(buf, ptr); ptr += 4;
        if (partNameSize <= 0 || ptr + partNameSize > buf.Length) return;
        
        string partName = Encoding.ASCII.GetString(buf, ptr, (int)partNameSize);
        ptr += (int)partNameSize;

        var pins = new Dictionary<string, BoardviewPinDto>();

        int pinLoops = 0;
        while (ptr < partSize + 4 && ptr < buf.Length)
        {
            if ((++pinLoops & 255) == 0) ct.ThrowIfCancellationRequested();
            byte subType = buf[ptr++];
            if (subType == 0x01 || subType == 0x05 || subType == 0x06)
            {
                if (ptr + 4 > buf.Length) break;
                uint skipSize = ReadUInt32(buf, ptr);
                ptr += 4;
                if (skipSize <= 0 || !SafeBinaryReader.TryAdvance(ref ptr, (int)skipSize, buf.Length)) break;
            }
            else if (subType == 0x09)
            {
                var pin = ParsePinBlock(buf, ref ptr, netDict);
                if (pin != null)
                {
                    pins[pin.Name] = pin;
                }
            }
            else
            {
                // Unknown block
                break;
            }
        }

        double minX = pins.Count > 0 ? pins.Values.Min(p => p.X) : 0;
        double minY = pins.Count > 0 ? pins.Values.Min(p => p.Y) : 0;
        double maxX = pins.Count > 0 ? pins.Values.Max(p => p.X) : 0;
        double maxY = pins.Count > 0 ? pins.Values.Max(p => p.Y) : 0;

        components[partName] = new BoardviewComponentDto(
            X: minX + (maxX - minX) / 2,
            Y: minY + (maxY - minY) / 2,
            MinX: minX, MinY: minY, MaxX: maxX, MaxY: maxY,
            Width: maxX - minX, Height: maxY - minY,
            Side: "TOP",
            PinsCount: pins.Count,
            Type: _roleDeducer.DeduceRole(partName, "SMD"),
            Pins: pins
        );
    }

    private BoardviewPinDto? ParsePinBlock(byte[] buf, ref int ptr, Dictionary<uint, string> netDict)
    {
        if (ptr + 4 > buf.Length) return null;
        uint pinBlockSize = ReadUInt32(buf, ptr);
        int pinBlockEnd = ptr + 4 + (int)pinBlockSize;
        if (pinBlockEnd <= ptr || pinBlockEnd > buf.Length) return null;
        ptr += 8;

        if (ptr + 8 > buf.Length) return null;
        uint xOrigin = ReadUInt32(buf, ptr); ptr += 4;
        uint yOrigin = ReadUInt32(buf, ptr); ptr += 4;
        ptr += 8;

        if (ptr + 4 > buf.Length) return null;
        uint pinNameSize = ReadUInt32(buf, ptr); ptr += 4;
        if (pinNameSize <= 0 || ptr + pinNameSize > buf.Length) return null;
        string pinName = Encoding.ASCII.GetString(buf, ptr, (int)pinNameSize);
        ptr += (int)pinNameSize;
        ptr += 32;

        if (ptr + 4 > buf.Length) return null;
        uint netIndex = ReadUInt32(buf, ptr);
        if (pinBlockEnd <= ptr) return null;
        ptr = pinBlockEnd;

        string net = "UNCONNECTED";
        if (netDict.TryGetValue(netIndex, out var n) && n != "NC")
            net = n;

        return new BoardviewPinDto(
            Name: pinName,
            Net: net,
            X: xOrigin / (double)XZZ_GLOBAL_SCALE,
            Y: yOrigin / (double)XZZ_GLOBAL_SCALE,
            Layer: "TOP"
        );
    }

    private void ParseNetBlock(byte[] buf, int offset, int size, Dictionary<uint, string> netDict, CancellationToken ct)
    {
        int ptr = offset;
        int end = offset + size;
        int nets = 0;
        while (ptr < end && ptr + 8 <= buf.Length)
        {
            if ((++nets & 255) == 0) ct.ThrowIfCancellationRequested();
            int start = ptr;
            uint netSize = ReadUInt32(buf, ptr); ptr += 4;
            uint netIndex = ReadUInt32(buf, ptr); ptr += 4;

            int nameLen = (int)netSize - 8;
            if (nameLen <= 0 || ptr + nameLen > buf.Length) break;
            string netName = Encoding.ASCII.GetString(buf, ptr, nameLen);
            netDict[netIndex] = netName;
            if (!SafeBinaryReader.TryAdvance(ref ptr, nameLen, buf.Length) || ptr <= start) break;
        }
    }

    private byte[] DesDecrypt(byte[] inbuf, ulong key, CancellationToken ct)
    {
        byte[] outbuf = new byte[inbuf.Length];
        
        // Reverse key bytes to match OpenBoardView DES key endianness mapping
        byte[] keyBytes = BitConverter.GetBytes(key);
        Array.Reverse(keyBytes);
        
        using (var des = DES.Create())
        {
            des.Mode = CipherMode.ECB;
            des.Padding = PaddingMode.None;
            des.Key = keyBytes;
            
            using (var decryptor = des.CreateDecryptor())
            {
                // In OBV: "Convert 8 bytes of the input buffer into a 64-bit unsigned int with byte order reversed"
                // Then DES decrypt, then reverse again.
                for (int i = 0; i + 8 <= inbuf.Length; i += 8)
                {
                    if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
                    byte[] block = new byte[8];
                    Array.Copy(inbuf, i, block, 0, 8);
                    Array.Reverse(block);
                    
                    byte[] dec = new byte[8];
                    decryptor.TransformBlock(block, 0, 8, dec, 0);
                    
                    Array.Reverse(dec);
                    Array.Copy(dec, 0, outbuf, i, 8);
                }
                
                // Copy any remainder (though it should be multiple of 8)
                int rem = inbuf.Length % 8;
                if (rem > 0)
                {
                    Array.Copy(inbuf, inbuf.Length - rem, outbuf, inbuf.Length - rem, rem);
                }
            }
        }
        return outbuf;
    }

    private static uint ReadUInt32(byte[] buf, int startPos)
    {
        return (uint)(buf[startPos] | (buf[startPos + 1] << 8) | (buf[startPos + 2] << 16) | (buf[startPos + 3] << 24));
    }

    private static int IndexOf(byte[] array, byte[] pattern, CancellationToken ct)
    {
        for (int i = 0; i <= array.Length - pattern.Length; i++)
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
}
