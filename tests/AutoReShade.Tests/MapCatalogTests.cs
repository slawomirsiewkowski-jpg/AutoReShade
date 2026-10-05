using AutoReShade.Core.Maps;

namespace AutoReShade.Tests;

public class MapCatalogTests
{
    [Fact]
    public void BuiltInListHasEveryRealmAndMap()
    {
        var catalog = MapCatalog.LoadBuiltIn();

        Assert.Equal(21, catalog.Realms.Count);
        Assert.Equal(50, catalog.Maps.Count);
        Assert.All(catalog.Maps, m => Assert.False(string.IsNullOrWhiteSpace(m.Names["en"])));
        Assert.Equal("Coal Tower", catalog.FindMap("coal-tower")!.DisplayName);
        Assert.Equal("the-macmillan-estate", catalog.FindMap("coal-tower")!.RealmId);
        Assert.Equal("Wieża Węglowa", catalog.FindMap("coal-tower")!.Names["pl"]);
    }

    [Fact]
    public void CustomFileAddsMapsAndLanguages()
    {
        const string custom = """
        {
          "realms": [
            { "id": "the-macmillan-estate", "names": {}, "maps": [
              { "id": "coal-tower", "names": { "xx": "Kohleturm X" }, "aliases": ["Coal Twr"] },
              { "id": "new-map", "names": { "en": "Brand New Map" } }
            ] },
            { "id": "new-realm", "names": { "en": "New Realm" }, "maps": [ { "id": "other-map", "names": { "en": "Other Map" } } ] }
          ]
        }
        """;

        var catalog = MapCatalog.Load(MapCatalog.ReadBuiltInJson(), custom);

        Assert.Equal(52, catalog.Maps.Count);
        Assert.Equal("Kohleturm X", catalog.FindMap("coal-tower")!.Names["xx"]);
        Assert.Contains("Coal Twr", catalog.FindMap("coal-tower")!.Aliases);
        Assert.Equal("the-macmillan-estate", catalog.FindMap("new-map")!.RealmId);
        Assert.NotNull(catalog.FindRealm("new-realm"));
    }

    [Fact]
    public void BrokenCustomFileIsReportedAndIgnored()
    {
        var file = Path.Combine(Path.GetTempPath(), $"custom-maps-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, "{ not json");
        try
        {
            var catalog = MapCatalog.LoadWithCustom(file, out var error);
            Assert.NotNull(error);
            Assert.Equal(50, catalog.Maps.Count);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
