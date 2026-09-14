using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Kanaei
{
    /// <summary>
    /// 「このアプリだけ切り替わらない」を自分で切り分けるための記録。
    /// 既定では止まっていて、メニューから入れたときだけ書く。
    /// </summary>
    internal static class Log
    {
        private const long MaxBytes = 1024 * 1024;   // 1MB を超えたら 1 世代だけ残して切り替える

        private static readonly object gate = new object();
        private static string file;

        public static bool Enabled;

        public static string FilePath
        {
            get { return file; }
        }

        public static void Init(string configPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(configPath);
                file = Path.Combine(dir, AppInfo.Id + ".log");
            }
            catch { file = null; }
        }

        public static void Write(string text)
        {
            if (!Enabled || file == null) return;
            try
            {
                lock (gate)
                {
                    Rotate();
                    File.AppendAllText(file,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                        + "  " + text + Environment.NewLine,
                        new UTF8Encoding(true));
                }
            }
            catch { }
        }

        public static void Write(string format, params object[] args)
        {
            if (!Enabled) return;
            try { Write(string.Format(CultureInfo.InvariantCulture, format, args)); }
            catch { }
        }

        private static void Rotate()
        {
            try
            {
                FileInfo fi = new FileInfo(file);
                if (!fi.Exists || fi.Length < MaxBytes) return;
                string old = file + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(file, old);
            }
            catch { }
        }

        /// <summary>前面ウィンドウのプロセス名。「どのアプリで効かないか」を残すために使う。</summary>
        public static string ForegroundApp()
        {
            try
            {
                IntPtr fg = N.GetForegroundWindow();
                if (fg == IntPtr.Zero) return "(なし)";
                uint pid;
                N.GetWindowThreadProcessId(fg, out pid);
                return System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
            }
            catch { return "(不明)"; }
        }
    }
}
