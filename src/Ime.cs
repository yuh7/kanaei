using System;
using System.Runtime.InteropServices;

namespace Kanaei
{
    /// <summary>前面ウィンドウの IME を ON / OFF する。</summary>
    internal static class Ime
    {
        /// <summary>IME の開閉状態を設定する。true = 日本語入力 ON。</summary>
        public static void Set(bool on, Config cfg)
        {
            IntPtr fg = N.GetForegroundWindow();
            if (fg == IntPtr.Zero)
            {
                Log.Write("切り替え {0}: 前面ウィンドウが無いので何もしない", on ? "日本語" : "英数");
                return;
            }

            bool vkOk = false;
            bool msgOk = false;
            bool msgTried = false;

            // 経路 1: VK_IME_ON / VK_IME_OFF。TSF ベースのアプリ（UWP・Chromium 系など）に効く。
            if (cfg.UseVkImeOnOff)
                vkOk = Input.TapKey(on ? N.VK_IME_ON : N.VK_IME_OFF);

            // 経路 2: IMM32 の WM_IME_CONTROL。従来型のアプリに効く。
            // どちらも「指定した状態にする」冪等な操作なので、併用しても反転しない。
            if (cfg.UseImeControlMessage)
            {
                msgTried = true;
                IntPtr ime = N.ImmGetDefaultIMEWnd(FocusedWindow(fg));
                if (ime != IntPtr.Zero)
                {
                    IntPtr result;
                    msgOk = N.SendMessageTimeout(ime, N.WM_IME_CONTROL, (IntPtr)N.IMC_SETOPENSTATUS,
                        (IntPtr)(on ? 1 : 0), N.SMTO_ABORTIFHUNG, 300, out result) != IntPtr.Zero;
                }
            }

            if (Log.Enabled)
            {
                bool? after = Get();
                Log.Write("切り替え {0} / アプリ={1} / VK経路={2} / メッセージ経路={3} / 直後の状態={4}",
                    on ? "日本語" : "英数",
                    Log.ForegroundApp(),
                    cfg.UseVkImeOnOff ? (vkOk ? "成功" : "失敗") : "無効",
                    msgTried ? (msgOk ? "成功" : "失敗") : "無効",
                    after == null ? "取得不可" : (after.Value ? "日本語" : "英数"));
            }
        }

        /// <summary>現在の IME 状態を取得する。取得できなければ null。</summary>
        public static bool? Get()
        {
            IntPtr fg = N.GetForegroundWindow();
            if (fg == IntPtr.Zero) return null;

            IntPtr ime = N.ImmGetDefaultIMEWnd(FocusedWindow(fg));
            if (ime == IntPtr.Zero) return null;

            IntPtr result;
            IntPtr ok = N.SendMessageTimeout(ime, N.WM_IME_CONTROL, (IntPtr)N.IMC_GETOPENSTATUS,
                IntPtr.Zero, N.SMTO_ABORTIFHUNG, 120, out result);
            if (ok == IntPtr.Zero) return null;
            return result != IntPtr.Zero;
        }

        /// <summary>前面ウィンドウの中で実際にフォーカスを持つウィンドウを返す。</summary>
        private static IntPtr FocusedWindow(IntPtr fg)
        {
            uint pid;
            uint tid = N.GetWindowThreadProcessId(fg, out pid);
            if (tid == 0) return fg;

            N.GUITHREADINFO gti = new N.GUITHREADINFO();
            gti.cbSize = Marshal.SizeOf(typeof(N.GUITHREADINFO));
            if (N.GetGUIThreadInfo(tid, ref gti) && gti.hwndFocus != IntPtr.Zero)
                return gti.hwndFocus;
            return fg;
        }
    }

    /// <summary>SendInput のラッパ。自前の入力には目印を付けてフックで無視できるようにする。</summary>
    internal static class Input
    {
        private static readonly int[] Modifiers = {
            N.VK_LSHIFT, N.VK_RSHIFT, N.VK_LCONTROL, N.VK_RCONTROL,
            N.VK_LMENU, N.VK_RMENU, N.VK_LWIN, N.VK_RWIN
        };

        public static bool TapKey(int vk)
        {
            N.INPUT[] inputs = new N.INPUT[2];
            inputs[0] = Key(vk, 0, false, false);
            inputs[1] = Key(vk, 0, false, true);
            return Send(inputs);
        }

        /// <summary>
        /// 未定義キーを一瞬挟んでから修飾キーを離す。Alt / Win の空打ち副作用を打ち消す。
        /// 送信に失敗したら false。呼び出し側は元のキーアップを握り潰さずに通し、
        /// 修飾キーが押されっぱなしになるのを防ぐこと。
        /// </summary>
        public static bool DummyThenRelease(int vk, uint scanCode, bool extended, int maskKey)
        {
            if (maskKey <= 0 || maskKey > 0xFF) maskKey = N.VK_DUMMY;
            N.INPUT[] inputs = new N.INPUT[3];
            inputs[0] = Key(maskKey, 0, false, false);
            inputs[1] = Key(maskKey, 0, false, true);
            inputs[2] = Key(vk, (ushort)scanCode, extended, true);
            return Send(inputs);
        }

        /// <summary>
        /// 押されたままになっている修飾キーを解放する。
        /// 起動直後とスリープ復帰直後は誰も修飾キーを握っていないはずなので、
        /// そこで残っていれば「stuck」と見なして離す。
        /// </summary>
        public static int ReleaseStuckModifiers()
        {
            int released = 0;
            foreach (int vk in Modifiers)
            {
                if ((N.GetAsyncKeyState(vk) & 0x8000) == 0) continue;

                N.INPUT[] inputs = new N.INPUT[1];
                inputs[0] = Key(vk, 0, IsExtended(vk), true);
                if (Send(inputs))
                {
                    released++;
                    Log.Write("押されたままの修飾キーを解放: {0}", KeyNames.Name(vk));
                }
                else
                {
                    Log.Write("修飾キーの解放に失敗: {0}", KeyNames.Name(vk));
                }
            }
            return released;
        }

        private static bool IsExtended(int vk)
        {
            return vk == N.VK_RCONTROL || vk == N.VK_RMENU || vk == N.VK_LWIN || vk == N.VK_RWIN;
        }

        private static N.INPUT Key(int vk, ushort scan, bool extended, bool up)
        {
            N.INPUT i = new N.INPUT();
            i.type = N.INPUT_KEYBOARD;
            i.u.ki.wVk = (ushort)vk;
            i.u.ki.wScan = scan;
            i.u.ki.dwFlags = (up ? N.KEYEVENTF_KEYUP : 0) | (extended ? N.KEYEVENTF_EXTENDEDKEY : 0);
            i.u.ki.time = 0;
            i.u.ki.dwExtraInfo = N.InjectTag;
            return i;
        }

        private static bool Send(N.INPUT[] inputs)
        {
            uint sent = N.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(N.INPUT)));
            if (sent == inputs.Length) return true;

            Log.Write("SendInput が {0}/{1} しか通らなかった (エラー {2})",
                sent, inputs.Length, Marshal.GetLastWin32Error());
            return false;
        }
    }
}
