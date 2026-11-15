using System;
using System.Runtime.InteropServices;
using System.Text;

namespace KlaudeRP
{
    /// <summary>Windows-Schnittstellen für Verknüpfungen und Taskleiste (Symbol, Gruppierung, Anheften).</summary>
    internal static class Shell
    {
        public static readonly Guid AppModelFormat = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
        public const uint PidId = 5, PidRelaunchCommand = 2, PidRelaunchIcon = 3, PidRelaunchName = 4;

        [DllImport("shell32.dll")]
        private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

        /// <summary>Gibt dem Fenster die App-Kennung und sagt der Taskleiste, welches Symbol und welcher Name beim Anheften gilt.</summary>
        public static void FensterKennzeichnen(IntPtr fenster, string appId, string befehl, string symbol, string name)
        {
            var iid = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreForWindow(fenster, ref iid, out var speicher) != 0 || speicher == null) return;
            try
            {
                Setzen(speicher, PidId, appId);
                Setzen(speicher, PidRelaunchCommand, befehl);
                Setzen(speicher, PidRelaunchIcon, symbol);
                Setzen(speicher, PidRelaunchName, name);
                speicher.Commit();
            }
            finally
            {
                Marshal.ReleaseComObject(speicher);
            }
        }

        public static void Setzen(IPropertyStore speicher, uint pid, string text)
        {
            var schluessel = new PropertyKey { fmtid = AppModelFormat, pid = pid };
            var wert = new PropVariant { vt = 31, p = Marshal.StringToCoTaskMemUni(text) };
            try { speicher.SetValue(ref schluessel, ref wert); }
            finally { Marshal.FreeCoTaskMem(wert.p); }
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        public class CShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        public interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        public interface IPropertyStore
        {
            void GetCount(out uint cProps);
            void GetAt(uint iProp, out PropertyKey pkey);
            void GetValue(ref PropertyKey key, out PropVariant pv);
            void SetValue(ref PropertyKey key, ref PropVariant pv);
            void Commit();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct PropertyKey
        {
            public Guid fmtid;
            public uint pid;
        }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        public struct PropVariant
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr p;
        }
    }
}
