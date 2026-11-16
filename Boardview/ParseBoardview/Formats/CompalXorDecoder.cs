// ============================================================================
// FILE: Features/Boardview/ParseBoardview/Formats/CompalXorDecoder.cs
// ============================================================================
namespace VIZO.BoardViewer.Boardview.ParseBoardview.Formats;

using System.Text;

public static class CompalXorDecoder
{
    public static string Decode(byte[] bytes, CancellationToken ct = default)
    {
        int key = 160;
        var decodedBytes = new List<byte>(bytes.Length);

        for (int i = 0; i < bytes.Length; i++)
        {
            if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
            byte b = bytes[i];

            if (b == 13 && (i + 1 < bytes.Length) && bytes[i + 1] == 10)
            {
                decodedBytes.Add(13);
                decodedBytes.Add(10);
                i++;
                key++;
                if (key > 285) key = 159;
                continue;
            }
            if (b == 10)
            {
                decodedBytes.Add(10);
                key++;
                if (key > 285) key = 159;
                continue;
            }

            int val = (key - b) % 256;
            if (val < 0) val += 256;
            decodedBytes.Add((byte)val);
        }

        return Encoding.Latin1.GetString(decodedBytes.ToArray());
    }
}