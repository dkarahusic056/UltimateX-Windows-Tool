# Lokalisierung (§84)

**Ansatz:** Deutsch ist die Quellsprache im Code. Für Englisch wird zur Laufzeit übersetzt – ohne jeden Text im Code anfassen zu müssen.

| Baustein | Aufgabe |
|---|---|
| `UltimateX.Core/Localization/Loc.cs` | Wörterbuch-Engine: exakte Texte + Vorlagen mit Platzhaltern (`Bereinigt: {0} Dateien, {1} freigegeben`). Platzhalter-Inhalte werden rekursiv übersetzt; zusammengesetzte Meldungen werden zeilen-, abschnitts- (`·`) und satzweise übersetzt. Unbekanntes bleibt unverändert (keine erfundenen Texte). |
| `UltimateX.Core/Localization/en.json` | Eingebettetes englisches Wörterbuch (~1 450 Texte, ~270 Vorlagen). |
| `UltimateX.App/Infrastructure/UiTranslator.cs` | Beobachtet geladene TextBlocks, Buttons, Überschriften, Spaltenköpfe und Tooltips und übersetzt Text bei jeder Änderung (auch gebundene Live-Werte). Eingabefelder werden nie verändert. |
| `Dialogs`, `NotificationService` | Meldungsfenster und Windows-Benachrichtigungen werden übersetzt. |
| `Format` | Zahlen/Datum im Format der aktiven Sprache. |

**Sprache wählen:** Einstellungen → Darstellung → Sprache (Automatisch = Windows-Anzeigesprache; Deutsch-Windows → Deutsch, sonst Englisch). Wirkt nach Neustart der App („App neu starten“).

**Neue Texte übersetzen:** `tools/localization/extract2.py` listet alle Texte, `en_*.tsv` enthalten die Übersetzungen (ID oder Vorlage), `build_json.py` erzeugt `en.json`. Der Test `LocalizationTests` prüft Einbettung und Vorlagen.
