// ============================================================================
// FILE: Features/Boardview/ParseBoardview/Formats/EagleXmlParser.cs (VIZO 2.0 + Metadata)
// ============================================================================
namespace VIZO.BoardViewer.Boardview.ParseBoardview.Formats;

using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using VIZO.BoardViewer.Boardview.ParseBoardview.Services;

public class EagleXmlParser : IBoardviewFormatParser
{
    private readonly ComponentRoleDeducer _roleDeducer;
    private readonly QuadrantAnchorScorer _anchorScorer;

    public EagleXmlParser(ComponentRoleDeducer roleDeducer, QuadrantAnchorScorer anchorScorer)
    {
        _roleDeducer = roleDeducer;
        _anchorScorer = anchorScorer;
    }

    public string FormatName => "XML / Eagle / Fritzing";

    public bool CanParse(ReadOnlySpan<byte> headerBytes, string fileName)
    {
        if (headerBytes.Length < 4) return false;
        return headerBytes[0] == (byte)'<' && (headerBytes[1] == (byte)'?' || headerBytes[1] == (byte)'f' || headerBytes[1] == (byte)'e' || headerBytes[1] == (byte)'b');
    }

    public BoardviewNetlistDto? Parse(byte[] fileBytes, string fileName, Action<string>? onLog = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var components = new Dictionary<string, BoardviewComponentDto>(StringComparer.OrdinalIgnoreCase);
        var nets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var ms = new MemoryStream(fileBytes, writable: false);
            using var reader = XmlReader.Create(ms, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                MaxCharactersInDocument = 50_000_000,
                MaxCharactersFromEntities = 1024
            });
            var doc = XDocument.Load(reader);

            int scanned = 0;
            foreach (var elem in doc.Descendants("element"))
            {
                if ((++scanned & 255) == 0) ct.ThrowIfCancellationRequested();
                string name = elem.Attribute("name")?.Value ?? "";
                string value = elem.Attribute("value")?.Value ?? "";
                string pkg = elem.Attribute("package")?.Value ?? "";
                string xStr = elem.Attribute("x")?.Value ?? "0";
                string yStr = elem.Attribute("y")?.Value ?? "0";
                string rot = elem.Attribute("rot")?.Value ?? "";

                if (!string.IsNullOrEmpty(name) &&
                    double.TryParse(xStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double x) &&
                    double.TryParse(yStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double y))
                {
                    string side = rot.StartsWith("M", StringComparison.OrdinalIgnoreCase) ? "BOTTOM" : "TOP";
                    components[name] = new BoardviewComponentDto(
                        X: x, Y: y, MinX: x - 0.05, MinY: y - 0.05, MaxX: x + 0.05, MaxY: y + 0.05, Width: 0.1, Height: 0.1,
                        Side: side, PinsCount: 0,
                        Type: _roleDeducer.DeduceRole(name, ""),
                        Pins: new Dictionary<string, BoardviewPinDto>(),
                        Description: !string.IsNullOrWhiteSpace(value) ? value : null,
                        Value: !string.IsNullOrWhiteSpace(value) ? value : null,
                        PartNumber: !string.IsNullOrWhiteSpace(value) ? value : null,
                        Package: !string.IsNullOrWhiteSpace(pkg) ? pkg : null
                    );
                }
            }

            foreach (var sig in doc.Descendants("signal"))
            {
                if ((++scanned & 255) == 0) ct.ThrowIfCancellationRequested();
                string netName = sig.Attribute("name")?.Value ?? "";
                if (string.IsNullOrEmpty(netName)) continue;

                var contactList = new List<string>();
                foreach (var cp in sig.Descendants("contactref"))
                {
                    if ((++scanned & 255) == 0) ct.ThrowIfCancellationRequested();
                    string elemName = cp.Attribute("element")?.Value ?? "";
                    string pad = cp.Attribute("pad")?.Value ?? "";
                    if (!string.IsNullOrEmpty(elemName)) contactList.Add($"{elemName}.{pad}");
                }
                if (contactList.Count > 0) nets[netName] = contactList;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }

        var bounds = new BoardviewBoundsDto(0, 0, 0, 0, 0, 0, "MM");
        var stats = new BoardviewStatsDto(components.Count, nets.Count, 0, 0, 0);

        return new BoardviewNetlistDto(
            Path.GetFileNameWithoutExtension(fileName), "XML / Eagle / Fritzing", bounds, stats, components,
            _anchorScorer.SelectAnchorCandidates(bounds, components), nets, [], []);
    }
}