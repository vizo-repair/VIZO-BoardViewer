namespace VIZO.BoardViewer.Boardview.ParseBoardview;

using VIZO.BoardViewer.Boardview;

public sealed class BoardviewParseGate
{
    public const int MaxParallel = 2;

    private readonly BoardviewFormatDetector _detector;
    private readonly SemaphoreSlim _gate = new(MaxParallel, MaxParallel);

    public BoardviewParseGate(BoardviewFormatDetector detector)
    {
        _detector = detector;
    }

    public async Task<BoardviewNetlistDto?> DetectAndParseAsync(
        byte[] fileBytes,
        string fileName,
        Action<string>? onLog = null,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await Task.Run(() => _detector.DetectAndParse(fileBytes, fileName, onLog, ct), ct);
        }
        finally
        {
            _gate.Release();
        }
    }
}
