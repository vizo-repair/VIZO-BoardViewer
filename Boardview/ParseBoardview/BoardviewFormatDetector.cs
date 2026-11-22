// ============================================================================
// FILE: Features/Boardview/ParseBoardview/BoardviewFormatDetector.cs
// ============================================================================
namespace VIZO.BoardViewer.Boardview.ParseBoardview;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using VIZO.BoardViewer.Boardview.ParseBoardview.Formats;
using VIZO.BoardViewer.Boardview.ParseBoardview.Services;

public class BoardviewFormatDetector
{
    public static readonly TimeSpan ParseTimeout = TimeSpan.FromSeconds(45);

    private readonly IEnumerable<IBoardviewFormatParser> _parsers;
    private readonly ComponentRoleDeducer _roleDeducer;
    private readonly BoardviewPrefixCatalog _catalog;

    public BoardviewFormatDetector(
        IEnumerable<IBoardviewFormatParser> parsers,
        ComponentRoleDeducer? roleDeducer = null,
        BoardviewPrefixCatalog? catalog = null)
    {
        _parsers = parsers;
        _roleDeducer = roleDeducer ?? new ComponentRoleDeducer();
        _catalog = catalog ?? BoardviewPrefixCatalog.Default;
    }

    public BoardviewNetlistDto? DetectAndParse(byte[] fileBytes, string fileName, Action<string>? onLog = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (fileBytes == null || fileBytes.Length < 4)
        {
            onLog?.Invoke($"❌ [BoardviewDetector] File '{fileName}' is empty or smaller than 4 bytes.");
            return null;
        }

        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        onLog?.Invoke($"🔍 [BoardviewDetector] Starting file analysis: '{fileName}' ({fileBytes.Length / 1024} KB, extension: '{ext}')");

        // Strip UTF-8 BOM if present
        int offset = 0;
        if (fileBytes.Length >= 3 && fileBytes[0] == 0xEF && fileBytes[1] == 0xBB && fileBytes[2] == 0xBF)
        {
            offset = 3;
            onLog?.Invoke("ℹ️ [BoardviewDetector] UTF-8 BOM detected and skipped.");
        }

        var headerSlice = fileBytes.AsSpan(offset, Math.Min(fileBytes.Length - offset, 65536));

        if (IsCadenceAllegro(headerSlice, fileName))
        {
            onLog?.Invoke($"⚠️ [BoardviewDetector] File '{fileName}' detected as raw Cadence Allegro PCB project. A .cad / .bdv export is required.");
            throw new NotSupportedException("errCadenceAllegroBrd");
        }

        // Preview first 80 header characters in log for quick diagnostics
        string preview = GetSafeHeaderPreview(headerSlice);
        onLog?.Invoke($"📄 [BoardviewDetector] Header start: \"{preview}\"");

        // ---------------------------------------------------------------------
        // STAGE 1: Find parser by CanParse signatures
        // ---------------------------------------------------------------------
        var matchedParsers = new List<IBoardviewFormatParser>();

        foreach (var parser in _parsers)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (parser.CanParse(headerSlice, fileName))
                {
                    matchedParsers.Add(parser);
                    onLog?.Invoke($"  ✓ Signature match: [{parser.FormatName}]");
                }
            }
            catch (Exception ex)
            {
                onLog?.Invoke($"  ⚠️ CanParse check error in [{parser.FormatName}]: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------------
        // STAGE 2: Fallback by file extension if signatures did not match
        // ---------------------------------------------------------------------
        if (matchedParsers.Count == 0)
        {
            onLog?.Invoke($"⚠️ [BoardviewDetector] No parser confirmed signature. Trying extension fallback '{ext}'...");

            foreach (var parser in _parsers)
            {
                if (ext is ".cad" && parser is CadPcbParser or GenCadAsciiParser)
                    matchedParsers.Add(parser);
                else if (ext is ".asc" && parser is GenCadAsciiParser or CadPcbParser)
                    matchedParsers.Add(parser);
                else if (ext is ".brd" && parser is BrdPcbParser or Brd2PcbParser or EagleXmlParser)
                    matchedParsers.Add(parser);
                else if (ext is ".bdv" && parser is CompalBdvParser or GenCadAsciiParser)
                    matchedParsers.Add(parser);
                else if (ext is ".fz" && parser is FzPcbParser)
                    matchedParsers.Add(parser);
                else if (ext is ".tvw" && parser is TvwPcbParser or XzzPcbParser) // TvwPcbParser first, then XzzPcbParser
                    matchedParsers.Add(parser);
                else if (ext is ".xzz" && parser is XzzPcbParser or TvwPcbParser)
                    matchedParsers.Add(parser);
                else if (ext is ".bvr" && parser is Bvr3PcbParser or BvrPcbParser)
                    matchedParsers.Add(parser);
                else if (ext is ".cst" && parser is CstPcbParser)
                    matchedParsers.Add(parser);
            }
        }

        if (matchedParsers.Count == 0)
        {
            var msg = $"Unsupported Boardview format '{fileName}'. Header signature did not match any parser.";
            onLog?.Invoke($"❌ [BoardviewDetector] {msg}");
            throw new NotSupportedException(msg);
        }

        // ---------------------------------------------------------------------
        // STAGE 3: Run matching parsers sequentially
        // ---------------------------------------------------------------------
        foreach (var selectedParser in matchedParsers)
        {
            ct.ThrowIfCancellationRequested();
            onLog?.Invoke($"🧩 [BoardviewDetector] Running parser: [{selectedParser.FormatName}]...");

            try
            {
                var dto = selectedParser.Parse(fileBytes, fileName, onLog, ct);
                if (dto != null && dto.Components != null && dto.Components.Count > 0)
                {
                    NormalizePinAwareRoles(dto);
                    onLog?.Invoke($"✅ [BoardviewDetector] SUCCESS! [{selectedParser.FormatName}] parsed {dto.Components.Count} components, {dto.Nets.Count} nets.");
                    return dto;
                }
                else
                {
                    onLog?.Invoke($"⚠️ [BoardviewDetector] Parser [{selectedParser.FormatName}] returned an empty result (0 components).");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                onLog?.Invoke($"⚠️ [BoardviewDetector] Failure inside [{selectedParser.FormatName}]: {ex.GetType().Name}: {ex.Message}");
            }
        }

        var failMsg = $"Failed to parse '{fileName}'. All matching parsers returned an empty result.";
        onLog?.Invoke($"❌ [BoardviewDetector] {failMsg}");
        throw new NotSupportedException(failMsg);
    }

    public bool IsSupported(byte[] fileBytes, string fileName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (fileBytes == null || fileBytes.Length < 4) return false;

        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is ".cad" or ".asc" or ".brd" or ".bdv" or ".fz" or ".tvw" or ".xzz" or ".bvr" or ".cst")
            return true;

        var header = fileBytes.AsSpan(0, Math.Min(fileBytes.Length, 65536));
        foreach (var parser in _parsers)
        {
            ct.ThrowIfCancellationRequested();
            if (parser.CanParse(header, fileName))
                return true;
        }
        return false;
    }

    private static string GetSafeHeaderPreview(ReadOnlySpan<byte> bytes)
    {
        int len = Math.Min(bytes.Length, 100);
        var sb = new StringBuilder(len);
        for (int i = 0; i < len; i++)
        {
            byte b = bytes[i];
            if (b is >= 32 and <= 126) sb.Append((char)b);
            else if (b is 10 or 13) sb.Append(' ');
            else sb.Append('.');
        }
        return sb.ToString().Trim();
    }
    public static bool IsCadenceAllegro(ReadOnlySpan<byte> header, string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext != ".brd") return false;

        // 1. If the file is text-based (Eagle XML, BRD2, Standard BRD), it is definitely NOT Cadence!
        // Check first 512 bytes for valid readable text
        if (header.Length >= 4)
        {
            // Encrypted Landrex/TRI strictly starts with this signature
            if (header[0] == 0x23 && header[1] == 0xE2 && header[2] == 0x63 && header[3] == 0x28)
                return false;

            string ascii = Encoding.ASCII.GetString(header[..Math.Min(header.Length, 1024)]);
            if (ascii.Contains("str_length:") || ascii.Contains("var_data:") ||
                ascii.Contains("BRDOUT:") || ascii.StartsWith("<?xml") || ascii.Contains("<eagle"))
            {
                return false;
            }

            // 2. Allegro signature at offset 0xF8 (248 bytes)
            if (header.Length >= 0xFC)
            {
                var tag = header.Slice(0xF8, 3);
                // "all" (allegro) or "dbd" (dbdoctor)
                if ((tag[0] == 'a' && tag[1] == 'l' && tag[2] == 'l') ||
                    (tag[0] == 'd' && tag[1] == 'b' && tag[2] == 'd'))
                {
                    return true;
                }
            }

            // 3. Cadence database version signature in bytes 2..3 (versions 16.x - 24.x)
            if (header[3] == 0x00 && header[2] >= 0x12 && header[2] <= 0x30)
            {
                // Additionally: internal strings "allegro" or "cadence" almost always appear
                // in the first 512 bytes of a Cadence binary
                string fullSample = Encoding.ASCII.GetString(header[..Math.Min(header.Length, 4096)]);
                if (fullSample.Contains("allegro", StringComparison.OrdinalIgnoreCase) ||
                    fullSample.Contains("cadence", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Or the file is purely binary (contains many zero bytes in the first 64 bytes),
                // which rules out any text-based Boardview
                int zeroCount = 0;
                for (int i = 0; i < Math.Min(64, header.Length); i++)
                {
                    if (header[i] == 0x00) zeroCount++;
                }

                if (zeroCount > 15)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void NormalizePinAwareRoles(BoardviewNetlistDto dto)
    {
        if (dto.Components == null || dto.Components.Count == 0)
            return;

        foreach (string refDes in dto.Components.Keys.ToList())
        {
            var comp = dto.Components[refDes];
            if (comp == null || string.IsNullOrWhiteSpace(refDes))
                continue;

            int pinsCount = comp.PinsCount > 0 ? comp.PinsCount : comp.Pins?.Count ?? 0;
            string resolved;

            if (pinsCount > 0 && _catalog.TryLookupConfident(refDes, pinsCount, out var confKind))
            {
                resolved = confKind.ToCanonicalName();
            }
            else if (pinsCount > 0 && _catalog.TryLookupFallback(refDes, pinsCount, out var fbKind))
            {
                resolved = fbKind.ToCanonicalName();
            }
            else
            {
                string deduced = pinsCount > 0
                    ? _roleDeducer.RefineRoleWithPinCount(refDes, comp.Type, pinsCount)
                    : _roleDeducer.DeduceRole(refDes, comp.Type, pinsCount);
                resolved = ComponentKinds.TryParse(deduced, out var parsedKind)
                    ? parsedKind.ToCanonicalName()
                    : ComponentKind.Unknown.ToCanonicalName();
            }

            if (!string.Equals(resolved, comp.Type, StringComparison.Ordinal))
            {
                dto.Components[refDes] = comp with { Type = resolved };
            }
        }
    }
}