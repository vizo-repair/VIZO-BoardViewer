// ============================================================================
// FILE: Features/Boardview/ParseBoardview/Formats/IBoardviewFormatParser.cs
// ============================================================================
namespace VIZO.BoardViewer.Boardview.ParseBoardview.Formats;

public interface IBoardviewFormatParser
{
    string FormatName { get; }
    bool CanParse(ReadOnlySpan<byte> headerBytes, string fileName);
    BoardviewNetlistDto? Parse(byte[] fileBytes, string fileName, Action<string>? onLog = null, CancellationToken ct = default);
}