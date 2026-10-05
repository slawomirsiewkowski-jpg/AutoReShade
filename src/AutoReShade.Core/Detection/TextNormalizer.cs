using System.Globalization;
using System.Text;

namespace AutoReShade.Core.Detection;

/// <summary>
/// Turns OCR output and map names into a comparable form: lower case, no accents,
/// no punctuation, and Roman numerals cleaned of typical OCR confusions (l, 1, | read instead of I).
/// </summary>
public static class TextNormalizer
{
    public static IReadOnlyList<string> Words(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;
            sb.Append(c);
        }

        var lowered = sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant()
            .Replace('-', ' ').Replace('_', ' ').Replace('/', ' ');
        var words = new List<string>();
        foreach (var rawToken in lowered.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = FixRomanNumeral(rawToken);
            var clean = new StringBuilder(token.Length);
            foreach (var c in token)
            {
                // Apostrophes and other punctuation are dropped: OCR often loses or invents them.
                if (char.IsLetterOrDigit(c)) clean.Append(LookAlikeLetter(c));
            }
            if (clean.Length > 0) words.Add(clean.ToString());
        }
        return words;
    }

    public static string Compact(IEnumerable<string> words) => string.Concat(words);

    /// <summary>
    /// How much text a string holds for matching purposes. Chinese, Japanese and Korean characters
    /// carry far more information than Latin letters, so they count double.
    /// </summary>
    public static int Weight(string compact)
    {
        var weight = 0;
        foreach (var c in compact)
            weight += c >= 'ᄀ' ? 2 : 1;
        return weight;
    }

    /// <summary>Map names contain no digits, so digits in OCR output are letters read wrong (C0AL T0WER).</summary>
    private static char LookAlikeLetter(char c) => c switch
    {
        '0' => 'o',
        '1' => 'i',
        '5' => 's',
        '8' => 'b',
        '6' => 'g',
        _ => c,
    };

    public static string Compact(string text) => Compact(Words(text));

    /// <summary>"lll", "1I", "|V" and similar short tokens are almost always a misread Roman numeral.</summary>
    private static string FixRomanNumeral(string token)
    {
        var trimmed = token.Trim('.', ',', ':', ';', '\'', '"', '(', ')');
        if (trimmed.Length is 0 or > 4) return token;

        var hasStroke = false;
        foreach (var c in trimmed)
        {
            if (c is 'i' or 'l' or '1' or '|' or '!') hasStroke = true;
            else if (c != 'v') return token;
        }
        if (!hasStroke && trimmed != "v") return token;

        var fixedToken = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
            fixedToken.Append(c == 'v' ? 'v' : 'i');
        return fixedToken.ToString();
    }
}
