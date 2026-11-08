using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace CueDeck
{
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(10, 12, 17);
        public static readonly Color TopBar = Color.FromArgb(12, 15, 21);
        public static readonly Color Sidebar = Color.FromArgb(9, 11, 16);
        public static readonly Color Surface = Color.FromArgb(17, 21, 29);
        public static readonly Color Surface2 = Color.FromArgb(21, 26, 36);
        public static readonly Color Surface3 = Color.FromArgb(27, 33, 45);
        public static readonly Color Border = Color.FromArgb(37, 44, 58);
        public static readonly Color BorderSoft = Color.FromArgb(27, 33, 44);
        public static readonly Color Text = Color.FromArgb(244, 247, 252);
        public static readonly Color Text2 = Color.FromArgb(198, 207, 222);
        public static readonly Color Muted = Color.FromArgb(128, 139, 157);
        public static readonly Color Muted2 = Color.FromArgb(86, 96, 114);
        public static readonly Color Accent = Color.FromArgb(140, 124, 255);
        public static readonly Color AccentHover = Color.FromArgb(164, 149, 255);
        public static readonly Color AccentDim = Color.FromArgb(43, 37, 74);
        public static readonly Color Teal = Color.FromArgb(74, 227, 200);
        public static readonly Color Green = Color.FromArgb(66, 221, 160);
        public static readonly Color Red = Color.FromArgb(255, 100, 124);
        public static readonly Color Amber = Color.FromArgb(255, 194, 102);
        public static readonly Color Blue = Color.FromArgb(90, 169, 255);

        private static readonly string BodyFamily;
        private static readonly string DisplayFamily;
        private static readonly string MonoFamily;
        private static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();
        private static readonly object FontLock = new object();

        static Theme()
        {
            BodyFamily = PickFamily(new string[] { "Segoe UI Variable Text", "Segoe UI", "Tahoma" });
            DisplayFamily = PickFamily(new string[] { "Segoe UI Variable Display", "Segoe UI", "Tahoma" });
            MonoFamily = PickFamily(new string[] { "Cascadia Mono", "Consolas", "Courier New" });
        }

        public static Color Alpha(Color color, int alpha)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), color);
        }

        public static Color Mix(Color a, Color b, float amount)
        {
            float t = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static Font Font(float size, FontStyle style)
        {
            return GetFont(BodyFamily, size, style);
        }

        public static Font DisplayFont(float size, FontStyle style)
        {
            return GetFont(DisplayFamily, size, style);
        }

        public static Font MonoFont(float size, FontStyle style)
        {
            return GetFont(MonoFamily, size, style);
        }

        public static Font GetFont(string family, float size, FontStyle style)
        {
            size = Math.Max(6f, size);
            string key = family + "|" + size.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "|" + (int)style;
            lock (FontLock)
            {
                Font font;
                if (Fonts.TryGetValue(key, out font)) return font;
                try { font = new Font(family, size, style, GraphicsUnit.Pixel); }
                catch { font = new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Pixel); }
                Fonts[key] = font;
                return font;
            }
        }

        private static string PickFamily(string[] names)
        {
            try
            {
                FontFamily[] families = FontFamily.Families;
                for (int n = 0; n < names.Length; n++)
                {
                    for (int i = 0; i < families.Length; i++)
                    {
                        if (string.Equals(families[i].Name, names[n], StringComparison.OrdinalIgnoreCase))
                            return families[i].Name;
                    }
                }
            }
            catch { }
            return "Segoe UI";
        }

        public static GraphicsPath RoundRect(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Min(Math.Min(radius * 2f, rect.Width), rect.Height);
            if (d <= 0.5f)
            {
                path.AddRectangle(rect);
                return path;
            }
            float r = d / 2f;
            path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, RectangleF rect, float radius, Color color)
        {
            using (GraphicsPath path = RoundRect(rect, radius))
            using (SolidBrush brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        public static void FillRoundGradient(Graphics g, RectangleF rect, float radius, Color a, Color b, float angle)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            using (GraphicsPath path = RoundRect(rect, radius))
            using (LinearGradientBrush brush = new LinearGradientBrush(rect, a, b, angle))
                g.FillPath(brush, path);
        }

        public static void StrokeRound(Graphics g, RectangleF rect, float radius, Color color, float width)
        {
            using (GraphicsPath path = RoundRect(rect, radius))
            using (Pen pen = new Pen(color, Math.Max(1f, width)))
                g.DrawPath(pen, path);
        }

        public static void DrawText(Graphics g, string text, Font font, Color color, RectangleF rect, StringAlignment align, StringAlignment line)
        {
            DrawText(g, text, font, color, rect, align, line, false);
        }

        public static void DrawText(Graphics g, string text, Font font, Color color, RectangleF rect, StringAlignment align, StringAlignment line, bool wrap)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = align;
                format.LineAlignment = line;
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.HotkeyPrefix = HotkeyPrefix.None;
                if (!wrap) format.FormatFlags = StringFormatFlags.NoWrap;
                using (SolidBrush brush = new SolidBrush(color))
                    g.DrawString(text, font, brush, rect, format);
            }
        }

        public static void DrawTextWrap(Graphics g, string text, Font font, Color color, RectangleF rect, StringAlignment align)
        {
            if (string.IsNullOrEmpty(text)) return;
            GraphicsState state = g.Save();
            try
            {
                g.SetClip(rect);
                using (StringFormat format = new StringFormat())
                {
                    format.Alignment = align;
                    format.LineAlignment = StringAlignment.Near;
                    format.Trimming = StringTrimming.EllipsisWord;
                    format.HotkeyPrefix = HotkeyPrefix.None;
                    using (SolidBrush brush = new SolidBrush(color))
                        g.DrawString(text, font, brush, rect, format);
                }
            }
            finally { g.Restore(state); }
        }

        public static void DrawImageCover(Graphics g, Image image, RectangleF dest, float radius)
        {
            if (image == null || dest.Width <= 0 || dest.Height <= 0) return;
            GraphicsState state = g.Save();
            try
            {
                using (GraphicsPath clip = RoundRect(dest, radius)) g.SetClip(clip);
                float srcAspect = image.Width / (float)Math.Max(1, image.Height);
                float dstAspect = dest.Width / Math.Max(1f, dest.Height);
                RectangleF src;
                if (srcAspect > dstAspect)
                {
                    float w = image.Height * dstAspect;
                    src = new RectangleF((image.Width - w) / 2f, 0, w, image.Height);
                }
                else
                {
                    float h = image.Width / dstAspect;
                    src = new RectangleF(0, (image.Height - h) / 2f, image.Width, h);
                }
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(image, dest, src, GraphicsUnit.Pixel);
            }
            finally { g.Restore(state); }
        }

        public static void DrawScrollbar(Graphics g, RectangleF viewport, float contentHeight, float offset)
        {
            if (contentHeight <= viewport.Height + 1f) return;
            float trackHeight = Math.Max(20f, viewport.Height);
            float thumbHeight = Math.Max(28f, trackHeight * (viewport.Height / contentHeight));
            float max = contentHeight - viewport.Height;
            float t = max <= 0 ? 0 : Math.Max(0f, Math.Min(1f, offset / max));
            float y = viewport.Y + (viewport.Height - thumbHeight) * t;
            RectangleF track = new RectangleF(viewport.Right - 5f, viewport.Y + 2f, 3f, viewport.Height - 4f);
            RectangleF thumb = new RectangleF(viewport.Right - 6f, y, 4f, thumbHeight);
            FillRound(g, track, 2f, Color.FromArgb(30, 255, 255, 255));
            FillRound(g, thumb, 2f, Color.FromArgb(85, 255, 255, 255));
        }

        public static void DrawLogo(Graphics g, RectangleF rect)
        {
            Color a = Accent;
            Color b = Teal;
            FillRoundGradient(g, rect, rect.Width * 0.28f, a, b, 45f);
            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;
            float r = rect.Width * 0.30f;
            using (Pen ring = new Pen(Color.FromArgb(235, 12, 14, 20), Math.Max(2f, rect.Width * 0.09f)))
                g.DrawEllipse(ring, cx - r, cy - r, r * 2f, r * 2f);
            using (SolidBrush hole = new SolidBrush(Color.FromArgb(240, 12, 14, 20)))
                g.FillEllipse(hole, cx - rect.Width * 0.065f, cy - rect.Width * 0.065f, rect.Width * 0.13f, rect.Width * 0.13f);
            using (SolidBrush white = new SolidBrush(Color.FromArgb(240, 255, 255, 255)))
            {
                PointF p1 = new PointF(cx + rect.Width * 0.075f, cy);
                PointF p2 = new PointF(cx - rect.Width * 0.065f, cy - rect.Height * 0.095f);
                PointF p3 = new PointF(cx - rect.Width * 0.065f, cy + rect.Height * 0.095f);
                g.FillPolygon(white, new PointF[] { p1, p2, p3 });
            }
        }

        public static void DrawCover(Graphics g, RectangleF rect, string key, string title, string kind, Image image, float scale)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            GraphicsState state = g.Save();
            try
            {
                using (GraphicsPath clip = RoundRect(rect, Math.Max(7f, rect.Width * 0.035f))) g.SetClip(clip);
                if (image != null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    float srcAspect = image.Width / (float)Math.Max(1, image.Height);
                    float dstAspect = rect.Width / Math.Max(1f, rect.Height);
                    RectangleF src;
                    if (srcAspect > dstAspect)
                    {
                        float w = image.Height * dstAspect;
                        src = new RectangleF((image.Width - w) / 2f, 0, w, image.Height);
                    }
                    else
                    {
                        float h = image.Width / dstAspect;
                        src = new RectangleF(0, (image.Height - h) / 2f, image.Width, h);
                    }
                    g.DrawImage(image, rect, src, GraphicsUnit.Pixel);
                    using (LinearGradientBrush shade = new LinearGradientBrush(
                        new RectangleF(rect.X, rect.Bottom - rect.Height * 0.38f, rect.Width, rect.Height * 0.38f),
                        Color.FromArgb(0, 0, 0, 0), Color.FromArgb(125, 0, 0, 0), 90f))
                        g.FillRectangle(shade, rect.X, rect.Bottom - rect.Height * 0.38f, rect.Width, rect.Height * 0.38f);
                }
                else
                {
                    Color a, b;
                    Palette(key, title, out a, out b);
                    using (LinearGradientBrush brush = new LinearGradientBrush(rect, a, b, 42f))
                        g.FillRectangle(brush, rect);

                    float cx = rect.Right - rect.Width * 0.06f;
                    float cy = rect.Top + rect.Height * 0.34f;
                    float radius = rect.Width * 0.40f;
                    using (SolidBrush glow = new SolidBrush(Color.FromArgb(22, 255, 255, 255)))
                        g.FillEllipse(glow, cx - radius, cy - radius, radius * 2f, radius * 2f);
                    using (Pen ring = new Pen(Color.FromArgb(42, 255, 255, 255), Math.Max(1f, 1.1f * scale)))
                    {
                        for (int i = 1; i <= 4; i++)
                        {
                            float rr = radius * (0.44f + i * 0.13f);
                            g.DrawEllipse(ring, cx - rr, cy - rr, rr * 2f, rr * 2f);
                        }
                    }
                    using (SolidBrush center = new SolidBrush(Color.FromArgb(55, 9, 11, 16)))
                        g.FillEllipse(center, cx - radius * 0.23f, cy - radius * 0.23f, radius * 0.46f, radius * 0.46f);
                    using (Pen arc = new Pen(Color.FromArgb(100, 255, 255, 255), Math.Max(1f, 1.6f * scale)))
                    {
                        g.DrawArc(arc, cx - radius * 0.9f, cy - radius * 0.9f, radius * 1.8f, radius * 1.8f, 208f, 52f);
                        g.DrawArc(arc, cx - radius * 0.68f, cy - radius * 0.68f, radius * 1.36f, radius * 1.36f, 25f, 36f);
                    }
                    using (LinearGradientBrush shade = new LinearGradientBrush(
                        new RectangleF(rect.X, rect.Bottom - rect.Height * 0.55f, rect.Width, rect.Height * 0.55f),
                        Color.FromArgb(0, 0, 0, 0), Color.FromArgb(105, 0, 0, 0), 90f))
                        g.FillRectangle(shade, rect.X, rect.Bottom - rect.Height * 0.55f, rect.Width, rect.Height * 0.55f);

                    string initials = Initials(title);
                    if (initials.Length > 0)
                    {
                        Font big = Font(Math.Max(18f, rect.Width * 0.19f), FontStyle.Bold);
                        DrawText(g, initials, big, Color.FromArgb(225, 255, 255, 255),
                            new RectangleF(rect.X + rect.Width * 0.08f, rect.Bottom - rect.Height * 0.40f, rect.Width * 0.7f, rect.Height * 0.30f),
                            StringAlignment.Near, StringAlignment.Far);
                    }
                }

                if (!string.IsNullOrEmpty(kind))
                {
                    Font chipFont = GetFont(BodyFamily, Math.Max(8.5f, 9.2f * scale), FontStyle.Bold);
                    SizeF size = g.MeasureString(kind, chipFont);
                    RectangleF chip = new RectangleF(rect.X + 9f * scale, rect.Y + 9f * scale, size.Width + 14f * scale, 20f * scale);
                    FillRound(g, chip, 6f * scale, Color.FromArgb(115, 5, 7, 11));
                    DrawText(g, kind, chipFont, Color.FromArgb(235, 255, 255, 255),
                        chip, StringAlignment.Center, StringAlignment.Center);
                }
            }
            finally { g.Restore(state); }
        }

        public static void Palette(string key, string title, out Color a, out Color b)
        {
            Color[][] palettes = new Color[][]
            {
                new Color[] { Color.FromArgb(113, 86, 255), Color.FromArgb(52, 216, 194) },
                new Color[] { Color.FromArgb(255, 91, 132), Color.FromArgb(129, 83, 255) },
                new Color[] { Color.FromArgb(255, 162, 72), Color.FromArgb(255, 83, 116) },
                new Color[] { Color.FromArgb(57, 210, 160), Color.FromArgb(58, 138, 255) },
                new Color[] { Color.FromArgb(244, 194, 64), Color.FromArgb(255, 112, 80) },
                new Color[] { Color.FromArgb(69, 196, 240), Color.FromArgb(124, 92, 255) },
                new Color[] { Color.FromArgb(241, 91, 181), Color.FromArgb(155, 93, 229) },
                new Color[] { Color.FromArgb(0, 187, 249), Color.FromArgb(0, 245, 212) }
            };
            string source = (key ?? "") + "|" + (title ?? "");
            uint hash = 2166136261;
            for (int i = 0; i < source.Length; i++)
            {
                hash ^= source[i];
                hash *= 16777619;
            }
            int index = (int)(hash % (uint)palettes.Length);
            a = palettes[index][0];
            b = palettes[index][1];
        }

        public static string Initials(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";
            string[] parts = title.Split(new char[] { ' ', '-', '_', ':', '.', '[', ']', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
            string result = "";
            for (int i = 0; i < parts.Length && result.Length < 2; i++)
            {
                string p = parts[i];
                if (p.Length == 0) continue;
                if (char.IsLetterOrDigit(p[0])) result += char.ToUpperInvariant(p[0]);
            }
            if (result.Length == 0)
            {
                for (int i = 0; i < title.Length && result.Length < 2; i++)
                    if (char.IsLetterOrDigit(title[i])) result += char.ToUpperInvariant(title[i]);
            }
            return result;
        }

        public static void DrawHeart(Graphics g, RectangleF rect, Color color, bool filled)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                float w = rect.Width;
                float h = rect.Height;
                float cx = rect.X + w / 2f;
                float top = rect.Y + h * 0.12f;
                float bottom = rect.Y + h * 0.94f;
                PointF p0 = new PointF(cx, bottom);
                PointF p1 = new PointF(cx, top + h * 0.30f);
                path.AddBezier(p0,
                    new PointF(rect.X - w * 0.06f, rect.Y + h * 0.58f),
                    new PointF(rect.X + w * 0.10f, top),
                    p1);
                path.AddBezier(p1,
                    new PointF(rect.Right - w * 0.10f, top),
                    new PointF(rect.Right + w * 0.06f, rect.Y + h * 0.58f),
                    p0);
                path.CloseFigure();
                if (filled)
                {
                    using (SolidBrush brush = new SolidBrush(color)) g.FillPath(brush, path);
                }
                else
                {
                    using (Pen pen = new Pen(color, Math.Max(1.2f, rect.Width * 0.08f)))
                    {
                        pen.LineJoin = LineJoin.Round;
                        g.DrawPath(pen, path);
                    }
                }
            }
        }

        public static void IconSearch(Graphics g, RectangleF r, Color color, float scale)
        {
            using (Pen pen = NewPen(color, scale))
            {
                float d = r.Width * 0.56f;
                g.DrawEllipse(pen, r.X + r.Width * 0.10f, r.Y + r.Height * 0.10f, d, d);
                g.DrawLine(pen, r.X + r.Width * 0.58f, r.Y + r.Height * 0.58f, r.X + r.Width * 0.92f, r.Y + r.Height * 0.92f);
            }
        }

        public static void IconPlus(Graphics g, RectangleF r, Color color, float scale)
        {
            using (Pen pen = NewPen(color, scale))
            {
                float cx = r.X + r.Width / 2f;
                float cy = r.Y + r.Height / 2f;
                float a = r.Width * 0.32f;
                g.DrawLine(pen, cx - a, cy, cx + a, cy);
                g.DrawLine(pen, cx, cy - a, cx, cy + a);
            }
        }

        public static void IconSettings(Graphics g, RectangleF r, Color color, float scale)
        {
            float cx = r.X + r.Width / 2f;
            float cy = r.Y + r.Height / 2f;
            float radius = r.Width * 0.27f;
            using (Pen pen = NewPen(color, scale))
            {
                g.DrawEllipse(pen, cx - radius, cy - radius, radius * 2f, radius * 2f);
                g.DrawEllipse(pen, cx - radius * 0.34f, cy - radius * 0.34f, radius * 0.68f, radius * 0.68f);
                for (int i = 0; i < 8; i++)
                {
                    float angle = (float)(Math.PI * 2.0 * i / 8.0);
                    float x1 = cx + (float)Math.Cos(angle) * radius * 1.28f;
                    float y1 = cy + (float)Math.Sin(angle) * radius * 1.28f;
                    float x2 = cx + (float)Math.Cos(angle) * radius * 1.72f;
                    float y2 = cy + (float)Math.Sin(angle) * radius * 1.72f;
                    g.DrawLine(pen, x1, y1, x2, y2);
                }
            }
        }

        public static void IconFolder(Graphics g, RectangleF r, Color color, float scale)
        {
            float x = r.X + r.Width * 0.10f;
            float y = r.Y + r.Height * 0.24f;
            float w = r.Width * 0.80f;
            float h = r.Height * 0.58f;
            using (Pen pen = NewPen(color, scale))
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddLine(x, y + h * 0.18f, x, y + h);
                path.AddLine(x, y + h, x + w, y + h);
                path.AddLine(x + w, y + h, x + w, y + h * 0.32f);
                path.AddLine(x + w, y + h * 0.32f, x + w * 0.48f, y + h * 0.32f);
                path.AddLine(x + w * 0.48f, y + h * 0.32f, x + w * 0.40f, y);
                path.AddLine(x + w * 0.40f, y, x, y);
                path.CloseFigure();
                g.DrawPath(pen, path);
            }
        }

        public static void IconPlay(Graphics g, RectangleF r, Color color)
        {
            using (SolidBrush brush = new SolidBrush(color))
            {
                PointF p1 = new PointF(r.X + r.Width * 0.30f, r.Y + r.Height * 0.20f);
                PointF p2 = new PointF(r.X + r.Width * 0.80f, r.Y + r.Height * 0.50f);
                PointF p3 = new PointF(r.X + r.Width * 0.30f, r.Y + r.Height * 0.80f);
                g.FillPolygon(brush, new PointF[] { p1, p2, p3 });
            }
        }

        public static void IconPause(Graphics g, RectangleF r, Color color)
        {
            using (SolidBrush brush = new SolidBrush(color))
            {
                float w = r.Width * 0.18f;
                g.FillRectangle(brush, r.X + r.Width * 0.27f, r.Y + r.Height * 0.22f, w, r.Height * 0.56f);
                g.FillRectangle(brush, r.X + r.Width * 0.55f, r.Y + r.Height * 0.22f, w, r.Height * 0.56f);
            }
        }

        public static void IconStop(Graphics g, RectangleF r, Color color)
        {
            using (SolidBrush brush = new SolidBrush(color))
                g.FillRectangle(brush, r.X + r.Width * 0.30f, r.Y + r.Height * 0.30f, r.Width * 0.40f, r.Height * 0.40f);
        }

        public static void IconEject(Graphics g, RectangleF r, Color color, float scale)
        {
            using (Pen pen = NewPen(color, scale))
            {
                float cx = r.X + r.Width / 2f;
                float top = r.Y + r.Height * 0.10f;
                float mid = r.Y + r.Height * 0.58f;
                g.DrawLine(pen, r.X + r.Width * 0.20f, mid, cx, top);
                g.DrawLine(pen, cx, top, r.X + r.Width * 0.80f, mid);
                g.DrawLine(pen, r.X + r.Width * 0.18f, r.Y + r.Height * 0.86f, r.X + r.Width * 0.82f, r.Y + r.Height * 0.86f);
            }
        }

        public static void IconChevronDown(Graphics g, RectangleF r, Color color, float scale)
        {
            using (Pen pen = NewPen(color, scale))
            {
                g.DrawLine(pen, r.X + r.Width * 0.22f, r.Y + r.Height * 0.38f, r.X + r.Width * 0.50f, r.Y + r.Height * 0.66f);
                g.DrawLine(pen, r.X + r.Width * 0.50f, r.Y + r.Height * 0.66f, r.X + r.Width * 0.78f, r.Y + r.Height * 0.38f);
            }
        }

        public static void IconChevronRight(Graphics g, RectangleF r, Color color, float scale)
        {
            using (Pen pen = NewPen(color, scale))
            {
                g.DrawLine(pen, r.X + r.Width * 0.38f, r.Y + r.Height * 0.20f, r.X + r.Width * 0.64f, r.Y + r.Height * 0.50f);
                g.DrawLine(pen, r.X + r.Width * 0.64f, r.Y + r.Height * 0.50f, r.X + r.Width * 0.38f, r.Y + r.Height * 0.80f);
            }
        }

        public static void IconClose(Graphics g, RectangleF r, Color color, float scale)
        {
            using (Pen pen = NewPen(color, scale))
            {
                g.DrawLine(pen, r.X + r.Width * 0.25f, r.Y + r.Height * 0.25f, r.X + r.Width * 0.75f, r.Y + r.Height * 0.75f);
                g.DrawLine(pen, r.X + r.Width * 0.75f, r.Y + r.Height * 0.25f, r.X + r.Width * 0.25f, r.Y + r.Height * 0.75f);
            }
        }

        public static void IconTrash(Graphics g, RectangleF r, Color color, float scale)
        {
            using (Pen pen = NewPen(color, scale))
            {
                g.DrawLine(pen, r.X + r.Width * 0.22f, r.Y + r.Height * 0.28f, r.X + r.Width * 0.78f, r.Y + r.Height * 0.28f);
                g.DrawLine(pen, r.X + r.Width * 0.38f, r.Y + r.Height * 0.16f, r.X + r.Width * 0.62f, r.Y + r.Height * 0.16f);
                g.DrawLine(pen, r.X + r.Width * 0.30f, r.Y + r.Height * 0.28f, r.X + r.Width * 0.36f, r.Y + r.Height * 0.84f);
                g.DrawLine(pen, r.X + r.Width * 0.70f, r.Y + r.Height * 0.28f, r.X + r.Width * 0.64f, r.Y + r.Height * 0.84f);
                g.DrawLine(pen, r.X + r.Width * 0.36f, r.Y + r.Height * 0.84f, r.X + r.Width * 0.64f, r.Y + r.Height * 0.84f);
            }
        }

        public static void IconDownload(Graphics g, RectangleF r, Color color, float scale)
        {
            using (Pen pen = NewPen(color, scale))
            {
                float cx = r.X + r.Width * 0.50f;
                g.DrawLine(pen, cx, r.Y + r.Height * 0.16f, cx, r.Y + r.Height * 0.62f);
                g.DrawLine(pen, r.X + r.Width * 0.30f, r.Y + r.Height * 0.44f, cx, r.Y + r.Height * 0.66f);
                g.DrawLine(pen, cx, r.Y + r.Height * 0.66f, r.X + r.Width * 0.70f, r.Y + r.Height * 0.44f);
                g.DrawLine(pen, r.X + r.Width * 0.20f, r.Y + r.Height * 0.82f, r.X + r.Width * 0.80f, r.Y + r.Height * 0.82f);
            }
        }

        public static void IconExternal(Graphics g, RectangleF r, Color color, float scale)
        {
            using (Pen pen = NewPen(color, scale))
            {
                g.DrawRectangle(pen, r.X + r.Width * 0.18f, r.Y + r.Height * 0.30f, r.Width * 0.50f, r.Height * 0.50f);
                g.DrawLine(pen, r.X + r.Width * 0.46f, r.Y + r.Height * 0.22f, r.X + r.Width * 0.82f, r.Y + r.Height * 0.22f);
                g.DrawLine(pen, r.X + r.Width * 0.82f, r.Y + r.Height * 0.22f, r.X + r.Width * 0.82f, r.Y + r.Height * 0.58f);
                g.DrawLine(pen, r.X + r.Width * 0.82f, r.Y + r.Height * 0.22f, r.X + r.Width * 0.52f, r.Y + r.Height * 0.52f);
            }
        }

        public static void IconRefresh(Graphics g, RectangleF r, Color color, float scale)
        {
            using (Pen pen = NewPen(color, scale))
            {
                RectangleF arcRect = new RectangleF(r.X + r.Width * 0.18f, r.Y + r.Height * 0.18f, r.Width * 0.64f, r.Height * 0.64f);
                g.DrawArc(pen, arcRect, 35f, 285f);
                float ax = arcRect.Right - arcRect.Width * 0.05f;
                float ay = arcRect.Y + arcRect.Height * 0.14f;
                g.DrawLine(pen, ax, ay, ax - r.Width * 0.15f, ay);
                g.DrawLine(pen, ax, ay, ax, ay + r.Height * 0.15f);
            }
        }

        public static void IconCheck(Graphics g, RectangleF r, Color color, float scale)
        {
            using (Pen pen = NewPen(color, scale * 1.2f))
            {
                g.DrawLine(pen, r.X + r.Width * 0.18f, r.Y + r.Height * 0.52f, r.X + r.Width * 0.42f, r.Y + r.Height * 0.76f);
                g.DrawLine(pen, r.X + r.Width * 0.42f, r.Y + r.Height * 0.76f, r.X + r.Width * 0.84f, r.Y + r.Height * 0.24f);
            }
        }

        public static void IconWarning(Graphics g, RectangleF r, Color color, float scale)
        {
            using (Pen pen = NewPen(color, scale))
            {
                g.DrawLine(pen, r.X + r.Width * 0.50f, r.Y + r.Height * 0.12f, r.X + r.Width * 0.90f, r.Y + r.Height * 0.84f);
                g.DrawLine(pen, r.X + r.Width * 0.90f, r.Y + r.Height * 0.84f, r.X + r.Width * 0.10f, r.Y + r.Height * 0.84f);
                g.DrawLine(pen, r.X + r.Width * 0.10f, r.Y + r.Height * 0.84f, r.X + r.Width * 0.50f, r.Y + r.Height * 0.12f);
                g.DrawLine(pen, r.X + r.Width * 0.50f, r.Y + r.Height * 0.36f, r.X + r.Width * 0.50f, r.Y + r.Height * 0.58f);
                g.DrawLine(pen, r.X + r.Width * 0.50f, r.Y + r.Height * 0.70f, r.X + r.Width * 0.50f, r.Y + r.Height * 0.71f);
            }
        }

        public static void IconInfo(Graphics g, RectangleF r, Color color, float scale)
        {
            using (Pen pen = NewPen(color, scale))
            {
                g.DrawEllipse(pen, r.X + r.Width * 0.12f, r.Y + r.Height * 0.12f, r.Width * 0.76f, r.Height * 0.76f);
                g.DrawLine(pen, r.X + r.Width * 0.50f, r.Y + r.Height * 0.42f, r.X + r.Width * 0.50f, r.Y + r.Height * 0.70f);
                g.DrawLine(pen, r.X + r.Width * 0.50f, r.Y + r.Height * 0.28f, r.X + r.Width * 0.50f, r.Y + r.Height * 0.29f);
            }
        }

        public static void IconNote(Graphics g, RectangleF r, Color color, float scale)
        {
            using (Pen pen = NewPen(color, scale))
            {
                g.DrawLine(pen, r.X + r.Width * 0.62f, r.Y + r.Height * 0.18f, r.X + r.Width * 0.62f, r.Y + r.Height * 0.66f);
                g.DrawLine(pen, r.X + r.Width * 0.62f, r.Y + r.Height * 0.18f, r.X + r.Width * 0.84f, r.Y + r.Height * 0.26f);
                g.DrawLine(pen, r.X + r.Width * 0.84f, r.Y + r.Height * 0.26f, r.X + r.Width * 0.84f, r.Y + r.Height * 0.58f);
                using (SolidBrush brush = new SolidBrush(color))
                {
                    g.FillEllipse(brush, r.X + r.Width * 0.36f, r.Y + r.Height * 0.62f, r.Width * 0.28f, r.Height * 0.22f);
                    g.FillEllipse(brush, r.X + r.Width * 0.68f, r.Y + r.Height * 0.54f, r.Width * 0.22f, r.Height * 0.20f);
                }
            }
        }

        public static void IconDisc(Graphics g, RectangleF r, Color color, float scale)
        {
            using (Pen pen = NewPen(color, scale))
            {
                g.DrawEllipse(pen, r.X + r.Width * 0.10f, r.Y + r.Height * 0.10f, r.Width * 0.80f, r.Height * 0.80f);
                g.DrawEllipse(pen, r.X + r.Width * 0.42f, r.Y + r.Height * 0.42f, r.Width * 0.16f, r.Height * 0.16f);
                g.DrawArc(pen, r.X + r.Width * 0.24f, r.Y + r.Height * 0.24f, r.Width * 0.52f, r.Height * 0.52f, 200f, 65f);
            }
        }

        private static Pen NewPen(Color color, float scale)
        {
            Pen pen = new Pen(color, Math.Max(1.2f, 1.6f * scale));
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            return pen;
        }
    }
}
