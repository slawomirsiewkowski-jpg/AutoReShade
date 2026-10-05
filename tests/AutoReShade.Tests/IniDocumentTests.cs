using System.Text;
using AutoReShade.Core.ReShade;

namespace AutoReShade.Tests;

public class IniDocumentTests
{
    [Fact]
    public void UnchangedDocumentRoundTripsExactly()
    {
        const string text = "Techniques=A@a.fx\n\n[a.fx]\nX=1\n; comment\n";
        Assert.Equal(text, IniDocument.Parse(text).ToString());
    }

    [Fact]
    public void SetReplacesOnlyTheValue()
    {
        var doc = IniDocument.Parse("[GENERAL]\r\nA=1\r\nB=2\r\n\r\n[INPUT]\r\nA=9\r\n");
        doc.Set("GENERAL", "a", "5");
        Assert.Equal("[GENERAL]\r\na=5\r\nB=2\r\n\r\n[INPUT]\r\nA=9\r\n", doc.ToString());
        Assert.Equal("9", doc.Get("INPUT", "A"));
    }

    [Fact]
    public void SetAddsMissingKeysAndSections()
    {
        var doc = IniDocument.Parse("Top=1\n\n[GENERAL]\nA=1\n\n[INPUT]\nK=2\n");
        doc.Set("GENERAL", "New", "x");
        doc.Set("", "Global", "y");
        doc.Set("EXTRA", "Z", "z");

        Assert.Equal("Top=1\nGlobal=y\n\n[GENERAL]\nA=1\nNew=x\n\n[INPUT]\nK=2\n\n[EXTRA]\nZ=z\n", doc.ToString());
    }

    [Fact]
    public void KeepsByteOrderMark()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bom-{Guid.NewGuid():N}.ini");
        File.WriteAllText(path, "[GENERAL]\r\nA=1\r\n", new UTF8Encoding(true));
        try
        {
            var doc = IniDocument.Load(path);
            doc.Set("GENERAL", "A", "Łódź");
            doc.Save(path);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            Assert.Equal("Łódź", IniDocument.Load(path).Get("GENERAL", "A"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
