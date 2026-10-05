using AutoReShade.Core.Maps;

namespace AutoReShade.Core.Detection;

public sealed record MapCandidate(MapInfo Map, double Score, string MatchedName, string SourceText);

public sealed record MatchResult(MapCandidate? Best, IReadOnlyList<MapCandidate> Ranking, string? RealmId)
{
    public static readonly MatchResult None = new(null, Array.Empty<MapCandidate>(), null);

    public MapInfo? Map => Best?.Map;
    public double Score => Best?.Score ?? 0;
}

/// <summary>
/// Finds which map name appears in a few lines of OCR text. Every language in the map list
/// is checked at once, so the game language does not have to be configured.
/// </summary>
/// <remarks>
/// Each line (and each pair of neighbouring lines, for names that wrap) is compared with every
/// known name. A name may also match only part of a line; then the characters left over count
/// against it, unless they are a recognised realm name. That keeps "Raccoon City Police Station"
/// from beating "Raccoon City Police Station East Wing", and "The Game" from matching inside
/// an unrelated sentence.
/// </remarks>
public sealed class MapNameMatcher
{
    /// <summary>Minimum similarity (0..1) for a line to count as a map name.</summary>
    public const double AcceptThreshold = 0.84;

    /// <summary>The winner must beat the best different map by at least this much.</summary>
    public const double RequiredMargin = 0.02;

    private const double LeftoverPenalty = 0.5;
    private const double RealmBonus = 0.02;
    private const int MinTextWeight = 3;

    /// <summary>Names shorter than this (e.g. Polish "Gra") only count when a line says exactly that.</summary>
    private const int ShortNameWeight = 5;
    private const int MaxWordsPerText = 16;

    private readonly List<NameEntry> _mapEntries = new();
    private readonly List<NameEntry> _realmEntries = new();
    private readonly MapCatalog _catalog;

    public MapNameMatcher(MapCatalog catalog)
    {
        _catalog = catalog;
        foreach (var map in catalog.Maps)
            foreach (var name in map.AllNames)
                AddEntry(_mapEntries, map.Id, name);
        foreach (var realm in catalog.Realms)
            foreach (var name in realm.Names.Values)
                AddEntry(_realmEntries, realm.Id, name);
    }

    public MatchResult Match(IEnumerable<string> ocrLines)
    {
        var lines = ocrLines
            .Select(l => new Text(l, TextNormalizer.Words(l).Take(MaxWordsPerText).ToList()))
            .Where(t => TextNormalizer.Weight(t.SpanCompact(0, t.Words.Count)) >= MinTextWeight)
            .ToList();
        if (lines.Count == 0) return MatchResult.None;

        // Long names sometimes wrap onto a second line, so also try neighbouring lines joined together.
        var texts = new List<Text>(lines);
        for (var i = 0; i + 1 < lines.Count; i++)
            texts.Add(new Text(lines[i].Source + " / " + lines[i + 1].Source,
                lines[i].Words.Concat(lines[i + 1].Words).Take(MaxWordsPerText).ToList()));

        string? realmId = null;
        double realmScore = 0;
        foreach (var text in texts)
        {
            text.RealmSpans = FindRealmSpans(text);
            foreach (var span in text.RealmSpans)
                if (span.Score > realmScore)
                {
                    realmScore = span.Score;
                    realmId = span.Id;
                }
        }

        var bestPerMap = new Dictionary<string, MapCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var text in texts)
        {
            foreach (var entry in _mapEntries)
            {
                var score = ScoreMap(text, entry);
                if (score <= 0) continue;
                var map = _catalog.FindMap(entry.Id)!;
                if (realmId is not null && string.Equals(map.RealmId, realmId, StringComparison.OrdinalIgnoreCase))
                    score += RealmBonus; // not capped at 1, so the bonus never erases the gap between two close names

                if (!bestPerMap.TryGetValue(map.Id, out var current) || score > current.Score)
                    bestPerMap[map.Id] = new MapCandidate(map, score, entry.Original, text.Source);
            }
        }

        var ranking = bestPerMap.Values
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.MatchedName.Length)
            .Take(5)
            .ToList();
        if (ranking.Count == 0) return new MatchResult(null, ranking, realmId);

        var best = ranking[0];
        var runnerUp = ranking.Count > 1 ? ranking[1].Score : 0;
        var accepted = best.Score >= AcceptThreshold
            && (best.Score - runnerUp >= RequiredMargin || ExtendsEveryCloseRival(best, ranking));
        return new MatchResult(accepted ? best : null, ranking, realmId);
    }

    /// <summary>
    /// "Raccoon City Police Station East Wing" contains "Raccoon City Police Station". When both read
    /// equally well, the text really did include the longer name, so the more specific map wins.
    /// </summary>
    private static bool ExtendsEveryCloseRival(MapCandidate best, IReadOnlyList<MapCandidate> ranking)
    {
        var bestCompact = TextNormalizer.Compact(best.MatchedName);
        return ranking.Skip(1)
            .Where(c => best.Score - c.Score < RequiredMargin)
            .All(c =>
            {
                var rival = TextNormalizer.Compact(c.MatchedName);
                return bestCompact.Length > rival.Length && bestCompact.Contains(rival, StringComparison.Ordinal);
            });
    }

    private List<Span> FindRealmSpans(Text text)
    {
        var spans = new List<Span>();
        foreach (var entry in _realmEntries)
        {
            for (var length = 1; length <= entry.WordCount + 1; length++)
                for (var start = 0; start + length <= text.Words.Count; start++)
                {
                    var score = Similarity(text.SpanCompact(start, length), entry.Compact);
                    if (score >= AcceptThreshold)
                        spans.Add(new Span(entry.Id, start, start + length, score));
                }
        }
        return spans;
    }

    private static double ScoreMap(Text text, NameEntry entry)
    {
        if (TextNormalizer.Weight(entry.Compact) < ShortNameWeight)
            return text.SpanCompact(0, text.Words.Count) == entry.Compact ? 1.0 : 0.0;

        double best = 0;
        var maxLength = Math.Min(text.Words.Count, entry.WordCount + 1);
        for (var length = 1; length <= maxLength; length++)
        {
            for (var start = 0; start + length <= text.Words.Count; start++)
            {
                var span = text.SpanCompact(start, length);
                var similarity = Similarity(span, entry.Compact);
                if (similarity <= best) continue;

                var leftover = text.TotalLength - span.Length - text.ExplainedOutside(start, start + length);
                var score = similarity * (1.0 - LeftoverPenalty * Math.Max(0, leftover) / text.TotalLength);
                if (score > best) best = score;
            }
        }
        return best;
    }

    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;
        var max = Math.Max(a.Length, b.Length);
        // Anything below this cannot reach the accept threshold even after bonuses, so stop early.
        var maxDistance = (int)Math.Floor(max * (1.0 - (AcceptThreshold - RealmBonus - 0.05)));
        if (Math.Abs(a.Length - b.Length) > maxDistance) return 0;
        var distance = BoundedLevenshtein(a, b, maxDistance);
        return distance > maxDistance ? 0 : 1.0 - (double)distance / max;
    }

    /// <summary>Edit distance, giving up (returning a value above <paramref name="limit"/>) once it is clearly too far.</summary>
    public static int BoundedLevenshtein(string a, string b, int limit = int.MaxValue)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                if (current[j] < rowMin) rowMin = current[j];
            }
            if (rowMin > limit) return limit + 1;
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }

    private static void AddEntry(List<NameEntry> list, string id, string name)
    {
        var words = TextNormalizer.Words(name);
        var compact = TextNormalizer.Compact(words);
        if (compact.Length == 0) return;
        if (list.Any(e => e.Id == id && e.Compact == compact)) return;
        list.Add(new NameEntry(id, name, compact, words.Count));
    }

    private sealed record NameEntry(string Id, string Original, string Compact, int WordCount);

    private sealed record Span(string Id, int Start, int End, double Score);

    private sealed class Text
    {
        private readonly Dictionary<(int, int), string> _spanCache = new();

        public Text(string source, IReadOnlyList<string> words)
        {
            Source = source;
            Words = words;
            TotalLength = words.Sum(w => w.Length);
        }

        public string Source { get; }
        public IReadOnlyList<string> Words { get; }
        public int TotalLength { get; }
        public List<Span> RealmSpans { get; set; } = new();

        public string SpanCompact(int start, int length)
        {
            if (!_spanCache.TryGetValue((start, length), out var value))
            {
                value = string.Concat(Words.Skip(start).Take(length));
                _spanCache[(start, length)] = value;
            }
            return value;
        }

        /// <summary>Characters outside [start, end) that belong to a recognised realm name.</summary>
        public int ExplainedOutside(int start, int end)
        {
            var explained = new bool[Words.Count];
            foreach (var realm in RealmSpans)
            {
                if (realm.Start < end && start < realm.End) continue; // overlaps the map name itself
                for (var i = realm.Start; i < realm.End; i++) explained[i] = true;
            }

            var total = 0;
            for (var i = 0; i < Words.Count; i++)
                if (explained[i] && (i < start || i >= end))
                    total += Words[i].Length;
            return total;
        }
    }
}
