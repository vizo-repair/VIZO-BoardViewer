// ============================================================================
// FILE: Boardview/ParseBoardview/Services/QuadrantAnchorScorer.cs
// Deterministic selection of 4 landmarks (Max Quadrilateral Area + Anti-Clustering)
// ============================================================================
namespace VIZO.BoardViewer.Boardview.ParseBoardview.Services;

using System;
using System.Collections.Generic;
using System.Linq;

public class QuadrantAnchorScorer
{
    private record CandidatePin(string RefDes, BoardviewComponentDto Comp, double Pin1X, double Pin1Y, double Quality);

    public List<BoardviewAnchorCandidateDto> SelectAnchorCandidates(
        BoardviewBoundsDto bounds,
        Dictionary<string, BoardviewComponentDto> components)
    {
        var result = new List<BoardviewAnchorCandidateDto>();
        if (components == null || components.Count == 0) return result;

        double boardDiagonal = Math.Sqrt(Math.Pow(bounds.Width, 2) + Math.Pow(bounds.Height, 2));
        double minDistance = Math.Max(0.5, boardDiagonal * 0.28);

        foreach (var side in new[] { "TOP", "BOTTOM" })
        {
            var eligible = components
                .Where(kvp => kvp.Value.Side.Equals(side, StringComparison.OrdinalIgnoreCase))
                .Where(kvp => IsEligibleLandmarkComponent(kvp.Key, kvp.Value))
                .Select(kvp =>
                {
                    double pin1X = kvp.Value.X;
                    double pin1Y = kvp.Value.Y;

                    if (kvp.Value.Pins != null && kvp.Value.Pins.Count > 0)
                    {
                        var p1 = kvp.Value.Pins.FirstOrDefault(p => p.Key is "1" or "1.0").Value
                              ?? kvp.Value.Pins.First().Value;
                        pin1X = p1.X;
                        pin1Y = p1.Y;
                    }

                    return new CandidatePin(
                        RefDes: kvp.Key,
                        Comp: kvp.Value,
                        Pin1X: pin1X,
                        Pin1Y: pin1Y,
                        Quality: GetQualityScore(kvp.Key, kvp.Value)
                    );
                })
                .ToList();

            if (eligible.Count < 4 && side == "TOP")
            {
                eligible = components
                    .Where(kvp => IsEligibleLandmarkComponent(kvp.Key, kvp.Value))
                    .Select(kvp =>
                    {
                        double pin1X = kvp.Value.Pins?.FirstOrDefault(p => p.Key is "1" or "1.0").Value?.X ?? kvp.Value.X;
                        double pin1Y = kvp.Value.Pins?.FirstOrDefault(p => p.Key is "1" or "1.0").Value?.Y ?? kvp.Value.Y;
                        return new CandidatePin(
                            RefDes: kvp.Key,
                            Comp: kvp.Value,
                            Pin1X: pin1X,
                            Pin1Y: pin1Y,
                            Quality: GetQualityScore(kvp.Key, kvp.Value)
                        );
                    })
                    .ToList();
            }

            if (eligible.Count < 4) continue;

            double midX = (bounds.MinX + bounds.MaxX) / 2.0;
            double midY = (bounds.MinY + bounds.MaxY) / 2.0;

            var qTL = eligible.Where(c => c.Pin1X <= midX && c.Pin1Y >= midY)
                              .OrderByDescending(c => Hypot(c.Pin1X - midX, c.Pin1Y - midY) * c.Quality)
                              .FirstOrDefault();

            var qTR = eligible.Where(c => c.Pin1X > midX && c.Pin1Y >= midY)
                              .OrderByDescending(c => Hypot(c.Pin1X - midX, c.Pin1Y - midY) * c.Quality)
                              .FirstOrDefault();

            var qBR = eligible.Where(c => c.Pin1X > midX && c.Pin1Y < midY)
                              .OrderByDescending(c => Hypot(c.Pin1X - midX, c.Pin1Y - midY) * c.Quality)
                              .FirstOrDefault();

            var qBL = eligible.Where(c => c.Pin1X <= midX && c.Pin1Y < midY)
                              .OrderByDescending(c => Hypot(c.Pin1X - midX, c.Pin1Y - midY) * c.Quality)
                              .FirstOrDefault();

            var selectedList = new List<CandidatePin>();
            if (qTL != null) selectedList.Add(qTL);
            if (qTR != null && !selectedList.Any(s => Distance(s.Pin1X, s.Pin1Y, qTR.Pin1X, qTR.Pin1Y) < minDistance)) selectedList.Add(qTR);
            if (qBR != null && !selectedList.Any(s => Distance(s.Pin1X, s.Pin1Y, qBR.Pin1X, qBR.Pin1Y) < minDistance)) selectedList.Add(qBR);
            if (qBL != null && !selectedList.Any(s => Distance(s.Pin1X, s.Pin1Y, qBL.Pin1X, qBL.Pin1Y) < minDistance)) selectedList.Add(qBL);

            foreach (var cand in eligible.OrderByDescending(c => c.Quality))
            {
                if (selectedList.Count >= 4) break;
                if (!selectedList.Any(s => s.RefDes == cand.RefDes || Distance(s.Pin1X, s.Pin1Y, cand.Pin1X, cand.Pin1Y) < minDistance))
                {
                    selectedList.Add(cand);
                }
            }

            for (int idx = 0; idx < selectedList.Count; idx++)
            {
                var item = selectedList[idx];
                result.Add(new BoardviewAnchorCandidateDto(
                    RefDes: item.RefDes,
                    Side: side,
                    Type: item.Comp.Type,
                    Quadrant: $"P{idx + 1}",
                    CadX: Math.Round(item.Pin1X, 4),
                    CadY: Math.Round(item.Pin1Y, 4),
                    Score: Math.Round(item.Quality * 100, 2)
                ));
            }
        }

        return result;
    }

    private static double Hypot(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);

    private static double Distance(double x1, double y1, double x2, double y2) =>
        Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2));

    private static bool IsEligibleLandmarkComponent(string refDes, BoardviewComponentDto comp)
    {
        if (BgaExclusionFilter.IsExcludedBgaOrConnector(refDes, comp)) return false;

        if (ComponentKinds.TryParse(comp.Type, out var kind) && kind != ComponentKind.Unknown)
        {
            if (ComponentKinds.IsExcludedFromLandmarks(kind)) return false;
            if (kind is ComponentKind.PowerIc or ComponentKind.Ic or ComponentKind.Transistor or ComponentKind.Optocoupler)
                return comp.PinsCount >= 3 && comp.PinsCount <= 48;
            if (kind == ComponentKind.Capacitor && (comp.Type.Contains("Polarized", StringComparison.OrdinalIgnoreCase)
                || refDes.StartsWith("PC", StringComparison.OrdinalIgnoreCase)
                || refDes.StartsWith("TC", StringComparison.OrdinalIgnoreCase)
                || refDes.StartsWith("EC", StringComparison.OrdinalIgnoreCase)))
                return true;
            return false;
        }

        string u = refDes.ToUpperInvariant();
        if (u.StartsWith("J") || u.StartsWith("CN") || u.StartsWith("H") || u.StartsWith("MH") || u.StartsWith("TP")) return false;

        if (u.StartsWith("PC") || u.StartsWith("TC") || comp.Type.Contains("Polarized", StringComparison.OrdinalIgnoreCase))
            return true;

        if ((u.StartsWith("PU") || u.StartsWith("U") || u.StartsWith("UE") || u.StartsWith("PQ") || u.StartsWith("QA") || u.StartsWith("Q")) &&
            comp.PinsCount >= 3 && comp.PinsCount <= 48)
        {
            return true;
        }

        return false;
    }

    private static double GetQualityScore(string refDes, BoardviewComponentDto comp)
    {
        if (ComponentKinds.TryParse(comp.Type, out var kind) && kind != ComponentKind.Unknown)
        {
            if (kind == ComponentKind.PowerIc || (kind == ComponentKind.Ic && comp.PinsCount >= 8)) return 1.5;
            if (kind == ComponentKind.Transistor) return 1.2;
            if (kind == ComponentKind.Capacitor) return 1.0;
            return 0.8;
        }

        string u = refDes.ToUpperInvariant();
        if (u.StartsWith("PU") || u.StartsWith("UE") || (u.StartsWith("U") && comp.PinsCount >= 8)) return 1.5;
        if (u.StartsWith("PQ") || u.StartsWith("QA") || u.StartsWith("Q")) return 1.2;
        if (u.StartsWith("PC") || u.StartsWith("TC")) return 1.0;
        return 0.8;
    }
}