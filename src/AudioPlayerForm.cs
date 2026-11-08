using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CueDeck
{
    public class AudioPlayerForm : Form
    {
        private class PlayerHotspot
        {
            public string Action;
            public string Arg;
            public RectangleF Rect;

            public bool Contains(PointF p)
            {
                return p.X >= Rect.X && p.X <= Rect.Right && p.Y >= Rect.Y && p.Y <= Rect.Bottom;
            }
        }

        private readonly CueSheet _sheet;
        private readonly List<CueTrack> _tracks;
        private readonly List<PlayerHotspot> _hotspots = new List<PlayerHotspot>();
        private MciPlayer _player;
        private System.Windows.Forms.Timer _timer;
        private CancellationTokenSource _loadCts;
        private int _index;
        private float _trackScroll;
        private float _scale = 1f;
        private bool _loading;
        private int _loadProgress;
        private string _status = "";
        private string _wavPath;
        private string _mode = "stopped";
        private int _positionMs;
        private int _lengthMs;
        private bool _autoAdvance;
        private string _error = "";
        private string _hover = "";

        private RectangleF _coverRect;
        private RectangleF _playRect;
        private RectangleF _prevRect;
        private RectangleF _nextRect;
        private RectangleF _stopRect;
        private RectangleF _progressRect;
        private RectangleF _trackViewport;
        private float _trackContentH;
        private float _trackRowH;
        private RectangleF _externalRect;

        public AudioPlayerForm(CueSheet sheet, int startTrackNumber)
        {
            _sheet = sheet;
            _tracks = sheet != null ? sheet.AudioTracks : new List<CueTrack>();
            _index = 0;
            if (startTrackNumber > 0)
            {
                for (int i = 0; i < _tracks.Count; i++)
                {
                    if (_tracks[i].Number == startTrackNumber) { _index = i; break; }
                }
            }
            InitializeWindow();
            UpdateLayout();
            if (_tracks.Count > 0) _lengthMs = (int)(_tracks[_index].Duration.TotalMilliseconds);
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 250;
            _timer.Tick += delegate { OnTick(); };
        }

        private void InitializeWindow()
        {
            _scale = Math.Max(1f, DeviceDpi / 96f);
            Text = "CueDeck · CD audio";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            KeyPreview = true;
            ClientSize = new Size((int)S(700), (int)S(570));
            MinimumSize = new Size((int)S(640), (int)S(520));
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) Icon = icon;
            }
            catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int one = 1;
                int result = DwmSetWindowAttribute(Handle, 20, ref one, 4);
                if (result != 0) DwmSetWindowAttribute(Handle, 19, ref one, 4);
                int caption = 0x00150F0C;
                int textColor = 0x00FCF7F4;
                DwmSetWindowAttribute(Handle, 35, ref caption, 4);
                DwmSetWindowAttribute(Handle, 36, ref textColor, 4);
            }
            catch { }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private float S(float value)
        {
            return value * _scale;
        }

        private Font Body(float px)
        {
            return Theme.Font(px * _scale, FontStyle.Regular);
        }

        private Font Semi(float px)
        {
            return Theme.DisplayFont(px * _scale, FontStyle.Bold);
        }

        private Font Mono(float px)
        {
            return Theme.MonoFont(px * _scale, FontStyle.Regular);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_tracks.Count > 0) PlayTrack(_index);
            else _status = "Nessuna traccia audio in questo disco.";
            _timer.Start();
            Invalidate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_loadCts != null)
            {
                try { _loadCts.Cancel(); } catch { }
            }
            if (_timer != null) _timer.Stop();
            if (_player != null)
            {
                try { _player.Dispose(); } catch { }
                _player = null;
            }
            base.OnFormClosing(e);
        }

        private void OnTick()
        {
            if (_loading)
            {
                Invalidate();
                return;
            }
            if (_player != null && _player.IsOpen)
            {
                string mode = _player.GetMode();
                int position = _player.GetPosition();
                bool changed = !string.Equals(mode, _mode, StringComparison.Ordinal) || position != _positionMs;
                _mode = mode;
                _positionMs = position;
                if (string.Equals(mode, "stopped", StringComparison.Ordinal) && _autoAdvance)
                {
                    _autoAdvance = false;
                    if (_index + 1 < _tracks.Count) PlayTrack(_index + 1);
                    else _mode = "stopped";
                }
                if (changed) Invalidate();
            }
            else if (_loading)
            {
                Invalidate();
            }
        }

        private void PlayTrack(int index)
        {
            if (index < 0 || index >= _tracks.Count)
            {
                _autoAdvance = false;
                return;
            }
            if (_loadCts != null)
            {
                try { _loadCts.Cancel(); } catch { }
                try { _loadCts.Dispose(); } catch { }
            }
            _loadCts = new CancellationTokenSource();
            CancellationTokenSource localCts = _loadCts;
            if (_player != null)
            {
                _player.Close();
            }
            _index = index;
            _loading = true;
            _loadProgress = 0;
            _error = "";
            _wavPath = null;
            _positionMs = 0;
            _mode = "loading";
            _autoAdvance = false;
            CueTrack track = _tracks[index];
            _lengthMs = (int)track.Duration.TotalMilliseconds;
            _status = "Preparo " + track.DisplayTitle + "...";
            Invalidate();
            Progress<int> progress = new Progress<int>(delegate(int percent)
            {
                _loadProgress = percent;
                _status = "Estraggo " + track.DisplayTitle + "...";
                Invalidate();
            });

            Task.Run(delegate
            {
                try
                {
                    string path = AudioTools.EnsurePreviewWave(_sheet, track, progress, localCts.Token);
                    PostUi(delegate { OnTrackReady(path, track); });
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Logger.Error("Audio preview", ex);
                    PostUi(delegate
                    {
                        _loading = false;
                        _error = ex.Message;
                        _status = "Impossibile preparare la traccia audio.";
                        Invalidate();
                    });
                }
            });
        }

        private void OnTrackReady(string path, CueTrack track)
        {
            if (IsDisposed) return;
            _loading = false;
            _wavPath = path;
            _status = "";
            if (_player == null) _player = new MciPlayer();
            bool opened = _player.Open(path);
            if (!opened)
            {
                _error = _player.LastError;
                _status = "Impossibile riprodurre la traccia.";
                Invalidate();
                return;
            }
            int length = _player.GetLength();
            if (length > 0) _lengthMs = length;
            _player.SetVolume(85);
            bool played = _player.Play();
            if (!played)
            {
                _error = _player.LastError;
                _status = "Impossibile avviare la riproduzione.";
                Invalidate();
                return;
            }
            _mode = "playing";
            _autoAdvance = true;
            _error = "";
            Invalidate();
        }

        private void PostUi(MethodInvoker action)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch { }
        }

        private void TogglePlay()
        {
            if (_loading) return;
            if (_player == null || !_player.IsOpen)
            {
                if (!string.IsNullOrEmpty(_wavPath)) OnTrackReady(_wavPath, _tracks[_index]);
                else PlayTrack(_index);
                return;
            }
            string mode = _player.GetMode();
            if (mode == "playing")
            {
                _player.Pause();
                _mode = "paused";
                _autoAdvance = false;
            }
            else if (mode == "paused")
            {
                _player.Resume();
                _mode = "playing";
                _autoAdvance = true;
            }
            else
            {
                _player.Seek(0);
                _player.Play();
                _mode = "playing";
                _positionMs = 0;
                _autoAdvance = true;
            }
            Invalidate();
        }

        private void StopPlayback()
        {
            if (_player != null && _player.IsOpen)
            {
                _player.Stop();
                _player.Seek(0);
            }
            _positionMs = 0;
            _mode = "stopped";
            _autoAdvance = false;
            Invalidate();
        }

        private void SeekAt(float x)
        {
            if (_lengthMs <= 0) return;
            float t = (x - _progressRect.X) / Math.Max(1f, _progressRect.Width);
            t = Math.Max(0f, Math.Min(1f, t));
            int ms = (int)(_lengthMs * t);
            if (_player != null && _player.IsOpen)
            {
                bool wasPlaying = _player.GetMode() == "playing";
                _player.Seek(ms);
                if (wasPlaying) _player.Play();
                _positionMs = ms;
                _mode = wasPlaying ? "playing" : _player.GetMode();
            }
            else
            {
                _positionMs = ms;
            }
            Invalidate();
        }

        private void UpdateLayout()
        {
            _scale = Math.Max(1f, DeviceDpi / 96f);
            _coverRect = new RectangleF(S(26), S(88), S(146), S(146));
            _playRect = new RectangleF(ClientSize.Width / 2f - S(29), S(256), S(58), S(58));
            _prevRect = new RectangleF(_playRect.X - S(58), S(263), S(42), S(42));
            _nextRect = new RectangleF(_playRect.Right + S(16), S(263), S(42), S(42));
            _stopRect = new RectangleF(_nextRect.Right + S(12), S(266), S(36), S(36));
            _progressRect = new RectangleF(S(26), S(340), ClientSize.Width - S(52), S(6));
            _trackRowH = S(31);
            _trackViewport = new RectangleF(S(26), S(390), ClientSize.Width - S(52), ClientSize.Height - S(410));
            _externalRect = new RectangleF(S(26), S(150), S(180), S(32));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateLayout();
            Invalidate();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            UpdateLayout();
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            _scale = Math.Max(1f, DeviceDpi / 96f);
            UpdateLayout();
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            _hotspots.Clear();
            using (SolidBrush bg = new SolidBrush(Theme.Bg)) g.FillRectangle(bg, ClientRectangle);
            using (SolidBrush top = new SolidBrush(Theme.TopBar)) g.FillRectangle(top, new RectangleF(0, 0, ClientSize.Width, S(64)));
            using (Pen border = new Pen(Theme.BorderSoft, 1f)) g.DrawLine(border, 0, S(64), ClientSize.Width, S(64));

            Theme.DrawText(g, "CD AUDIO", Semi(9.5f), Theme.Teal, new RectangleF(S(26), S(12), S(300), S(14)), StringAlignment.Near, StringAlignment.Center);
            string title = _sheet != null ? _sheet.Title : "CD audio";
            Theme.DrawText(g, title, Semi(20f), Theme.Text, new RectangleF(S(26), S(27), ClientSize.Width - S(52), S(28)), StringAlignment.Near, StringAlignment.Center);
            string performer = _sheet != null ? _sheet.Performer : "";
            if (!string.IsNullOrEmpty(performer))
                Theme.DrawText(g, performer, Body(11f), Theme.Muted, new RectangleF(S(26), S(52), ClientSize.Width - S(52), S(14)), StringAlignment.Near, StringAlignment.Center);

            Theme.DrawCover(g, _coverRect, _sheet != null ? _sheet.SourcePath : "audio", _sheet != null ? _sheet.Title : "CD audio", "CD AUDIO", null, _scale);

            CueTrack current = _tracks.Count > 0 ? _tracks[_index] : null;
            float infoX = S(192);
            if (current != null)
            {
                Theme.DrawText(g, "TRACCIA " + current.Number.ToString("00") + " / " + (_sheet != null ? _sheet.Tracks.Count : _tracks.Count).ToString("00"), Semi(9.5f), Theme.Muted2,
                    new RectangleF(infoX, S(94), ClientSize.Width - infoX - S(26), S(14)), StringAlignment.Near, StringAlignment.Center);
                Theme.DrawText(g, current.DisplayTitle, Semi(17f), Theme.Text,
                    new RectangleF(infoX, S(112), ClientSize.Width - infoX - S(26), S(26)), StringAlignment.Near, StringAlignment.Center);
                Theme.DrawText(g, _loading ? _status : (string.IsNullOrEmpty(_error) ? StateLabel() : _error),
                    Body(10.8f), _error.Length > 0 ? Theme.Red : Theme.Muted,
                    new RectangleF(infoX, S(142), ClientSize.Width - infoX - S(26), S(34)), StringAlignment.Near, StringAlignment.Center);
            }

            if (_loading)
            {
                RectangleF bar = new RectangleF(S(26), S(218), ClientSize.Width - S(52), S(6));
                UiUtil.DrawProgress(g, bar, _loadProgress, _scale);
                Theme.DrawText(g, "Estrazione audio in corso...", Body(10f), Theme.Muted,
                    new RectangleF(S(26), S(228), ClientSize.Width - S(52), S(16)), StringAlignment.Near, StringAlignment.Center);
            }

            DrawCircleButton(g, _prevRect, "prev", Hover("prev", ""), false);
            AddHotspot("prev", "", _prevRect, _tracks.Count > 1);
            DrawCircleButton(g, _nextRect, "next", Hover("next", ""), false);
            AddHotspot("next", "", _nextRect, _tracks.Count > 1);
            bool isPlaying = _mode == "playing";
            DrawCircleButton(g, _playRect, isPlaying ? "pause" : "play", Hover("play", ""), true);
            AddHotspot("play", "", _playRect, current != null && !_loading);
            DrawCircleButton(g, _stopRect, "stop", Hover("stop", ""), false);
            AddHotspot("stop", "", _stopRect, current != null && !_loading);

            Theme.FillRound(g, _progressRect, S(3), Theme.Surface3);
            float pct = _lengthMs > 0 ? Math.Max(0f, Math.Min(1f, _positionMs / (float)_lengthMs)) : 0f;
            if (pct > 0)
            {
                RectangleF fill = new RectangleF(_progressRect.X, _progressRect.Y, _progressRect.Width * pct, _progressRect.Height);
                using (GraphicsPath path = Theme.RoundRect(fill, S(3)))
                using (LinearGradientBrush brush = new LinearGradientBrush(_progressRect, Theme.Accent, Theme.Teal, 0f))
                    g.FillPath(brush, path);
            }
            RectangleF knob = new RectangleF(_progressRect.X + _progressRect.Width * pct - S(5), _progressRect.Y - S(4), S(10), S(14));
            using (SolidBrush knobBrush = new SolidBrush(Theme.Text)) g.FillEllipse(knobBrush, knob);
            AddHotspot("seek", "", new RectangleF(_progressRect.X - S(6), _progressRect.Y - S(10), _progressRect.Width + S(12), S(26)), true);
            Theme.DrawText(g, FormatTime(_positionMs), Mono(10f), Theme.Muted,
                new RectangleF(S(26), S(352), S(70), S(16)), StringAlignment.Near, StringAlignment.Center);
            Theme.DrawText(g, FormatTime(_lengthMs), Mono(10f), Theme.Muted,
                new RectangleF(ClientSize.Width - S(96), S(352), S(70), S(16)), StringAlignment.Far, StringAlignment.Center);

            if (!string.IsNullOrEmpty(_error) && !string.IsNullOrEmpty(_wavPath))
            {
                _externalRect = new RectangleF(S(192), S(178), S(180), S(30));
                DrawOutline(g, _externalRect, "Apri con Windows", "external", Hover("external", ""));
                AddHotspot("external", "", _externalRect, true);
            }

            using (Pen line = new Pen(Theme.BorderSoft, 1f))
                g.DrawLine(line, S(26), S(376), ClientSize.Width - S(26), S(376));
            string tracksLabel = "TRACCE  ·  " + _tracks.Count + "  ·  " + TextUtil.FormatDuration(_sheet != null ? _sheet.TotalDuration : TimeSpan.Zero);
            Theme.DrawText(g, tracksLabel, Semi(9.5f), Theme.Muted2,
                new RectangleF(S(26), S(366), ClientSize.Width - S(52), S(14)), StringAlignment.Near, StringAlignment.Center);

            RenderTrackList(g);
            if (_hotspots.Count == 0)
            {
                // Keep the form interactive even when there is no content.
            }
        }

        private string StateLabel()
        {
            if (_loading) return _status;
            if (_tracks.Count == 0) return "Nessuna traccia audio.";
            if (_mode == "paused") return "In pausa";
            if (_mode == "playing") return "Riproduzione in corso";
            if (_mode == "stopped") return "Pronto";
            return "Pronto";
        }

        private void RenderTrackList(Graphics g)
        {
            _trackContentH = _tracks.Count * _trackRowH;
            _trackScroll = UiUtil.Clamp(_trackScroll, 0, Math.Max(0, _trackContentH - _trackViewport.Height));
            GraphicsState state = g.Save();
            try
            {
                g.SetClip(_trackViewport);
                for (int i = 0; i < _tracks.Count; i++)
                {
                    CueTrack track = _tracks[i];
                    float y = _trackViewport.Y + i * _trackRowH - _trackScroll;
                    if (y + _trackRowH < _trackViewport.Y || y > _trackViewport.Bottom) continue;
                    bool active = i == _index;
                    bool hover = Hover("track", i.ToString());
                    RectangleF row = new RectangleF(_trackViewport.X - S(4), y, _trackViewport.Width + S(8), _trackRowH - S(2));
                    if (active) Theme.FillRound(g, row, S(7), Theme.Alpha(Theme.Accent, 32));
                    else if (hover) Theme.FillRound(g, row, S(7), Theme.Surface2);
                    Theme.DrawText(g, track.Number.ToString("00"), Mono(10f), active ? Theme.Accent : Theme.Muted2,
                        new RectangleF(_trackViewport.X, y, S(24), _trackRowH), StringAlignment.Near, StringAlignment.Center);
                    Theme.IconNote(g, new RectangleF(_trackViewport.X + S(27), y + S(8), S(15), S(15)), active ? Theme.Teal : Theme.Muted, _scale);
                    Theme.DrawText(g, track.DisplayTitle, Body(11.5f), active ? Theme.Text : Theme.Text2,
                        new RectangleF(_trackViewport.X + S(51), y, _trackViewport.Width - S(112), _trackRowH), StringAlignment.Near, StringAlignment.Center);
                    Theme.DrawText(g, TextUtil.FormatDuration(track.Duration), Mono(9.8f), Theme.Muted2,
                        new RectangleF(_trackViewport.Right - S(58), y, S(58), _trackRowH), StringAlignment.Far, StringAlignment.Center);
                    AddHotspot("track", i.ToString(), row, true);
                }
            }
            finally { g.Restore(state); }
            Theme.DrawScrollbar(g, _trackViewport, _trackContentH, _trackScroll);
        }

        private void DrawCircleButton(Graphics g, RectangleF rect, string icon, bool hover, bool accent)
        {
            if (accent)
            {
                Theme.FillRoundGradient(g, rect, rect.Width / 2f,
                    hover ? Theme.AccentHover : Theme.Accent,
                    hover ? Theme.Mix(Theme.AccentHover, Theme.Teal, 0.4f) : Theme.Mix(Theme.Accent, Theme.Teal, 0.25f), 30f);
            }
            else
            {
                Theme.FillRound(g, rect, rect.Width / 2f, hover ? Theme.Surface2 : Theme.Surface);
                Theme.StrokeRound(g, rect, rect.Width / 2f, hover ? Theme.Border : Theme.BorderSoft, 1f);
            }
            Color color = accent ? Color.White : (hover ? Theme.Text : Theme.Text2);
            if (icon == "play") Theme.IconPlay(g, new RectangleF(rect.X + rect.Width * 0.28f, rect.Y + rect.Height * 0.24f, rect.Width * 0.48f, rect.Height * 0.52f), color);
            else if (icon == "pause") Theme.IconPause(g, new RectangleF(rect.X + rect.Width * 0.29f, rect.Y + rect.Height * 0.25f, rect.Width * 0.42f, rect.Height * 0.50f), color);
            else if (icon == "stop") Theme.IconStop(g, new RectangleF(rect.X + rect.Width * 0.30f, rect.Y + rect.Height * 0.30f, rect.Width * 0.40f, rect.Height * 0.40f), color);
            else if (icon == "prev") DrawPrevNext(g, rect, color, false);
            else if (icon == "next") DrawPrevNext(g, rect, color, true);
        }

        private void DrawPrevNext(Graphics g, RectangleF r, Color color, bool next)
        {
            using (SolidBrush brush = new SolidBrush(color))
            {
                float cx = r.X + r.Width / 2f;
                float cy = r.Y + r.Height / 2f;
                float w = r.Width * 0.12f;
                float h = r.Height * 0.22f;
                if (next)
                {
                    g.FillPolygon(brush, new PointF[] { new PointF(cx - w, cy - h), new PointF(cx + w, cy), new PointF(cx - w, cy + h) });
                    g.FillRectangle(brush, cx + w, cy - h, w * 1.4f, h * 2f);
                }
                else
                {
                    g.FillPolygon(brush, new PointF[] { new PointF(cx + w, cy - h), new PointF(cx - w, cy), new PointF(cx + w, cy + h) });
                    g.FillRectangle(brush, cx - w * 2.4f, cy - h, w * 1.4f, h * 2f);
                }
            }
        }

        private void DrawOutline(Graphics g, RectangleF rect, string label, string icon, bool hover)
        {
            Theme.FillRound(g, rect, S(8), hover ? Theme.Surface2 : Theme.Surface);
            Theme.StrokeRound(g, rect, S(8), hover ? Theme.Accent : Theme.Border, 1f);
            RectangleF iconRect = new RectangleF(rect.X + S(12), rect.Y + S(8), S(14), S(14));
            Theme.IconExternal(g, iconRect, Theme.Text2, _scale);
            Theme.DrawText(g, label, Body(10.5f), Theme.Text2,
                new RectangleF(rect.X + S(32), rect.Y, rect.Width - S(38), rect.Height), StringAlignment.Near, StringAlignment.Center);
        }

        private string FormatTime(int milliseconds)
        {
            if (milliseconds < 0) milliseconds = 0;
            int totalSeconds = milliseconds / 1000;
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            return minutes + ":" + seconds.ToString("00");
        }

        private void AddHotspot(string action, string arg, RectangleF rect, bool enabled)
        {
            if (!enabled) return;
            PlayerHotspot h = new PlayerHotspot();
            h.Action = action;
            h.Arg = arg;
            h.Rect = rect;
            _hotspots.Add(h);
        }

        private bool Hover(string action, string arg)
        {
            return string.Equals(_hover, action + "\u0001" + (arg ?? ""), StringComparison.Ordinal);
        }

        private PlayerHotspot FindHotspot(Point point)
        {
            PointF p = new PointF(point.X, point.Y);
            for (int i = _hotspots.Count - 1; i >= 0; i--)
                if (_hotspots[i].Contains(p)) return _hotspots[i];
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            PlayerHotspot hit = FindHotspot(e.Location);
            string key = hit != null ? hit.Action + "\u0001" + hit.Arg : "";
            if (!string.Equals(key, _hover, StringComparison.Ordinal))
            {
                _hover = key;
                Cursor = hit != null ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover.Length > 0)
            {
                _hover = "";
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            PlayerHotspot hit = FindHotspot(e.Location);
            if (hit == null) return;
            if (hit.Action == "seek")
            {
                SeekAt(e.X);
                return;
            }
            if (hit.Action == "play") { TogglePlay(); return; }
            if (hit.Action == "stop") { StopPlayback(); return; }
            if (hit.Action == "prev") { PlayTrack(_index - 1); return; }
            if (hit.Action == "next") { PlayTrack(_index + 1); return; }
            if (hit.Action == "track")
            {
                int index;
                if (int.TryParse(hit.Arg, out index)) PlayTrack(index);
                return;
            }
            if (hit.Action == "external" && !string.IsNullOrEmpty(_wavPath))
            {
                try { Process.Start(_wavPath); } catch { }
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e.X < _trackViewport.X || e.X > _trackViewport.Right || e.Y < _trackViewport.Y || e.Y > _trackViewport.Bottom) return;
            float delta = e.Delta > 0 ? -S(90) : S(90);
            _trackScroll = UiUtil.Clamp(_trackScroll + delta, 0, Math.Max(0, _trackContentH - _trackViewport.Height));
            Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Space)
            {
                TogglePlay();
                return true;
            }
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            if (keyData == Keys.Left)
            {
                PlayTrack(_index - 1);
                return true;
            }
            if (keyData == Keys.Right)
            {
                PlayTrack(_index + 1);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
