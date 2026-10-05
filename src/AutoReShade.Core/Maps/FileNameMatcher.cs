using AutoReShade.Core.Detection;

namespace AutoReShade.Core.Maps;

public sealed record FileNameMatch(string? MapId, string? RealmId);

/// <summary>
/// Guesses which map or realm a file is meant for from its name, including short names
/// such as "11.STX.Macmillan.ini" (realm), "18.STX.Saloon.ini" (map) or "Badham 3.png".
/// </summary>
public static class FileNameMatcher
{
    private const int MinPartLength = 5;

    public static FileNameMatch? Match(MapCatalog catalog, string fileName)
    {
        // File names often use dots and commas as separators ("11.STX.Macmillan").
        var words = TextNormalizer.Words(Path.GetFileNameWithoutExtension(fileName).Replace('.', ' ').Replace(',', ' '));
        var targets = catalog.Realms.SelectMany(r =>
                r.Names.Values.Select(n => (Map: (MapInfo?)null, Realm: r, Compact: TextNormalizer.Compact(n)))
                    .Concat(r.Maps.SelectMany(m => m.AllNames.Select(n => (Map: (MapInfo?)m, Realm: r, Compact: TextNormalizer.Compact(n))))))
            .Where(t => t.Compact.Length >= 4)
            .ToList();

        var bestLength = 0;
        var bestScore = 0;
        var winners = new List<(MapInfo? Map, RealmInfo Realm)>();
        for (var start = 0; start < words.Count; start++)
        {
            for (var length = 1; start + length <= words.Count; length++)
            {
                var part = string.Concat(words.Skip(start).Take(length));
                foreach (var target in targets)
                {
                    int score;
                    if (part == target.Compact || part.Contains(target.Compact, StringComparison.Ordinal)) score = 3;
                    else if (part.Length >= MinPartLength && target.Compact.Contains(part, StringComparison.Ordinal)) score = 2;
                    else continue;

                    var matched = Math.Min(part.Length, target.Compact.Length);
                    if (matched > bestLength || (matched == bestLength && score > bestScore))
                    {
                        bestLength = matched;
                        bestScore = score;
                        winners.Clear();
                    }
                    if (matched == bestLength && score == bestScore && !winners.Contains((target.Map, target.Realm)))
                        winners.Add((target.Map, target.Realm));
                }
            }
        }

        if (winners.Count == 0) return null;
        var maps = winners.Where(w => w.Map is not null).Select(w => w.Map!.Id).Distinct().ToList();
        var realms = winners.Select(w => w.Realm.Id).Distinct().ToList();
        if (winners.Any(w => w.Map is null) && realms.Count == 1) return new FileNameMatch(null, realms[0]);
        if (maps.Count == 1 && realms.Count == 1) return new FileNameMatch(maps[0], realms[0]);
        // "Badham" fits all five Badham maps: they share a realm, so it is meant for the realm.
        return realms.Count == 1 ? new FileNameMatch(null, realms[0]) : null;
    }

    /// <summary>Preset files to realm/map assignments. When two files fit the same target, the better fit wins.</summary>
    public static (Dictionary<string, string> Realms, Dictionary<string, string> Maps) AssignPresets(MapCatalog catalog, IEnumerable<string> presetFiles)
    {
        var realms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var maps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in presetFiles.OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
        {
            var match = Match(catalog, file);
            if (match?.MapId is { } mapId) maps[mapId] = file;
            else if (match?.RealmId is { } realmId) realms[realmId] = file;
        }
        return (realms, maps);
    }
}
