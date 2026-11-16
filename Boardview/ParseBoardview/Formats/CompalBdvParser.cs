// ============================================================================
// FILE: Features/Boardview/ParseBoardview/Formats/CompalBdvParser.cs
// ============================================================================
namespace VIZO.BoardViewer.Boardview.ParseBoardview.Formats;

public class CompalBdvParser : IBoardviewFormatParser
{
    private readonly GenCadAsciiParser _asciiParser;

    public CompalBdvParser(GenCadAsciiParser asciiParser) => _asciiParser = asciiParser;

    public string FormatName => "Compal XOR BDV";

    public bool CanParse(ReadOnlySpan<byte> headerBytes, string fileName) =>
        headerBytes.Length >= 3 && headerBytes[0] == (byte)'d' && headerBytes[1] == (byte)'d' && headerBytes[2] == (byte)':';

    public BoardviewNetlistDto? Parse(byte[] fileBytes, string fileName, Action<string>? onLog = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        onLog?.Invoke("🔓 [CompalBdvParser] Removing XOR mask using sliding key...");
        string decodedAscii = CompalXorDecoder.Decode(fileBytes, ct);
        return _asciiParser.ParseAsciiText(decodedAscii, fileName, onLog, ct);
    }
}