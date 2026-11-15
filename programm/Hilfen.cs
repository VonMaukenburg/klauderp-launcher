using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace KlaudeRP
{
    /// <summary>Fehler mit einer Meldung, die der Spieler so lesen soll.</summary>
    internal sealed class Hinweis : Exception
    {
        public Hinweis(string text) : base(text) { }
    }

    /// <summary>Feste Adressen. Entwickler können sie mit daten\entwickler.json überschreiben (z. B. lokaler Testserver).</summary>
    internal static class Einstellungen
    {
        public const string StandardDatenUrl = "http://159.195.131.197:30120/rp-launcher";
        public const string StandardServer = "159.195.131.197:30120";
        public const string StandardTeamspeak = "159.195.131.197";
        public const int StandardTeamspeakPort = 9987;

        public static readonly string DatenUrl;
        public static readonly bool Entwickler;

        static Einstellungen()
        {
            DatenUrl = StandardDatenUrl;
            var datei = Path.Combine(Installation.DatenOrdner, "entwickler.json");
            if (!File.Exists(datei)) return;
            try
            {
                var d = Json.Lesen(File.ReadAllText(datei, Encoding.UTF8));
                if (d.TryGetValue("datenUrl", out var u) && u is string url && url.StartsWith("http")) DatenUrl = url.TrimEnd('/');
                Entwickler = d.TryGetValue("werkzeuge", out var w) && w is bool b && b;
            }
            catch { }
        }
    }

    internal static class Json
    {
        private static readonly JavaScriptSerializer S = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        // DeserializeObject liefert Listen als object[] (Deserialize<…> würde ArrayList liefern)
        public static Dictionary<string, object> Lesen(string text) => S.DeserializeObject(text) as Dictionary<string, object> ?? new Dictionary<string, object>();
        public static string Schreiben(object o) => S.Serialize(o);
    }

    internal static class Netz
    {
        private static readonly HttpClient Kurz = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        private static readonly HttpClient Lang = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        public static async Task<string> Text(string adresse, bool lang = false)
        {
            using (var antwort = await (lang ? Lang : Kurz).GetAsync(adresse).ConfigureAwait(false))
            {
                antwort.EnsureSuccessStatusCode();
                return await antwort.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        /// <summary>Datei aus dem Update-Ordner des Servers (kommt als Base64 an).</summary>
        public static async Task<byte[]> Datei(string pfad)
        {
            var text = await Text(Einstellungen.DatenUrl + "/datei/" + pfad, true).ConfigureAwait(false);
            return Convert.FromBase64String(text.Trim());
        }
    }

    internal static class Pruefsumme
    {
        public static string Sha256(byte[] daten)
        {
            using (var h = SHA256.Create()) return Hex(h.ComputeHash(daten));
        }

        public static string Sha256Datei(string pfad)
        {
            using (var h = SHA256.Create())
            using (var f = File.OpenRead(pfad)) return Hex(h.ComputeHash(f));
        }

        private static string Hex(byte[] b)
        {
            var s = new StringBuilder(b.Length * 2);
            foreach (var x in b) s.Append(x.ToString("x2"));
            return s.ToString();
        }
    }

    internal static class Dateien
    {
        /// <summary>Erst in eine Nebendatei schreiben, dann tauschen: so ist die Datei nie halb geschrieben.</summary>
        public static void SicherSchreiben(string pfad, byte[] inhalt)
        {
            var neu = pfad + ".klauderp-neu";
            File.WriteAllBytes(neu, inhalt);
            if (File.Exists(pfad)) File.Replace(neu, pfad, null);
            else File.Move(neu, pfad);
        }
    }
}
