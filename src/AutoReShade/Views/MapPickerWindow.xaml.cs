using System.Windows;
using System.Windows.Input;
using AutoReShade.Core.Detection;
using AutoReShade.Core.Maps;

namespace AutoReShade.Views;

/// <summary>Emergency manual map choice, for when detection missed the loading screen.</summary>
public partial class MapPickerWindow : Window
{
    private readonly List<Entry> _all;

    public MapPickerWindow(MapCatalog catalog, MapInfo? current)
    {
        InitializeComponent();
        _all = catalog.Realms
            .SelectMany(r => r.Maps.Select(m => new Entry(m, r.DisplayName, TextNormalizer.Compact(r.DisplayName + " " + string.Join(" ", m.AllNames)))))
            .OrderBy(e => e.Map.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        MapList.ItemsSource = _all;
        MapList.SelectedItem = _all.FirstOrDefault(e => e.Map.Id == current?.Id) ?? _all.FirstOrDefault();
        Loaded += (_, _) => SearchBox.Focus();
    }

    public event Action<MapInfo>? MapChosen;

    private void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var query = TextNormalizer.Compact(SearchBox.Text);
        var filtered = query.Length == 0 ? _all : _all.Where(x => x.SearchText.Contains(query, StringComparison.Ordinal)).ToList();
        MapList.ItemsSource = filtered;
        MapList.SelectedIndex = filtered.Count > 0 ? 0 : -1;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (MapList.Items.Count == 0) return;
        if (e.Key == Key.Down)
        {
            MapList.SelectedIndex = Math.Min(MapList.Items.Count - 1, MapList.SelectedIndex + 1);
            MapList.ScrollIntoView(MapList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            MapList.SelectedIndex = Math.Max(0, MapList.SelectedIndex - 1);
            MapList.ScrollIntoView(MapList.SelectedItem);
            e.Handled = true;
        }
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnChoose(sender, e);
    }

    private void OnChoose(object sender, RoutedEventArgs e)
    {
        if (MapList.SelectedItem is not Entry entry) return;
        Close();
        MapChosen?.Invoke(entry.Map);
    }

    public sealed record Entry(MapInfo Map, string RealmName, string SearchText);
}
