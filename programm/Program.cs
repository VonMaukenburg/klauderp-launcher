using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace KlaudeRP
{
    internal static class Program
    {
        // Keine eigene App-Kennung (AppUserModelID): mit ihr zeigte die Taskleiste ein leeres Symbol (getestet 15.11.2026).
        // Ohne sie nimmt Windows Symbol und Gruppe direkt aus der exe und dem Fenster.
        public const string AppId = "KlaudeRP.Launcher";
        public const string Titel = "Klaude RP";

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                if (Array.IndexOf(args, "--entfernen") >= 0)
                {
                    Installation.Entfernen();
                    return 0;
                }
                if (!Installation.LaeuftAusInstallation())
                {
                    // Erster Start (z. B. aus „Downloads“): einrichten, die eingerichtete Fassung starten, fertig.
                    Installation.Installieren();
                    return 0;
                }
                Installation.Pruefen();
            }
            catch (Exception e)
            {
                MessageBox.Show("Klaude RP konnte nicht eingerichtet werden:\n\n" + e.Message, Titel, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            using (var sperre = new Mutex(true, "Local\\KlaudeRP.Launcher", out bool erster))
            {
                if (!erster)
                {
                    // Nach einem Update wartet die neue Fassung kurz, bis die alte zu ist.
                    bool frei = false;
                    if (Array.IndexOf(args, "--neustart") >= 0)
                    {
                        try { frei = sperre.WaitOne(15000); }
                        catch (AbandonedMutexException) { frei = true; }
                    }
                    if (!frei)
                    {
                        Installation.VorhandenesFensterZeigen();
                        return 0;
                    }
                }
                Starten();
                try { sperre.ReleaseMutex(); } catch (ApplicationException) { }
            }
            return 0;
        }

        // Eigene Methode, damit WebView2 erst geladen wird, wenn die Dateien sicher da sind.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Starten()
        {
            Application.Run(new Hauptfenster());
        }
    }
}
