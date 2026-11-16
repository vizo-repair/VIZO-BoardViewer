// ============================================================================
// FILE: Features/Boardview/ParseBoardview/Services/BgaExclusionFilter.cs
// ============================================================================
namespace VIZO.BoardViewer.Boardview.ParseBoardview.Services;

using System.Text.RegularExpressions;

public static class BgaExclusionFilter
{
    private static readonly Regex BgaPinRegex = new(@"^[A-Za-z]{1,2}\d{1,3}$", RegexOptions.Compiled);

    public static bool IsExcludedBgaOrConnector(string refDes, BoardviewComponentDto comp)
    {
        string u = refDes.ToUpperInvariant();

        // 1. Exclude jumpers, mounting holes, and test points
        if (u.StartsWith("PJ") || u.StartsWith("PJP") || u.StartsWith("TP") || u.StartsWith("TEST") || u.StartsWith("H") || u.StartsWith("MH"))
            return true;

        // 2. Exclude CPUs, hubs/chipsets, and GPUs
        if (u.StartsWith("UAPU") || u.StartsWith("UCPU") || u.StartsWith("UGPU") || u.StartsWith("UV") || u.StartsWith("VRAM") || u.StartsWith("PCH") || u.StartsWith("FCH"))
            return true;

        // 3. Exclude chips with pin count > 64
        if (comp.PinsCount > 64) return true;

        // 4. Exclude chips with matrix/grid pin names (A1, B2, AA1)
        if (comp.Pins != null && comp.Pins.Keys.Any(k => BgaPinRegex.IsMatch(k)))
            return true;

        return false;
    }
}