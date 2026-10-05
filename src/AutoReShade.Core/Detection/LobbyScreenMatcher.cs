using AutoReShade.Core.Maps;

namespace AutoReShade.Core.Detection;

/// <summary>
/// Recognises the lobby in OCR text: the Ready button together with the "[ESC]" key hint
/// at the bottom of the screen. That combination never appears during a match, so seeing it
/// means the last match is over.
/// </summary>
public sealed class LobbyScreenMatcher
{
    private const string EscapeHint = "esc";

    /// <summary>A key hint is a short button label such as "BACK [ESC]", never a sentence.</summary>
    private const int MaxKeyHintWords = 3;

    /// <summary>OCR turns stray marks next to a button ("| READY") into one-letter words; those are ignored.</summary>
    private const int MinWordLength = 2;

    private readonly HashSet<string> _readyTexts;

    public LobbyScreenMatcher(MapCatalog catalog)
    {
        _readyTexts = catalog.ReadyButtonNames.Values
            .Select(TextNormalizer.Compact)
            .Where(text => text.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
    }

    public bool IsLobby(IEnumerable<string> ocrLines)
    {
        var lines = ocrLines
            .Select(line => TextNormalizer.Words(line).Where(word => word.Length >= MinWordLength).ToList())
            .ToList();
        return lines.Any(IsReadyButton) && lines.Any(IsEscapeKeyHint);
    }

    private static bool IsEscapeKeyHint(IReadOnlyList<string> words) =>
        words.Count is > 0 and <= MaxKeyHintWords && words[^1] == EscapeHint;

    /// <summary>The whole line must be the button text, so a sentence that merely contains "ready" does not count.</summary>
    private bool IsReadyButton(IReadOnlyList<string> words) =>
        words.Count > 0 && _readyTexts.Contains(TextNormalizer.Compact(words));
}
