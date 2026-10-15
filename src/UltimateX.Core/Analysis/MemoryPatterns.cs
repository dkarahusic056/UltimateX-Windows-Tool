namespace UltimateX.Core.Analysis;

/// <summary>Testmuster des RAM-Stabilitätstests (angelehnt an die klassischen MemTest-Muster).</summary>
public enum MemoryPattern
{
    SolidZeros,
    SolidOnes,
    Checkerboard,
    InverseCheckerboard,
    WalkingOnes,
    WalkingZeros,
    AddressInAddress,
    Random,
}

/// <summary>Ein gefundener Speicherfehler (Adresse = virtuelle Adresse im Testpuffer).</summary>
public sealed record MemoryError(MemoryPattern Pattern, int Pass, ulong Address, ulong Expected, ulong Actual)
{
    public int FlippedBits => System.Numerics.BitOperations.PopCount(Expected ^ Actual);
}

public enum MemTestMode { Quick, Standard, Extended }

/// <summary>Parameter je Testmodus.</summary>
public sealed record MemTestPlan(MemTestMode Mode, double MemoryFraction, int Passes, IReadOnlyList<MemoryPattern> Patterns)
{
    public static MemTestPlan For(MemTestMode mode) => mode switch
    {
        MemTestMode.Quick => new(mode, 0.25, 1, [MemoryPattern.Checkerboard, MemoryPattern.AddressInAddress, MemoryPattern.Random]),
        MemTestMode.Standard => new(mode, 0.50, 2, Enum.GetValues<MemoryPattern>()),
        _ => new(mode, 0.70, 4, Enum.GetValues<MemoryPattern>()),
    };
}

/// <summary>
/// Reine Musterlogik des RAM-Tests: Werte werden deterministisch aus Muster, globalem Index und Seed berechnet,
/// sodass Schreiben und Prüfen unabhängig voneinander erfolgen (kein zweiter Puffer nötig).
/// </summary>
public static class MemoryPatterns
{
    public static string Name(MemoryPattern p) => p switch
    {
        MemoryPattern.SolidZeros => "Nur Nullen",
        MemoryPattern.SolidOnes => "Nur Einsen",
        MemoryPattern.Checkerboard => "Schachbrett",
        MemoryPattern.InverseCheckerboard => "Schachbrett invertiert",
        MemoryPattern.WalkingOnes => "Wandernde Eins",
        MemoryPattern.WalkingZeros => "Wandernde Null",
        MemoryPattern.AddressInAddress => "Adresse in Adresse",
        _ => "Zufallsmuster",
    };

    public static ulong Expected(MemoryPattern p, long index, ulong seed) => p switch
    {
        MemoryPattern.SolidZeros => 0UL,
        MemoryPattern.SolidOnes => ulong.MaxValue,
        MemoryPattern.Checkerboard => (index & 1) == 0 ? 0xAAAAAAAAAAAAAAAAUL : 0x5555555555555555UL,
        MemoryPattern.InverseCheckerboard => (index & 1) == 0 ? 0x5555555555555555UL : 0xAAAAAAAAAAAAAAAAUL,
        MemoryPattern.WalkingOnes => 1UL << (int)(index & 63),
        MemoryPattern.WalkingZeros => ~(1UL << (int)(index & 63)),
        MemoryPattern.AddressInAddress => (ulong)index ^ seed,
        _ => SplitMix((ulong)index + seed),
    };

    /// <summary>Schneller, gut verteilter Zahlengenerator (SplitMix64) – reproduzierbar je Index.</summary>
    public static ulong SplitMix(ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    public static void Fill(Span<ulong> block, MemoryPattern p, long baseIndex, ulong seed)
    {
        switch (p)
        {
            case MemoryPattern.SolidZeros: block.Clear(); return;
            case MemoryPattern.SolidOnes: block.Fill(ulong.MaxValue); return;
        }
        for (var i = 0; i < block.Length; i++) block[i] = Expected(p, baseIndex + i, seed);
    }

    /// <summary>
    /// Prüft einen Block und meldet Abweichungen (höchstens <paramref name="maxErrors"/>).
    /// <paramref name="addressOf"/> rechnet einen lokalen Index in eine Adresse um (für die Anzeige).
    /// </summary>
    public static int Verify(ReadOnlySpan<ulong> block, MemoryPattern p, long baseIndex, ulong seed, int pass,
        Func<int, ulong> addressOf, List<MemoryError> errors, int maxErrors)
    {
        var found = 0;
        for (var i = 0; i < block.Length; i++)
        {
            var expected = Expected(p, baseIndex + i, seed);
            var actual = block[i];
            if (actual == expected) continue;
            found++;
            if (errors.Count < maxErrors) errors.Add(new MemoryError(p, pass, addressOf(i), expected, actual));
        }
        return found;
    }

    /// <summary>Bewertung: 0 Fehler = PASS; Fehler nur in einem einzelnen Bit eines Durchlaufs = WARNING; sonst FAIL.</summary>
    public static CheckStatus Evaluate(long errorCount, IReadOnlyList<MemoryError> errors, bool completed)
    {
        if (errorCount == 0) return completed ? CheckStatus.Pass : CheckStatus.NotAvailable;
        var singleBitOnce = errorCount == 1 && errors.Count == 1 && errors[0].FlippedBits == 1;
        return singleBitOnce ? CheckStatus.Warning : CheckStatus.Fail;
    }
}
