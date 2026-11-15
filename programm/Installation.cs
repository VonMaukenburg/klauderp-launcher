using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KlaudeRP
{
    /// <summary>Einrichten im Benutzerordner (keine Admin-Rechte), Verknüpfungen, Eintrag unter „Apps“, Entfernen.</summary>
    internal static class Installation
    {
        public static readonly string Basis = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KlaudeRP");
        public static readonly string Exe = Path.Combine(Basis, "KlaudeRP.exe");
        public static readonly string AppOrdner = Path.Combine(Basis, "app");
        public static readonly string DatenOrdner = Path.Combine(Basis, "daten");
        public static readonly string WebViewOrdner = Path.Combine(Basis, "webview");
        public static readonly string Symbol = Path.Combine(Basis, "klauderp.ico");

        // WebView2-Dateien, die neben der exe liegen müssen
        public static readonly string[] Bibliotheken = { "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "WebView2Loader.dll", "klauderp.ico" };

        private const string DeinstallSchluessel = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KlaudeRP";

        public static string Version
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return $"{v.Major}.{v.Minor}.{v.Build}";
            }
        }

        public static bool LaeuftAusInstallation()
        {
            return string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(Exe), StringComparison.OrdinalIgnoreCase);
        }

        public static void Installieren()
        {
            Directory.CreateDirectory(Basis);
            Directory.CreateDirectory(DatenOrdner);
            try
            {
                File.Copy(Application.ExecutablePath, Exe, true);
            }
            catch (IOException)
            {
                // Die eingerichtete Fassung läuft gerade: einfach die nach vorne holen.
                if (File.Exists(Exe))
                {
                    VorhandenesFensterZeigen();
                    return;
                }
                throw;
            }
            BibliothekenAuspacken(true);
            OberflaecheAuspacken(true);
            VerknuepfungenAnlegen();
            AppsEintragSchreiben();
            Process.Start(new ProcessStartInfo(Exe) { UseShellExecute = false, WorkingDirectory = Basis });
        }

        /// <summary>Beim normalen Start: fehlende Teile ergänzen, Reste eines Updates aufräumen.</summary>
        public static void Pruefen()
        {
            Directory.CreateDirectory(DatenOrdner);
            BibliothekenAuspacken(false);
            if (!File.Exists(Path.Combine(AppOrdner, "index.html"))) OberflaecheAuspacken(true);
            // Rest von 1.0.2 (FiveM-Start über eine Verknüpfung, wieder entfernt)
            try { File.Delete(Path.Combine(DatenOrdner, "FiveM verbinden.lnk")); } catch { }
            foreach (var alt in Directory.GetFiles(Basis, "*.alt", SearchOption.TopDirectoryOnly))
            {
                try { File.Delete(alt); } catch { /* noch in Benutzung, beim nächsten Mal */ }
            }
            AppsEintragSchreiben();
            // Startmenü-Eintrag immer (Taskleisten-Symbol), Desktop nur auffrischen, wenn er noch da ist
            try
            {
                Verknuepfung(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Klaude RP.lnk"));
                var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Klaude RP.lnk");
                if (File.Exists(desktop)) Verknuepfung(desktop);
            }
            catch { }
        }

        private static Stream Eingebettet(string name)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream("eingebettet/" + name);
        }

        private static void BibliothekenAuspacken(bool immer)
        {
            foreach (var name in Bibliotheken)
            {
                var ziel = Path.Combine(Basis, name);
                if (!immer && File.Exists(ziel)) continue;
                using (var quelle = Eingebettet(name))
                {
                    if (quelle == null) throw new InvalidOperationException("Datei fehlt im Programm: " + name);
                    try
                    {
                        using (var aus = File.Create(ziel)) quelle.CopyTo(aus);
                    }
                    catch (IOException) when (File.Exists(ziel))
                    {
                        // Wird gerade benutzt: vorhandene Datei bleibt
                    }
                }
            }
        }

        private static void OberflaecheAuspacken(bool leeren)
        {
            using (var quelle = Eingebettet("web.zip"))
            {
                if (quelle == null) throw new InvalidOperationException("Oberfläche fehlt im Programm (web.zip).");
                if (leeren && Directory.Exists(AppOrdner)) Directory.Delete(AppOrdner, true);
                Directory.CreateDirectory(AppOrdner);
                using (var zip = new ZipArchive(quelle, ZipArchiveMode.Read))
                {
                    foreach (var eintrag in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(eintrag.Name)) continue;
                        var ziel = Path.GetFullPath(Path.Combine(AppOrdner, eintrag.FullName));
                        if (!ziel.StartsWith(Path.GetFullPath(AppOrdner) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(ziel));
                        eintrag.ExtractToFile(ziel, true);
                    }
                }
            }
        }

        // ------------------------------------------------------------ Verknüpfungen
        public static void VerknuepfungenAnlegen()
        {
            var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Klaude RP.lnk");
            var start = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Klaude RP.lnk");
            Verknuepfung(desktop);
            Verknuepfung(start);
        }

        private static void Verknuepfung(string pfad)
        {
            var link = (Shell.IShellLinkW)new Shell.CShellLink();
            link.SetPath(Exe);
            link.SetWorkingDirectory(Basis);
            link.SetDescription("Klaude RP starten");
            link.SetIconLocation(Symbol, 0);
            ((IPersistFile)link).Save(pfad, true);
            Marshal.ReleaseComObject(link);
        }

        private static void AppsEintragSchreiben()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(DeinstallSchluessel))
            {
                if (k == null) return;
                k.SetValue("DisplayName", "Klaude RP");
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "Klaude RP");
                k.SetValue("DisplayIcon", Symbol);
                k.SetValue("InstallLocation", Basis);
                k.SetValue("UninstallString", "\"" + Exe + "\" --entfernen");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        public static void Entfernen()
        {
            var antwort = MessageBox.Show("Klaude RP Launcher vom PC entfernen?\n\nDeine Grafik- und Tasteneinstellungen in FiveM bleiben, wie sie sind.",
                Program.Titel, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (antwort != DialogResult.Yes) return;
            foreach (var p in Process.GetProcessesByName("KlaudeRP").Where(p => p.Id != Process.GetCurrentProcess().Id))
            {
                try { p.CloseMainWindow(); p.WaitForExit(3000); } catch { }
            }
            foreach (var lnk in new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Klaude RP.lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Klaude RP.lnk"),
            })
            {
                try { File.Delete(lnk); } catch { }
            }
            try { Registry.CurrentUser.DeleteSubKeyTree(DeinstallSchluessel, false); } catch { }
            // Die laufende exe kann sich nicht selbst löschen: das macht ein kurzer Befehl, nachdem wir zu sind.
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{Basis}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }

        // ------------------------------------------------------------ Fenster einer laufenden Fassung
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);

        public static void VorhandenesFensterZeigen()
        {
            var ich = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName("KlaudeRP"))
            {
                if (p.Id == ich || p.MainWindowHandle == IntPtr.Zero) continue;
                if (IsIconic(p.MainWindowHandle)) ShowWindow(p.MainWindowHandle, 9);
                SetForegroundWindow(p.MainWindowHandle);
                return;
            }
        }

    }
}
