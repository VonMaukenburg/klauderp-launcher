using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Win32;

namespace KlaudeRP
{
    /// <summary>Alles, was die Oberfläche vom PC will. Nur diese Aktionen gibt es, mit festen Pfaden.</summary>
    internal sealed class Aktionen
    {
        private readonly Hauptfenster fenster;
        private readonly Aktualisierung aktualisierung;
        private Dictionary<string, object> info;

        private static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private static readonly string Lokal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private static readonly string CitizenFX = Path.Combine(Roaming, "CitizenFX");
        private static readonly string GrafikDatei = Path.Combine(CitizenFX, "gta5_settings.xml");
        private static readonly string TastenDatei = Path.Combine(CitizenFX, "fivem.cfg");
        private static readonly string SicherungOrdner = Path.Combine(Installation.DatenOrdner, "sicherungen");
        private static readonly string ProfilDatei = Path.Combine(Installation.DatenOrdner, "tastenprofile.json");

        public Aktionen(Hauptfenster fenster)
        {
            this.fenster = fenster;
            aktualisierung = new Aktualisierung();
        }

        public async Task<object> Ausfuehren(string aktion, Dictionary<string, object> d)
        {
            switch (aktion)
            {
                case "fenster": return Fenster(Text(d, "aktion"));
                case "info": return await Info();
                case "status": return await Status();
                case "spielen": return await Spielen();
                case "teamspeak": return await TeamspeakOeffnen();
                case "grafikLesen": return await Task.Run(() => GrafikLesen());
                case "grafikSchreiben": return await Task.Run(() => GrafikSchreiben(d));
                case "tastenLesen": return await Task.Run(() => TastenLesen());
                case "tastenSchreiben": return await Task.Run(() => TastenSchreiben(d));
                case "profileLesen": return await Task.Run(() => ProfileLesen());
                case "profileSpeichern": return await Task.Run(() => ProfileSpeichern(d));
                case "cachePruefen": return await CachePruefen();
                case "cacheLoeschen": return await Task.Run(() => CacheLoeschen(d));
                case "updatePruefen": return await aktualisierung.Pruefen();
                case "updateInstallieren": return await UpdateInstallieren();
                case "sicherungZurueck": return await Task.Run(() => SicherungZurueck(d));
                default: throw new Hinweis("Unbekannte Aktion.");
            }
        }

        private static string Text(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) ? v as string : null;

        private object Fenster(string was)
        {
            if (was == "ziehen") fenster.Ziehen();
            else if (was == "min") fenster.Minimieren();
            else if (was == "zu") fenster.Close();
            return true;
        }

        // ------------------------------------------------------------ Server-Infos
        private async Task<Dictionary<string, object>> Info()
        {
            try
            {
                info = Json.Lesen(await Netz.Text(Einstellungen.DatenUrl + "/info"));
            }
            catch
            {
                if (info == null) throw new Hinweis("Server nicht erreichbar.");
            }
            return info;
        }

        private string ServerAdresse()
        {
            if (info != null && info.TryGetValue("server", out var s) && s is Dictionary<string, object> srv && srv.TryGetValue("adresse", out var a) && a is string adr
                && Regex.IsMatch(adr, @"^[A-Za-z0-9.\-]+:\d{2,5}$")) return adr;
            return Einstellungen.StandardServer;
        }

        private (string adresse, int port) Teamspeak()
        {
            if (info != null && info.TryGetValue("teamspeak", out var t) && t is Dictionary<string, object> ts && ts.TryGetValue("adresse", out var a) && a is string adr
                && Regex.IsMatch(adr, @"^[A-Za-z0-9.\-]+$"))
            {
                int port = ts.TryGetValue("port", out var p) && p is int pi ? pi : Einstellungen.StandardTeamspeakPort;
                return (adr, port);
            }
            return (Einstellungen.StandardTeamspeak, Einstellungen.StandardTeamspeakPort);
        }

        // ------------------------------------------------------------ Status
        public static bool FiveMLaeuft()
        {
            return Process.GetProcesses().Any(p =>
            {
                try { return p.ProcessName.StartsWith("FiveM", StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            });
        }

        private static string FiveMOrdner()
        {
            var standard = Path.Combine(Lokal, "FiveM");
            if (File.Exists(Path.Combine(standard, "FiveM.exe"))) return standard;
            foreach (var schluessel in new[] { @"Software\Classes\fivem\shell\open\command" })
            {
                foreach (var wurzel in new[] { Registry.CurrentUser, Registry.LocalMachine })
                {
                    try
                    {
                        using (var k = wurzel.OpenSubKey(schluessel))
                        {
                            var befehl = k?.GetValue(null) as string;
                            var m = befehl == null ? null : Regex.Match(befehl, "\"?([^\"]+FiveM\\.exe)\"?", RegexOptions.IgnoreCase);
                            if (m != null && m.Success && File.Exists(m.Groups[1].Value)) return Path.GetDirectoryName(m.Groups[1].Value);
                        }
                    }
                    catch { }
                }
            }
            return null;
        }

        // YaCA läuft nur in TeamSpeak 3. ts3server:// kann aber auf TeamSpeak 5/6 zeigen (getestet
        // 15.11.2026: TS6 ging auf, YaCA verband nicht). Darum suchen wir ts3client_win64.exe selbst und starten genau den.
        private static string Teamspeak3()
        {
            var kandidaten = new List<string>();
            foreach (var wurzel in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                foreach (var schluessel in new[] { @"SOFTWARE\TeamSpeak 3 Client", @"SOFTWARE\WOW6432Node\TeamSpeak 3 Client" })
                {
                    try
                    {
                        using (var k = wurzel.OpenSubKey(schluessel))
                            if (k?.GetValue(null) is string ordner && ordner.Length > 0) kandidaten.Add(ordner.Trim('"'));
                    }
                    catch { }
                }
            }
            kandidaten.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TeamSpeak 3 Client"));
            kandidaten.Add(Path.Combine(Lokal, "TeamSpeak 3 Client"));
            foreach (var ordner in kandidaten)
            {
                try
                {
                    var exe = Path.Combine(ordner, "ts3client_win64.exe");
                    if (File.Exists(exe)) return exe;
                }
                catch { }
            }
            return null;
        }

        private static bool YacaInstalliert()
        {
            var ordner = Path.Combine(Roaming, "TS3Client", "plugins");
            return Directory.Exists(ordner) && Directory.GetFiles(ordner, "yaca*.dll").Length > 0;
        }

        private async Task<Dictionary<string, object>> Status()
        {
            if (info == null) { try { await Info(); } catch { } }
            var server = new Dictionary<string, object> { ["online"] = false };
            try
            {
                var uhr = Stopwatch.StartNew();
                var dyn = Json.Lesen(await Netz.Text("http://" + ServerAdresse() + "/dynamic.json"));
                uhr.Stop();
                server["online"] = true;
                server["spieler"] = dyn.TryGetValue("clients", out var c) ? c : 0;
                server["max"] = dyn.TryGetValue("sv_maxclients", out var m) ? m : "?";
                server["ping"] = uhr.ElapsedMilliseconds;
            }
            catch { }
            return new Dictionary<string, object>
            {
                ["server"] = server,
                ["fivem"] = new Dictionary<string, object> { ["installiert"] = FiveMOrdner() != null, ["laeuft"] = FiveMLaeuft() },
                ["teamspeak"] = new Dictionary<string, object> { ["installiert"] = Teamspeak3() != null, ["yaca"] = YacaInstalliert() },
            };
        }

        // ------------------------------------------------------------ Spielen
        private async Task<Dictionary<string, object>> Spielen()
        {
            if (info == null) { try { await Info(); } catch { } }
            var fivem = FiveMOrdner();
            if (fivem == null)
            {
                Hauptfenster.Extern("https://fivem.net/");
                return Antwort(false, "FiveM ist nicht installiert. Die Download-Seite ist offen, nach der Installation einfach noch mal auf Spielen.");
            }
            string hinweis = null;
            var ts3 = Teamspeak3();
            if (ts3 != null)
            {
                // TS3 öffnet bei jedem ts3server:// einen neuen Server-Tab, auch wenn man schon drin ist (getestet: fünfmal derselbe
                // Name im Kanal). Läuft TS3 schon, verbindet „Spielen“ darum nicht noch einmal; dafür gibt es „Nur TeamSpeak öffnen“.
                if (!Teamspeak3Laeuft())
                {
                    TeamspeakStarten(ts3);
                    await Task.Delay(1500);
                }
                if (!YacaInstalliert()) hinweis = "Das Sprach-Plugin YaCA fehlt in TeamSpeak. Ohne es hörst du im Spiel niemanden.";
            }
            else
            {
                hinweis = "TeamSpeak 3 ist nicht installiert. Ohne TeamSpeak gibt es keine Sprache im Spiel.";
            }
            var argumente = "fivem://connect/" + ServerAdresse();
            var vorher = NeuestesFiveMLog(fivem);
            FiveMStarten(fivem, argumente, 1);
            _ = FiveMBeobachten(fivem, argumente, vorher);
            return Antwort(hinweis == null, hinweis ?? "FiveM startet und verbindet sich mit Klaude RP.");
        }

        private static bool Teamspeak3Laeuft()
        {
            try { return Process.GetProcessesByName("ts3client_win64").Length > 0; }
            catch { return false; }
        }

        private static void FiveMStarten(string fivem, string argumente, int versuch)
        {
            // FiveM (der Rockstar-Teil) bricht mit „This application should be launched directly from the shell or a web browser“ ab,
            // wenn KlaudeRP.exe es selbst startet (15.11.2026, echte Klicks). Der alte PowerShell-Launcher funktioniert: Dort
            // startet ein Windows-Programm FiveM und bleibt dabei offen. Darum wie dort: eine unsichtbare PowerShell mit
            // Start-Process "fivem://…", die noch eine Minute offen bleibt. Mit 1.0.4 3 von 3 Mal erfolgreich; der Weg über
            // cmd.exe start /wait wurde 3 von 3 Mal abgelehnt und ist darum raus.
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"Start-Process '{argumente}'; Start-Sleep 60\"");
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WindowStyle = ProcessWindowStyle.Hidden;
            start.WorkingDirectory = fivem;
            StartLog($"Versuch {versuch}: {Path.GetFileName(start.FileName)} {start.Arguments}");
            Process.Start(start);
        }

        private static void StartLog(string text)
        {
            try { File.AppendAllText(Path.Combine(Installation.DatenOrdner, "start-log.txt"), DateTime.Now + " " + text + "\r\n"); } catch { }
        }

        private static string NeuestesFiveMLog(string fivem)
        {
            try
            {
                var ordner = Path.Combine(fivem, "FiveM.app", "logs");
                return new DirectoryInfo(ordner).GetFiles("CitizenFX_log_*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;
            }
            catch { return null; }
        }

        // Sicherheitsnetz: neues FiveM-Log bis zu 60 s beobachten. Bricht FiveM mit „This application should be launched directly from the
        // shell or a web browser“ ab, FiveM beenden und einmal neu starten. Klappt auch das nicht, sagt der Launcher es.
        private async Task FiveMBeobachten(string fivem, string argumente, string altesLog)
        {
            try
            {
                for (var versuch = 1; versuch <= 2; versuch++)
                {
                    var ergebnis = await AufFiveMLogWarten(fivem, altesLog);
                    StartLog($"Versuch {versuch}: Ergebnis {ergebnis}");
                    if (ergebnis != "abbruch") return;          // läuft oder nicht feststellbar: nichts tun
                    foreach (var p in Process.GetProcesses())
                    {
                        try { if (p.ProcessName.StartsWith("FiveM", StringComparison.OrdinalIgnoreCase)) p.Kill(); } catch { }
                    }
                    if (versuch == 2) break;
                    await Task.Delay(3000);
                    altesLog = NeuestesFiveMLog(fivem);
                    FiveMStarten(fivem, argumente, versuch + 1);
                }
                fenster.Ereignis(new Dictionary<string, object>
                {
                    ["ereignis"] = "meldung", ["art"] = "warnung",
                    ["text"] = "FiveM hat den Start abgelehnt. Starte FiveM einmal normal (Desktop-Symbol) und klicke dann noch mal auf Spielen.",
                });
            }
            catch { }
        }

        private static async Task<string> AufFiveMLogWarten(string fivem, string altesLog)
        {
            var ende = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < ende)
            {
                await Task.Delay(2000);
                var log = NeuestesFiveMLog(fivem);
                if (log == null || log == altesLog) continue;
                string text;
                try
                {
                    using (var s = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var r = new StreamReader(s)) text = r.ReadToEnd();
                }
                catch { continue; }
                if (text.Contains("launched directly from the shell")) return "abbruch";
                if (text.Contains("GTAProce")) return "laeuft";
            }
            return "unbekannt";
        }

        private void TeamspeakStarten(string ts3)
        {
            var (adresse, port) = Teamspeak();
            // ts3client hat uiAccess="true" im Manifest: direkt (CreateProcess) gibt das „erfordert erhöhte Rechte“, über die Shell nicht.
            Process.Start(new ProcessStartInfo(ts3, $"\"ts3server://{adresse}?port={port}\"")
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(ts3),
            });
        }

        private async Task<Dictionary<string, object>> TeamspeakOeffnen()
        {
            if (info == null) { try { await Info(); } catch { } }
            var ts3 = Teamspeak3();
            if (ts3 == null)
            {
                Hauptfenster.Extern("https://www.teamspeak.com/de/downloads/");
                return Antwort(false, "TeamSpeak 3 ist nicht installiert. Die Download-Seite ist offen.");
            }
            TeamspeakStarten(ts3);
            return Antwort(true, "TeamSpeak verbindet sich mit Klaude RP.");
        }

        private static Dictionary<string, object> Antwort(bool ok, string hinweis) => new Dictionary<string, object> { ["ok"] = ok, ["hinweis"] = hinweis };

        private static void FiveMMussZuSein()
        {
            if (FiveMLaeuft()) throw new Hinweis("Bitte schließe FiveM zuerst ganz (auch das kleine FiveM-Fenster), dann noch einmal.");
        }

        private static void ErsteSicherung(string datei)
        {
            Directory.CreateDirectory(SicherungOrdner);
            var ziel = Path.Combine(SicherungOrdner, Path.GetFileName(datei));
            if (!File.Exists(ziel) && File.Exists(datei)) File.Copy(datei, ziel);
        }

        // ------------------------------------------------------------ Grafik (%APPDATA%\CitizenFX\gta5_settings.xml)
        private static readonly Regex GrafikName = new Regex(@"^[A-Za-z_]+[A-Za-z0-9_]*$");
        private static readonly Regex GrafikWert = new Regex(@"^(true|false|-?\d+(\.\d+)?)$");

        private static object GrafikLesen()
        {
            if (!File.Exists(GrafikDatei)) return new Dictionary<string, object> { ["vorhanden"] = false };
            var doc = new XmlDocument();
            doc.Load(GrafikDatei);
            var werte = new Dictionary<string, object>();
            var g = doc.SelectSingleNode("/Settings/graphics");
            if (g != null)
            {
                foreach (XmlNode n in g.ChildNodes)
                {
                    if (n is XmlElement e && e.HasAttribute("value")) werte[e.Name] = e.GetAttribute("value");
                }
            }
            return new Dictionary<string, object> { ["vorhanden"] = g != null, ["werte"] = werte };
        }

        private static object GrafikSchreiben(Dictionary<string, object> d)
        {
            if (!File.Exists(GrafikDatei)) throw new Hinweis("Keine FiveM-Grafikdatei gefunden. Starte FiveM einmal, danach geht es.");
            FiveMMussZuSein();
            if (!(d.TryGetValue("werte", out var w) && w is Dictionary<string, object> werte) || werte.Count == 0) throw new Hinweis("Keine Werte.");

            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(GrafikDatei);
            var g = doc.SelectSingleNode("/Settings/graphics") as XmlElement ?? throw new Hinweis("Die Grafikdatei von FiveM sieht ungewohnt aus, nichts geändert.");
            foreach (var paar in werte)
            {
                var wert = Convert.ToString(paar.Value, System.Globalization.CultureInfo.InvariantCulture);
                if (!GrafikName.IsMatch(paar.Key) || wert == null || !GrafikWert.IsMatch(wert)) continue;
                var e = g.SelectSingleNode(paar.Key) as XmlElement;
                if (e == null)
                {
                    e = doc.CreateElement(paar.Key);
                    g.AppendChild(doc.CreateWhitespace("  "));
                    g.AppendChild(e);
                    g.AppendChild(doc.CreateWhitespace("\r\n  "));
                }
                e.SetAttribute("value", wert);
            }
            // Eigene Einstellung: FiveM soll sie nicht durch Automatik ersetzen
            if (doc.SelectSingleNode("/Settings/configSource") is XmlElement quelle) quelle.InnerText = "SMC_USER";

            ErsteSicherung(GrafikDatei);
            using (var puffer = new MemoryStream())
            {
                var einst = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, NewLineHandling = NewLineHandling.None };
                using (var schreiber = XmlWriter.Create(puffer, einst)) doc.Save(schreiber);
                // XmlWriter schreibt „utf-8“ klein, FiveM selbst schreibt „UTF-8“: so bleibt die Datei wie gewohnt.
                var text = Encoding.UTF8.GetString(puffer.ToArray());
                text = new Regex("encoding=\"utf-8\"").Replace(text, "encoding=\"UTF-8\"", 1);
                Dateien.SicherSchreiben(GrafikDatei, new UTF8Encoding(false).GetBytes(text));
            }
            return true;
        }

        // ------------------------------------------------------------ Tasten (%APPDATA%\CitizenFX\fivem.cfg, Zeilen „rbind <ressource> KEYBOARD <taste> "<befehl>"“)
        private static readonly Regex Bind = new Regex("^\\s*rbind\\s+(\\S+)\\s+(\\S+)\\s+(\\S+)\\s+\"([^\"]*)\"\\s*$");
        private static readonly Regex Name = new Regex(@"^[A-Za-z0-9_.:\-+~!]+$");
        private static readonly Regex TasteId = new Regex(@"^[A-Z0-9_]+$");

        private static object TastenLesen()
        {
            if (!File.Exists(TastenDatei)) return new Dictionary<string, object> { ["vorhanden"] = false };
            var belegung = new Dictionary<string, object>();
            foreach (var zeile in File.ReadAllLines(TastenDatei, Encoding.UTF8))
            {
                var m = Bind.Match(zeile);
                if (!m.Success || !m.Groups[2].Value.Equals("KEYBOARD", StringComparison.OrdinalIgnoreCase)) continue;
                belegung[m.Groups[1].Value + "|" + m.Groups[4].Value] = m.Groups[3].Value.ToUpperInvariant();
            }
            return new Dictionary<string, object> { ["vorhanden"] = true, ["belegung"] = belegung };
        }

        private static object TastenSchreiben(Dictionary<string, object> d)
        {
            if (!File.Exists(TastenDatei)) throw new Hinweis("Keine FiveM-Einstellungen gefunden. Starte FiveM einmal, danach geht es.");
            FiveMMussZuSein();
            if (!(d.TryGetValue("belegung", out var b) && b is Dictionary<string, object> belegung)) throw new Hinweis("Keine Tasten.");
            var standard = d.TryGetValue("standard", out var s) && s is Dictionary<string, object> st ? st : new Dictionary<string, object>();

            var roh = File.ReadAllText(TastenDatei, Encoding.UTF8);
            var umbruch = roh.Contains("\r\n") ? "\r\n" : "\n";
            var zeilen = roh.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
            if (zeilen.Count > 0 && zeilen[zeilen.Count - 1] == "") zeilen.RemoveAt(zeilen.Count - 1);

            var neu = new List<string>();
            foreach (var paar in belegung)
            {
                var teile = paar.Key.Split('|');
                var taste = (paar.Value as string ?? "").ToUpperInvariant();
                if (teile.Length != 2 || !Name.IsMatch(teile[0]) || !Name.IsMatch(teile[1]) || !TasteId.IsMatch(taste)) continue;
                var ressource = teile[0];
                var befehl = teile[1];
                // Vorhandene Tastatur-Zeilen genau dieser Funktion entfernen, alles andere bleibt.
                int vorher = zeilen.RemoveAll(z =>
                {
                    var m = Bind.Match(z);
                    return m.Success && m.Groups[1].Value == ressource && m.Groups[4].Value == befehl
                        && m.Groups[2].Value.Equals("KEYBOARD", StringComparison.OrdinalIgnoreCase);
                });
                var std = standard.TryGetValue(paar.Key, out var sv) ? (sv as string ?? "").ToUpperInvariant() : null;
                if (vorher > 0 || taste != std) neu.Add($"rbind {ressource} KEYBOARD {taste} \"{befehl}\"");
            }
            zeilen.AddRange(neu);

            ErsteSicherung(TastenDatei);
            Dateien.SicherSchreiben(TastenDatei, new UTF8Encoding(false).GetBytes(string.Join(umbruch, zeilen) + umbruch));
            return true;
        }

        private static object ProfileLesen()
        {
            if (!File.Exists(ProfilDatei)) return new Dictionary<string, object> { ["profile"] = new object[0] };
            try { return Json.Lesen(File.ReadAllText(ProfilDatei, Encoding.UTF8)); }
            catch { return new Dictionary<string, object> { ["profile"] = new object[0] }; }
        }

        private static object ProfileSpeichern(Dictionary<string, object> d)
        {
            Directory.CreateDirectory(Installation.DatenOrdner);
            var profile = d.TryGetValue("profile", out var p) ? p : new object[0];
            Dateien.SicherSchreiben(ProfilDatei, new UTF8Encoding(false).GetBytes(Json.Schreiben(new Dictionary<string, object> { ["profile"] = profile })));
            return true;
        }

        private static object SicherungZurueck(Dictionary<string, object> d)
        {
            FiveMMussZuSein();
            var datei = Text(d, "art") == "grafik" ? GrafikDatei : Text(d, "art") == "tasten" ? TastenDatei : null;
            if (datei == null) throw new Hinweis("Unbekannte Sicherung.");
            var quelle = Path.Combine(SicherungOrdner, Path.GetFileName(datei));
            if (!File.Exists(quelle)) throw new Hinweis("Es gibt noch keine Sicherung. Der Launcher legt sie an, wenn er zum ersten Mal etwas ändert.");
            Dateien.SicherSchreiben(datei, File.ReadAllBytes(quelle));
            return true;
        }

        // ------------------------------------------------------------ Cache (nur Dateien von Klaude RP)
        // FiveM legt jede Server-Datei als server-cache-priv\cache_<SHA1> ab. Der Server nennt die SHA1 seiner Dateien,
        // gelöscht wird nur, was auf dieser Liste steht.
        private string cacheToken;
        private List<string> cacheDateien;
        private static readonly Regex CacheName = new Regex("^cache_([0-9a-f]{40})$");

        private async Task<object> CachePruefen()
        {
            Dictionary<string, object> liste;
            try { liste = Json.Lesen(await Netz.Text(Einstellungen.DatenUrl + "/cache", true)); }
            catch { throw new Hinweis("Server nicht erreichbar, der Cache kann gerade nicht geprüft werden."); }
            if (!(liste.TryGetValue("bereit", out var b) && b is bool bereit && bereit) || !(liste.TryGetValue("hashes", out var h) && h is object[] hashes))
                return new Dictionary<string, object> { ["listeBereit"] = false };

            return await Task.Run(() =>
            {
                var unsere = new HashSet<string>(hashes.OfType<string>(), StringComparer.Ordinal);
                var gefunden = new List<string>();
                long bytes = 0;
                var fivem = FiveMOrdner() ?? Path.Combine(Lokal, "FiveM");
                foreach (var unter in new[] { "server-cache-priv", "server-cache" })
                {
                    var ordner = Path.Combine(fivem, "FiveM.app", "data", unter);
                    if (!Directory.Exists(ordner)) continue;
                    foreach (var datei in Directory.EnumerateFiles(ordner, "cache_*", SearchOption.TopDirectoryOnly))
                    {
                        var m = CacheName.Match(Path.GetFileName(datei));
                        if (!m.Success || !unsere.Contains(m.Groups[1].Value)) continue;
                        gefunden.Add(datei);
                        try { bytes += new FileInfo(datei).Length; } catch { }
                    }
                }
                cacheDateien = gefunden;
                cacheToken = Guid.NewGuid().ToString("N");
                return (object)new Dictionary<string, object> { ["listeBereit"] = true, ["anzahl"] = gefunden.Count, ["bytes"] = bytes, ["token"] = cacheToken };
            });
        }

        private object CacheLoeschen(Dictionary<string, object> d)
        {
            if (cacheToken == null || Text(d, "token") != cacheToken || cacheDateien == null) throw new Hinweis("Bitte zuerst prüfen.");
            FiveMMussZuSein();
            int anzahl = 0;
            long bytes = 0;
            foreach (var datei in cacheDateien)
            {
                try
                {
                    var laenge = new FileInfo(datei).Length;
                    File.Delete(datei);
                    anzahl++;
                    bytes += laenge;
                }
                catch (FileNotFoundException) { }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            cacheToken = null;
            cacheDateien = null;
            return new Dictionary<string, object> { ["geloescht"] = anzahl, ["bytes"] = bytes };
        }

        // ------------------------------------------------------------ Update
        private async Task<object> UpdateInstallieren()
        {
            await aktualisierung.Installieren(anteil => fenster.Ereignis(new Dictionary<string, object> { ["ereignis"] = "fortschritt", ["anteil"] = anteil }));
            // Neu starten: die neue Fassung wartet, bis diese zu ist.
            Process.Start(new ProcessStartInfo(Installation.Exe, "--neustart") { UseShellExecute = false, WorkingDirectory = Installation.Basis });
            fenster.BeginInvoke(new Action(() => fenster.Close()));
            return true;
        }
    }
}
