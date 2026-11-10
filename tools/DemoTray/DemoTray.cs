// DemoTray — a tiny, harmless tray app used to test Tray Killer and to stage screenshots.
// Usage: DemoTray.exe [tooltip] [hexColor] [letter]
//        DemoTray.exe --ico <out.ico> <hexColor> <letter>   (writes a matching app icon)
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

static class DemoTray
{
    static Bitmap Draw(int size, Color color, string letter)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float m = size / 32f;
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, size, size), ControlPaint.Light(color, 0.25f), color, 45f))
                g.FillEllipse(b, m, m, size - 2 * m, size - 2 * m);
            using (var f = new Font("Segoe UI", size * 0.48f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(letter, f, Brushes.White, new RectangleF(0, size * 0.03f, size, size), sf);
        }
        return bmp;
    }

    static void WriteIco(string path, Color color, string letter)
    {
        int[] sizes = { 64, 48, 32, 16 };
        var images = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
            using (var b = Draw(sizes[i], color, letter))
            using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); images[i] = ms.ToArray(); }
        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)sizes[i]); w.Write((byte)sizes[i]); w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32); w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) w.Write(img);
        }
    }

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length >= 4 && args[0] == "--ico")
        {
            WriteIco(args[1], ColorTranslator.FromHtml("#" + args[2].TrimStart('#')), args[3]);
            return;
        }

        string tip = args.Length > 0 ? args[0] : "Demo tray app";
        Color color = args.Length > 1 ? ColorTranslator.FromHtml("#" + args[1].TrimStart('#')) : Color.FromArgb(0x3B, 0x82, 0xF6);
        string letter = args.Length > 2 ? args[2] : tip.Substring(0, 1).ToUpperInvariant();

        using (var bmp = Draw(32, color, letter))
        {
            var icon = Icon.FromHandle(bmp.GetHicon());
            var ni = new NotifyIcon { Icon = icon, Text = tip.Length > 63 ? tip.Substring(0, 63) : tip, Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Exit", null, (s, e) => { ni.Visible = false; Application.Exit(); });
            ni.ContextMenuStrip = menu;
            Application.Run();
            ni.Dispose();
        }
    }
}
