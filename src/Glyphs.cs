using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Kanaei
{
    internal enum Mark
    {
        App,        // アプリのアイコン。英数側と日本語側を半分ずつ塗り分けた 2 色マーク。
        English,    // 英数入力中
        Japanese,   // 日本語入力中
        Disabled    // 停止中
    }

    /// <summary>
    /// アイコンの描画。ビルド時の .ico 生成と、実行時のトレイアイコン生成で同じコードを使う。
    /// </summary>
    internal static class Glyphs
    {
        private static readonly Color InkDark = Color.FromArgb(0x2B, 0x30, 0x38);
        private static readonly Color InkDark2 = Color.FromArgb(0x1B, 0x1F, 0x25);
        private static readonly Color Blue = Color.FromArgb(0x36, 0x8C, 0xFF);
        private static readonly Color Blue2 = Color.FromArgb(0x15, 0x5F, 0xD8);
        private static readonly Color Grey = Color.FromArgb(0x7A, 0x81, 0x8C);
        private static readonly Color Grey2 = Color.FromArgb(0x5A, 0x61, 0x6B);

        private const string LatinFont = "Segoe UI";
        private const string KanaFont = "Yu Gothic UI";

        public static Bitmap Render(int size, Mark mark)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                RectangleF box = new RectangleF(0.5f, 0.5f, size - 1f, size - 1f);
                float radius = size * 0.24f;

                using (GraphicsPath path = Rounded(box, radius))
                {
                    switch (mark)
                    {
                        case Mark.App:
                            PaintSplit(g, path, box, size);
                            break;
                        case Mark.English:
                            Fill(g, path, box, InkDark, InkDark2);
                            Glyph(g, "A", LatinFont, box, size * 0.60f, Color.White, size);
                            break;
                        case Mark.Japanese:
                            Fill(g, path, box, Blue, Blue2);
                            Glyph(g, "あ", KanaFont, box, size * 0.74f, Color.White, size);
                            break;
                        case Mark.Disabled:
                            Fill(g, path, box, Grey, Grey2);
                            // 停止中は色を落として字も薄くする。小さく表示されても「効いていない」と分かる。
                            Glyph(g, "あ", KanaFont, box, size * 0.74f,
                                Color.FromArgb(135, 255, 255, 255), size);
                            break;
                    }
                }
            }
            return bmp;
        }

        /// <summary>左半分が英数（濃いグレー）、右半分が日本語（青）。このアプリの成り立ちそのもの。</summary>
        private static void PaintSplit(Graphics g, GraphicsPath path, RectangleF box, int size)
        {
            GraphicsState state = g.Save();
            g.SetClip(path);

            float mid = box.Left + box.Width / 2f;
            using (Brush left = new LinearGradientBrush(
                new RectangleF(box.Left, box.Top, box.Width / 2f, box.Height), InkDark, InkDark2, 90f))
                g.FillRectangle(left, box.Left, box.Top, box.Width / 2f + 0.5f, box.Height);
            using (Brush right = new LinearGradientBrush(
                new RectangleF(mid, box.Top, box.Width / 2f, box.Height), Blue, Blue2, 90f))
                g.FillRectangle(right, mid, box.Top, box.Width / 2f + 0.5f, box.Height);

            g.Restore(state);

            if (size >= 32)
            {
                // 両方を並べても潰れない大きさなら「A / あ」を並べる。
                RectangleF l = new RectangleF(box.Left, box.Top, box.Width / 2f, box.Height);
                RectangleF r = new RectangleF(box.Left + box.Width / 2f, box.Top, box.Width / 2f, box.Height);
                Glyph(g, "A", LatinFont, l, size * 0.44f, Color.White, size);
                Glyph(g, "あ", KanaFont, r, size * 0.48f, Color.White, size);
            }
            else
            {
                // 小さいサイズでは 2 文字は潰れるので、塗り分けだけ残して「あ」を 1 文字置く。
                Glyph(g, "あ", KanaFont, box, size * 0.72f, Color.White, size);
            }
        }

        private static void Fill(Graphics g, GraphicsPath path, RectangleF box, Color a, Color b)
        {
            using (Brush brush = new LinearGradientBrush(
                new RectangleF(box.X, box.Y - 1, box.Width, box.Height + 2), a, b, 90f))
                g.FillPath(brush, path);
        }

        private static void Glyph(Graphics g, string text, string family, RectangleF area,
            float emSize, Color color, int size)
        {
            if (emSize < 1f) return;
            using (Font f = new Font(family, emSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Brush b = new SolidBrush(color))
            {
                StringFormat sf = new StringFormat(StringFormatFlags.NoWrap);
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                RectangleF r = area;
                r.Offset(0f, -size * 0.02f);
                g.DrawString(text, f, b, r, sf);
            }
        }

        /// <summary>アイコンを斜めに切り取ったような溝。背景色で引くので「止まっている」感じが出る。</summary>
        private static void Slash(Graphics g, RectangleF box, int size)
        {
            float pad = size * 0.14f;
            using (Pen p = new Pen(Grey2, Math.Max(1.4f, size * 0.14f)))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                g.DrawLine(p, box.Left + pad, box.Bottom - pad, box.Right - pad, box.Top + pad);
            }
        }

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = radius * 2f;
            GraphicsPath p = new GraphicsPath();
            if (d <= 0f)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.Left, r.Top, d, d, 180f, 90f);
            p.AddArc(r.Right - d, r.Top, d, d, 270f, 90f);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90f, 90f);
            p.CloseFigure();
            return p;
        }

        /// <summary>ビットマップからアイコンを作る。ハンドルは複製後に解放する。</summary>
        public static Icon ToIcon(Bitmap bmp, Action<IntPtr> destroy)
        {
            IntPtr h = bmp.GetHicon();
            using (Icon tmp = Icon.FromHandle(h))
            {
                Icon copy = (Icon)tmp.Clone();
                destroy(h);
                return copy;
            }
        }
    }
}
