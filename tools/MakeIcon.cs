using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class MakeIcon
{
    private static int Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "cuedeck.ico";
        int[] sizes = new int[] { 256, 64, 48, 32, 16 };
        byte[][] pngs = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            using (Bitmap bmp = Draw(sizes[i]))
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                pngs[i] = ms.ToArray();
            }
        }
        string dir = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using (FileStream fs = new FileStream(output, FileMode.Create, FileAccess.Write))
        using (BinaryWriter bw = new BinaryWriter(fs))
        {
            bw.Write((ushort)0);
            bw.Write((ushort)1);
            bw.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int size = sizes[i];
                bw.Write((byte)(size >= 256 ? 0 : size));
                bw.Write((byte)(size >= 256 ? 0 : size));
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write((uint)pngs[i].Length);
                bw.Write((uint)offset);
                offset += pngs[i].Length;
            }
            for (int i = 0; i < pngs.Length; i++) bw.Write(pngs[i]);
        }
        Console.WriteLine("Icon written: " + Path.GetFullPath(output));
        return 0;
    }

    private static Bitmap Draw(int size)
    {
        Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);
            float pad = size * 0.035f;
            RectangleF rect = new RectangleF(pad, pad, size - pad * 2f, size - pad * 2f);
            using (GraphicsPath path = Round(rect, size * 0.23f))
            using (LinearGradientBrush brush = new LinearGradientBrush(rect,
                Color.FromArgb(35, 42, 62), Color.FromArgb(10, 12, 18), 55f))
                g.FillPath(brush, path);

            float cx = size * 0.50f;
            float cy = size * 0.50f;
            float r = size * 0.30f;
            using (LinearGradientBrush disc = new LinearGradientBrush(
                new RectangleF(cx - r, cy - r, r * 2, r * 2),
                Color.FromArgb(140, 124, 255), Color.FromArgb(74, 227, 200), 35f))
                g.FillEllipse(disc, cx - r, cy - r, r * 2f, r * 2f);

            using (Pen pen = new Pen(Color.FromArgb(25, 18, 32), Math.Max(1f, size * 0.045f)))
                g.DrawEllipse(pen, cx - r * 0.72f, cy - r * 0.72f, r * 1.44f, r * 1.44f);
            using (SolidBrush hole = new SolidBrush(Color.FromArgb(16, 20, 30)))
                g.FillEllipse(hole, cx - r * 0.17f, cy - r * 0.17f, r * 0.34f, r * 0.34f);

            using (SolidBrush white = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
            {
                PointF p1 = new PointF(cx + r * 0.24f, cy);
                PointF p2 = new PointF(cx - r * 0.20f, cy - r * 0.34f);
                PointF p3 = new PointF(cx - r * 0.20f, cy + r * 0.34f);
                g.FillPolygon(white, new PointF[] { p1, p2, p3 });
            }

            if (size >= 48)
            {
                using (Pen shine = new Pen(Color.FromArgb(90, 255, 255, 255), Math.Max(1f, size * 0.012f)))
                {
                    g.DrawArc(shine, cx - r * 0.92f, cy - r * 0.92f, r * 1.84f, r * 1.84f, 202f, 55f);
                    g.DrawArc(shine, cx - r * 0.60f, cy - r * 0.60f, r * 1.2f, r * 1.2f, 30f, 34f);
                }
            }
        }
        return bmp;
    }

    private static GraphicsPath Round(RectangleF rect, float radius)
    {
        float d = radius * 2f;
        GraphicsPath path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
        path.CloseFigure();
        return path;
    }
}
