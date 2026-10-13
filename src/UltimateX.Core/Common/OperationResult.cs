namespace UltimateX.Core.Common;

/// <summary>
/// Status eines erhobenen Werts. Grundregel des Produkts: keine erfundenen Werte.
/// Was nicht zuverlässig ermittelt werden kann, wird als <see cref="NotAvailable"/>
/// oder <see cref="RequiresAdmin"/> gekennzeichnet statt geschätzt.
/// </summary>
public enum DataState
{
    Ok,
    Warning,
    Problem,
    NotAvailable,
    RequiresAdmin,
}

/// <summary>Ergebnis einer Aktion, ohne Exceptions über Schichtgrenzen zu werfen.</summary>
public sealed record OperationResult(bool Success, string Message, string? TechnicalDetails = null)
{
    public static OperationResult Ok(string message, string? details = null) => new(true, message, details);
    public static OperationResult Fail(string message, string? details = null) => new(false, message, details);
}

/// <summary>Ein einzelner angezeigter Wert inkl. Status (für Statuskarten und Detailtabellen).</summary>
public sealed record InfoItem(string Label, string Value, DataState State = DataState.Ok, bool IsSensitive = false)
{
    public static InfoItem Unavailable(string label, string reason = "nicht verfügbar")
        => new(label, reason, DataState.NotAvailable);
}
