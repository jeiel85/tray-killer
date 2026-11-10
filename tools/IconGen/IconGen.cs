// IconGen — draws the Tray Killer icon (crosshair on a crimson→orange squircle) at every size
// and packs multi-resolution .ico files. Usage: IconGen.exe <outDir>
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class IconGen
{
    static readonly Color A1 = Color.FromArgb(0xFF, 0x1F, 0x57); // crimson
    static readonly Color A2 = Color.FromArgb(0xFF, 0x7A, 0x2B); // orange
    static readonly Color P1 = Color.FromArgb(0x4B, 0x55, 0x63); // paused: slate
    static readonly Color P2 = Color.FromArgb(0x9C, 0xA3, 0xAF);

    static void Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(outDir);
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        WriteIco(Path.Combine(outDir, "TrayKiller.ico"), sizes, false);
        WriteIco(Path.Combine(outDir, "TrayKiller-paused.ico"), sizes, true);
        foreach (int s in new[] { 32, 64, 128, 256, 512, 1024 })
            using (var b = Draw(s, false)) b.Save(Path.Combine(outDir, "logo-" + s + ".png"), ImageFormat.Png);
        // preview strip for eyeballing small sizes
        using (var strip = new Bitmap(16 + 20 + 24 + 32 + 48 + 64 + 7 * 8, 64))
        using (var g = Graphics.FromImage(strip))
        {
            g.Clear(Color.FromArgb(0x1C, 0x1F, 0x26));
            int x = 4;
            foreach (int s in new[] { 16, 20, 24, 32, 48, 64 })
            {
                using (var b = Draw(s, false)) g.DrawImageUnscaled(b, x, (64 - s) / 2);
                x += s + 8;
            }
            strip.Save(Path.Combine(outDir, "preview-strip.png"), ImageFormat.Png);
        }
        Console.WriteLine("icons written to " + Path.GetFullPath(outDir));
    }

    static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static Bitmap Draw(int S, bool paused)
    {
        var bmp = new Bitmap(S, S, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.Clear(Color.Transparent);

            float m = S <= 24 ? 0f : S * 0.035f;
            var rect = new RectangleF(m, m, S - 2 * m, S - 2 * m);
            float radius = rect.Width * 0.27f;
            using (var path = RoundRect(rect, radius))
            {
                using (var br = new LinearGradientBrush(new PointF(rect.Left, rect.Top), new PointF(rect.Right, rect.Bottom),
                    paused ? P1 : A1, paused ? P2 : A2))
                    g.FillPath(br, path);

                if (S >= 48)
                {
                    // soft top highlight
                    var hl = new RectangleF(rect.X, rect.Y, rect.Width, rect.Height * 0.55f);
                    using (var hb = new LinearGradientBrush(hl, Color.FromArgb(30, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                    {
                        var clip = g.Clip;
                        g.SetClip(path);
                        g.FillRectangle(hb, hl);
                        g.Clip = clip;
                    }
                    // hairline inner border
                    using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255), Math.Max(1f, S / 128f)))
                    using (var inner = RoundRect(new RectangleF(rect.X + 0.5f, rect.Y + 0.5f, rect.Width - 1, rect.Height - 1), radius))
                        g.DrawPath(pen, inner);
                }
            }

            // crosshair
            float c = S / 2f;
            float R = S * 0.25f;
            float w = S <= 24 ? 2f : S <= 40 ? S * 0.08f : S * 0.072f;
            var white = Color.FromArgb(255, 255, 255, 255);
            using (var pen = new Pen(white, w))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawEllipse(pen, c - R, c - R, 2 * R, 2 * R);
                float outer = R + S * (S <= 24 ? 0.17f : 0.16f);
                float inner = S <= 24 ? R + w * 0.5f : R - S * 0.075f;
                if (S <= 24) { pen.StartCap = LineCap.Flat; pen.EndCap = LineCap.Flat; }
                g.DrawLine(pen, c, c - inner, c, c - outer);
                g.DrawLine(pen, c, c + inner, c, c + outer);
                g.DrawLine(pen, c - inner, c, c - outer, c);
                g.DrawLine(pen, c + inner, c, c + outer, c);
            }
            float dot = Math.Max(1.4f, S * 0.07f);
            using (var b = new SolidBrush(white)) g.FillEllipse(b, c - dot, c - dot, 2 * dot, 2 * dot);
        }
        return bmp;
    }

    static void WriteIco(string file, int[] sizes, bool paused)
    {
        var images = new List<byte[]>();
        foreach (int s in sizes)
            using (var b = Draw(s, paused)) images.Add(s >= 64 ? Png(b) : Dib(b));

        using (var fs = new FileStream(file, FileMode.Create))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) w.Write(img);
        }
    }

    static byte[] Png(Bitmap b)
    {
        using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }

    static byte[] Dib(Bitmap b)
    {
        int s = b.Width;
        int maskStride = ((s + 31) / 32) * 4;
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(40); w.Write(s); w.Write(s * 2); w.Write((short)1); w.Write((short)32);
            w.Write(0); w.Write(s * s * 4 + maskStride * s); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            var data = b.LockBits(new Rectangle(0, 0, s, s), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var px = new byte[data.Stride * s];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
            b.UnlockBits(data);
            for (int y = s - 1; y >= 0; y--) w.Write(px, y * data.Stride, s * 4);
            for (int y = s - 1; y >= 0; y--)
            {
                var row = new byte[maskStride];
                for (int x = 0; x < s; x++)
                    if (px[y * data.Stride + x * 4 + 3] == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
                w.Write(row);
            }
            return ms.ToArray();
        }
    }
}

