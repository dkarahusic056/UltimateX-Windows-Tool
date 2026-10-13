using UltimateX.Core.Localization;

// Loc ist statisch → Tests nicht parallel ausführen, damit andere Tests immer Deutsch sehen.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace UltimateX.Core.Tests;

public sealed class LocalizationTests : IDisposable
{
    public LocalizationTests()
    {
        using var s = typeof(Loc).Assembly.GetManifestResourceStream("UltimateX.Core.Localization.en.json")
                      ?? throw new InvalidOperationException("en.json ist nicht eingebettet.");
        Loc.InitializeForTests(s);
    }

    public void Dispose() => Loc.ResetForTests();

    [Fact]
    public void Dictionary_IsEmbeddedAndLarge() => Assert.True(Loc.EntryCount > 1000);

    [Theory]
    [InlineData("Speicher bereinigen", "Clean up storage")]
    [InlineData("Abbrechen", "Cancel")]
    [InlineData("Neustart erforderlich", "Restart required")]
    public void Exact(string de, string en) => Assert.Equal(en, Loc.T(de));

    [Fact]
    public void Template_WithNumbers() =>
        Assert.Equal("Cleaned: 1.234 files, 2,5 GB freed", Loc.T("Bereinigt: 1.234 Dateien, 2,5 GB freigegeben"));

    [Fact]
    public void Template_TranslatesPlaceholderContent() =>
        Assert.Equal("Status: Note", Loc.T("Status: Hinweis"));

    [Fact]
    public void MultiLine_IsTranslatedLineByLine() =>
        Assert.Equal("Restart required\nNo errors found.", Loc.T("Neustart erforderlich\nKeine Fehler gefunden."));

    [Fact]
    public void Unknown_StaysUnchanged()
    {
        Assert.Equal("Samsung SSD 990 PRO 2TB", Loc.T("Samsung SSD 990 PRO 2TB"));
        Assert.Equal("", Loc.T(null));
    }

    [Fact]
    public void Whitespace_IsPreserved() => Assert.Equal("  Cancel ", Loc.T("  Abbrechen "));

    [Fact]
    public void Format_UsesTranslatedPattern() =>
        Assert.Equal("Version 2.0 is available.", Loc.F("Version {0} ist verfügbar.", "2.0"));

    [Fact]
    public void German_IsPassThrough()
    {
        Loc.ResetForTests();
        Assert.Equal("Abbrechen", Loc.T("Abbrechen"));
    }
}
