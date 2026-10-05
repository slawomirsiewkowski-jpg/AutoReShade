using AutoReShade.Core.Detection;

namespace AutoReShade.Core.Maps;

/// <summary>
/// Matches clock image files to maps by file name, e.g. "Coal Tower.png", "macmillan_coal-tower_clock.png"
/// or "Badham Preschool III.png". The longest map name found in the file name wins.
/// </summary>
public static class ClockAutoAssigner
{
    public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

    public static IReadOnlyDictionary<string, string> Assign(MapCatalog catalog, IEnumerable<string> files)
    {
        var names = catalog.Maps
            .SelectMany(m => m.AllNames.Select(n => (Map: m, Compact: TextNormalizer.Compact(n))))
            .Where(x => x.Compact.Length >= 4)
            .ToList();

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bestLength = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
        {
            var compactFile = TextNormalizer.Compact(Path.GetFileNameWithoutExtension(file));
            var match = names
                .Where(n => compactFile.Contains(n.Compact, StringComparison.Ordinal))
                .OrderByDescending(n => n.Compact.Length)
                .FirstOrDefault();
            if (match.Map is null) continue;

            // If two files fit the same map, keep the one whose name fits best (least extra text).
            var extra = compactFile.Length - match.Compact.Length;
            if (bestLength.TryGetValue(match.Map.Id, out var previousExtra) && previousExtra <= extra) continue;
            bestLength[match.Map.Id] = extra;
            result[match.Map.Id] = file;
        }
        return result;
    }

    public static IEnumerable<string> ImagesIn(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            : Enumerable.Empty<string>();
}
