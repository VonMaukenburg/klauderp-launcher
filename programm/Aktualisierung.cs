using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace KlaudeRP
{
    /// <summary>
    /// Updates vom Server: manifest.json (Version + jede Datei mit SHA-256) und manifest.sig (RSA-Signatur).
    /// Nur wenn die Signatur zu unserem Schlüssel passt (Schluessel.cs), wird etwas installiert. Geladen werden nur
    /// Dateien, deren SHA-256 sich vom PC unterscheidet. Die Dateien liegen auf dem Server unter objekte/&lt;sha256&gt;.
    /// </summary>
    internal sealed class Aktualisierung
    {
        private sealed class Eintrag
        {
            public string Pfad;
            public string Sha256;
            public long Groesse;
        }

        private static readonly Regex PfadMuster = new Regex(@"^[A-Za-z0-9_.\-]+(/[A-Za-z0-9_.\-]+)*$");
        private static readonly Regex ShaMuster = new Regex("^[0-9a-f]{64}$");

        private string neueVersion;
        private List<Eintrag> alle;
        private List<Eintrag> fehlend;

        public async Task<Dictionary<string, object>> Pruefen()
        {
            var antwort = new Dictionary<string, object> { ["version"] = Installation.Version, ["neu"] = false, ["automatisch"] = true };
            byte[] manifest, signatur;
            try
            {
                manifest = await Netz.Datei("manifest.json");
                signatur = await Netz.Datei("manifest.sig");
            }
            catch
            {
                antwort["fehler"] = "Update-Server nicht erreichbar";
                return antwort;
            }
            if (!SignaturPasst(manifest, signatur))
            {
                antwort["fehler"] = "Update hat keine gültige Unterschrift, wird nicht installiert";
                return antwort;
            }

            var d = Json.Lesen(Encoding.UTF8.GetString(manifest));
            neueVersion = d.TryGetValue("version", out var v) ? v as string : "?";
            alle = new List<Eintrag>();
            if (d.TryGetValue("dateien", out var liste) && liste is object[] dateien)
            {
                foreach (var o in dateien.OfType<Dictionary<string, object>>())
                {
                    var e = new Eintrag
                    {
                        Pfad = o.TryGetValue("pfad", out var p) ? p as string : null,
                        Sha256 = o.TryGetValue("sha256", out var s) ? s as string : null,
                        Groesse = o.TryGetValue("groesse", out var g) ? Convert.ToInt64(g) : 0,
                    };
                    if (e.Pfad == null || e.Sha256 == null || !PfadMuster.IsMatch(e.Pfad) || e.Pfad.Contains("..") || !ShaMuster.IsMatch(e.Sha256)) continue;
                    alle.Add(e);
                }
            }

            fehlend = await Task.Run(() => alle.Where(e =>
            {
                var ziel = Ziel(e.Pfad);
                try { return !File.Exists(ziel) || Pruefsumme.Sha256Datei(ziel) != e.Sha256; }
                catch { return true; }
            }).ToList());

            antwort["neu"] = fehlend.Count > 0;
            antwort["neueVersion"] = neueVersion;
            antwort["bytes"] = fehlend.Sum(e => e.Groesse);
            antwort["dateien"] = fehlend.Count;
            return antwort;
        }

        private static string Ziel(string pfad) => Path.Combine(Installation.Basis, pfad.Replace('/', Path.DirectorySeparatorChar));

        private static bool SignaturPasst(byte[] daten, byte[] signatur)
        {
            try
            {
                using (var rsa = new RSACng())
                {
                    rsa.ImportParameters(new RSAParameters
                    {
                        Modulus = Convert.FromBase64String(Schluessel.Modulus),
                        Exponent = Convert.FromBase64String(Schluessel.Exponent),
                    });
                    return rsa.VerifyData(daten, signatur, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch
            {
                return false;
            }
        }

        public async Task Installieren(Action<double> fortschritt)
        {
            if (fehlend == null) await Pruefen();
            if (fehlend == null || fehlend.Count == 0) throw new Hinweis("Kein Update vorhanden.");

            // 1) Alles laden und prüfen, erst dann etwas ersetzen
            var zwischen = Path.Combine(Installation.Basis, "update");
            if (Directory.Exists(zwischen)) Directory.Delete(zwischen, true);
            Directory.CreateDirectory(zwischen);
            long gesamt = Math.Max(1, fehlend.Sum(e => e.Groesse)), geladen = 0;
            foreach (var e in fehlend)
            {
                var daten = await Netz.Datei("objekte/" + e.Sha256);
                if (Pruefsumme.Sha256(daten) != e.Sha256) throw new Hinweis("Eine Update-Datei kam beschädigt an. Bitte später noch einmal.");
                File.WriteAllBytes(Path.Combine(zwischen, e.Sha256), daten);
                geladen += e.Groesse;
                fortschritt(Math.Min(1.0, (double)geladen / gesamt));
            }

            // 2) Ersetzen. Laufende Dateien (exe, WebView2) lassen sich umbenennen, aber nicht überschreiben.
            await Task.Run(() =>
            {
                foreach (var e in fehlend)
                {
                    var ziel = Ziel(e.Pfad);
                    Directory.CreateDirectory(Path.GetDirectoryName(ziel));
                    if (File.Exists(ziel))
                    {
                        try { File.Delete(ziel); }
                        catch (Exception)
                        {
                            var alt = ziel + ".alt";
                            if (Path.GetDirectoryName(ziel) != Installation.Basis.TrimEnd('\\')) alt = Path.Combine(Installation.Basis, Path.GetFileName(ziel) + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + ".alt");
                            if (File.Exists(alt)) File.Delete(alt);
                            File.Move(ziel, alt);
                        }
                    }
                    File.Move(Path.Combine(zwischen, e.Sha256), ziel);
                }

                // Dateien der Oberfläche, die es nicht mehr gibt, entfernen
                var behalten = new HashSet<string>(alle.Select(x => Path.GetFullPath(Ziel(x.Pfad))), StringComparer.OrdinalIgnoreCase);
                if (Directory.Exists(Installation.AppOrdner))
                {
                    foreach (var datei in Directory.GetFiles(Installation.AppOrdner, "*", SearchOption.AllDirectories))
                    {
                        if (!behalten.Contains(Path.GetFullPath(datei))) { try { File.Delete(datei); } catch { } }
                    }
                }
                try { Directory.Delete(zwischen, true); } catch { }
            });
            fehlend = null;
        }
    }
}
