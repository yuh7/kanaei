using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Kanaei
{
    /// <summary>
    /// 低レベルキーボード／マウスフック。割り当てたキーの「空打ち」だけを拾う。
    /// 空打ち = そのキーを押してから離すまでの間、他のキーもマウスも操作されなかったこと。
    /// </summary>
    internal class KeyHook : IDisposable
    {
        private readonly Config cfg;
        private readonly Control marshal;      // フックを抜けてから処理を走らせるための受け皿
        private readonly N.HookProc keyProc;   // GC で回収されないよう参照を保持する
        private readonly N.HookProc mouseProc;

        private IntPtr keyHook = IntPtr.Zero;
        private IntPtr mouseHook = IntPtr.Zero;

        private int pendingVk;        // 押しっぱなしで空打ち候補になっているキー
        private bool pendingAlive;    // まだ空打ちとして成立しうるか
        private int pendingTick;

        /// <summary>空打ちを検出した。引数は「日本語入力にするか」。</summary>
        public event Action<bool> Tapped;

        /// <summary>設定していれば、押されたキーを横取りしてこのコールバックへ渡す（学習・確認モード）。</summary>
        public Func<int, uint, bool> Capture;

        public bool Paused { get; set; }

        /// <summary>
        /// 進行中の空打ち判定を捨てる。押されたままの修飾キーを外から解放したあとなど、
        /// 実際のキーの状態と食い違ったときに呼ぶ。
        /// </summary>
        public void ResetPending()
        {
            pendingVk = 0;
            pendingAlive = false;
        }

        public KeyHook(Config cfg, Control marshal)
        {
            this.cfg = cfg;
            this.marshal = marshal;
            this.keyProc = KeyCallback;
            this.mouseProc = MouseCallback;
        }

        public void Install()
        {
            IntPtr mod = N.GetModuleHandle(null);
            if (keyHook == IntPtr.Zero)
                keyHook = N.SetWindowsHookEx(N.WH_KEYBOARD_LL, keyProc, mod, 0);
            if (mouseHook == IntPtr.Zero && cfg.CancelOnMouse)
                mouseHook = N.SetWindowsHookEx(N.WH_MOUSE_LL, mouseProc, mod, 0);

            if (keyHook == IntPtr.Zero)
                throw new InvalidOperationException("キーボードフックを設定できませんでした。エラー " +
                    Marshal.GetLastWin32Error().ToString());
        }

        public void Dispose()
        {
            if (keyHook != IntPtr.Zero) { N.UnhookWindowsHookEx(keyHook); keyHook = IntPtr.Zero; }
            if (mouseHook != IntPtr.Zero) { N.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
        }

        private IntPtr Next(int nCode, IntPtr wParam, IntPtr lParam)
        {
            return N.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private static readonly IntPtr Swallow = new IntPtr(1);

        private IntPtr KeyCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0) return Next(nCode, wParam, lParam);

            N.KBDLLHOOKSTRUCT d = (N.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(N.KBDLLHOOKSTRUCT));

            // 自分で送った入力は無視する。
            if (d.dwExtraInfo == N.InjectTag) return Next(nCode, wParam, lParam);

            int msg = wParam.ToInt32();
            bool down = (msg == N.WM_KEYDOWN || msg == N.WM_SYSKEYDOWN);
            bool up = (msg == N.WM_KEYUP || msg == N.WM_SYSKEYUP);
            int vk = (int)d.vkCode;

            // 学習・確認モード中は、押されたキーを全部横取りする。
            Func<int, uint, bool> capture = Capture;
            if (capture != null)
            {
                if (down)
                {
                    int capturedVk = vk;
                    uint capturedScan = d.scanCode;
                    marshal.BeginInvoke((Action)delegate { RunCapture(capturedVk, capturedScan); });
                }
                return Swallow;
            }

            if (Paused || !cfg.IsConfigured) return Next(nCode, wParam, lParam);

            bool isRight;
            if (vk == cfg.RightKey) isRight = true;
            else if (vk == cfg.LeftKey) isRight = false;
            else
            {
                // 割り当て外のキーが押されたら、進行中の空打ち候補は取り消し（＝組み合わせ操作）。
                if (down) pendingAlive = false;
                return Next(nCode, wParam, lParam);
            }

            bool isModifier = KeyNames.IsModifier(vk);
            bool swallowKey = !isModifier && cfg.SwallowBoundKey;

            if (down)
            {
                if (pendingVk == vk && pendingAlive)
                {
                    // オートリピート。押しっぱなしなので状態はそのまま。
                }
                else if (pendingVk != 0 && pendingVk != vk)
                {
                    // 左右のキーを同時に押した場合はどちらも成立させない。
                    pendingAlive = false;
                }
                else
                {
                    pendingVk = vk;
                    pendingAlive = true;
                    pendingTick = Environment.TickCount;
                }
                return swallowKey ? Swallow : Next(nCode, wParam, lParam);
            }

            if (up)
            {
                bool fire = pendingAlive && pendingVk == vk;
                if (fire && cfg.TapTimeoutMs > 0 &&
                    unchecked(Environment.TickCount - pendingTick) > cfg.TapTimeoutMs)
                    fire = false;

                pendingVk = 0;
                pendingAlive = false;

                if (!fire) return swallowKey ? Swallow : Next(nCode, wParam, lParam);

                bool toJapanese = isRight;

                // Alt / Win は「離した瞬間」にメニューやスタートが開く。元のキーアップを止め、
                // 未定義キーを挟んでから自前でキーアップを送ることで副作用だけを打ち消す。
                if (isModifier && cfg.SuppressModifierSideEffect && KeyNames.NeedsDummyKey(vk))
                {
                    bool sent = Input.DummyThenRelease(vk, d.scanCode,
                        (d.flags & N.LLKHF_EXTENDED) != 0, cfg.MaskKey);
                    Fire(toJapanese);

                    // 自前のキーアップを送れなかったときに元のキーアップまで握り潰すと、
                    // 修飾キーが押されっぱなしになる。そのときは素直に通す。
                    if (!sent)
                    {
                        Log.Write("キーアップを送れなかったので元のキーアップを通した: {0}", KeyNames.Name(vk));
                        return Next(nCode, wParam, lParam);
                    }
                    return Swallow;
                }

                Fire(toJapanese);
                return swallowKey ? Swallow : Next(nCode, wParam, lParam);
            }

            return Next(nCode, wParam, lParam);
        }

        /// <summary>IME の切り替えはフックを抜けてから行う（フック内で長居するとフックが外される）。</summary>
        private void Fire(bool toJapanese)
        {
            Action<bool> h = Tapped;
            if (h == null) return;
            marshal.BeginInvoke((Action)delegate { h(toJapanese); });
        }

        private void RunCapture(int vk, uint scan)
        {
            Func<int, uint, bool> capture = Capture;
            if (capture != null) capture(vk, scan);
        }

        private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == N.WM_LBUTTONDOWN || msg == N.WM_RBUTTONDOWN || msg == N.WM_MBUTTONDOWN ||
                    msg == N.WM_XBUTTONDOWN || msg == N.WM_MOUSEWHEEL || msg == N.WM_MOUSEHWHEEL)
                {
                    // Alt+クリック・Ctrl+ホイールなどを空打ちと誤認しない。
                    pendingAlive = false;
                }
            }
            return Next(nCode, wParam, lParam);
        }
    }
}
