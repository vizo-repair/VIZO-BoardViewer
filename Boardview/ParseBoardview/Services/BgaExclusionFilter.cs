// ============================================================================
// FILE: Boardview/ParseBoardview/Services/BgaExclusionFilter.cs
// ============================================================================
namespace VIZO.BoardViewer.Boardview.ParseBoardview.Services;

using System.Linq;
using System.Text.RegularExpressions;

public static class BgaExclusionFilter
{
    private static readonly Regex BgaPinRegex = new(@"^[A-Za-z]{1,2}\d{1,3}$", RegexOptions.Compiled);

    public static bool IsExcludedBgaOrConnector(string refDes, BoardviewComponentDto comp)
    {
        string u = refDes.ToUpperInvariant();

        // 0. Canonical type from ComponentKind (priority, without regex)
        if (ComponentKinds.TryParse(comp.Type, out var kind) && ComponentKinds.IsExcludedFromLandmarks(kind))
            return true;

        // 1. Exclude jumpers, mounting holes, and test points (fallback for legacy netlists)
        if (u.StartsWith("PJ") || u.StartsWith("PJP") || u.StartsWith("TP") || u.StartsWith("TEST") || u.StartsWith("H") || u.StartsWith("MH"))
            return true;

        // 2. Exclude CPUs, chipsets, and GPUs
        if (u.StartsWith("UAPU") || u.StartsWith("UCPU") || u.StartsWith("UGPU") || u.StartsWith("UV") || u.StartsWith("VRAM") || u.StartsWith("PCH") || u.StartsWith("FCH"))
            return true;

        // 3. Exclude chips with > 64 pins
        if (comp.PinsCount > 64) return true;

        // 4. Exclude chips with matrix/BGA pins (A1, B2, AA1)
        if (comp.Pins != null && comp.Pins.Keys.Any(k => BgaPinRegex.IsMatch(k)))
            return true;

        return false;
    }
}