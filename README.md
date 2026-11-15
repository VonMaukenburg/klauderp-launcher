# Klaude RP Launcher

Der offizielle Launcher von **Klaude Roleplay**, einem deutschen GTA-RP-Server auf FiveM. Hier liegt der komplette Quellcode,
damit jeder selbst nachsehen kann, was das Programm auf dem PC macht.

## Was er macht

- **Spielen:** startet TeamSpeak 3 (`ts3client_win64.exe "ts3server://…"`) und danach FiveM mit `fivem://connect/<server>`.
  Fehlen FiveM, TeamSpeak 3 oder das YaCA-Plugin, sagt er es und öffnet die Download-Seite.
- **Grafik:** schreibt nur den Abschnitt `<graphics>` in `%APPDATA%\CitizenFX\gta5_settings.xml` (die Grafikdatei von FiveM).
  Auflösung und Fenstermodus bleiben.
- **Tasten:** schreibt nur die `rbind`-Zeilen der Klaude-RP-Scripte in `%APPDATA%\CitizenFX\fivem.cfg`, alle anderen Zeilen bleiben.
- **Sicherungen:** Vor der ersten Änderung an Grafik oder Tasten werden die Originale nach `%LOCALAPPDATA%\KlaudeRP\daten\sicherungen`
  kopiert und lassen sich mit einem Klick zurückholen. Geschrieben wird nur, wenn FiveM geschlossen ist.
- **Cache leeren, nur Klaude RP:** FiveM speichert jede Server-Datei als `server-cache-priv\cache_<SHA1>`. Der Server nennt die SHA1
  seiner eigenen Dateien, gelöscht wird nur, was auf dieser Liste steht. Erst „Prüfen“, dann „Löschen“ mit Bestätigung.
- **Updates:** lädt beim Start nur geänderte Dateien und nimmt sie nur mit gültiger RSA-Signatur an (öffentlicher Schlüssel in
  `programm/Schluessel.cs`).

Der Launcher braucht keine Admin-Rechte und installiert sich nach `%LOCALAPPDATA%\KlaudeRP`. Deinstallieren geht unter
„Einstellungen → Apps“. Er sendet keine Daten von deinem PC: Er fragt beim Server nur Status, Neuigkeiten, die Cache-Liste und Updates ab.

## Aufbau

| Teil | Ordner |
| --- | --- |
| Programm (C#, .NET Framework 4.8, WinForms + WebView2) | `programm/` (`Aktionen.cs` enthält alle Aktionen am PC) |
| Oberfläche (HTML/CSS/JS, Schriften und Bilder lokal) | `web/` |
| Bauen, Signieren, Hilfsskripte | `bauen.py`, `cache-liste.py`, `tasten-liste.py`, `symbol-erzeugen.py` |

Die Oberfläche spricht nur über eine feste Liste von Aktionen mit dem Programm. Fremde Seiten öffnen sich im normalen Browser und
nur über `https`.

Dieser Code ist ein Auszug aus dem Server-Projekt. Fragen? Öffne gerne ein Ticket in unserem Discord.
