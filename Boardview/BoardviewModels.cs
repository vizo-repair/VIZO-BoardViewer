// ============================================================================
// FILE: Features/Boardview/BoardviewModels.cs (VIZO 2.0)
// ============================================================================
namespace VIZO.BoardViewer.Boardview;

using System.Text.Json.Serialization;
using VIZO.BoardViewer.Serialization;

public record BoardviewPointDto(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y
);

public record BoardviewBoundsDto(
    [property: JsonPropertyName("minX")] double MinX,
    [property: JsonPropertyName("minY")] double MinY,
    [property: JsonPropertyName("maxX")] double MaxX,
    [property: JsonPropertyName("maxY")] double MaxY,
    [property: JsonPropertyName("width")] double Width,
    [property: JsonPropertyName("height")] double Height,
    [property: JsonPropertyName("unit")] string Unit = "INCH",
    [property: JsonPropertyName("outline")] List<BoardviewPointDto>? Outline = null
);

public record BoardviewPinDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("net")] string Net,
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("layer")] string Layer = "TOP"
);

public record BoardviewComponentDto(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("minX")] double MinX,
    [property: JsonPropertyName("minY")] double MinY,
    [property: JsonPropertyName("maxX")] double MaxX,
    [property: JsonPropertyName("maxY")] double MaxY,
    [property: JsonPropertyName("width")] double Width,
    [property: JsonPropertyName("height")] double Height,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("pinsCount")] int PinsCount,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("pins")] Dictionary<string, BoardviewPinDto> Pins,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("value")] string? Value = null,
    [property: JsonPropertyName("partNumber")] string? PartNumber = null,
    [property: JsonPropertyName("package")] string? Package = null
);

public record BoardviewBridgeDto(
    [property: JsonPropertyName("refDes")] string RefDes,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("netA")] string NetA,
    [property: JsonPropertyName("netB")] string NetB,
    [property: JsonPropertyName("pinA")] string? PinA = null,
    [property: JsonPropertyName("pinB")] string? PinB = null,
    [property: JsonPropertyName("side")] string Side = "TOP"
);

public record BoardviewTestPointDto(
    [property: JsonPropertyName("nail")] string Nail,
    [property: JsonPropertyName("net")] string Net,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("isVia")] bool IsVia = false,
    [property: JsonPropertyName("probeLabel")] string? ProbeLabel = null,
    [property: JsonPropertyName("pointType")] string PointType = "VIA"
);

public record BoardviewAnchorCandidateDto(
    [property: JsonPropertyName("refDes")] string RefDes,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("quadrant")] string Quadrant,
    [property: JsonPropertyName("cadX")] double CadX,
    [property: JsonPropertyName("cadY")] double CadY,
    [property: JsonPropertyName("score")] double Score
);

// NEW: Physical trace description on the board
public record BoardviewTrackDto(
    [property: JsonPropertyName("net")] string Net,
    [property: JsonPropertyName("layer")] string Layer,
    [property: JsonPropertyName("width")] double Width,
    [property: JsonPropertyName("path")] List<BoardviewPointDto> Path
);

public record SuggestedAnchorDto(
    [property: JsonPropertyName("pin")]
    [property: JsonConverter(typeof(FlexibleIntJsonConverter))]
    int Pin,
    [property: JsonPropertyName("refDes")] string RefDes,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("cadX")] double CadX,
    [property: JsonPropertyName("cadY")] double CadY,
    [property: JsonPropertyName("targetX")] double TargetX,
    [property: JsonPropertyName("targetY")] double TargetY,
    [property: JsonPropertyName("box2d")] List<int>? Box2D = null,
    [property: JsonPropertyName("rawAiX")] double? RawAiX = null,
    [property: JsonPropertyName("rawAiY")] double? RawAiY = null
);

public record BoardviewStatsDto(
    [property: JsonPropertyName("totalComponents")] int TotalComponents,
    [property: JsonPropertyName("totalNets")] int TotalNets,
    [property: JsonPropertyName("totalBridges")] int TotalBridges,
    [property: JsonPropertyName("totalVias")] int TotalVias,
    [property: JsonPropertyName("totalTestPoints")] int TotalTestPoints
);

public record BoardviewNetlistDto(
    [property: JsonPropertyName("board")] string Board,
    [property: JsonPropertyName("format")] string Format,
    [property: JsonPropertyName("bounds")] BoardviewBoundsDto Bounds,
    [property: JsonPropertyName("stats")] BoardviewStatsDto Stats,
    [property: JsonPropertyName("components")] Dictionary<string, BoardviewComponentDto> Components,
    [property: JsonPropertyName("anchorCandidates")] List<BoardviewAnchorCandidateDto> AnchorCandidates,
    [property: JsonPropertyName("nets")] Dictionary<string, List<string>> Nets,
    [property: JsonPropertyName("bridges")] List<BoardviewBridgeDto> Bridges,
    [property: JsonPropertyName("testPoints")] List<BoardviewTestPointDto> TestPoints,
    // NEW: Array of traces for rendering real current flow
    [property: JsonPropertyName("tracks")] List<BoardviewTrackDto>? Tracks = null
);

public record CleanComponentDto(
    [property: JsonPropertyName("r")] string Role,
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("w")] double? Width = null,
    [property: JsonPropertyName("h")] double? Height = null,
    [property: JsonPropertyName("s")] string Side = "TOP",
    [property: JsonPropertyName("v")] string? Value = null,
    [property: JsonPropertyName("d")] string? Description = null,
    [property: JsonPropertyName("p")] string? Package = null
);

public record CleanBoardviewNetlistDto(
    [property: JsonPropertyName("board")] string Board,
    [property: JsonPropertyName("components")] Dictionary<string, CleanComponentDto> Components,
    [property: JsonPropertyName("nets")] Dictionary<string, List<string>> Nets,
    [property: JsonPropertyName("bridges")] List<BoardviewBridgeDto>? Bridges = null,
    [property: JsonPropertyName("anchorCandidates")] List<BoardviewAnchorCandidateDto>? AnchorCandidates = null
);

public record ParseBoardviewRequest(
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("fileBase64")] string FileBase64
);

public record AutoAlignBoardviewRequest(
    [property: JsonPropertyName("photoBase64")] string? PhotoBase64 = null,
    [property: JsonPropertyName("side")] string? Side = null,
    [property: JsonPropertyName("language")] string? Language = "en",
    [property: JsonPropertyName("netlist")] BoardviewNetlistDto? Netlist = null
);

public record AutoAlignBoardviewResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("detectedSide")] string? DetectedSide,
    [property: JsonPropertyName("suggestedAnchors")] List<SuggestedAnchorDto>? SuggestedAnchors,
    [property: JsonPropertyName("message")] string? Message = null,
    [property: JsonPropertyName("rotateDeg")] double RotateDeg = 0,
    [property: JsonPropertyName("flipH")] bool FlipH = false,
    [property: JsonPropertyName("debugLogs")] List<string>? DebugLogs = null,
    [property: JsonPropertyName("landmarks")] List<SuggestedAnchorDto>? Landmarks = null
);