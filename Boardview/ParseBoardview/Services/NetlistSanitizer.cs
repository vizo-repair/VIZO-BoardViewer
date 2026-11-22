// ============================================================================
// FILE: Boardview/ParseBoardview/Services/NetlistSanitizer.cs (VIZO BoardViewer)
// ============================================================================
namespace VIZO.BoardViewer.Boardview.ParseBoardview.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using VIZO.BoardViewer.Boardview;
using VIZO.BoardViewer.Serialization;

public class NetlistSanitizer
{
    private readonly ComponentRoleDeducer _roleDeducer;

    public NetlistSanitizer(ComponentRoleDeducer roleDeducer) => _roleDeducer = roleDeducer;

    public string? GenerateCleanAiNetlistJson(BoardviewNetlistDto netlist)
    {
        if (netlist == null) return null;

        var majorComponents = new Dictionary<string, CleanComponentDto>(StringComparer.OrdinalIgnoreCase);
        var catalog = BoardviewPrefixCatalog.Default;

        foreach (var (refDes, comp) in netlist.Components)
        {
            int pinsCount = comp.PinsCount > 0 ? comp.PinsCount : comp.Pins?.Count ?? 0;
            string role;
            bool pinConsistentCanonical = ComponentKinds.IsCanonicalName(comp.Type)
                && !(pinsCount > 0 && catalog.IsContextDependent(refDes, pinsCount))
                && !(pinsCount >= 3 && comp.Type.Equals("TestPoint", StringComparison.OrdinalIgnoreCase));
            if (pinConsistentCanonical)
            {
                role = comp.Type;
            }
            else if (pinsCount > 0 && catalog.TryLookupConfident(refDes, pinsCount, out var catalogKind))
            {
                role = catalogKind.ToCanonicalName();
            }
            else
            {
                string deduced = pinsCount > 0
                    ? _roleDeducer.RefineRoleWithPinCount(refDes, comp.Type, pinsCount)
                    : _roleDeducer.DeduceRole(refDes, comp.Type, pinsCount);
                role = ComponentKinds.TryParse(deduced, out var parsedKind) && parsedKind != ComponentKind.Unknown
                    ? parsedKind.ToCanonicalName()
                    : deduced;
            }

            if (ShouldIncludeComponent(refDes, role, pinsCount, comp))
            {
                string rawSide = comp.Side ?? "TOP";
                string cleanSide = rawSide.ToUpperInvariant().StartsWith("B") ? "BOTTOM" : "TOP";
                double cW = comp.Width > 0 ? comp.Width : Math.Abs(comp.MaxX - comp.MinX);
                double cH = comp.Height > 0 ? comp.Height : Math.Abs(comp.MaxY - comp.MinY);

                double minX = (comp.MaxX > comp.MinX) ? comp.MinX : (cW > 0 ? comp.X - cW / 2.0 : comp.X);
                double maxX = (comp.MaxX > comp.MinX) ? comp.MaxX : (cW > 0 ? comp.X + cW / 2.0 : comp.X);
                double minY = (comp.MaxY > comp.MinY) ? comp.MinY : (cH > 0 ? comp.Y - cH / 2.0 : comp.Y);
                double maxY = (comp.MaxY > comp.MinY) ? comp.MaxY : (cH > 0 ? comp.Y + cH / 2.0 : comp.Y);

                double realX = (minX + maxX) / 2.0;
                double realY = (minY + maxY) / 2.0;

                majorComponents[refDes] = new CleanComponentDto(
                    Role: role,
                    X: Math.Round(realX, 4),
                    Y: Math.Round(realY, 4),
                    Width: cW > 0 ? Math.Round(cW, 4) : null,
                    Height: cH > 0 ? Math.Round(cH, 4) : null,
                    Side: cleanSide,
                    Value: !string.IsNullOrWhiteSpace(comp.Value) ? comp.Value.Trim() : null,
                    Description: !string.IsNullOrWhiteSpace(comp.Description) ? comp.Description.Trim() : null,
                    Package: !string.IsNullOrWhiteSpace(comp.Package) ? comp.Package.Trim() : null
                );
            }
        }

        var cleanNets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (netName, pins) in netlist.Nets)
        {
            if (string.IsNullOrWhiteSpace(netName)
                || netName.StartsWith("NC_", StringComparison.OrdinalIgnoreCase)
                || netName.Equals("NC", StringComparison.OrdinalIgnoreCase)
                || IsGroundNet(netName))
                continue;

            var validPins = pins.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (validPins.Count > 0) cleanNets[netName] = validPins;
        }

        return VizoJsonSerializer.Serialize(new CleanBoardviewNetlistDto(
            Board: netlist.Board,
            Components: majorComponents,
            Nets: cleanNets,
            Bridges: netlist.Bridges,
            AnchorCandidates: netlist.AnchorCandidates
        ));
    }

    private static bool ShouldIncludeComponent(string refDes, string role, int pinsCount, BoardviewComponentDto comp)
    {
        if (ComponentKinds.TryParse(role, out var kind) && kind != ComponentKind.Unknown)
        {
            if (ComponentKinds.IsExcludedFromCleanNetlist(kind))
                return false;

            if (kind == ComponentKind.Resistor)
                return refDes.StartsWith("PR", StringComparison.OrdinalIgnoreCase) && LooksLikeShuntOrUnknownValue(comp);

            return true;
        }

        if (pinsCount > 2)
            return true;

        return refDes.StartsWith("PR", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeShuntOrUnknownValue(BoardviewComponentDto comp)
    {
        string val = $"{comp.Value} {comp.Description}".Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(val)) return true;
        if (val.Contains("K", StringComparison.Ordinal) || (val.Contains("M", StringComparison.Ordinal) && !val.Contains("MOHM", StringComparison.Ordinal)))
            return false;
        return true;
    }

    private static bool IsGroundNet(string netName)
    {
        string u = netName.Trim().ToUpperInvariant();
        if (u is "0" or "$0" or "0V" or "GROUND" or "GND" or "EARTH") return true;
        foreach (string token in u.Split(['_', '-', '/', '.', ' ', '+'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token is "GND" or "GROUND" or "AGND" or "PGND" or "DGND" or "SGND" or "CGND" or "EGND" or "HGND" or "EARTH" or "GNDA" or "GNDP"
                || token.StartsWith("GND", StringComparison.Ordinal)
                || token.EndsWith("GND", StringComparison.Ordinal)
                || token.StartsWith("VSS", StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}