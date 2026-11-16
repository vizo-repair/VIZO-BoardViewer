namespace VIZO.BoardViewer.Boardview.ParseBoardview.Formats;

public static class SafeBinaryReader
{
    public const int MaxDecompressedBytes = 200 * 1024 * 1024;

    public static byte[] DecompressLimited(Stream input, int maxBytes = MaxDecompressedBytes, CancellationToken ct = default)
    {
        using var outMs = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            total += read;
            if (total > maxBytes)
                throw new InvalidDataException($"Decompressed size exceeds {maxBytes} bytes.");
            outMs.Write(buffer, 0, read);
        }
        return outMs.ToArray();
    }

    public static bool TryAdvance(ref int ptr, int delta, int limit)
    {
        if (delta <= 0) return false;
        if (ptr + delta > limit) return false;
        ptr += delta;
        return true;
    }
}
