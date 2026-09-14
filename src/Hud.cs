using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Kanaei
{
    /// <summary>
    /// 切り替えた瞬間だけ、入力位置のそばに「あ」/「A」を一瞬出して消す表示。
    /// 新しい Microsoft IME では「入力モード切替の通知」が廃止されたので、その穴埋め。
    ///
    /// クリックは素通しし、フォーカスも奪わない。UpdateLayeredWindow で
    /// ピクセル単位の半透明を使うので、角も文字も綺麗に抜ける。
    /// </summary>
    internal class Hud : Form
    {
        private readonly Timer timer = new Timer();
        private int elapsed;
        private int holdMs;
        private int fadeMs;

        public Hud()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Visible = false;

            timer.Interval = 16;
            timer.Tick += delegate { Step(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= N.WS_EX_LAYERED | N.WS_EX_TRANSPARENT | N.WS_EX_TOOLWINDOW
                            | N.WS_EX_NOACTIVATE | N.WS_EX_TOPMOST;
                return cp;
            }
        }

        /// <summary>表示してもアクティブにならないようにする。</summary>
        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        public void Flash(Mark mark, Config cfg)
        {
            int size = Math.Max(16, cfg.IndicatorSize);
            holdMs = Math.Max(0, cfg.IndicatorHoldMs);
            fadeMs = Math.Max(1, cfg.IndicatorFadeMs);

            Point at = Place(AnchorPoint(cfg.IndicatorPosition), size);

            timer.Stop();
            elapsed = 0;
            if (!Visible) Show();
            Draw(mark, at, size, 255);
            timer.Start();
        }

        private void Step()
        {
            elapsed += timer.Interval;
            if (elapsed <= holdMs) return;

            int left = elapsed - holdMs;
            if (left >= fadeMs)
            {
                timer.Stop();
                Hide();
                return;
            }
            SetAlpha((byte)(255 - (255 * left / fadeMs)));
        }

        /// <summary>アイコンと同じ絵を描いてウィンドウに流し込む。</summary>
        private void Draw(Mark mark, Point at, int size, byte alpha)
        {
            IntPtr screenDc = N.GetDC(IntPtr.Zero);
            IntPtr memDc = N.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                N.BITMAPINFO bi = new N.BITMAPINFO();
                bi.bmiHeader.biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(
                    typeof(N.BITMAPINFOHEADER));
                bi.bmiHeader.biWidth = size;
                bi.bmiHeader.biHeight = -size;   // 上から下へ
                bi.bmiHeader.biPlanes = 1;
                bi.bmiHeader.biBitCount = 32;
                bi.bmiHeader.biCompression = 0;  // BI_RGB

                IntPtr bits;
                hBitmap = N.CreateDIBSection(screenDc, ref bi, N.DIB_RGB_COLORS, out bits, IntPtr.Zero, 0);
                if (hBitmap == IntPtr.Zero) return;
                oldBitmap = N.SelectObject(memDc, hBitmap);

                // UpdateLayeredWindow は乗算済みアルファを要求するので PArgb の面に描く。
                using (Bitmap surface = new Bitmap(size, size, size * 4,
                           PixelFormat.Format32bppPArgb, bits))
                using (Graphics g = Graphics.FromImage(surface))
                using (Bitmap art = Glyphs.Render(size, mark))
                {
                    g.Clear(Color.Transparent);
                    g.DrawImageUnscaled(art, 0, 0);
                }

                N.POINT dst = new N.POINT(at.X, at.Y);
                N.POINT src = new N.POINT(0, 0);
                N.SIZE sz = new N.SIZE(size, size);
                N.BLENDFUNCTION blend = Blend(alpha);

                N.UpdateLayeredWindow(Handle, screenDc, ref dst, ref sz, memDc, ref src, 0,
                    ref blend, N.ULW_ALPHA);
            }
            finally
            {
                if (hBitmap != IntPtr.Zero)
                {
                    N.SelectObject(memDc, oldBitmap);
                    N.DeleteObject(hBitmap);
                }
                N.DeleteDC(memDc);
                N.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        /// <summary>絵はそのまま、透明度だけ差し替える。フェード中はこれだけを呼ぶ。</summary>
        private void SetAlpha(byte alpha)
        {
            N.BLENDFUNCTION blend = Blend(alpha);
            N.UpdateLayeredWindowAlpha(Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                IntPtr.Zero, IntPtr.Zero, 0, ref blend, N.ULW_ALPHA);
        }

        private static N.BLENDFUNCTION Blend(byte alpha)
        {
            N.BLENDFUNCTION b = new N.BLENDFUNCTION();
            b.BlendOp = N.AC_SRC_OVER;
            b.BlendFlags = 0;
            b.SourceConstantAlpha = alpha;
            b.AlphaFormat = N.AC_SRC_ALPHA;
            return b;
        }

        /// <summary>どこを基準に出すかを決める。</summary>
        private static Point AnchorPoint(string mode)
        {
            if (mode == "screen")
            {
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                return new Point(area.Left + area.Width / 2, area.Top + area.Height / 2);
            }

            if (mode != "mouse")
            {
                // 文字カーソルの位置。取れるアプリなら一番自然な場所に出せる。
                Point caret = Caret();
                if (caret != Point.Empty) return caret;
            }

            N.POINT p;
            if (N.GetCursorPos(out p)) return new Point(p.X, p.Y);
            return Point.Empty;
        }

        private static Point Caret()
        {
            IntPtr fg = N.GetForegroundWindow();
            if (fg == IntPtr.Zero) return Point.Empty;

            uint pid;
            uint tid = N.GetWindowThreadProcessId(fg, out pid);
            if (tid == 0) return Point.Empty;

            N.GUITHREADINFO gti = new N.GUITHREADINFO();
            gti.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(N.GUITHREADINFO));
            if (!N.GetGUIThreadInfo(tid, ref gti)) return Point.Empty;
            if (gti.hwndCaret == IntPtr.Zero) return Point.Empty;
            if (gti.rcCaret.Bottom == gti.rcCaret.Top) return Point.Empty;

            N.POINT p = new N.POINT(gti.rcCaret.Left, gti.rcCaret.Bottom);
            if (!N.ClientToScreen(gti.hwndCaret, ref p)) return Point.Empty;
            return new Point(p.X, p.Y);
        }

        /// <summary>基準点から少しずらし、画面からはみ出さないところへ収める。</summary>
        private static Point Place(Point anchor, int size)
        {
            if (anchor == Point.Empty)
            {
                Rectangle a = Screen.PrimaryScreen.WorkingArea;
                anchor = new Point(a.Left + a.Width / 2, a.Top + a.Height / 2);
            }

            Rectangle area = Screen.FromPoint(anchor).WorkingArea;
            int x = anchor.X + size / 4;
            int y = anchor.Y + size / 4;

            if (x + size > area.Right) x = area.Right - size;
            if (y + size > area.Bottom) y = anchor.Y - size - size / 4;
            if (x < area.Left) x = area.Left;
            if (y < area.Top) y = area.Top;
            return new Point(x, y);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
