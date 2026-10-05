using System.Text;

namespace AutoReShade.Core.ReShade;

/// <summary>
/// Minimal INI editor that changes only the keys it is asked to change and keeps
/// every other line, the newline style and the byte-order mark exactly as they were.
/// </summary>
public sealed class IniDocument
{
    private readonly List<string> _lines;
    private readonly bool _hasBom;
    private readonly string _newline;

    private IniDocument(List<string> lines, bool hasBom, string newline)
    {
        _lines = lines;
        _hasBom = hasBom;
        _newline = newline;
    }

    public static IniDocument Parse(string text, bool hasBom = false)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        // A trailing newline produces one empty element; drop it so saving does not add lines.
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return new IniDocument(lines, hasBom, newline);
    }

    public static IniDocument Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = new UTF8Encoding(false).GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
        return Parse(text, hasBom);
    }

    public string? Get(string section, string key)
    {
        var index = FindKey(section, key);
        if (index < 0) return null;
        var line = _lines[index];
        return line[(line.IndexOf('=') + 1)..];
    }

    public void Set(string section, string key, string value)
    {
        var index = FindKey(section, key);
        if (index >= 0)
        {
            _lines[index] = $"{key}={value}";
            return;
        }

        var (start, end) = FindSection(section);
        if (section.Length == 0)
        {
            var globalInsert = end;
            while (globalInsert > 0 && _lines[globalInsert - 1].Trim().Length == 0) globalInsert--;
            _lines.Insert(globalInsert, $"{key}={value}");
            return;
        }

        if (start < 0)
        {
            if (_lines.Count > 0 && _lines[^1].Trim().Length > 0) _lines.Add(string.Empty);
            _lines.Add($"[{section}]");
            _lines.Add($"{key}={value}");
            return;
        }

        // Insert after the last non-empty line of the section.
        var insertAt = end;
        while (insertAt > start + 1 && _lines[insertAt - 1].Trim().Length == 0) insertAt--;
        _lines.Insert(insertAt, $"{key}={value}");
    }

    public override string ToString() => string.Join(_newline, _lines) + _newline;

    /// <summary>Writes to a temporary file first, so a crash never leaves a half-written ReShade.ini.</summary>
    public void Save(string path)
    {
        var temp = path + ".autoreshade.tmp";
        var encoding = new UTF8Encoding(_hasBom);
        File.WriteAllText(temp, ToString(), encoding);
        File.Move(temp, path, overwrite: true);
    }

    private int FindKey(string section, string key)
    {
        var (start, end) = FindSection(section);
        if (start < 0 && section.Length > 0) return -1;
        for (var i = Math.Max(0, start + (section.Length > 0 ? 1 : 0)); i < end; i++)
        {
            var line = _lines[i].TrimStart();
            var eq = line.IndexOf('=');
            if (eq <= 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (string.Equals(line[..eq].Trim(), key, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>Returns the header line index and the exclusive end of a section. "" is the part before the first header.</summary>
    private (int Start, int End) FindSection(string section)
    {
        if (section.Length == 0)
        {
            var firstHeader = _lines.FindIndex(IsHeader);
            return (-1, firstHeader < 0 ? _lines.Count : firstHeader);
        }

        for (var i = 0; i < _lines.Count; i++)
        {
            if (!IsHeader(_lines[i])) continue;
            var name = _lines[i].Trim()[1..^1].Trim();
            if (!string.Equals(name, section, StringComparison.OrdinalIgnoreCase)) continue;
            var end = i + 1;
            while (end < _lines.Count && !IsHeader(_lines[end])) end++;
            return (i, end);
        }
        return (-1, -1);
    }

    private static bool IsHeader(string line)
    {
        var t = line.Trim();
        return t.Length >= 2 && t[0] == '[' && t[^1] == ']';
    }
}
