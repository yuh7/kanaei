using System;
using System.Drawing;
using System.Reflection;

namespace Kanaei
{
    /// <summary>アプリの名前まわりを 1 箇所にまとめる。名前を変えたくなったらここだけ直す。</summary>
    internal static class AppInfo
    {
        /// <summary>画面に出す名前。</summary>
        public const string Name = "かなえい";

        /// <summary>ファイル名・レジストリ値など、英数字でないと困るところで使う名前。</summary>
        public const string Id = "Kanaei";

        public const string Tagline = "空打ちで英数・日本語を切り替える";

        public const string ConfigFile = Id + ".ini";

        // 旧名。見つかったら引き継ぐ。
        // Windows のファイル名・レジストリ値は大文字小文字を区別しないので、
        // 現在の名前と同じものを並べないこと（自分自身を複製しようとしてしまう）。
        public static readonly string[] LegacyConfigFiles = { "Tefutefu.ini", "Kanae.ini" };
        public static readonly string[] LegacyAppDataConfigs = { "Tefutefu\\Tefutefu.ini", "Kanae\\Kanae.ini" };
        public static readonly string[] LegacyRunValues = { "Tefutefu", "Kanae" };

        public const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        public const string RunValue = Id;

        public const string MutexName = "Local\\Kanaei-single-instance";

        // Windows 11 が通知領域のアイコンを表示するかどうかを持っている場所。
        public const string NotifyIconSettings = "Control Panel\\NotifyIconSettings";

        public static string ExePath
        {
            get { return Assembly.GetExecutingAssembly().Location; }
        }

        /// <summary>exe に埋め込んだアイコン。ウィンドウのタイトルバーなどに使う。</summary>
        public static Icon Window()
        {
            try
            {
                Icon ico = Icon.ExtractAssociatedIcon(ExePath);
                if (ico != null) return ico;
            }
            catch { }
            using (Bitmap bmp = Glyphs.Render(32, Mark.App))
                return Glyphs.ToIcon(bmp, delegate(IntPtr h) { N.DestroyIcon(h); });
        }
    }
}
