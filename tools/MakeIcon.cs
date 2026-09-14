using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Kanaei
{
    /// <summary>
    /// ビルド時にアプリ用の .ico を書き出す。プレビュー用の PNG も一緒に出す。
    /// 小さいサイズは互換性重視で BMP、大きいサイズは PNG 圧縮で格納する。
    /// </summary>
    internal static class MakeIcon
    {
        private static readonly int[] BmpSizes = { 16, 20, 24, 32, 40, 48, 64 };
        private static readonly int[] PngSizes = { 128, 256 };

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("usage: MakeIcon <out.ico> [previewDir]");
                return 1;
            }
            string icoPath = args[0];
            string previewDir = args.Length > 1 ? args[1] : null;

            try
            {
                List<byte[]> images = new List<byte[]>();
                List<int> sizes = new List<int>();

                foreach (int s in BmpSizes)
                {
                    using (Bitmap bmp = Glyphs.Render(s, Mark.App))
                    {
                        images.Add(EncodeBmp(bmp));
                        sizes.Add(s);
                    }
                }
                foreach (int s in PngSizes)
                {
                    using (Bitmap bmp = Glyphs.Render(s, Mark.App))
                    {
                        images.Add(EncodePng(bmp));
                        sizes.Add(s);
                    }
                }

                WriteIco(icoPath, sizes, images);
                Console.WriteLine("icon: " + icoPath);

                if (!string.IsNullOrEmpty(previewDir))
                {
                    Directory.CreateDirectory(previewDir);
                    SavePreview(previewDir, "app", Mark.App);
                    SavePreview(previewDir, "english", Mark.English);
                    SavePreview(previewDir, "japanese", Mark.Japanese);
                    SavePreview(previewDir, "disabled", Mark.Disabled);
                    SaveTraySheet(previewDir);
                    Console.WriteLine("preview: " + previewDir);
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static void SavePreview(string dir, string name, Mark mark)
        {
            using (Bitmap bmp = Glyphs.Render(256, mark))
                bmp.Save(Path.Combine(dir, name + "-256.png"), ImageFormat.Png);
        }

        /// <summary>実寸に近い大きさで並べたシート。小さいときにどう見えるかの確認用。</summary>
        private static void SaveTraySheet(string dir)
        {
            int[] sizes = { 16, 20, 24, 32, 48 };
            Mark[] marks = { Mark.App, Mark.English, Mark.Japanese, Mark.Disabled };
            int cell = 64;
            using (Bitmap sheet = new Bitmap(cell * sizes.Length, cell * marks.Length))
            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.FromArgb(0xF3, 0xF3, 0xF3));
                for (int r = 0; r < marks.Length; r++)
                {
                    for (int c = 0; c < sizes.Length; c++)
                    {
                        using (Bitmap bmp = Glyphs.Render(sizes[c], marks[r]))
                        {
                            g.DrawImageUnscaled(bmp,
                                c * cell + (cell - sizes[c]) / 2,
                                r * cell + (cell - sizes[c]) / 2);
                        }
                    }
                }
                sheet.Save(Path.Combine(dir, "sheet.png"), ImageFormat.Png);
            }
        }

        private static byte[] EncodePng(Bitmap bmp)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        /// <summary>ICO の中に入れる DIB。BITMAPINFOHEADER + 32bpp 下から上 + AND マスク。</summary>
        private static byte[] EncodeBmp(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            int xorStride = w * 4;
            int andStride = ((w + 31) / 32) * 4;   // 1bpp、4 バイト境界
            int total = 40 + xorStride * h + andStride * h;

            byte[] buf = new byte[total];
            using (MemoryStream ms = new MemoryStream(buf))
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write(40);              // biSize
                bw.Write(w);               // biWidth
                bw.Write(h * 2);           // biHeight (XOR + AND)
                bw.Write((short)1);        // biPlanes
                bw.Write((short)32);       // biBitCount
                bw.Write(0);               // biCompression = BI_RGB
                bw.Write(xorStride * h);   // biSizeImage
                bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

                BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    byte[] row = new byte[xorStride];
                    for (int y = h - 1; y >= 0; y--)   // 下から上へ
                    {
                        IntPtr src = new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride);
                        System.Runtime.InteropServices.Marshal.Copy(src, row, 0, xorStride);
                        bw.Write(row, 0, xorStride);
                    }
                }
                finally { bmp.UnlockBits(data); }

                // アルファで抜くので AND マスクは全部 0（＝不透明扱い）で良い。
                bw.Write(new byte[andStride * h]);
            }
            return buf;
        }

        private static void WriteIco(string path, List<int> sizes, List<byte[]> images)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using (FileStream fs = File.Create(path))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                bw.Write((short)0);              // reserved
                bw.Write((short)1);              // type = icon
                bw.Write((short)images.Count);

                int offset = 6 + 16 * images.Count;
                for (int i = 0; i < images.Count; i++)
                {
                    int s = sizes[i];
                    bw.Write((byte)(s >= 256 ? 0 : s));
                    bw.Write((byte)(s >= 256 ? 0 : s));
                    bw.Write((byte)0);           // colorCount
                    bw.Write((byte)0);           // reserved
                    bw.Write((short)1);          // planes
                    bw.Write((short)32);         // bitCount
                    bw.Write(images[i].Length);
                    bw.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] img in images) bw.Write(img);
            }
        }
    }
}
