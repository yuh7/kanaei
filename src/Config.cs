using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Kanaei
{
    /// <summary>設定ファイル (Kanaei.ini) の読み書き。依存を増やさないための素朴な INI パーサ。</summary>
    internal class Config
    {
        // 左のキー = 英数（IME OFF） / 右のキー = 日本語（IME ON）
        public int LeftKey = 0;
        public int RightKey = 0;

        // 単独押しと判定する最大押下時間 (ms)。0 で無制限。
        public int TapTimeoutMs = 0;
        // マウス操作でタップ判定を取り消す（Alt+クリック等の誤爆防止）。
        public bool CancelOnMouse = true;
        // VK_IME_ON / VK_IME_OFF を送る（TSF 対応アプリ向け）。
        public bool UseVkImeOnOff = true;
        // WM_IME_CONTROL / IMC_SETOPENSTATUS を送る（IMM32 経路）。
        public bool UseImeControlMessage = true;
        // Alt / Win の単独押しによるメニュー・スタート起動を打ち消す。
        public bool SuppressModifierSideEffect = true;
        // そのときに挟むキー。Microsoft が未割り当てとしている 0xE8。
        // 0x07 は Windows 10 1909 以降 Game Bar の起動に予約されたので使わないこと。
        public int MaskKey = 0xE8;
        // 修飾キー以外を割り当てたときに、そのキーをアプリへ渡さない（干渉ゼロ）。
        public bool SwallowBoundKey = true;
        // タスクトレイのアイコンに現在の IME 状態を表示する。
        public bool ShowImeStateInTray = true;

        // 動作の記録をファイルに残す。効かないアプリを切り分けるとき用。
        public bool DebugLog = false;

        // 切り替えた瞬間、入力位置のそばに「あ」/「A」を一瞬だけ出す。
        public bool ShowIndicator = true;
        public int IndicatorSize = 56;
        public int IndicatorHoldMs = 280;
        public int IndicatorFadeMs = 220;
        // caret = 文字カーソルの位置 / mouse = マウスの位置 / screen = 画面中央
        public string IndicatorPosition = "caret";

        public string Path;

        public static string DefaultPath()
        {
            string exeDir = System.IO.Path.GetDirectoryName(AppInfo.ExePath);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            string local = System.IO.Path.Combine(exeDir, AppInfo.ConfigFile);
            if (File.Exists(local)) return local;

            string roamingDir = System.IO.Path.Combine(appData, AppInfo.Id);
            string roaming = System.IO.Path.Combine(roamingDir, AppInfo.ConfigFile);
            if (File.Exists(roaming)) return roaming;

            // exe と同じ場所に書けるならそこ、無理なら %APPDATA%。
            string target;
            if (CanWrite(exeDir))
            {
                target = local;
            }
            else
            {
                Directory.CreateDirectory(roamingDir);
                target = roaming;
            }

            // 旧名の設定が残っていれば引き継ぐ。新しいものから順に探す。
            foreach (string file in AppInfo.LegacyConfigFiles)
            {
                string old = System.IO.Path.Combine(exeDir, file);
                if (!File.Exists(old)) continue;
                try { File.Copy(old, target, false); } catch { }
                return target;
            }
            foreach (string rel in AppInfo.LegacyAppDataConfigs)
            {
                string old = System.IO.Path.Combine(appData, rel);
                if (!File.Exists(old)) continue;
                try { File.Copy(old, target, false); } catch { }
                return target;
            }
            return target;
        }

        private static bool CanWrite(string dir)
        {
            try
            {
                string probe = System.IO.Path.Combine(dir, ".kanaei-write-test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        public static Config Load(string path)
        {
            Config c = new Config();
            c.Path = path;
            if (!File.Exists(path)) return c;

            Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                int semi = val.IndexOf(';');
                if (semi >= 0) val = val.Substring(0, semi).Trim();
                kv[key] = val;
            }

            c.LeftKey = GetInt(kv, "LeftKey", c.LeftKey);
            c.RightKey = GetInt(kv, "RightKey", c.RightKey);
            c.TapTimeoutMs = GetInt(kv, "TapTimeoutMs", c.TapTimeoutMs);
            c.CancelOnMouse = GetBool(kv, "CancelOnMouse", c.CancelOnMouse);
            c.UseVkImeOnOff = GetBool(kv, "UseVkImeOnOff", c.UseVkImeOnOff);
            c.UseImeControlMessage = GetBool(kv, "UseImeControlMessage", c.UseImeControlMessage);
            c.SuppressModifierSideEffect = GetBool(kv, "SuppressModifierSideEffect", c.SuppressModifierSideEffect);
            c.MaskKey = GetInt(kv, "MaskKey", c.MaskKey);
            c.SwallowBoundKey = GetBool(kv, "SwallowBoundKey", c.SwallowBoundKey);
            c.ShowImeStateInTray = GetBool(kv, "ShowImeStateInTray", c.ShowImeStateInTray);
            c.DebugLog = GetBool(kv, "DebugLog", c.DebugLog);
            c.ShowIndicator = GetBool(kv, "ShowIndicator", c.ShowIndicator);
            c.IndicatorSize = GetInt(kv, "IndicatorSize", c.IndicatorSize);
            c.IndicatorHoldMs = GetInt(kv, "IndicatorHoldMs", c.IndicatorHoldMs);
            c.IndicatorFadeMs = GetInt(kv, "IndicatorFadeMs", c.IndicatorFadeMs);
            c.IndicatorPosition = GetString(kv, "IndicatorPosition", c.IndicatorPosition);
            return c;
        }

        private static string GetString(Dictionary<string, string> kv, string key, string def)
        {
            string v;
            if (!kv.TryGetValue(key, out v) || v.Length == 0) return def;
            return v.ToLowerInvariant();
        }

        private static int GetInt(Dictionary<string, string> kv, string key, int def)
        {
            string v;
            if (!kv.TryGetValue(key, out v) || v.Length == 0) return def;
            try
            {
                if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    return int.Parse(v.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return int.Parse(v, CultureInfo.InvariantCulture);
            }
            catch { return def; }
        }

        private static bool GetBool(Dictionary<string, string> kv, string key, bool def)
        {
            string v;
            if (!kv.TryGetValue(key, out v) || v.Length == 0) return def;
            v = v.ToLowerInvariant();
            if (v == "1" || v == "true" || v == "yes" || v == "on") return true;
            if (v == "0" || v == "false" || v == "no" || v == "off") return false;
            return def;
        }

        public bool IsConfigured
        {
            get { return LeftKey != 0 && RightKey != 0; }
        }

        public void Save()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; " + AppInfo.Name + " 設定ファイル");
            sb.AppendLine("; キーの割り当てはトレイアイコンのメニュー →「キー割り当てを変更」からでも変更できます。");
            sb.AppendLine();
            sb.AppendLine("[keys]");
            sb.AppendLine("; 左のキー = 英数（IME OFF） / 右のキー = 日本語（IME ON）。値は仮想キーコード。");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "LeftKey=0x{0:X2}   ; {1}", LeftKey, KeyNames.Name(LeftKey)));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "RightKey=0x{0:X2}  ; {1}", RightKey, KeyNames.Name(RightKey)));
            sb.AppendLine();
            sb.AppendLine("[behavior]");
            sb.AppendLine("; 空打ちと判定する最大押下時間 (ms)。0 で無制限。");
            sb.AppendLine("TapTimeoutMs=" + TapTimeoutMs.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("; マウス操作で空打ち判定を取り消す（Alt+クリック等の誤爆防止）。");
            sb.AppendLine("CancelOnMouse=" + B(CancelOnMouse));
            sb.AppendLine("; VK_IME_ON / VK_IME_OFF を送る（TSF 経路）。");
            sb.AppendLine("UseVkImeOnOff=" + B(UseVkImeOnOff));
            sb.AppendLine("; WM_IME_CONTROL を送る（IMM32 経路）。");
            sb.AppendLine("UseImeControlMessage=" + B(UseImeControlMessage));
            sb.AppendLine("; Alt / Win の空打ちで出るメニュー・スタートメニューを打ち消す。");
            sb.AppendLine("SuppressModifierSideEffect=" + B(SuppressModifierSideEffect));
            sb.AppendLine("; そのときに挟むキー。0x07 は Game Bar の起動に予約されているので使わないこと。");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "MaskKey=0x{0:X2}", MaskKey));
            sb.AppendLine("; 修飾キー以外を割り当てた場合、そのキーをアプリに渡さない（干渉ゼロ）。");
            sb.AppendLine("SwallowBoundKey=" + B(SwallowBoundKey));
            sb.AppendLine("; トレイアイコンに現在の IME 状態（A / あ）を表示する。");
            sb.AppendLine("ShowImeStateInTray=" + B(ShowImeStateInTray));
            sb.AppendLine("; 動作の記録をファイルに残す（効かないアプリを切り分けるとき用）。");
            sb.AppendLine("DebugLog=" + B(DebugLog));
            sb.AppendLine();
            sb.AppendLine("[indicator]");
            sb.AppendLine("; 切り替えた瞬間、入力位置のそばに「あ」/「A」を一瞬だけ出す。");
            sb.AppendLine("ShowIndicator=" + B(ShowIndicator));
            sb.AppendLine("; 表示の大きさ (px)。");
            sb.AppendLine("IndicatorSize=" + IndicatorSize.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("; はっきり見えている時間 (ms) と、そのあと消えるまでの時間 (ms)。");
            sb.AppendLine("IndicatorHoldMs=" + IndicatorHoldMs.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("IndicatorFadeMs=" + IndicatorFadeMs.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("; どこに出すか。caret = 文字カーソル / mouse = マウス / screen = 画面中央。");
            sb.AppendLine("IndicatorPosition=" + IndicatorPosition);

            File.WriteAllText(Path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string B(bool v) { return v ? "1" : "0"; }
    }
}
