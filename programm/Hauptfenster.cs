using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace KlaudeRP
{
    /// <summary>Rahmenloses Fenster mit der Oberfläche (WebView2). Die Seite redet nur über Nachrichten mit dem Programm.</summary>
    internal sealed class Hauptfenster : Form
    {
        private const string Host = "launcher.klauderp.example";
        private readonly WebView2 web = new WebView2();
        private readonly Aktionen aktionen;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public Hauptfenster()
        {
            Text = Program.Titel;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(10, 10, 10);
            AutoScaleMode = AutoScaleMode.None;
            try { Icon = File.Exists(Installation.Symbol) ? new Icon(Installation.Symbol) : Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            GroesseSetzen();

            web.Dock = DockStyle.Fill;
            web.DefaultBackgroundColor = Color.FromArgb(10, 10, 10);
            Controls.Add(web);

            aktionen = new Aktionen(this);
            Load += async (s, e) => await WebStarten();
        }

        // Rahmenlos, aber mit Minimieren über die Taskleiste und Schatten
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= 0x00020000;      // WS_MINIMIZEBOX
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int rund = 2; // DWMWCP_ROUND (Windows 11: abgerundete Ecken)
            try { DwmSetWindowAttribute(Handle, 33, ref rund, sizeof(int)); } catch { }
            int dunkel = 1;
            try { DwmSetWindowAttribute(Handle, 20, ref dunkel, sizeof(int)); } catch { }
        }

        /// <summary>1280 × 760 bei 100 % Skalierung, auf großen Skalierungen so verkleinert, dass es auf den Bildschirm passt.</summary>
        private void GroesseSetzen()
        {
            float faktor = DeviceDpi / 96f;
            var bereich = Screen.FromPoint(Cursor.Position).WorkingArea;
            float b = 1280 * faktor, h = 760 * faktor;
            float passt = Math.Min(1f, Math.Min(bereich.Width * 0.94f / b, bereich.Height * 0.94f / h));
            ClientSize = new Size((int)(b * passt), (int)(h * passt));
        }

        private async Task WebStarten()
        {
            try
            {
                var umgebung = await CoreWebView2Environment.CreateAsync(null, Installation.WebViewOrdner);
                await web.EnsureCoreWebView2Async(umgebung);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                var a = MessageBox.Show("Für Klaude RP fehlt die „Microsoft Edge WebView2 Runtime“ (kostenlos von Microsoft, in Windows 11 schon dabei).\n\nJetzt die Download-Seite öffnen?",
                    Program.Titel, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (a == DialogResult.Yes) Extern("https://developer.microsoft.com/de-de/microsoft-edge/webview2/");
                Close();
                return;
            }
            catch (Exception e)
            {
                MessageBox.Show("Die Oberfläche konnte nicht starten:\n\n" + e.Message, Program.Titel, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
                return;
            }

            var cw = web.CoreWebView2;
            var s = cw.Settings;
            s.AreDefaultContextMenusEnabled = false;
            s.AreDevToolsEnabled = Einstellungen.Entwickler;
            s.AreBrowserAcceleratorKeysEnabled = Einstellungen.Entwickler;
            s.IsZoomControlEnabled = false;
            s.IsStatusBarEnabled = false;
            s.IsPinchZoomEnabled = false;
            s.IsSwipeNavigationEnabled = false;
            s.IsGeneralAutofillEnabled = false;
            s.IsPasswordAutosaveEnabled = false;

            cw.SetVirtualHostNameToFolderMapping(Host, Installation.AppOrdner, CoreWebView2HostResourceAccessKind.DenyCors);
            cw.NavigationStarting += (o, a) =>
            {
                if (!a.Uri.StartsWith("https://" + Host + "/", StringComparison.OrdinalIgnoreCase))
                {
                    a.Cancel = true;
                    Extern(a.Uri);
                }
            };
            cw.NewWindowRequested += (o, a) =>
            {
                a.Handled = true;
                Extern(a.Uri);
            };
            cw.WebMessageReceived += async (o, a) => await NachrichtVerarbeiten(a.WebMessageAsJson);
            cw.Navigate("https://" + Host + "/index.html");
        }

        /// <summary>Nur https-Adressen gehen nach außen, und nur in den normalen Browser.</summary>
        public static void Extern(string adresse)
        {
            if (Uri.TryCreate(adresse, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps)
            {
                try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); } catch { }
            }
        }

        private async Task NachrichtVerarbeiten(string rohJson)
        {
            string id = null;
            try
            {
                var n = Json.Lesen(rohJson);
                id = n.TryGetValue("id", out var i) ? i as string : null;
                var aktion = n.TryGetValue("aktion", out var a) ? a as string : "";
                var daten = n.TryGetValue("daten", out var d) ? d as Dictionary<string, object> : null;
                var ergebnis = await aktionen.Ausfuehren(aktion, daten ?? new Dictionary<string, object>());
                Senden(new Dictionary<string, object> { ["id"] = id, ["ergebnis"] = ergebnis });
            }
            catch (Hinweis h)
            {
                Senden(new Dictionary<string, object> { ["id"] = id, ["fehler"] = h.Message });
            }
            catch (Exception e)
            {
                Senden(new Dictionary<string, object> { ["id"] = id, ["fehler"] = "Unerwarteter Fehler: " + e.Message });
            }
        }

        private void Senden(object nachricht)
        {
            if (IsDisposed || web.CoreWebView2 == null) return;
            var text = json.Serialize(nachricht);
            if (InvokeRequired) BeginInvoke(new Action(() => web.CoreWebView2?.PostWebMessageAsJson(text)));
            else web.CoreWebView2.PostWebMessageAsJson(text);
        }

        public void Ereignis(Dictionary<string, object> daten)
        {
            Senden(daten);
        }

        // ------------------------------------------------------------ Fenster-Aktionen der Seite
        public void Ziehen()
        {
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // WM_NCLBUTTONDOWN, HTCAPTION
        }

        public void Minimieren() => WindowState = FormWindowState.Minimized;
    }
}
