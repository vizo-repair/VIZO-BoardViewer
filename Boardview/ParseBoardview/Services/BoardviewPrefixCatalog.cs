// ============================================================================
// FILE: Boardview/ParseBoardview/Services/BoardviewPrefixCatalog.cs
// Embedded catalog of 5,500+ real (Prefix|PinsBucket) -> ComponentKind signatures
// ============================================================================
namespace VIZO.BoardViewer.Boardview.ParseBoardview.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

public sealed class BoardviewPrefixCatalog
{
    private const string ResourceSuffix = "boardview_prefix_catalog.json";

    private static readonly Regex RefDesPrefixRegex = new(
        @"^(?<prefix>.*?)(?<num>\d+)(?<suffix>[A-Za-z])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> AlwaysContextDependent = new(StringComparer.OrdinalIgnoreCase)
    {
        "T|4-6", "T|7-16",
        "PT|4-6", "PT|7-16",
        "PC|4-6", "PC|7-16",
        "EC|4-6", "EC|7-16"
    };

    private static readonly Lazy<BoardviewPrefixCatalog> LazyDefault = new(() => LoadEmbedded());

    private readonly Dictionary<string, ComponentKind> _confident;
    private readonly Dictionary<string, ComponentKind> _fallbacks;
    private readonly HashSet<string> _contextDependent;

    public static BoardviewPrefixCatalog Default => LazyDefault.Value;

    public BoardviewPrefixCatalog()
        : this(LazyDefault.Value._confident, LazyDefault.Value._fallbacks, LazyDefault.Value._contextDependent)
    {
    }

    internal BoardviewPrefixCatalog(
        Dictionary<string, ComponentKind> confident,
        Dictionary<string, ComponentKind> fallbacks,
        HashSet<string> contextDependent)
    {
        _confident = confident;
        _fallbacks = fallbacks;
        _contextDependent = contextDependent;
    }

    public int ConfidentCount => _confident.Count;
    public int FallbackCount => _fallbacks.Count;
    public int ContextDependentCount => _contextDependent.Count;

    public static string ExtractPrefix(string? refDes)
    {
        if (string.IsNullOrWhiteSpace(refDes))
            return string.Empty;

        string clean = refDes.Trim().Split(['.', ':', '-'])[0].Trim();
        if (string.IsNullOrWhiteSpace(clean))
            return string.Empty;

        var match = RefDesPrefixRegex.Match(clean);
        string prefix = match.Success ? match.Groups["prefix"].Value : clean;
        if (string.IsNullOrEmpty(prefix))
            prefix = "NUM_ONLY";

        return prefix.ToUpperInvariant();
    }

    public static string ToPinsBucket(int pinsCount) => pinsCount switch
    {
        <= 0 => "0",
        1 => "1",
        2 => "2",
        3 => "3",
        <= 6 => "4-6",
        <= 16 => "7-16",
        _ => "17+"
    };

    public static string BuildKey(string prefix, int pinsCount)
        => $"{prefix.ToUpperInvariant()}|{ToPinsBucket(pinsCount)}";

    public bool TryLookupConfident(string? refDes, int pinsCount, out ComponentKind kind)
    {
        kind = ComponentKind.Unknown;
        if (pinsCount <= 0 || string.IsNullOrWhiteSpace(refDes))
            return false;

        string clean = refDes.Trim().Split(['.', ':'])[0].Trim().ToUpperInvariant();
        if (_confident.TryGetValue(BuildKey(clean, pinsCount), out kind) && kind != ComponentKind.Unknown)
            return true;

        string prefix = ExtractPrefix(refDes);
        if (string.IsNullOrEmpty(prefix))
            return false;

        string key = BuildKey(prefix, pinsCount);
        if (_contextDependent.Contains(key) || AlwaysContextDependent.Contains(key))
            return false;

        return _confident.TryGetValue(key, out kind) && kind != ComponentKind.Unknown;
    }

    public bool TryLookupFallback(string? refDes, int pinsCount, out ComponentKind kind)
    {
        kind = ComponentKind.Unknown;
        if (pinsCount <= 0 || string.IsNullOrWhiteSpace(refDes))
            return false;

        if (TryLookupConfident(refDes, pinsCount, out kind))
            return true;

        string prefix = ExtractPrefix(refDes);
        if (string.IsNullOrEmpty(prefix))
            return false;

        return _fallbacks.TryGetValue(BuildKey(prefix, pinsCount), out kind) && kind != ComponentKind.Unknown;
    }

    public bool IsContextDependent(string? refDes, int pinsCount)
    {
        if (pinsCount <= 0 || string.IsNullOrWhiteSpace(refDes))
            return false;

        string prefix = ExtractPrefix(refDes);
        if (string.IsNullOrEmpty(prefix))
            return false;

        string key = BuildKey(prefix, pinsCount);
        return _contextDependent.Contains(key) || AlwaysContextDependent.Contains(key);
    }

    private static BoardviewPrefixCatalog LoadEmbedded()
    {
        var confident = new Dictionary<string, ComponentKind>(StringComparer.OrdinalIgnoreCase);
        var fallbacks = new Dictionary<string, ComponentKind>(StringComparer.OrdinalIgnoreCase);
        var contextDep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var asm = typeof(BoardviewPrefixCatalog).Assembly;
        string? resName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase));

        if (resName != null)
        {
            using var stream = asm.GetManifestResourceStream(resName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                string json = reader.ReadToEnd();
                var dto = JsonSerializer.Deserialize<CatalogFileDto>(json);
                if (dto != null)
                {
                    if (dto.Entries != null)
                    {
                        foreach (var (k, v) in dto.Entries)
                        {
                            if (ComponentKinds.TryParse(v, out var parsed) && parsed != ComponentKind.Unknown)
                                confident[k] = parsed;
                        }
                    }

                    if (dto.Fallbacks != null)
                    {
                        foreach (var (k, v) in dto.Fallbacks)
                        {
                            if (ComponentKinds.TryParse(v, out var parsed) && parsed != ComponentKind.Unknown)
                                fallbacks[k] = parsed;
                        }
                    }

                    if (dto.ContextDependent != null)
                    {
                        foreach (string k in dto.ContextDependent)
                        {
                            if (!string.IsNullOrWhiteSpace(k))
                                contextDep.Add(k.Trim());
                        }
                    }
                }
            }
        }

        return new BoardviewPrefixCatalog(confident, fallbacks, contextDep);
    }

    private sealed class CatalogFileDto
    {
        [JsonPropertyName("entries")] public Dictionary<string, string>? Entries { get; set; }
        [JsonPropertyName("fallbacks")] public Dictionary<string, string>? Fallbacks { get; set; }
        [JsonPropertyName("contextDependent")] public List<string>? ContextDependent { get; set; }
    }
}

