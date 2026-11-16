namespace VIZO.BoardViewer.Boardview.ParseBoardview.Formats;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using VIZO.BoardViewer.Boardview.ParseBoardview.Services;

public class TvwPcbParser : IBoardviewFormatParser
{
    private readonly ComponentRoleDeducer _roleDeducer;
    private readonly QuadrantAnchorScorer _anchorScorer;

    public string FormatName => "Tebo-ICT View (.tvw)";

    public TvwPcbParser(ComponentRoleDeducer roleDeducer, QuadrantAnchorScorer anchorScorer)
    {
        _roleDeducer = roleDeducer;
        _anchorScorer = anchorScorer;
    }

    public bool CanParse(ReadOnlySpan<byte> headerBytes, string fileName)
    {
        if (headerBytes.Length < 24) return false;
        
        string headerStr = Encoding.Latin1.GetString(headerBytes.Slice(0, Math.Min(headerBytes.Length, 128)));

        if (headerStr.Contains("O95w-28ps49m") || headerStr.Contains("G5u9k8s") || headerStr.Contains("G34vS4z") || headerStr.Contains("Tebo-ictview"))
            return true;

        if (fileName.EndsWith(".tvw", StringComparison.OrdinalIgnoreCase) && !headerStr.Contains("XZZPCB"))
            return true;

        return false;
    }

    public BoardviewNetlistDto? Parse(byte[] fileBytes, string fileName, Action<string>? onLog = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var ms = new MemoryStream(fileBytes);
        using var r = new BinaryReader(ms);

        try
        {
            onLog?.Invoke("🔍 [TvwPcbParser] Reading extended TVW header (7 string sections)...");

            // 1. Header
            string fileType = ReadPString(r);
            uint unknown1 = r.ReadUInt32();
            string customer = ReadPString(r);
            string password = ReadPString(r);
            string date = ReadPString(r);
            string h5 = ReadPString(r);
            string h6 = ReadPString(r);
            string h7 = ReadPString(r);

            uint size1 = r.ReadUInt32();
            uint size2 = r.ReadUInt32();
            uint size3 = r.ReadUInt32();
            uint layerCount = r.ReadUInt32();
            uint zero = r.ReadUInt32();

            if (layerCount == 0 || layerCount > 64) layerCount = 16;

            onLog?.Invoke($"📑 [TvwPcbParser] Header: Customer='{DecodeTvwString(customer)}', Date='{DecodeTvwString(date)}', Layers={layerCount}");

            var layerPadsArray = new List<TvwPad>[layerCount];
            for (int li = 0; li < layerCount; li++) layerPadsArray[li] = new List<TvwPad>();

            var allTracks = new List<BoardviewTrackDtoAdapter>();
            var testPoints = new List<BoardviewTestPointDto>();
            uint topLayerIndex = 2; // Default Top layer
            uint botLayerIndex = 7; // Default Bottom layer

            // 2. Read layers
            for (uint i = 0; i < layerCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (r.BaseStream.Position >= r.BaseStream.Length - 100) break;

                try
                {
                    uint objType = DetectObjType(r); // 3=Logic, 1=Through
                    if (objType == 0) break;

                    uint m1 = r.ReadUInt32(); // 2
                    uint m2 = r.ReadUInt32(); // 1
                    string name = ReadPString(r);
                    string initialName = ReadPString(r);
                    string initialPath = ReadPString(r);
                    uint layerType = r.ReadUInt32(); // 1=Top, 2=Bottom, 3=Signal...
                    uint padColor = r.ReadUInt32();
                    uint lineColor = r.ReadUInt32();

                    if (layerType == 1) topLayerIndex = i;
                    if (layerType == 2) botLayerIndex = i;

                    if (objType == 3) // LogicLayer
                    {
                        var currentPads = layerPadsArray[i];

                        uint shapeCount = r.ReadUInt32();
                        if (shapeCount > 0 && shapeCount < 10000)
                        {
                            if (shapeCount >= 10) shapeCount -= 10;
                            for (int j = 0; j < shapeCount; j++)
                            {
                                r.ReadUInt32(); // one
                                r.ReadBytes(8); // size
                                uint sType = r.ReadUInt32();
                                if (sType == 0) r.ReadBytes(8);
                                else if (sType == 1) { r.ReadSingle(); r.ReadInt32(); }
                                else if (sType == 5)
                                {
                                    r.ReadUInt32(); ReadPString(r); r.ReadBytes(16);
                                    uint subCount = r.ReadUInt32();
                                    if (subCount > 5000) break;
                                    for (int k = 0; k < subCount; k++)
                                    {
                                        uint subType = r.ReadUInt32();
                                        if (subType == 2) { r.ReadBytes(12); uint vCount = r.ReadUInt32(); if (vCount > 50000) break; r.ReadBytes((int)vCount * 8); }
                                        else if (subType == 5) { r.ReadBytes(12); r.ReadBytes(16); r.ReadInt32(); }
                                    }
                                }
                                else if (sType == 3) { r.ReadSingle(); r.ReadInt32(); }
                                else { r.ReadBytes(8); }
                            }

                            uint skip1 = r.ReadUInt32();
                            bool extraData = (skip1 == 2);
                            r.ReadUInt32(); r.ReadUInt32();

                            // Pads
                            uint padCount = r.ReadUInt32();
                            if (padCount > 0 && padCount < 300000)
                            {
                                r.ReadUInt32(); // 2
                                for (int j = 0; j < padCount; j++)
                                {
                                    int net = r.ReadInt32();
                                    uint dcode = r.ReadUInt32();
                                    double px = r.ReadInt32() / 100.0;
                                    double py = r.ReadInt32() / 100.0;
                                    byte isExposed = r.ReadByte();
                                    byte isCopper = r.ReadByte();
                                    byte tpParam = r.ReadByte();

                                    if (isCopper != 0)
                                    {
                                        byte isSomething = r.ReadByte();
                                        if (tpParam == 1) r.ReadBytes(12);
                                        if (isExposed != 0 || isSomething != 0) r.ReadBytes(16);
                                        byte hasHole = r.ReadByte();
                                        byte tailParam = r.ReadByte();
                                        if (hasHole != 0) r.ReadBytes(16);
                                    }
                                    currentPads.Add(new TvwPad { NetIndex = net, X = px, Y = py });
                                }
                            }

                            // Lines
                            LoadLinesToTracks(r, allTracks, layerType);

                            // Arcs
                            uint arcCount = r.ReadUInt32();
                            if (arcCount > 0 && arcCount < 200000)
                            {
                                r.ReadUInt32();
                                for (int j = 0; j < arcCount; j++) { r.ReadInt32(); r.ReadUInt32(); r.ReadBytes(8); r.ReadInt32(); r.ReadSingle(); r.ReadSingle(); }
                            }

                            // Surfaces
                            uint surfCount = r.ReadUInt32();
                            if (surfCount > 0 && surfCount < 100000)
                            {
                                r.ReadUInt32();
                                for (int j = 0; j < surfCount; j++)
                                {
                                    r.ReadInt32();
                                    uint eCount = r.ReadUInt32();
                                    if (eCount > 50000) break;
                                    r.ReadBytes((int)eCount * 8);
                                    r.ReadInt32();
                                    uint vCount = r.ReadUInt32();
                                    if (vCount > 0 && vCount < 10000)
                                    {
                                        for (int k = 0; k < vCount; k++) { r.ReadUInt32(); uint veCount = r.ReadUInt32(); if (veCount > 10000) break; r.ReadBytes((int)veCount * 8); }
                                        r.ReadUInt32();
                                    }
                                }
                            }

                            if (extraData)
                            {
                                r.ReadBytes(16);
                                LoadLinesToTracks(r, allTracks, layerType);
                                arcCount = r.ReadUInt32();
                                if (arcCount > 0 && arcCount < 200000)
                                {
                                    r.ReadUInt32();
                                    for (int j = 0; j < arcCount; j++) { r.ReadInt32(); r.ReadUInt32(); r.ReadBytes(8); r.ReadInt32(); r.ReadSingle(); r.ReadSingle(); }
                                }
                                r.ReadUInt32();
                            }
                        }

                        // UnknownItems
                        uint uiCount = r.ReadUInt32();
                        r.ReadUInt32();
                        if (uiCount > 0 && uiCount < 50000)
                        {
                            for (int j = 0; j < uiCount; j++) { ReadPString(r); r.ReadBytes(8); r.ReadBytes(24); r.ReadBytes(3); r.ReadBytes(4); }
                            r.ReadUInt32();
                        }
                        r.ReadUInt32();

                        // TestPoints
                        uint tpCount = r.ReadUInt32();
                        if (tpCount > 0 && tpCount < 100000)
                        {
                            for (int j = 0; j < tpCount; j++)
                            {
                                r.ReadByte(); r.ReadBytes(16);
                                double px = r.ReadInt32() / 100.0;
                                double py = r.ReadInt32() / 100.0;
                                r.ReadBytes(4); r.ReadByte(); r.ReadBytes(12);
                                testPoints.Add(new BoardviewTestPointDto($"TP_{testPoints.Count + 1}", "", layerType == 1 ? "TOP" : "BOTTOM", px, py, false, "TP"));
                            }
                        }
                        r.ReadUInt32(); r.ReadUInt32();
                        uint tps2Count = r.ReadUInt32(); r.ReadUInt32();
                        if (tps2Count > 0 && tps2Count < 50000) for (int j = 0; j < tps2Count; j++) r.ReadBytes(54);
                        uint tps3Count = r.ReadUInt32(); r.ReadUInt32();
                        if (tps3Count > 0 && tps3Count < 50000) for (int j = 0; j < tps3Count; j++) r.ReadBytes(54);
                        uint seqCount = r.ReadUInt32(); uint seqParam = r.ReadUInt32();
                        if (seqCount > 0 && seqCount < 50000) for (int j = 0; j < seqCount; j++) r.ReadBytes(9);
                        if (seqParam == 1) r.ReadBytes(12);
                    }
                    else if (objType == 1) // ThroughLayer (Drills)
                    {
                        r.ReadUInt32(); // 0
                        r.ReadUInt32(); // 0
                        uint toolCount = r.ReadUInt32();

                        if (toolCount == 0) continue; // Empty Gigabyte drill section

                        toolCount--;
                        for (int j = 0; j < toolCount; j++)
                        {
                            r.ReadByte(); r.ReadByte(); r.ReadInt32(); r.ReadBytes(20); r.ReadBytes(3);
                        }
                        r.ReadByte();

                        uint drillCount = r.ReadUInt32();
                        r.ReadUInt32(); // v2
                        r.ReadBytes(16); // 4 zero dwords

                        for (int j = 0; j < drillCount; j++)
                        {
                            byte code = r.ReadByte();
                            if (code == 0x08) { r.ReadInt32(); r.ReadUInt32(); double x = r.ReadInt32() / 100.0; double y = r.ReadInt32() / 100.0; }
                            else if (code == 0x0A || code == 0x0B) r.ReadBytes(28);
                            else break;
                        }
                    }
                }
                catch (Exception layerEx)
                {
                    onLog?.Invoke($"  ⚠️ Warning in layer {i + 1} ({layerEx.Message}). Jumping to net table.");
                    break;
                }
            }

            onLog?.Invoke("🧩 [TvwPcbParser] Copper layers captured. Extracting net table (Nets)...");

            // 3. Search for net table using scanForNetTable algorithm from tvw-parser.ts
            var netTableResult = ScanForNetTable(fileBytes, Math.Max(0, (int)r.BaseStream.Position - 1000), ct);
            if (!netTableResult.HasValue)
            {
                netTableResult = ScanForNetTable(fileBytes, 0, ct);
            }

            var netNames = new List<string>();
            int scanPartsStartPos = (int)r.BaseStream.Position;

            if (netTableResult.HasValue)
            {
                r.BaseStream.Position = netTableResult.Value.Pos;
                uint netCount = (uint)netTableResult.Value.Count;
                for (int i = 0; i < netCount; i++)
                {
                    netNames.Add(ReadPString(r));
                }
                scanPartsStartPos = (int)r.BaseStream.Position;
                onLog?.Invoke($"🌐 [TvwPcbParser] Net table found at 0x{netTableResult.Value.Pos:X}: {netNames.Count} nets.");
            }
            else
            {
                onLog?.Invoke("⚠️ [TvwPcbParser] Net table not found, continuing parse without net names.");
            }

            // 4. Search for parts section using scanForPartsSection algorithm from tvw-parser.ts
            onLog?.Invoke("🔎 [TvwPcbParser] Searching for components (Parts)...");
            int partsSectionPos = ScanForPartsSection(fileBytes, scanPartsStartPos, onLog, ct);
            if (partsSectionPos == -1)
            {
                partsSectionPos = ScanForPartsSection(fileBytes, 0, onLog, ct);
            }

            if (partsSectionPos == -1)
            {
                onLog?.Invoke("❌ [TvwPcbParser] Failed to locate components section.");
                return null;
            }

            r.BaseStream.Position = partsSectionPos;
            uint partCount = r.ReadUInt32();
            r.ReadUInt32(); // skip dword

            onLog?.Invoke($"🎯 [TvwPcbParser] Section found! Loading {partCount} components...");

            // 5. Parse components and pins
            var componentsMap = new Dictionary<string, BoardviewComponentDto>(StringComparer.OrdinalIgnoreCase);
            var netsMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            for (uint i = 0; i < partCount; i++)
            {
                if ((i & 255) == 0) ct.ThrowIfCancellationRequested();

                var part = LoadPart(r);
                var compPinsMap = new Dictionary<string, BoardviewPinDto>(StringComparer.OrdinalIgnoreCase);

                // Bind primary pins to pads on part.Layer
                var padLayer = (part.Layer < layerCount) ? layerPadsArray[part.Layer] : null;

                foreach (var pin in part.Pins)
                {
                    uint padIdx = pin.Handle / 8;
                    TvwPad? pad = (padLayer != null && padIdx < padLayer.Count) ? padLayer[(int)padIdx] : null;

                    string netName = "UNCONNECTED";
                    if (pad != null && pad.NetIndex >= 0 && pad.NetIndex < netNames.Count)
                    {
                        netName = netNames[pad.NetIndex];
                    }

                    string pinSide = (part.Layer == botLayerIndex) ? "BOTTOM" : "TOP";
                    compPinsMap[pin.Name] = new BoardviewPinDto(
                        Name: pin.Name, Net: netName, X: pad != null ? pad.X : part.Cx, Y: pad != null ? pad.Y : part.Cy, Layer: pinSide
                    );

                    if (netName != "UNCONNECTED")
                    {
                        if (!netsMap.ContainsKey(netName)) netsMap[netName] = new List<string>();
                        netsMap[netName].Add($"{part.Name}.{pin.Name}");
                    }
                }

                // Bind extended dual-sided pins (Gigabyte Landrex edge-connectors)
                if (part.PinsExt != null && part.PinsExt.Count > 0)
                {
                    uint oppLayerIdx = (part.Layer == topLayerIndex) ? botLayerIndex : topLayerIndex;
                    var oppPadLayer = (oppLayerIdx < layerCount) ? layerPadsArray[oppLayerIdx] : null;

                    foreach (var pin in part.PinsExt)
                    {
                        uint padIdx = pin.Handle / 8;
                        TvwPad? pad = (oppPadLayer != null && padIdx < oppPadLayer.Count) ? oppPadLayer[(int)padIdx] : null;

                        string netName = "UNCONNECTED";
                        if (pad != null && pad.NetIndex >= 0 && pad.NetIndex < netNames.Count)
                        {
                            netName = netNames[pad.NetIndex];
                        }

                        string pinSide = (oppLayerIdx == botLayerIndex) ? "BOTTOM" : "TOP";
                        compPinsMap[pin.Name] = new BoardviewPinDto(
                            Name: pin.Name, Net: netName, X: pad != null ? pad.X : part.Cx, Y: pad != null ? pad.Y : part.Cy, Layer: pinSide
                        );

                        if (netName != "UNCONNECTED")
                        {
                            if (!netsMap.ContainsKey(netName)) netsMap[netName] = new List<string>();
                            netsMap[netName].Add($"{part.Name}.{pin.Name}");
                        }
                    }
                }

                string compSide = (part.Layer == botLayerIndex) ? "BOTTOM" : "TOP";
                string role = _roleDeducer.DeduceRole(part.Name, part.PartType == 0 ? "IC" : "SMD");

                componentsMap[part.Name] = new BoardviewComponentDto(
                    X: part.Cx, Y: part.Cy, MinX: part.MinX, MinY: part.MinY, MaxX: part.MaxX, MaxY: part.MaxY,
                    Width: Math.Abs(part.MaxX - part.MinX), Height: Math.Abs(part.MaxY - part.MinY),
                    Side: compSide, PinsCount: compPinsMap.Count, Type: role,
                    Pins: compPinsMap, Value: part.Value, Description: part.PackageName
                );
            }

            // 6. Attach net names to tracks
            foreach (var track in allTracks)
            {
                if (track.NetIndex >= 0 && track.NetIndex < netNames.Count)
                {
                    track.ResolvedNetName = netNames[track.NetIndex];
                }
            }

            var validTracks = allTracks
                .Where(t => !string.IsNullOrEmpty(t.ResolvedNetName))
                .Select(t => new BoardviewTrackDto(
                    Net: t.ResolvedNetName!,
                    Layer: t.LayerType == 2 ? "BOTTOM" : "TOP",
                    Width: 5.0,
                    Path: new List<BoardviewPointDto> { new(t.X1, t.Y1), new(t.X2, t.Y2) }
                ))
                .ToList();

            // 7. Build Bounds and DTO
            double bMinX = componentsMap.Count > 0 ? componentsMap.Values.Min(c => c.MinX) - 50 : 0;
            double bMinY = componentsMap.Count > 0 ? componentsMap.Values.Min(c => c.MinY) - 50 : 0;
            double bMaxX = componentsMap.Count > 0 ? componentsMap.Values.Max(c => c.MaxX) + 50 : 0;
            double bMaxY = componentsMap.Count > 0 ? componentsMap.Values.Max(c => c.MaxY) + 50 : 0;

            var bounds = new BoardviewBoundsDto(
                MinX: bMinX, MinY: bMinY, MaxX: bMaxX, MaxY: bMaxY,
                Width: bMaxX - bMinX, Height: bMaxY - bMinY, Unit: "MIL",
                Outline: new List<BoardviewPointDto> { new(bMinX, bMinY), new(bMaxX, bMinY), new(bMaxX, bMaxY), new(bMinX, bMaxY) }
            );

            var anchorCandidates = _anchorScorer.SelectAnchorCandidates(bounds, componentsMap);
            var stats = new BoardviewStatsDto(componentsMap.Count, netsMap.Count, 0, 0, testPoints.Count);

            onLog?.Invoke($"✅ [TvwPcbParser] SUCCESS! Parsed: {componentsMap.Count} components, {netsMap.Count} nets, {validTracks.Count} track segments.");

            return new BoardviewNetlistDto(
                Board: Path.GetFileNameWithoutExtension(fileName),
                Format: FormatName,
                Bounds: bounds,
                Stats: stats,
                Components: componentsMap,
                AnchorCandidates: anchorCandidates,
                Nets: netsMap,
                Bridges: [],
                TestPoints: testPoints,
                Tracks: validTracks
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            onLog?.Invoke($"❌ [TvwPcbParser] Parser error: {ex.Message}");
            return null;
        }
    }

    private static (int Pos, int Count)? ScanForNetTable(byte[] view, int startPos, CancellationToken ct)
    {
        int endPos = view.Length - 8;
        for (int p = Math.Max(0, startPos); p < endPos; p++)
        {
            if ((p & 65535) == 0) ct.ThrowIfCancellationRequested();
            uint v1 = BitConverter.ToUInt32(view, p);
            uint v2 = BitConverter.ToUInt32(view, p + 4);

            if (v1 != v2 || v1 < 50 || v1 > 50000) continue;

            int pstrStart = p + 8;
            int np = pstrStart;
            int valid = 0;
            int maxCheck = Math.Min((int)v1, 20);
            bool ok = true;

            for (int i = 0; i < maxCheck; i++)
            {
                if (np >= view.Length) { ok = false; break; }
                byte len = view[np++];
                if (len > 80 || np + len > view.Length) { ok = false; break; }
                if (len > 0)
                {
                    for (int c = 0; c < len; c++)
                    {
                        byte ch = view[np + c];
                        if (ch < 0x20 || ch > 0x7E) { ok = false; break; }
                    }
                    if (!ok) break;
                    valid++;
                }
                np += len;
            }

            if (ok && valid >= Math.Min(10, maxCheck))
            {
                return (pstrStart, (int)v1);
            }
        }
        return null;
    }

    private static int ScanForPartsSection(byte[] view, int from, Action<string>? onLog, CancellationToken ct)
    {
        int end = view.Length - 8;
        for (int p = Math.Max(0, from); p < end; p++)
        {
            if ((p & 65535) == 0) ct.ThrowIfCancellationRequested();
            uint partCount = BitConverter.ToUInt32(view, p);
            if (partCount < 2 || partCount > 50000) continue;

            if (!IsPrintablePstrAt(view, p + 8)) continue;

            if (LooksLikePartsHeader(view, p))
            {
                onLog?.Invoke($"🎯 [TvwPcbParser] Parts section confirmed at 0x{p:X}: {partCount} parts");
                return p;
            }
        }
        return -1;
    }

    private static bool IsPrintablePstrAt(byte[] view, int pos)
    {
        if (pos >= view.Length) return false;
        byte len = view[pos];
        if (len < 1 || len > 40 || pos + 1 + len > view.Length) return false;
        for (int i = 0; i < len; i++)
        {
            byte ch = view[pos + 1 + i];
            if (ch < 0x20 || ch > 0x7E) return false;
        }
        return true;
    }

    private static bool LooksLikePartsHeader(byte[] view, int pos)
    {
        try
        {
            using var ms = new MemoryStream(view, pos, view.Length - pos);
            using var r = new BinaryReader(ms);
            uint partCount = r.ReadUInt32();
            r.ReadUInt32(); // skip dword
            if (partCount < 2 || partCount > 50000) return false;

            int probe = Math.Min((int)partCount, 12);
            for (int i = 0; i < probe; i++)
            {
                var part = LoadPart(r);
                if (string.IsNullOrEmpty(part.Name) || part.Name.Length > 40) return false;
                if (!part.Name.All(c => c >= 0x20 && c <= 0x7E)) return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static TvwPart LoadPart(BinaryReader r)
    {
        string name = ReadPString(r);
        double minX = r.ReadInt32() / 100.0;
        double minY = r.ReadInt32() / 100.0;
        double maxX = r.ReadInt32() / 100.0;
        double maxY = r.ReadInt32() / 100.0;
        double cx = r.ReadInt32() / 100.0;
        double cy = r.ReadInt32() / 100.0;
        int angle = r.ReadInt32();
        r.ReadUInt32(); // decal
        uint partType = r.ReadUInt32();
        r.ReadUInt32(); // z1 = 0
        double heightMils = r.ReadInt32() / 100.0;
        bool flag0 = r.ReadByte() != 0;
        string value = ReadPString(r);
        ReadPString(r); // tolP
        ReadPString(r); // tolN
        string packageName = ReadPString(r);
        string serial = "";
        if (flag0)
        {
            serial = ReadPString(r);
            r.ReadUInt32(); // z2 = 0
        }
        uint pinCount = r.ReadUInt32();
        uint layer = r.ReadUInt32();
        r.ReadUInt32(); // p2 = 0

        if (pinCount > 15000) throw new InvalidDataException("Invalid TVW pinCount");

        var pins = new List<TvwPin>((int)pinCount);
        uint extContactSum = 0;
        for (int i = 0; i < pinCount; i++)
        {
            var (pin, z2) = LoadPin(r, trailing: true);
            pins.Add(pin);
            extContactSum += z2;
        }

        List<TvwPin>? pinsExt = null;
        if (extContactSum > 0)
        {
            if (extContactSum > 15000) throw new InvalidDataException("Invalid TVW extContactSum");
            r.ReadUInt32(); // contFlag
            r.ReadUInt32(); // reserved
            pinsExt = new List<TvwPin>((int)extContactSum);
            for (int i = 0; i < extContactSum; i++)
            {
                pinsExt.Add(LoadPin(r, trailing: i != extContactSum - 1).Pin);
            }
        }
        else if (pinCount > 0 && LooksLikePinExtension(r))
        {
            r.ReadUInt32(); // contFlag
            r.ReadUInt32(); // reserved
            pinsExt = new List<TvwPin>((int)pinCount);
            for (int i = 0; i < pinCount; i++)
            {
                pinsExt.Add(LoadPin(r, trailing: i != pinCount - 1).Pin);
            }
        }

        return new TvwPart
        {
            Name = name, MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY,
            Cx = cx, Cy = cy, Angle = angle, PartType = partType, Layer = layer,
            Value = value, PackageName = packageName, Pins = pins, PinsExt = pinsExt
        };
    }

    private static (TvwPin Pin, uint Z2) LoadPin(BinaryReader r, bool trailing = true)
    {
        uint handle = r.ReadUInt32();
        r.ReadUInt32(); // z1 = 0
        uint id = r.ReadUInt32();
        string name = ReadPString(r);
        uint z2 = 0;
        if (trailing) z2 = r.ReadUInt32();
        return (new TvwPin { Handle = handle, Id = id, Name = name }, z2);
    }

    private static bool LooksLikePinExtension(BinaryReader r)
    {
        long pos = r.BaseStream.Position;
        if (pos + 20 > r.BaseStream.Length) return false;
        try
        {
            uint contFlag = r.ReadUInt32();
            uint reserved = r.ReadUInt32();
            if (reserved != 0) return false;
            if (contFlag == 0 || contFlag > 16) return false;
            uint handle = r.ReadUInt32();
            uint z1 = r.ReadUInt32();
            if (z1 != 0) return false;
            if (handle == 0) return false;
            uint id = r.ReadUInt32();
            if (id > 100000) return false;
            return true;
        }
        finally
        {
            r.BaseStream.Position = pos;
        }
    }

    private static void LoadLinesToTracks(BinaryReader r, List<BoardviewTrackDtoAdapter> allTracks, uint layerType)
    {
        uint lineCount = r.ReadUInt32();
        if (lineCount > 0 && lineCount < 200000)
        {
            r.ReadUInt32();
            for (int j = 0; j < lineCount; j++)
            {
                int net = r.ReadInt32();
                uint dcode = r.ReadUInt32();
                double x1 = r.ReadInt32() / 100.0;
                double y1 = r.ReadInt32() / 100.0;
                double x2 = r.ReadInt32() / 100.0;
                double y2 = r.ReadInt32() / 100.0;
                if (layerType == 1 || layerType == 2)
                {
                    allTracks.Add(new BoardviewTrackDtoAdapter { NetIndex = net, LayerType = layerType, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 });
                }
            }
        }
    }

    private static uint DetectObjType(BinaryReader r)
    {
        for (int i = 0; i < 4; i++)
        {
            uint type = r.ReadUInt32();
            if (type != 0) return type;
        }
        return 0;
    }

    private static string ReadPString(BinaryReader r)
    {
        byte len = r.ReadByte();
        if (len == 0) return string.Empty;
        return Encoding.UTF8.GetString(r.ReadBytes(len));
    }

    private static string DecodeTvwString(string s)
    {
        char[] arr = s.ToCharArray();
        for (int i = 0; i < arr.Length; i++)
        {
            char c = arr[i];
            if (c >= 'a' && c <= 'j')
            {
                int x = c - (i % 3) - 4;
                if (x < 'a') x += 10;
                arr[i] = (char)(154 - x);
            }
            else if (c >= 'k' && c <= 'z')
            {
                int x = c - (i % 10) - 5;
                if (x < 'k') x += 16;
                arr[i] = (char)x;
            }
            else if (c >= 'A' && c <= 'Z')
            {
                int x = c + (i % 10) + 5;
                if (x > 'Z') x -= 26;
                arr[i] = (char)x;
            }
            else if (c >= '0' && c <= '9')
            {
                int x = c + (i % 3) + 4;
                if (x > '9') x -= 10;
                arr[i] = (char)(x + 49);
            }
        }
        return new string(arr);
    }

    private class TvwPad
    {
        public int NetIndex { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }

    private class TvwPin
    {
        public uint Handle;
        public uint Id;
        public string Name = "";
    }

    private class TvwPart
    {
        public string Name = "";
        public double MinX, MinY, MaxX, MaxY, Cx, Cy;
        public int Angle;
        public uint PartType;
        public uint Layer;
        public string Value = "";
        public string PackageName = "";
        public List<TvwPin> Pins = new();
        public List<TvwPin>? PinsExt;
    }

    private class BoardviewTrackDtoAdapter
    {
        public int NetIndex { get; set; }
        public string? ResolvedNetName { get; set; }
        public uint LayerType { get; set; }
        public double X1, Y1, X2, Y2;
    }
}