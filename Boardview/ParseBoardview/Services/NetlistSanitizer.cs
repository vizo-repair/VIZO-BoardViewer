// ============================================================================
// FILE: Features/Boardview/ParseBoardview/Services/NetlistSanitizer.cs (VIZO 2.0)
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

        foreach (var (refDes, comp) in netlist.Components)
        {
            bool isMajor = comp.Type is "IC" or "Transistor" or "Connector" ||
                           refDes.StartsWith("PU") || refDes.StartsWith("U") ||
                           refDes.StartsWith("PQ") || refDes.StartsWith("Q") ||
                           refDes.StartsWith("J") || refDes.StartsWith("CN") ||
                           !string.IsNullOrWhiteSpace(comp.Description) ||
                           !string.IsNullOrWhiteSpace(comp.Value);

            if (isMajor)
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
                    Role: _roleDeducer.DeduceRole(refDes, comp.Type),
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
            if (string.IsNullOrWhiteSpace(netName) || netName.StartsWith("NC_") || netName.Equals("NC", StringComparison.OrdinalIgnoreCase))
                continue;

            var validPins = pins.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (validPins.Count > 0) cleanNets[netName] = validPins;
            //if (cleanNets.Count >= 400) break;
        }

        //if (majorComponents.Count > 350)
        //{
        //    majorComponents = majorComponents.Take(350).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        //}

        return VizoJsonSerializer.Serialize(new CleanBoardviewNetlistDto(
            Board: netlist.Board,
            Components: majorComponents,
            Nets: cleanNets,
            Bridges: netlist.Bridges,
            AnchorCandidates: netlist.AnchorCandidates
        ));
    }
}