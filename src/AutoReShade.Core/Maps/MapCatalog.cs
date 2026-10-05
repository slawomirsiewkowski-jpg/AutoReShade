using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoReShade.Core.Maps;

public sealed class MapInfo
{
    public MapInfo(string id, string realmId, IReadOnlyDictionary<string, string> names, IReadOnlyList<string> aliases)
    {
        Id = id;
        RealmId = realmId;
        Names = names;
        Aliases = aliases;
    }

    public string Id { get; }
    public string RealmId { get; }

    /// <summary>Game language code (en, de, pl, ...) to the name shown on the loading screen.</summary>
    public IReadOnlyDictionary<string, string> Names { get; }

    /// <summary>Extra spellings that should also be recognised (any language).</summary>
    public IReadOnlyList<string> Aliases { get; }

    public string DisplayName => Names.TryGetValue("en", out var en) ? en : Names.Values.FirstOrDefault() ?? Id;

    public IEnumerable<string> AllNames => Names.Values.Concat(Aliases).Distinct(StringComparer.OrdinalIgnoreCase);

    public override string ToString() => DisplayName;
}

public sealed class RealmInfo
{
    public RealmInfo(string id, IReadOnlyDictionary<string, string> names, IReadOnlyList<MapInfo> maps)
    {
        Id = id;
        Names = names;
        Maps = maps;
    }

    public string Id { get; }
    public IReadOnlyDictionary<string, string> Names { get; }
    public IReadOnlyList<MapInfo> Maps { get; }

    public string DisplayName => Names.TryGetValue("en", out var en) ? en : Names.Values.FirstOrDefault() ?? Id;

    public override string ToString() => DisplayName;
}

/// <summary>
/// The list of realms and maps. The built-in list ships inside the app (Data/maps.json);
/// users can add maps or languages through custom-maps.json without rebuilding.
/// </summary>
public sealed class MapCatalog
{
    private const string BuiltInResource = "AutoReShade.maps.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Dictionary<string, MapInfo> _mapsById;
    private readonly Dictionary<string, RealmInfo> _realmsById;

    private MapCatalog(IReadOnlyList<RealmInfo> realms, IReadOnlyDictionary<string, string> readyButtonNames, int version)
    {
        Realms = realms;
        ReadyButtonNames = readyButtonNames;
        Version = version;
        Maps = realms.SelectMany(r => r.Maps).ToList();
        _mapsById = Maps.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        _realmsById = realms.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
    }

    public int Version { get; }
    public IReadOnlyList<RealmInfo> Realms { get; }
    public IReadOnlyList<MapInfo> Maps { get; }

    /// <summary>Game language code to the text of the lobby's Ready button.</summary>
    public IReadOnlyDictionary<string, string> ReadyButtonNames { get; }

    public MapInfo? FindMap(string? id) => id is not null && _mapsById.TryGetValue(id, out var m) ? m : null;
    public RealmInfo? FindRealm(string? id) => id is not null && _realmsById.TryGetValue(id, out var r) ? r : null;

    public static string ReadBuiltInJson()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BuiltInResource)
            ?? throw new InvalidOperationException("Built-in map list is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static MapCatalog LoadBuiltIn() => Load(ReadBuiltInJson(), customJson: null);

    /// <summary>Loads the built-in list and merges the user's custom file on top (if it exists and is valid).</summary>
    public static MapCatalog LoadWithCustom(string customMapsFile, out string? customError)
    {
        customError = null;
        string? customJson = null;
        if (File.Exists(customMapsFile))
        {
            try
            {
                customJson = File.ReadAllText(customMapsFile);
                Parse(customJson);
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
            {
                customError = $"custom-maps.json could not be read: {ex.Message}";
                customJson = null;
            }
        }

        return Load(ReadBuiltInJson(), customJson);
    }

    public static MapCatalog Load(string json, string? customJson)
    {
        var file = Parse(json);
        if (customJson is not null)
            Merge(file, Parse(customJson));
        return Build(file);
    }

    private static MapListFile Parse(string json)
    {
        var file = JsonSerializer.Deserialize<MapListFile>(json, JsonOptions)
            ?? throw new InvalidDataException("Map list is empty.");
        file.Realms ??= new List<RealmDto>();
        file.ReadyButton ??= new Dictionary<string, string>();
        foreach (var realm in file.Realms)
        {
            if (string.IsNullOrWhiteSpace(realm.Id))
                throw new InvalidDataException("Every realm needs an \"id\".");
            realm.Names ??= new Dictionary<string, string>();
            realm.Maps ??= new List<MapDto>();
            foreach (var map in realm.Maps)
            {
                if (string.IsNullOrWhiteSpace(map.Id))
                    throw new InvalidDataException($"A map in realm \"{realm.Id}\" has no \"id\".");
                map.Names ??= new Dictionary<string, string>();
                map.Aliases ??= new List<string>();
            }
        }
        return file;
    }

    private static void Merge(MapListFile target, MapListFile extra)
    {
        foreach (var (lang, text) in extra.ReadyButton!)
            target.ReadyButton![lang] = text;

        foreach (var extraRealm in extra.Realms!)
        {
            var realm = target.Realms!.FirstOrDefault(r => string.Equals(r.Id, extraRealm.Id, StringComparison.OrdinalIgnoreCase));
            if (realm is null)
            {
                target.Realms!.Add(extraRealm);
                continue;
            }

            foreach (var (lang, name) in extraRealm.Names!)
                realm.Names![lang] = name;

            foreach (var extraMap in extraRealm.Maps!)
            {
                var map = realm.Maps!.FirstOrDefault(m => string.Equals(m.Id, extraMap.Id, StringComparison.OrdinalIgnoreCase));
                if (map is null)
                {
                    realm.Maps!.Add(extraMap);
                    continue;
                }

                foreach (var (lang, name) in extraMap.Names!)
                    map.Names![lang] = name;
                foreach (var alias in extraMap.Aliases!)
                    if (!map.Aliases!.Contains(alias, StringComparer.OrdinalIgnoreCase))
                        map.Aliases!.Add(alias);
            }
        }
    }

    private static MapCatalog Build(MapListFile file)
    {
        var realms = new List<RealmInfo>();
        var seenMapIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var realm in file.Realms!)
        {
            var maps = new List<MapInfo>();
            foreach (var map in realm.Maps!)
            {
                if (!seenMapIds.Add(map.Id!))
                    throw new InvalidDataException($"Map id \"{map.Id}\" is used more than once.");
                maps.Add(new MapInfo(map.Id!, realm.Id!, Clean(map.Names!), map.Aliases!.Where(a => !string.IsNullOrWhiteSpace(a)).ToList()));
            }
            realms.Add(new RealmInfo(realm.Id!, Clean(realm.Names!), maps));
        }
        return new MapCatalog(realms, Clean(file.ReadyButton!), file.Version);
    }

    private static IReadOnlyDictionary<string, string> Clean(Dictionary<string, string> names) =>
        names.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
             .ToDictionary(kv => kv.Key, kv => kv.Value.Trim(), StringComparer.OrdinalIgnoreCase);

    private sealed class MapListFile
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("readyButton")] public Dictionary<string, string>? ReadyButton { get; set; }
        [JsonPropertyName("realms")] public List<RealmDto>? Realms { get; set; }
    }

    private sealed class RealmDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("names")] public Dictionary<string, string>? Names { get; set; }
        [JsonPropertyName("maps")] public List<MapDto>? Maps { get; set; }
    }

    private sealed class MapDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("names")] public Dictionary<string, string>? Names { get; set; }
        [JsonPropertyName("aliases")] public List<string>? Aliases { get; set; }
    }
}
