// Draws the scaler logo and writes A2G-Scaler.ico (16-256 px) and help/img/logo.png (256 px).
// Not part of the build. Run from the repo folder (Windows PowerShell 5.1):
//   Add-Type -Path tools\MakeIcon.cs -ReferencedAssemblies System.Drawing
//   [MakeIcon]::Write("A2G-Scaler.ico", "help\img\logo.png")
// Write also creates help\img\logo.small.png as a preview of the small sizes; do not commit it.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class MakeIcon
{
    static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath(); float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    public static Bitmap Draw(int s)
    {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float m = s <= 16 ? 0 : s * 0.03f;
            var bg = new RectangleF(m, m, s - 2 * m, s - 2 * m);
            using (var path = Round(bg, s * 0.2f))
            using (var br = new LinearGradientBrush(bg, Color.FromArgb(58, 160, 72), Color.FromArgb(24, 100, 40), 90f))
                g.FillPath(br, path);
            // pitch stripes
            if (s >= 32)
                using (var path = Round(bg, s * 0.2f))
                {
                    g.SetClip(path);
                    using (var stripe = new SolidBrush(Color.FromArgb(22, 255, 255, 255)))
                        for (int i = 0; i < 6; i += 2) g.FillRectangle(stripe, bg.X + bg.Width * i / 6f, bg.Y, bg.Width / 6f, bg.Height);
                    g.ResetClip();
                }
            float pad = s * 0.2f, big = s - 2 * pad, small = big * 0.4f;
            float lw = Math.Max(1f, (float)Math.Round(s * 0.075f));
            float h = lw / 2, l = pad + h, t = pad + h, r = pad + big - h, b = pad + big - h;
            // corner brackets of the large (scaled) picture
            using (var pen = new Pen(Color.White, lw) { StartCap = LineCap.Square, EndCap = LineCap.Square, LineJoin = LineJoin.Miter })
            {
                if (s < 24) g.DrawRectangle(pen, l, t, r - l, b - t);
                else
                {
                    float a = big * 0.26f;
                    g.DrawLines(pen, new[] { new PointF(l, t + a), new PointF(l, t), new PointF(l + a, t) });
                    g.DrawLines(pen, new[] { new PointF(r - a, t), new PointF(r, t), new PointF(r, t + a) });
                    g.DrawLines(pen, new[] { new PointF(r, b - a), new PointF(r, b), new PointF(r - a, b) });
                }
            }
            // small solid picture (the 640x480 original) in the bottom-left corner
            using (var br = new SolidBrush(Color.White))
                g.FillRectangle(br, pad, pad + big - small, small, small);
            // arrow from the small picture towards the top-right corner
            if (s >= 24)
            {
                var gold = Color.FromArgb(255, 210, 60);
                float gap = big * 0.12f, head = Math.Max(4f, s * 0.2f);
                var p0 = new PointF(pad + small + gap, pad + big - small - gap);
                var p1 = new PointF(pad + big - gap * 1.4f, pad + gap * 1.4f);
                float dx = p1.X - p0.X, dy = p1.Y - p0.Y, len = (float)Math.Sqrt(dx * dx + dy * dy);
                float ux = dx / len, uy = dy / len;
                var baseC = new PointF(p1.X - ux * head, p1.Y - uy * head);
                using (var pen = new Pen(gold, lw * 1.2f) { StartCap = LineCap.Round })
                    g.DrawLine(pen, p0, new PointF(baseC.X + ux, baseC.Y + uy));
                using (var br = new SolidBrush(gold))
                    g.FillPolygon(br, new[] { p1, new PointF(baseC.X - uy * head * 0.55f, baseC.Y + ux * head * 0.55f), new PointF(baseC.X + uy * head * 0.55f, baseC.Y - ux * head * 0.55f) });
            }
        }
        return bmp;
    }

    static byte[] Dib(Bitmap b)
    {
        int s = b.Width; var ms = new MemoryStream(); var w = new BinaryWriter(ms);
        int mask = ((s + 31) / 32) * 4 * s;
        w.Write(40); w.Write(s); w.Write(s * 2); w.Write((short)1); w.Write((short)32);
        w.Write(0); w.Write(s * s * 4 + mask); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (int y = s - 1; y >= 0; y--)
            for (int x = 0; x < s; x++) { var c = b.GetPixel(x, y); w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A); }
        w.Write(new byte[mask]);
        return ms.ToArray();
    }

    public static void Write(string icoPath, string previewPath)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 256 };
        var data = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
            using (var b = Draw(sizes[i]))
            {
                if (sizes[i] == 256) { var ms = new MemoryStream(); b.Save(ms, ImageFormat.Png); data[i] = ms.ToArray(); b.Save(previewPath, ImageFormat.Png); }
                else data[i] = Dib(b);
            }
        using (var w = new BinaryWriter(File.Create(icoPath)))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] & 255)); w.Write((byte)(sizes[i] & 255)); w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32); w.Write(data[i].Length); w.Write(offset);
                offset += data[i].Length;
            }
            foreach (var d in data) w.Write(d);
        }
        // preview strip of the small sizes, 4x enlarged
        using (var strip = new Bitmap(16 * 4 + 24 * 4 + 32 * 4 + 48 * 4 + 50, 48 * 4, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(strip))
        {
            g.Clear(Color.FromArgb(240, 240, 240)); g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
            int x = 0;
            foreach (int s in new[] { 16, 24, 32, 48 }) using (var b = Draw(s)) { g.DrawImage(b, x, 0, s * 4, s * 4); x += s * 4 + 10; }
            strip.Save(Path.ChangeExtension(previewPath, ".small.png"), ImageFormat.Png);
        }
    }
}
