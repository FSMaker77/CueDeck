using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace CueDeck
{
    public class Hotspot
    {
        public string Action;
        public string Arg;
        public string Tooltip;
        public RectangleF Rect;
        public bool Enabled;

        public Hotspot()
        {
            Action = "";
            Arg = "";
            Tooltip = "";
            Enabled = true;
        }

        public bool Contains(PointF point)
        {
            return Enabled && Rect.Contains(point);
        }
    }

    public class CardLayout
    {
        public DiscEntry Entry;
        public RectangleF Rect;
        public RectangleF Cover;
        public RectangleF Favorite;
        public RectangleF Action;
    }

    public class ToastInfo
    {
        public string Title;
        public string Message;
        public string Kind;
        public string Action;
        public string Arg;
        public string ActionLabel;
        public DateTime ExpiresUtc;

        public ToastInfo()
        {
            Title = "";
            Message = "";
            Kind = "info";
            Action = "";
            Arg = "";
            ActionLabel = "";
        }
    }

    public class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Theme.Surface3; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Surface3; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Surface3; } }
        public override Color MenuItemBorder { get { return Theme.Border; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
        public override Color ToolStripBorder { get { return Theme.Border; } }
        public override Color ButtonSelectedHighlight { get { return Theme.Surface3; } }
        public override Color ButtonSelectedBorder { get { return Theme.Border; } }
        public override Color ButtonSelectedGradientBegin { get { return Theme.Surface3; } }
        public override Color ButtonSelectedGradientEnd { get { return Theme.Surface3; } }
        public override Color ButtonPressedGradientBegin { get { return Theme.AccentDim; } }
        public override Color ButtonPressedGradientEnd { get { return Theme.AccentDim; } }
    }

    public class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted2;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(Theme.Surface)) e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }
    }

    public class ImageCache : IDisposable
    {
        private readonly Dictionary<string, Image> _images = new Dictionary<string, Image>();
        private readonly List<string> _order = new List<string>();
        private const int MaxItems = 24;

        public Image Get(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            string key;
            try { key = path + "|" + new FileInfo(path).LastWriteTimeUtc.Ticks; }
            catch { key = path; }
            Image image;
            if (_images.TryGetValue(key, out image)) return image;
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (Image source = Image.FromStream(fs, true, true))
                {
                    image = new Bitmap(source);
                }
                _images[key] = image;
                _order.Add(key);
                Trim();
                return image;
            }
            catch
            {
                return null;
            }
        }

        private void Trim()
        {
            while (_order.Count > MaxItems)
            {
                string key = _order[0];
                _order.RemoveAt(0);
                Image image;
                if (_images.TryGetValue(key, out image))
                {
                    _images.Remove(key);
                    try { image.Dispose(); } catch { }
                }
            }
        }

        public void Dispose()
        {
            foreach (KeyValuePair<string, Image> pair in _images)
            {
                try { pair.Value.Dispose(); } catch { }
            }
            _images.Clear();
            _order.Clear();
        }
    }

    public static class UiUtil
    {
        public static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static string RelativeDate(DateTime utc)
        {
            if (utc == DateTime.MinValue) return "Mai";
            DateTime local = utc.ToLocalTime();
            DateTime now = DateTime.Now;
            TimeSpan diff = now - local;
            if (diff.TotalMinutes < 1) return "Adesso";
            if (diff.TotalMinutes < 60) return (int)diff.TotalMinutes + " min fa";
            if (local.Date == now.Date) return "Oggi, " + local.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (local.Date == now.Date.AddDays(-1)) return "Ieri, " + local.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (diff.TotalDays < 7) return (int)diff.TotalDays + " giorni fa";
            return local.ToString("dd MMM yyyy", CultureInfo.GetCultureInfo("it-IT"));
        }

        public static void DrawToggle(Graphics g, RectangleF rect, bool on, float scale)
        {
            Color bg = on ? Theme.Accent : Theme.Surface3;
            Theme.FillRound(g, rect, rect.Height / 2f, bg);
            float knob = rect.Height - 6f * scale;
            float x = on ? rect.Right - knob - 3f * scale : rect.X + 3f * scale;
            using (SolidBrush brush = new SolidBrush(on ? Color.White : Theme.Muted))
                g.FillEllipse(brush, x, rect.Y + 3f * scale, knob, knob);
        }

        public static void DrawProgress(Graphics g, RectangleF rect, int percent, float scale)
        {
            Theme.FillRound(g, rect, rect.Height / 2f, Theme.Surface3);
            float width = Math.Max(0, Math.Min(rect.Width, rect.Width * percent / 100f));
            if (width > 0.5f)
            {
                RectangleF fill = new RectangleF(rect.X, rect.Y, width, rect.Height);
                using (GraphicsPath path = Theme.RoundRect(fill, rect.Height / 2f))
                using (LinearGradientBrush brush = new LinearGradientBrush(rect, Theme.Accent, Theme.Teal, 0f))
                    g.FillPath(brush, path);
            }
        }

        public static void DrawSpinner(Graphics g, RectangleF rect, float angle, float scale)
        {
            using (Pen track = new Pen(Theme.Surface3, 3f * scale))
                g.DrawEllipse(track, rect);
            using (Pen pen = new Pen(Theme.Accent, 3f * scale))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawArc(pen, rect, angle, 90f);
            }
        }

        public static void DrawDashedRoundRect(Graphics g, RectangleF rect, float radius, Color color, float width)
        {
            using (GraphicsPath path = Theme.RoundRect(rect, radius))
            using (Pen pen = new Pen(color, width))
            {
                pen.DashStyle = DashStyle.Dash;
                pen.DashPattern = new float[] { 5f, 4f };
                g.DrawPath(pen, path);
            }
        }

        public static void DrawBadge(Graphics g, RectangleF rect, string text, Font font, Color bg, Color fg, float radius)
        {
            Theme.FillRound(g, rect, radius, bg);
            Theme.DrawText(g, text, font, fg, rect, StringAlignment.Center, StringAlignment.Center);
        }

        public static void DrawIconCircle(Graphics g, RectangleF rect, Color bg, Color border)
        {
            Theme.FillRound(g, rect, rect.Width / 2f, bg);
            if (border.A > 0) Theme.StrokeRound(g, rect, rect.Width / 2f, border, 1f);
        }

        public static bool Contains(RectangleF rect, PointF point)
        {
            return point.X >= rect.X && point.X <= rect.Right && point.Y >= rect.Y && point.Y <= rect.Bottom;
        }

        public static string ShortenPath(string path, int maxChars)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try
            {
                string full = Path.GetFullPath(path);
                return TextUtil.TruncateMiddle(full, maxChars);
            }
            catch { return TextUtil.TruncateMiddle(path, maxChars); }
        }

        public static void OpenFolder(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                if (File.Exists(path))
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else if (Directory.Exists(path))
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex) { Logger.Error("Open folder " + path, ex); }
        }

        public static void OpenUrl(string url)
        {
            try { System.Diagnostics.Process.Start(url); }
            catch (Exception ex) { Logger.Error("Open URL " + url, ex); }
        }
    }
}
