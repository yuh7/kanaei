using System;
using System.Collections.Generic;
using System.Globalization;

namespace Kanaei
{
    /// <summary>仮想キーコードの表示名と、空打ちしたときの干渉に関する注意書き。</summary>
    internal static class KeyNames
    {
        private static readonly Dictionary<int, string> Map = Build();

        private static Dictionary<int, string> Build()
        {
            Dictionary<int, string> m = new Dictionary<int, string>();
            m[0x08] = "BackSpace"; m[0x09] = "Tab"; m[0x0D] = "Enter"; m[0x13] = "Pause";
            m[0x14] = "CapsLock"; m[0x15] = "かな / IME かなモード"; m[0x17] = "IME Junja";
            m[0x19] = "漢字 / IME 漢字モード"; m[0x1B] = "Esc";
            m[0x1C] = "変換"; m[0x1D] = "無変換"; m[0x1E] = "Accept"; m[0x1F] = "ModeChange";
            m[0x20] = "Space"; m[0x21] = "PageUp"; m[0x22] = "PageDown"; m[0x23] = "End"; m[0x24] = "Home";
            m[0x25] = "←"; m[0x26] = "↑"; m[0x27] = "→"; m[0x28] = "↓";
            m[0x2C] = "PrintScreen"; m[0x2D] = "Insert"; m[0x2E] = "Delete";
            m[0x5B] = "左 Win"; m[0x5C] = "右 Win"; m[0x5D] = "アプリケーションキー";
            m[0x90] = "NumLock"; m[0x91] = "ScrollLock";
            m[0xA0] = "左 Shift"; m[0xA1] = "右 Shift";
            m[0xA2] = "左 Ctrl"; m[0xA3] = "右 Ctrl";
            m[0xA4] = "左 Alt"; m[0xA5] = "右 Alt";
            m[0xBA] = ":"; m[0xBB] = ";"; m[0xBC] = ","; m[0xBD] = "-"; m[0xBE] = "."; m[0xBF] = "/";
            m[0xC0] = "@"; m[0xDB] = "["; m[0xDC] = "\\"; m[0xDD] = "]"; m[0xDE] = "^"; m[0xE2] = "_ (ろ)";
            m[0xF0] = "英数 (OEM_ATTN)"; m[0xF2] = "ひらがな (OEM_COPY)";
            m[0xF3] = "半角/全角 (OEM_AUTO)"; m[0xF4] = "半角/全角 (OEM_ENLW)"; m[0xF5] = "OEM_BACKTAB";

            for (int i = 0; i < 10; i++) m[0x30 + i] = ((char)('0' + i)).ToString();
            for (int i = 0; i < 26; i++) m[0x41 + i] = ((char)('A' + i)).ToString();
            for (int i = 0; i < 10; i++) m[0x60 + i] = "テンキー " + i.ToString(CultureInfo.InvariantCulture);
            for (int i = 1; i <= 24; i++) m[0x70 + i - 1] = "F" + i.ToString(CultureInfo.InvariantCulture);
            return m;
        }

        public static string Name(int vk)
        {
            if (vk == 0) return "(未設定)";
            string s;
            if (Map.TryGetValue(vk, out s)) return s;
            return string.Format(CultureInfo.InvariantCulture, "VK 0x{0:X2}", vk);
        }

        public static string Describe(int vk)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}  (0x{1:X2})", Name(vk), vk);
        }

        /// <summary>Alt / Ctrl / Shift / Win のように、他キーと組み合わせて使う修飾キーか。</summary>
        public static bool IsModifier(int vk)
        {
            switch (vk)
            {
                case N.VK_LSHIFT:
                case N.VK_RSHIFT:
                case N.VK_LCONTROL:
                case N.VK_RCONTROL:
                case N.VK_LMENU:
                case N.VK_RMENU:
                case N.VK_LWIN:
                case N.VK_RWIN:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>キー単独押しで OS 側の副作用（メニュー起動など）が出るキーか。</summary>
        public static bool NeedsDummyKey(int vk)
        {
            return vk == N.VK_LMENU || vk == N.VK_RMENU || vk == N.VK_LWIN || vk == N.VK_RWIN;
        }

        /// <summary>空打ちしたときの干渉と、kanaei 側の対処を説明する。無ければ空文字。</summary>
        public static string InterferenceNote(int vk)
        {
            switch (vk)
            {
                case 0x1C: // 変換
                    return "空打ちの干渉: ほぼ無し。MS-IME で「再変換」が動く場合がありますが、kanaei がキーを握り潰すのでアプリには届きません。\n"
                         + "注意: 変換中の再変換・カタカナ変換としては使えなくなります。";
                case 0x1D: // 無変換
                    return "空打ちの干渉: ほぼ無し。最も安全な選択です。\n"
                         + "注意: 変換中のひらがな／カタカナ切り替えとしては使えなくなります。";
                case N.VK_LMENU:
                case N.VK_RMENU:
                    return "空打ちの干渉: 本来 Alt 単独押しでメニューバーが起動します（エクスプローラー・Office・ブラウザなど）。\n"
                         + "対処: kanaei が Alt を離す直前に未定義キーを挟み込み、メニュー起動を打ち消します。Alt+Tab などの修飾キー用途はそのまま使えます。";
                case N.VK_LWIN:
                case N.VK_RWIN:
                    return "空打ちの干渉: 本来 Win 単独押しでスタートメニューが開きます。\n"
                         + "対処: Alt と同じく未定義キーを挟んで打ち消します。Win+R などの組み合わせはそのまま使えます。";
                case N.VK_LCONTROL:
                    return "空打ちの干渉: OS 側の副作用はありません。\n"
                         + "注意: 左 Ctrl は Ctrl+C などで多用するため、他キーを押す直前に離してしまうと誤って切り替わることがあります。右 Ctrl の方が安全です。";
                case N.VK_RCONTROL:
                    return "空打ちの干渉: OS 側の副作用はありません。使用頻度も低く、安全な選択です。";
                case N.VK_LSHIFT:
                case N.VK_RSHIFT:
                    return "空打ちの干渉: Shift を 5 回連打すると「固定キー機能」のダイアログが出ます（Windows の設定で無効化できます）。\n"
                         + "注意: 大文字入力のたびに空打ち判定が走るため、誤爆しやすい選択です。";
                case 0x14: // CapsLock
                    return "空打ちの干渉: 本来 CapsLock が切り替わります。\n"
                         + "対処: kanaei がキーを握り潰すため切り替わりません。ただし CapsLock 本来の機能は使えなくなります。";
                case 0xF3:
                case 0xF4:
                case 0x19:
                    return "注意: 半角/全角キーは「トグル」動作のキーです。左右で ON/OFF を固定する用途には向きません（IME 側が勝手に反転する場合があります）。";
                case 0x20:
                    return "警告: Space を割り当てると空白が入力できなくなります。強く非推奨です。";
                case 0x0D:
                case 0x09:
                case 0x1B:
                case 0x08:
                    return "警告: このキーは入力に必須です。割り当てると通常の操作ができなくなります。強く非推奨です。";
                default:
                    if (vk >= 0x70 && vk <= 0x87)
                        return "空打ちの干渉: ほぼ無し。特に F13〜F24 は通常どのアプリも使わないため安全です。";
                    if ((vk >= 0x41 && vk <= 0x5A) || (vk >= 0x30 && vk <= 0x39))
                        return "警告: 通常の文字キーです。割り当てるとその文字が入力できなくなります。強く非推奨です。";
                    return "このキーの空打ち干渉は特に確認されていません。kanaei はキーを握り潰すため、アプリには届きません。";
            }
        }

        /// <summary>強く非推奨なキーなら true。</summary>
        public static bool IsDangerous(int vk)
        {
            if (vk == 0x20 || vk == 0x0D || vk == 0x09 || vk == 0x1B || vk == 0x08) return true;
            if (vk >= 0x41 && vk <= 0x5A) return true;
            if (vk >= 0x30 && vk <= 0x39) return true;
            return false;
        }
    }
}
