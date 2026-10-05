using AutoReShade.Core.Maps;

namespace AutoReShade.Core.Detection;

/// <summary>
/// Recognises the screens that mean the last match is over, from OCR text:
/// the lobby (the Ready button together with the "[ESC]" key hint) and the results screen
/// (its Continue button). Neither appears during a match.
/// </summary>
public sealed class MatchEndMatcher
{
    private const string EscapeHint = "esc";

    /// <summary>A key hint is a short button label such as "BACK [ESC]", never a sentence.</summary>
    private const int MaxKeyHintWords = 3;

    /// <summary>OCR turns stray marks next to a button ("| READY") into one-letter words; those are ignored.</summary>
    private const int MinWordLength = 2;

    private readonly HashSet<string> _readyTexts;
    private readonly HashSet<string> _continueTexts;

    public MatchEndMatcher(MapCatalog catalog)
    {
        _readyTexts = ButtonTexts(catalog.ReadyButtonNames.Values);
        _continueTexts = ButtonTexts(catalog.ContinueButtonNames.Values);
    }

    public bool IsLobby(IEnumerable<string> ocrLines)
    {
        var lines = ButtonLines(ocrLines);
        return lines.Any(words => IsButton(words, _readyTexts)) && lines.Any(IsEscapeKeyHint);
    }

    /// <summary>Expects the text read around the Continue button in the bottom-right corner.</summary>
    public bool IsScoreboard(IEnumerable<string> ocrLines) =>
        ButtonLines(ocrLines).Any(words => IsButton(words, _continueTexts));

    private static HashSet<string> ButtonTexts(IEnumerable<string> names) =>
        names.Select(TextNormalizer.Compact).Where(text => text.Length > 0).ToHashSet(StringComparer.Ordinal);

    private static List<List<string>> ButtonLines(IEnumerable<string> ocrLines) =>
        ocrLines.Select(line => TextNormalizer.Words(line).Where(word => word.Length >= MinWordLength).ToList()).ToList();

    private static bool IsEscapeKeyHint(IReadOnlyList<string> words) =>
        words.Count is > 0 and <= MaxKeyHintWords && words[^1] == EscapeHint;

    /// <summary>The whole line must be the button text, so a sentence that merely contains "ready" does not count.</summary>
    private static bool IsButton(IReadOnlyList<string> words, HashSet<string> buttonTexts) =>
        words.Count > 0 && buttonTexts.Contains(TextNormalizer.Compact(words));
}
