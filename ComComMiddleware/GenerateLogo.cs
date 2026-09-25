using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

class GenerateLogo
{
    static int Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : ".";
        int[] sizes = new int[] { 16, 32, 48, 256 };

        var pngStreams = new System.Collections.Generic.List<byte[]>();
        foreach (int size in sizes)
        {
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                float pad = size / 16f;
                var rect = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);
                using (var brush = new SolidBrush(Color.FromArgb(0, 112, 192)))
                {
                    g.FillEllipse(brush, rect);
                }

                using (var pen = new Pen(Color.FromArgb(200, 255, 255, 255), Math.Max(1f, size / 32f)))
                {
                    g.DrawEllipse(pen, rect);
                }

                string text = "CC";
                float emSize = size * 0.45f;
                using (var font = new Font("Microsoft YaHei UI", emSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var textBrush = new SolidBrush(Color.White))
                {
                    g.DrawString(text, font, textBrush, new RectangleF(0, 0, size, size), sf);
                }

                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    pngStreams.Add(ms.ToArray());
                }
            }
        }

        string icoPath = Path.Combine(outDir, "logo.ico");
        using (var fs = new FileStream(icoPath, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((short)0);
            bw.Write((short)1);
            bw.Write((short)sizes.Length);

            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                byte[] png = pngStreams[i];
                bw.Write((byte)(s == 256 ? 0 : s));
                bw.Write((byte)(s == 256 ? 0 : s));
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((short)1);
                bw.Write((short)32);
                bw.Write(png.Length);
                bw.Write(offset);
                offset += png.Length;
            }

            for (int i = 0; i < sizes.Length; i++)
            {
                bw.Write(pngStreams[i]);
            }
        }

        Console.WriteLine("Generated: " + icoPath);
        return 0;
    }
}
