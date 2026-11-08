using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace CueDeck
{
    public partial class MainForm
    {
        private class DetailAction
        {
            public string Label;
            public string Action;
            public string Icon;

            public DetailAction(string label, string action, string icon)
            {
                Label = label;
                Action = action;
                Icon = icon;
            }
        }

        private void RecalcLayout()
        {
            _scale = Math.Max(1f, DeviceDpi / 96f);
            float topH = S(70);
            float sideW = S(224);
            float pad = S(20);
            float gap = S(18);
            float detailW = S(348);
            _topRect = new RectangleF(0, 0, ClientSize.Width, topH);
            _sidebarRect = new RectangleF(0, topH, sideW, ClientSize.Height - topH);
            _detailRect = new RectangleF(ClientSize.Width - pad - detailW, topH + pad, detailW, ClientSize.Height - topH - pad * 2);
            float libX = sideW + pad;
            float libRight = _detailRect.X - gap;
            _libraryRect = new RectangleF(libX, topH + pad, Math.Max(S(240), libRight - libX), ClientSize.Height - topH - pad * 2);
            _searchRect = new RectangleF(sideW + pad, S(16), Math.Min(S(340), Math.Max(S(150), ClientSize.Width - sideW - pad - S(380))), S(38));
            _addRect = new RectangleF(ClientSize.Width - pad - S(138), S(16), S(138), S(38));
            _settingsRect = new RectangleF(_addRect.X - S(48), S(17), S(36), S(36));
            _sortRect = new RectangleF(_libraryRect.Right - S(142), _libraryRect.Y + S(5), S(142), S(32));
            _scanRect = new RectangleF(_sortRect.X - S(42), _libraryRect.Y + S(5), S(32), S(32));
        }

        private RectangleF GetPromptCardRect()
        {
            float w = Math.Min(S(500), ClientSize.Width - S(60));
            float h = S(236);
            return new RectangleF((ClientSize.Width - w) / 2f, (ClientSize.Height - h) / 2f, w, h);
        }

        private RectangleF GetPromptInputRect(RectangleF card)
        {
            return new RectangleF(card.X + S(28), card.Y + S(94), card.Width - S(56), S(36));
        }

        private void UpdateChildControls()
        {
            if (_searchBox == null) return;
            RecalcLayout();
            bool showSearch = !_settingsOpen && !_confirmOpen && !_promptOpen && !_busy && Visible;
            if (!showSearch)
            {
                _searchBox.Visible = false;
            }
            else
            {
                _searchBox.Visible = true;
                _searchBox.Font = Body(13.5f);
                int h = _searchBox.PreferredHeight;
                int top = (int)Math.Round(_searchRect.Y + (_searchRect.Height - h) / 2f);
                int x = (int)Math.Round(_searchRect.X + S(38));
                int w = (int)Math.Round(Math.Max(S(60), _searchRect.Width - S(52)));
                _searchBox.SetBounds(x, top, w, h);
                _searchBox.BringToFront();
                if (_searchBox.Focused && _placeholderActive)
                {
                    _placeholderActive = false;
                    _suppressSearchText = true;
                    _searchBox.Text = "";
                    _suppressSearchText = false;
                    _searchBox.ForeColor = Theme.Text;
                    _search = "";
                }
                else if (!_searchBox.Focused && _searchBox.Text.Length == 0) SetSearchPlaceholder();
            }

            if (_promptBox != null)
            {
                _promptBox.Visible = _promptOpen;
                if (_promptOpen)
                {
                    _promptBox.Font = Body(14f);
                    RectangleF card = GetPromptCardRect();
                    RectangleF input = GetPromptInputRect(card);
                    int h = _promptBox.PreferredHeight;
                    _promptBox.SetBounds(
                        (int)Math.Round(input.X + S(12)),
                        (int)Math.Round(input.Y + (input.Height - h) / 2f),
                        (int)Math.Round(input.Width - S(24)),
                        h);
                    _promptBox.BringToFront();
                }
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Entire surface is painted by OnPaint.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            _scale = Math.Max(1f, DeviceDpi / 96f);
            RecalcLayout();
            if (_promptOpen) UpdateChildControls();
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            _hotspots.Clear();
            _cards.Clear();

            using (SolidBrush background = new SolidBrush(Theme.Bg))
                g.FillRectangle(background, ClientRectangle);

            RenderTopBar(g);
            RenderSidebar(g);
            RenderLibrary(g);
            RenderDetail(g);

            if (_settingsOpen)
            {
                _hotspots.Clear();
                RenderSettings(g);
            }
            if (_promptOpen)
            {
                _hotspots.Clear();
                RenderPrompt(g);
            }
            if (_confirmOpen)
            {
                _hotspots.Clear();
                RenderConfirm(g);
            }
            if (_busy)
            {
                _hotspots.Clear();
                RenderBusy(g);
            }
            if (_toast != null) RenderToast(g);
            if (_dragOver) RenderDrag(g);
        }

        private void RenderTopBar(Graphics g)
        {
            using (SolidBrush brush = new SolidBrush(Theme.TopBar))
                g.FillRectangle(brush, _topRect);
            using (Pen border = new Pen(Theme.BorderSoft, 1f))
                g.DrawLine(border, 0, _topRect.Bottom - 0.5f, ClientSize.Width, _topRect.Bottom - 0.5f);

            RectangleF logo = new RectangleF(S(20), S(18), S(34), S(34));
            Theme.DrawLogo(g, logo);
            Theme.DrawText(g, "CueDeck", Semi(16f), Theme.Text, new RectangleF(S(64), S(13), S(260), S(26)), StringAlignment.Near, StringAlignment.Center);
            Theme.DrawText(g, "I tuoi CD virtuali, sempre pronti", Body(10.5f), Theme.Muted, new RectangleF(S(65), S(36), S(300), S(18)), StringAlignment.Near, StringAlignment.Center);

            bool searchFocused = _searchBox != null && _searchBox.Focused && !_placeholderActive;
            Theme.FillRound(g, _searchRect, S(10), Theme.Surface);
            Theme.StrokeRound(g, _searchRect, S(10), searchFocused ? Theme.Accent : Theme.Border, searchFocused ? 1.4f : 1f);
            Theme.IconSearch(g, new RectangleF(_searchRect.X + S(13), _searchRect.Y + S(11), S(16), S(16)), searchFocused ? Theme.Text2 : Theme.Muted, _scale);
            if (_searchBox == null || !_searchBox.Visible)
                Theme.DrawText(g, "Cerca un disco...", Body(13.5f), Theme.Muted,
                    new RectangleF(_searchRect.X + S(38), _searchRect.Y, _searchRect.Width - S(50), _searchRect.Height), StringAlignment.Near, StringAlignment.Center);
            AddHotspot("focusSearch", "", _searchRect, true);

            DrawAccentButton(g, _addRect, "Aggiungi", "plus", Hover("add", ""));
            AddHotspot("add", "", _addRect, true);

            bool settingsHover = Hover("settings", "");
            DrawIconButton(g, _settingsRect, "settings", settingsHover, false);
            AddHotspot("settings", "", _settingsRect, true);
        }

        private void RenderSidebar(Graphics g)
        {
            using (SolidBrush brush = new SolidBrush(Theme.Sidebar))
                g.FillRectangle(brush, _sidebarRect);
            using (Pen border = new Pen(Theme.BorderSoft, 1f))
                g.DrawLine(border, _sidebarRect.Right - 0.5f, _sidebarRect.Top, _sidebarRect.Right - 0.5f, _sidebarRect.Bottom);

            float x = S(12);
            float w = _sidebarRect.Width - S(24);
            float y = _sidebarRect.Y + S(22);
            Theme.DrawText(g, "LIBRERIA", Semi(10f), Theme.Muted2, new RectangleF(S(22), y, S(180), S(16)), StringAlignment.Near, StringAlignment.Center);
            y += S(26);

            string[] keys = new string[] { "all", "mounted", "favorites", "data", "audio" };
            string[] labels = new string[] { "Tutti i dischi", "Montati", "Preferiti", "CD dati", "CD audio" };
            int[] counts = new int[] { _countAll, _countMounted, _countFavorites, _countData, _countAudio };
            for (int i = 0; i < keys.Length; i++)
            {
                RectangleF item = new RectangleF(x, y, w, S(36));
                bool selected = _filter == keys[i];
                bool hover = Hover("nav", keys[i]);
                if (selected)
                {
                    Theme.FillRound(g, item, S(8), Theme.Alpha(Theme.Accent, 30));
                    Theme.FillRound(g, new RectangleF(item.X, item.Y + S(9), S(3), item.Height - S(18)), S(2), Theme.Accent);
                }
                else if (hover)
                {
                    Theme.FillRound(g, item, S(8), Theme.Surface2);
                }
                Color iconColor = selected ? Theme.Accent : (hover ? Theme.Text2 : Theme.Muted);
                RectangleF iconRect = new RectangleF(item.X + S(12), item.Y + S(10), S(16), S(16));
                if (keys[i] == "all") Theme.IconDisc(g, iconRect, iconColor, _scale);
                else if (keys[i] == "mounted")
                {
                    Theme.IconDisc(g, iconRect, iconColor, _scale);
                    using (SolidBrush dot = new SolidBrush(Theme.Green))
                        g.FillEllipse(dot, iconRect.Right - S(4), iconRect.Top + S(1), S(5), S(5));
                }
                else if (keys[i] == "favorites") Theme.DrawHeart(g, new RectangleF(iconRect.X + S(1), iconRect.Y + S(2), S(14), S(14)), iconColor, selected);
                else if (keys[i] == "data") Theme.IconDisc(g, iconRect, selected ? Theme.Accent : Theme.Blue, _scale);
                else Theme.IconNote(g, iconRect, selected ? Theme.Accent : Theme.Teal, _scale);

                Theme.DrawText(g, labels[i], selected ? Semi(13f) : Body(13f), selected ? Theme.Text : (hover ? Theme.Text2 : Theme.Text2),
                    new RectangleF(item.X + S(40), item.Y, item.Width - S(86), item.Height), StringAlignment.Near, StringAlignment.Center);
                if (counts[i] > 0)
                {
                    string countText = counts[i].ToString();
                    SizeF countSize = g.MeasureString(countText, Mono(10.5f));
                    RectangleF badge = new RectangleF(item.Right - countSize.Width - S(12), item.Y + S(10), countSize.Width + S(8), S(17));
                    Theme.FillRound(g, badge, S(5), selected ? Theme.Alpha(Theme.Accent, 65) : Theme.Surface3);
                    Theme.DrawText(g, countText, Mono(10.5f), selected ? Color.White : Theme.Muted, badge, StringAlignment.Center, StringAlignment.Center);
                }
                AddHotspot("nav", keys[i], item, true);
                y += S(38);
            }

            y += S(8);
            using (Pen line = new Pen(Theme.BorderSoft, 1f))
                g.DrawLine(line, S(20), y, _sidebarRect.Right - S(20), y);
            y += S(16);

            Theme.DrawText(g, "DRIVE VIRTUALI", Semi(10f), Theme.Muted2, new RectangleF(S(22), y, S(180), S(16)), StringAlignment.Near, StringAlignment.Center);
            y += S(24);

            float statusTop = _sidebarRect.Bottom - S(64);
            float maxRowsHeight = statusTop - y - S(10);
            int maxRows = Math.Max(0, Math.Min(4, (int)(maxRowsHeight / S(52))));
            int show = Math.Min(_mounted.Count, maxRows);
            if (_mounted.Count == 0)
            {
                RectangleF empty = new RectangleF(x, y, w, S(56));
                UiUtil.DrawDashedRoundRect(g, empty, S(8), Theme.Alpha(Theme.Border, 180), 1f);
                Theme.DrawText(g, "Nessun disco montato", Body(11.5f), Theme.Muted, new RectangleF(empty.X + S(10), empty.Y + S(7), empty.Width - S(20), S(18)), StringAlignment.Near, StringAlignment.Center);
                Theme.DrawText(g, "Trascina qui un file .cue/.bin", Body(10f), Theme.Muted2, new RectangleF(empty.X + S(10), empty.Y + S(26), empty.Width - S(20), S(16)), StringAlignment.Near, StringAlignment.Center);
            }
            else
            {
                for (int i = 0; i < show; i++)
                {
                    MountedDisc mount = _mounted[i];
                    RectangleF row = new RectangleF(x, y, w, S(46));
                    bool hover = Hover("driveOpen", mount.DriveLetter);
                    Theme.FillRound(g, row, S(8), hover ? Theme.Surface2 : Theme.Surface);
                    Theme.StrokeRound(g, row, S(8), Theme.BorderSoft, 1f);
                    RectangleF letter = new RectangleF(row.X + S(8), row.Y + S(8), S(30), S(30));
                    Theme.FillRoundGradient(g, letter, S(7), Theme.Accent, Theme.Mix(Theme.Accent, Theme.Teal, 0.5f), 45f);
                    Theme.DrawText(g, mount.DriveLetter, Semi(13f), Color.FromArgb(20, 20, 30),
                        letter, StringAlignment.Center, StringAlignment.Center);
                    Theme.DrawText(g, mount.Title, Body(11.5f), Theme.Text,
                        new RectangleF(row.X + S(46), row.Y + S(6), row.Width - S(82), S(18)), StringAlignment.Near, StringAlignment.Center);
                    Theme.DrawText(g, mount.FullMount ? "WinCDEmu" : "Montato", Body(9.5f), Theme.Muted2,
                        new RectangleF(row.X + S(46), row.Y + S(24), row.Width - S(82), S(14)), StringAlignment.Near, StringAlignment.Center);
                    RectangleF eject = new RectangleF(row.Right - S(30), row.Y + S(13), S(20), S(20));
                    if (Hover("eject", mount.DriveLetter))
                        Theme.FillRound(g, new RectangleF(eject.X - S(3), eject.Y - S(3), S(26), S(26)), S(6), Theme.Surface3);
                    Theme.IconEject(g, eject, hover ? Theme.Text2 : Theme.Muted, _scale);
                    AddHotspot("driveOpen", mount.DriveLetter, row, true);
                    AddHotspot("eject", mount.DriveLetter, new RectangleF(eject.X - S(4), eject.Y - S(4), S(28), S(28)), true);
                    y += S(52);
                }
                if (_mounted.Count > show)
                {
                    Theme.DrawText(g, "Vedi tutte le " + _mounted.Count + " unità", Body(10.5f), Theme.Accent,
                        new RectangleF(x + S(8), y, w, S(18)), StringAlignment.Near, StringAlignment.Center);
                    AddHotspot("openMounted", "", new RectangleF(x, y, w, S(20)), true);
                }
            }

            using (Pen line = new Pen(Theme.BorderSoft, 1f))
                g.DrawLine(line, S(20), statusTop, _sidebarRect.Right - S(20), statusTop);
            Theme.DrawText(g, "Cache " + TextUtil.FormatBytes(_cacheSize), Body(10.5f), Theme.Muted,
                new RectangleF(S(22), statusTop + S(8), S(180), S(16)), StringAlignment.Near, StringAlignment.Center);
            Color statusColor = _winCDEmuAvailable ? Theme.Green : Theme.Muted2;
            using (SolidBrush dot = new SolidBrush(statusColor))
                g.FillEllipse(dot, S(22), statusTop + S(29), S(6), S(6));
            Theme.DrawText(g, _winCDEmuAvailable ? "WinCDEmu pronto" : "WinCDEmu assente", Body(10.5f), Theme.Muted,
                new RectangleF(S(34), statusTop + S(24), S(170), S(16)), StringAlignment.Near, StringAlignment.Center);
            AddHotspot("openSettings", "", new RectangleF(S(12), statusTop, _sidebarRect.Width - S(24), S(52)), true);
        }

        private void RenderLibrary(Graphics g)
        {
            string title = FilterTitle();
            Theme.DrawText(g, title, Semi(20f), Theme.Text,
                new RectangleF(_libraryRect.X, _libraryRect.Y, Math.Max(S(80), _scanRect.X - _libraryRect.X - S(10)), S(28)), StringAlignment.Near, StringAlignment.Center);
            SizeF titleSize = g.MeasureString(title, Semi(20f));
            int count = !string.IsNullOrEmpty(_search) ? _visible.Count : FilterCount();
            string countText = count.ToString();
            SizeF countSize = g.MeasureString(countText, Mono(11f));
            float countX = _libraryRect.X + titleSize.Width + S(10);
            if (countX + countSize.Width + S(18) < _scanRect.X - S(6))
            {
                RectangleF countBadge = new RectangleF(countX, _libraryRect.Y + S(5), countSize.Width + S(14), S(20));
                Theme.FillRound(g, countBadge, S(6), Theme.Surface2);
                Theme.DrawText(g, countText, Mono(11f), Theme.Muted, countBadge, StringAlignment.Center, StringAlignment.Center);
            }
            string subtitle = _libraryRect.Width >= S(560)
                ? "Doppio clic per montare · tasto destro per le opzioni"
                : "Doppio clic per montare";
            Theme.DrawText(g, subtitle, Body(10.5f), Theme.Muted2,
                new RectangleF(_libraryRect.X, _libraryRect.Y + S(28), Math.Max(S(80), _scanRect.X - _libraryRect.X - S(10)), S(18)), StringAlignment.Near, StringAlignment.Center);

            bool sortHover = Hover("sort", "");
            Theme.FillRound(g, _sortRect, S(8), Theme.Surface);
            Theme.StrokeRound(g, _sortRect, S(8), sortHover ? Theme.Border : Theme.BorderSoft, 1f);
            Theme.DrawText(g, SortLabel(), Body(11.5f), Theme.Text2,
                new RectangleF(_sortRect.X + S(12), _sortRect.Y, _sortRect.Width - S(38), _sortRect.Height), StringAlignment.Near, StringAlignment.Center);
            Theme.IconChevronDown(g, new RectangleF(_sortRect.Right - S(25), _sortRect.Y + S(8), S(16), S(16)), Theme.Muted, _scale);
            AddHotspot("sort", "", _sortRect, true);

            bool scanHover = Hover("scan", "");
            Theme.FillRound(g, _scanRect, S(8), scanHover ? Theme.Surface2 : Theme.Surface);
            Theme.StrokeRound(g, _scanRect, S(8), scanHover ? Theme.Border : Theme.BorderSoft, 1f);
            Theme.IconFolder(g, new RectangleF(_scanRect.X + S(8), _scanRect.Y + S(8), S(16), S(16)), scanHover ? Theme.Text2 : Theme.Muted, _scale);
            AddHotspot("scan", "", _scanRect, true);

            _libViewport = new RectangleF(_libraryRect.X, _libraryRect.Y + S(52), _libraryRect.Width, _libraryRect.Height - S(52));
            if (_visible.Count == 0)
            {
                RenderEmptyLibrary(g);
                _libContentH = 0;
                return;
            }

            float gap = S(16);
            float minWidth = S(236);
            int cols = (int)Math.Floor((_libViewport.Width + gap) / (minWidth + gap));
            cols = UiUtil.Clamp(cols, 1, 5);
            float cardW = (_libViewport.Width - gap * (cols - 1)) / cols;
            float cardH = cardW + S(42);
            _columns = cols;
            _cardW = cardW;
            _cardH = cardH;
            int rows = (int)Math.Ceiling(_visible.Count / (double)cols);
            _libContentH = rows * cardH + Math.Max(0, rows - 1) * gap;
            _libScroll = UiUtil.Clamp(_libScroll, 0, Math.Max(0, _libContentH - _libViewport.Height));

            GraphicsState state = g.Save();
            try
            {
                g.SetClip(_libViewport);
                for (int i = 0; i < _visible.Count; i++)
                {
                    int row = i / cols;
                    int col = i % cols;
                    float x = _libViewport.X + col * (cardW + gap);
                    float y = _libViewport.Y + row * (cardH + gap) - _libScroll;
                    if (y + cardH < _libViewport.Y - 2 || y > _libViewport.Bottom + 2) continue;
                    RectangleF rect = new RectangleF(x, y, cardW, cardH);
                    RenderCard(g, _visible[i], rect, cardW);
                }
            }
            finally { g.Restore(state); }
            Theme.DrawScrollbar(g, _libViewport, _libContentH, _libScroll);
        }

        private void RenderEmptyLibrary(Graphics g)
        {
            RectangleF area = new RectangleF(_libViewport.X + S(12), _libViewport.Y + S(12), _libViewport.Width - S(24), Math.Min(_libViewport.Height - S(24), S(470)));
            if (area.Width < S(240)) area.Width = S(240);
            if (area.Height < S(280)) area.Height = S(280);
            UiUtil.DrawDashedRoundRect(g, area, S(16), Theme.Alpha(Theme.Border, 210), 1f);
            float cx = area.X + area.Width / 2f;
            bool emptyLibrary = _library.Data.Discs.Count == 0;
            RectangleF art = new RectangleF(cx - S(64), area.Y + S(34), S(128), S(128));
            Theme.DrawCover(g, art, "empty", "CueDeck", "CUE/BIN", null, _scale);
            string heading = emptyLibrary ? "La tua libreria è vuota" : "Nessun disco trovato";
            string subtitle = emptyLibrary
                ? "Trascina qui file .cue e .bin, oppure aggiungi una cartella.\nCueDeck monta la traccia dati come unità virtuale, senza driver."
                : "Prova a cambiare filtro o a cercare un altro titolo.";
            Theme.DrawText(g, heading, Semi(21f), Theme.Text, new RectangleF(area.X + S(20), art.Bottom + S(18), area.Width - S(40), S(30)), StringAlignment.Center, StringAlignment.Center);
            Theme.DrawTextWrap(g, subtitle, Body(12f), Theme.Muted, new RectangleF(area.X + S(70), art.Bottom + S(54), area.Width - S(140), S(50)), StringAlignment.Center);

            if (emptyLibrary)
            {
                float buttonW = S(150);
                float buttonH = S(38);
                float gap = S(12);
                RectangleF add = new RectangleF(cx - buttonW - gap / 2f, art.Bottom + S(116), buttonW, buttonH);
                RectangleF scan = new RectangleF(cx + gap / 2f, art.Bottom + S(116), buttonW, buttonH);
                DrawAccentButton(g, add, "Aggiungi file", "plus", Hover("emptyAdd", ""));
                DrawOutlineButton(g, scan, "Scansiona", "folder", Hover("emptyScan", ""));
                AddHotspot("emptyAdd", "", add, true);
                AddHotspot("emptyScan", "", scan, true);
            }
            else
            {
                float buttonW = S(160);
                RectangleF reset = new RectangleF(cx - buttonW / 2f, art.Bottom + S(120), buttonW, S(38));
                DrawAccentButton(g, reset, "Mostra tutti", "disc", Hover("resetFilters", ""));
                AddHotspot("resetFilters", "", reset, true);
            }
        }

        private void RenderCard(Graphics g, DiscEntry entry, RectangleF rect, float cardW)
        {
            bool selected = _selected != null && _selected.Id == entry.Id;
            bool hover = Hover("selectCard", entry.Id);
            bool mounted = FindMountForEntry(entry.Id) != null;
            string coverPath = GetCoverPath(entry);
            Image cover = _imageCache.Get(coverPath);
            CueSheet sheet = PeekSheet(entry);
            string error = "";
            if (sheet == null)
            {
                lock (_sheetLock)
                {
                    SheetCacheEntry cached;
                    if (_sheetCache.TryGetValue(LibraryStore.PathKey(entry.Path), out cached)) error = cached.Error;
                }
            }
            bool missing = !File.Exists(entry.Path);

            RectangleF shadow = new RectangleF(rect.X, rect.Y + S(2), rect.Width, rect.Height);
            Theme.FillRound(g, shadow, S(12), Color.FromArgb(30, 0, 0, 0));
            Color cardColor = selected ? Theme.Surface2 : (hover ? Theme.Surface2 : Theme.Surface);
            Theme.FillRound(g, rect, S(12), cardColor);
            Color borderColor = selected ? Theme.Accent : (mounted ? Theme.Alpha(Theme.Green, 150) : Theme.BorderSoft);
            Theme.StrokeRound(g, rect, S(12), borderColor, selected ? 1.6f : 1f);

            RectangleF coverRect = new RectangleF(rect.X + S(10), rect.Y + S(10), rect.Width - S(20), rect.Width - S(20));
            RectangleF coverClip = coverRect;
            Theme.DrawCover(g, coverClip, entry.Id, entry.Title, KindLabel(entry, sheet), cover, _scale);
            AddHotspot("selectCard", entry.Id, rect, true);
            CardLayout layout = new CardLayout();
            layout.Entry = entry;
            layout.Rect = rect;
            layout.Cover = coverRect;
            layout.Favorite = new RectangleF(coverRect.Right - S(34), coverRect.Y + S(8), S(26), S(26));
            layout.Action = new RectangleF(coverRect.Right - S(84), coverRect.Bottom - S(37), S(76), S(28));
            _cards.Add(layout);

            if (mounted)
            {
                string drive = GetDriveLetter(entry.Id);
                if (!string.IsNullOrEmpty(drive))
                {
                    RectangleF badge = new RectangleF(coverRect.X + S(8), coverRect.Y + S(34), S(37), S(21));
                    Theme.FillRound(g, badge, S(6), Theme.Alpha(Theme.Green, 225));
                    Theme.DrawText(g, drive + ":", Semi(10.5f), Color.FromArgb(9, 25, 17), badge, StringAlignment.Center, StringAlignment.Center);
                }
            }
            if (missing)
            {
                RectangleF badge = new RectangleF(coverRect.X + S(8), coverRect.Bottom - S(31), S(96), S(22));
                Theme.FillRound(g, badge, S(6), Theme.Alpha(Theme.Red, 225));
                Theme.DrawText(g, "File mancante", Semi(9.5f), Color.FromArgb(32, 8, 12), badge, StringAlignment.Center, StringAlignment.Center);
            }

            bool favoriteHover = Hover("favorite", entry.Id);
            RectangleF favorite = new RectangleF(coverRect.Right - S(34), coverRect.Y + S(8), S(26), S(26));
            UiUtil.DrawIconCircle(g, favorite, favoriteHover ? Theme.Alpha(Theme.Red, 230) : Color.FromArgb(120, 5, 7, 11), Color.FromArgb(55, 255, 255, 255));
            Theme.DrawHeart(g, new RectangleF(favorite.X + S(7), favorite.Y + S(7), S(12), S(12)), entry.Favorite ? Color.White : Color.FromArgb(220, 255, 255, 255), entry.Favorite);
            AddHotspot("favorite", entry.Id, favorite, true);

            string actionLabel;
            string actionIcon;
            if (missing)
            {
                actionLabel = "Non disp.";
                actionIcon = "warning";
            }
            else if (mounted)
            {
                actionLabel = "Apri";
                actionIcon = "folder";
            }
            else if (sheet != null && sheet.IsAudioOnly)
            {
                actionLabel = "Ascolta";
                actionIcon = "play";
            }
            else
            {
                actionLabel = "Monta";
                actionIcon = "disc";
            }
            RectangleF actionRect = new RectangleF(coverRect.Right - S(84), coverRect.Bottom - S(37), S(76), S(28));
            bool actionHover = Hover("cardAction", entry.Id);
            Color actionBg = actionHover && !missing ? Theme.Accent : Color.FromArgb(195, 7, 9, 14);
            Theme.FillRound(g, actionRect, S(7), actionBg);
            Theme.StrokeRound(g, actionRect, S(7), missing ? Theme.Alpha(Theme.Red, 150) : Color.FromArgb(70, 255, 255, 255), 1f);
            DrawMiniIcon(g, new RectangleF(actionRect.X + S(9), actionRect.Y + S(7), S(13), S(13)), actionIcon, missing ? Theme.Red : Color.White, _scale);
            Theme.DrawText(g, actionLabel, Semi(10.5f), missing ? Theme.Red : Color.White,
                new RectangleF(actionRect.X + S(25), actionRect.Y, actionRect.Width - S(28), actionRect.Height), StringAlignment.Near, StringAlignment.Center);
            if (!missing) AddHotspot("cardAction", entry.Id, actionRect, true);

            float textY = coverRect.Bottom + S(8);
            Theme.DrawText(g, entry.Title, Semi(13.8f), Theme.Text,
                new RectangleF(rect.X + S(12), textY, rect.Width - S(24), S(20)), StringAlignment.Near, StringAlignment.Center);
            string meta = CardMeta(entry, sheet, error, missing);
            Theme.DrawText(g, meta, Body(10.5f), missing || !string.IsNullOrEmpty(error) ? Theme.Red : Theme.Muted,
                new RectangleF(rect.X + S(12), textY + S(21), rect.Width - S(24), S(16)), StringAlignment.Near, StringAlignment.Center);
        }

        private string KindLabel(DiscEntry entry, CueSheet sheet)
        {
            if (sheet != null) return sheet.KindLabel;
            if (entry.Kind == DiscKinds.Iso) return "ISO";
            if (entry.Kind == DiscKinds.Bin) return "BIN";
            return "CUE/BIN";
        }

        private string CardMeta(DiscEntry entry, CueSheet sheet, string error, bool missing)
        {
            if (missing) return "File immagine non trovato";
            if (!string.IsNullOrEmpty(error)) return "Impossibile leggere il disco";
            long size = GetDisplaySize(entry);
            if (sheet == null) return KindLabel(entry, null) + (size > 0 ? " · " + TextUtil.FormatBytes(size) : "");
            string meta = sheet.KindLabel;
            if (!string.IsNullOrEmpty(sheet.TrackSummary)) meta += " · " + sheet.TrackSummary;
            if (size > 0) meta += " · " + TextUtil.FormatBytes(size);
            return meta;
        }

        private void RenderDetail(Graphics g)
        {
            Theme.FillRound(g, _detailRect, S(14), Theme.Surface);
            Theme.StrokeRound(g, _detailRect, S(14), Theme.BorderSoft, 1f);
            if (_selected == null)
            {
                RenderDetailEmpty(g);
                return;
            }

            DiscEntry entry = _selected;
            float pad = S(16);
            float coverH = _detailRect.Height < S(600) ? S(112) : Math.Min(S(190), Math.Max(S(144), _detailRect.Height * 0.255f));
            _coverRect = new RectangleF(_detailRect.X + pad, _detailRect.Y + pad, _detailRect.Width - pad * 2, coverH);
            Theme.DrawCover(g, _coverRect, entry.Id, entry.Title, KindLabel(entry, _selectedSheet), _imageCache.Get(GetCoverPath(entry)), _scale);

            bool favoriteHover = Hover("detailFavorite", "");
            RectangleF favorite = new RectangleF(_coverRect.Right - S(36), _coverRect.Y + S(9), S(27), S(27));
            UiUtil.DrawIconCircle(g, favorite, favoriteHover ? Theme.Alpha(Theme.Red, 230) : Color.FromArgb(120, 5, 7, 11), Color.FromArgb(55, 255, 255, 255));
            Theme.DrawHeart(g, new RectangleF(favorite.X + S(7), favorite.Y + S(7), S(13), S(13)), entry.Favorite ? Color.White : Color.FromArgb(220, 255, 255, 255), entry.Favorite);
            AddHotspot("detailFavorite", "", favorite, true);

            MountedDisc mount = FindMountForEntry(entry.Id);
            if (mount != null)
            {
                RectangleF badge = new RectangleF(_coverRect.X + S(9), _coverRect.Bottom - S(33), S(86), S(23));
                Theme.FillRound(g, badge, S(6), Theme.Alpha(Theme.Green, 230));
                Theme.DrawText(g, "Montato " + mount.DriveLetter + ":", Semi(10f), Color.FromArgb(9, 25, 17), badge, StringAlignment.Center, StringAlignment.Center);
            }

            float y = _coverRect.Bottom + S(12);
            Theme.DrawText(g, entry.Title, Semi(16.5f), Theme.Text,
                new RectangleF(_detailRect.X + pad, y, _detailRect.Width - pad * 2, S(22)), StringAlignment.Near, StringAlignment.Center);
            y += S(24);
            string subtitle = "";
            if (_selectedSheet != null && !string.IsNullOrEmpty(_selectedSheet.Performer)) subtitle = _selectedSheet.Performer;
            if (!string.IsNullOrEmpty(subtitle))
            {
                Theme.DrawText(g, subtitle, Body(11f), Theme.Text2,
                    new RectangleF(_detailRect.X + pad, y, _detailRect.Width - pad * 2, S(16)), StringAlignment.Near, StringAlignment.Center);
                y += S(18);
            }
            long size = GetDisplaySize(entry);
            string meta = KindLabel(entry, _selectedSheet);
            if (_selectedSheet != null && !string.IsNullOrEmpty(_selectedSheet.TrackSummary)) meta += " · " + _selectedSheet.TrackSummary;
            if (size > 0) meta += " · " + TextUtil.FormatBytes(size);
            Theme.DrawText(g, meta, Body(10.8f), Theme.Muted,
                new RectangleF(_detailRect.X + pad, y, _detailRect.Width - pad * 2, S(16)), StringAlignment.Near, StringAlignment.Center);
            y += S(20);

            Color noteColor;
            string note = DetailNote(out noteColor);
            if (!string.IsNullOrEmpty(note))
            {
                Theme.DrawText(g, note, Body(10.3f), noteColor,
                    new RectangleF(_detailRect.X + pad, y, _detailRect.Width - pad * 2, S(16)), StringAlignment.Near, StringAlignment.Center);
                y += S(18);
            }

            y += S(6);
            string primaryLabel;
            string primaryIcon;
            int primaryStyle;
            bool primaryEnabled;
            GetPrimaryUi(entry, mount, out primaryLabel, out primaryIcon, out primaryStyle, out primaryEnabled);
            RectangleF primary = new RectangleF(_detailRect.X + pad, y, _detailRect.Width - pad * 2, S(42));
            if (primaryStyle == 0) DrawAccentButton(g, primary, primaryLabel, primaryIcon, Hover("detailPrimary", ""));
            else if (primaryStyle == 1) DrawOutlineButton(g, primary, primaryLabel, primaryIcon, Hover("detailPrimary", ""), true);
            else DrawDisabledButton(g, primary, primaryLabel, primaryIcon);
            if (primaryEnabled) AddHotspot("detailPrimary", "", primary, true);
            y = primary.Bottom + S(8);

            List<DetailAction> actions = BuildDetailActions(entry, _selectedSheet, mount);
            if (actions.Count > 0)
            {
                float gap = S(8);
                float width = (_detailRect.Width - pad * 2 - gap * (actions.Count - 1)) / actions.Count;
                for (int i = 0; i < actions.Count; i++)
                {
                    RectangleF button = new RectangleF(_detailRect.X + pad + i * (width + gap), y, width, S(32));
                    DrawOutlineButton(g, button, actions[i].Label, actions[i].Icon, Hover(actions[i].Action, ""), false);
                    AddHotspot(actions[i].Action, actions[i].Action == "downloadWinCDEmu" ? "" : "", button, true);
                    if (actions[i].Action == "detailFullMount" && !_winCDEmuAvailable)
                    {
                        // Should not happen, but keep the command safe.
                    }
                }
                y += S(40);
            }

            using (Pen line = new Pen(Theme.BorderSoft, 1f))
                g.DrawLine(line, _detailRect.X + pad, y, _detailRect.Right - pad, y);
            y += S(12);

            int trackCount = _selectedSheet != null ? _selectedSheet.Tracks.Count : 0;
            string trackLabel = "TRACCE";
            if (trackCount > 0)
                trackLabel += "  ·  " + trackCount + "  ·  " + TextUtil.FormatDuration(_selectedSheet.TotalDuration);
            Theme.DrawText(g, trackLabel, Semi(10f), Theme.Muted2,
                new RectangleF(_detailRect.X + pad, y, _detailRect.Width - pad * 2, S(16)), StringAlignment.Near, StringAlignment.Center);
            y += S(22);

            float footerY = _detailRect.Bottom - S(36);
            _trackViewport = new RectangleF(_detailRect.X + pad, y, _detailRect.Width - pad * 2, Math.Max(S(36), footerY - y - S(6)));
            RenderTracks(g, _selectedSheet, _trackViewport);

            RectangleF footer = new RectangleF(_detailRect.X + pad, footerY, _detailRect.Width - pad * 2, S(24));
            Theme.IconFolder(g, new RectangleF(footer.X, footer.Y + S(4), S(14), S(14)), Hover("detailReveal", "") ? Theme.Text2 : Theme.Muted2, _scale);
            Theme.DrawText(g, UiUtil.ShortenPath(entry.Path, 44), Body(9.8f), Hover("detailReveal", "") ? Theme.Text2 : Theme.Muted,
                new RectangleF(footer.X + S(22), footer.Y, footer.Width - S(22), footer.Height), StringAlignment.Near, StringAlignment.Center);
            AddHotspot("detailReveal", "", footer, true);
        }

        private void RenderDetailEmpty(Graphics g)
        {
            float centerY = _detailRect.Y + _detailRect.Height * 0.40f;
            RectangleF art = new RectangleF(_detailRect.X + _detailRect.Width / 2f - S(42), centerY - S(90), S(84), S(84));
            Theme.DrawCover(g, art, "detail-empty", "CueDeck", "", null, _scale);
            Theme.DrawText(g, "Seleziona un disco", Semi(16f), Theme.Text,
                new RectangleF(_detailRect.X + S(20), centerY + S(4), _detailRect.Width - S(40), S(24)), StringAlignment.Center, StringAlignment.Center);
            Theme.DrawTextWrap(g, "Qui vedrai copertina, tracce e comandi.\nDoppio clic su una scheda per montarla.",
                Body(11.5f), Theme.Muted, new RectangleF(_detailRect.X + S(34), centerY + S(34), _detailRect.Width - S(68), S(46)), StringAlignment.Center);
        }

        private void GetPrimaryUi(DiscEntry entry, MountedDisc mount, out string label, out string icon, out int style, out bool enabled)
        {
            label = "Monta disco";
            icon = "disc";
            style = 0;
            enabled = true;
            if (_busy)
            {
                label = "Operazione in corso...";
                icon = "disc";
                style = 2;
                enabled = false;
                return;
            }
            if (mount != null)
            {
                label = "Smonta da " + mount.DriveLetter + ":";
                icon = "eject";
                style = 1;
                return;
            }
            if (!File.Exists(entry.Path))
            {
                label = "File non trovato";
                icon = "warning";
                style = 2;
                enabled = false;
                return;
            }
            if (_selectedSheet == null)
            {
                label = "Impossibile leggere il disco";
                icon = "warning";
                style = 2;
                enabled = false;
                return;
            }
            if (_selectedSheet.IsAudioOnly)
            {
                label = "Riproduci CD audio";
                icon = "play";
                return;
            }
            if (_selectedSheet.CanMountNative)
            {
                label = "Monta disco";
                icon = "disc";
                return;
            }
            if (_winCDEmuAvailable)
            {
                label = "Monta con WinCDEmu";
                icon = "disc";
                return;
            }
            label = "WinCDEmu non installato";
            icon = "external";
            style = 2;
            enabled = false;
        }

        private List<DetailAction> BuildDetailActions(DiscEntry entry, CueSheet sheet, MountedDisc mount)
        {
            List<DetailAction> actions = new List<DetailAction>();
            bool mounted = mount != null;
            if (mounted)
                actions.Add(new DetailAction("Apri", "detailOpen", "folder"));
            if (sheet != null && sheet.HasAudio)
            {
                if (!(sheet.IsAudioOnly && !mounted))
                    actions.Add(new DetailAction("Audio", "detailAudio", "note"));
                actions.Add(new DetailAction("Esporta", "detailExport", "download"));
            }
            if (!mounted)
            {
                if (sheet != null && (sheet.IsAudioOnly || !sheet.CanMountNative))
                {
                    if (_winCDEmuAvailable)
                        actions.Add(new DetailAction(sheet.IsAudioOnly ? "Monta CD" : "Completo", "detailFullMount", "disc"));
                    else
                        actions.Add(new DetailAction("Installa", "downloadWinCDEmu", "download"));
                }
                else if (sheet != null && sheet.CanMountNative && sheet.HasAudio && _winCDEmuAvailable)
                {
                    actions.Add(new DetailAction("Completo", "detailFullMount", "disc"));
                }
                if (File.Exists(entry.Path))
                    actions.Add(new DetailAction("Cartella", "detailReveal", "folder"));
            }
            if (actions.Count > 3) actions.RemoveRange(3, actions.Count - 3);
            return actions;
        }
        private string DetailNote(out Color color)
        {
            color = Theme.Muted;
            if (_selectedError.Length > 0)
            {
                color = Theme.Red;
                return _selectedError;
            }
            if (_selected == null) return "";
            if (!File.Exists(_selected.Path))
            {
                color = Theme.Red;
                return "File immagine non trovato.";
            }
            if (_selectedSheet == null) return "";
            if (!_selectedSheet.CanMountNative && _selectedSheet.HasData)
            {
                color = Theme.Amber;
                return "Formato dati non montabile in modalità nativa: serve WinCDEmu.";
            }
            if (_selectedSheet.IsAudioOnly)
            {
                color = Theme.Teal;
                return _winCDEmuAvailable ? "CD audio: ascolta, esporta o monta come CD virtuale." : "CD audio: ascolta o esporta le tracce; per un'unità virtuale serve WinCDEmu.";
            }
            if (_selectedSheet.HasAudio && _selectedSheet.HasData)
            {
                color = Theme.Muted;
                return _winCDEmuAvailable ? "Il montaggio nativo espone solo la traccia dati." : "Le tracce audio non sono incluse nel montaggio nativo.";
            }
            if (_selectedSheet.Warnings.Count > 0)
            {
                string warning = _selectedSheet.Warnings[0];
                if (warning.IndexOf("audio", StringComparison.OrdinalIgnoreCase) < 0 && warning.IndexOf("CD audio", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    color = Theme.Amber;
                    return warning;
                }
            }
            return "";
        }

        private void RenderTracks(Graphics g, CueSheet sheet, RectangleF viewport)
        {
            if (sheet == null || sheet.Tracks.Count == 0)
            {
                Theme.DrawText(g, "Nessuna traccia disponibile.", Body(11f), Theme.Muted2,
                    new RectangleF(viewport.X, viewport.Y + S(8), viewport.Width, S(20)), StringAlignment.Near, StringAlignment.Center);
                _trackContentH = 0;
                return;
            }
            float rowH = S(33);
            _trackContentH = sheet.Tracks.Count * rowH;
            _trackViewport = viewport;
            _trackScroll = UiUtil.Clamp(_trackScroll, 0, Math.Max(0, _trackContentH - viewport.Height));
            GraphicsState state = g.Save();
            try
            {
                g.SetClip(viewport);
                for (int i = 0; i < sheet.Tracks.Count; i++)
                {
                    CueTrack track = sheet.Tracks[i];
                    float rowY = viewport.Y + i * rowH - _trackScroll;
                    if (rowY + rowH < viewport.Y || rowY > viewport.Bottom) continue;
                    bool playable = AudioTools.CanExtract(track);
                    bool hover = playable && Hover("trackPlay", track.Number.ToString());
                    RectangleF row = new RectangleF(viewport.X - S(4), rowY, viewport.Width + S(8), rowH - S(2));
                    if (hover) Theme.FillRound(g, row, S(7), Theme.Surface2);
                    Theme.DrawText(g, track.Number.ToString("00"), Mono(10.5f), Theme.Muted2,
                        new RectangleF(viewport.X, rowY, S(24), rowH), StringAlignment.Near, StringAlignment.Center);
                    RectangleF iconRect = new RectangleF(viewport.X + S(26), rowY + S(8), S(18), S(18));
                    Color iconColor = track.IsAudio ? Theme.Teal : Theme.Blue;
                    if (track.IsAudio) Theme.IconNote(g, iconRect, iconColor, _scale);
                    else Theme.IconDisc(g, iconRect, iconColor, _scale);
                    string title = track.IsAudio ? track.DisplayTitle : "Dati · " + track.CueType;
                    Theme.DrawText(g, title, Body(11.5f), playable ? (hover ? Theme.Text : Theme.Text2) : Theme.Muted,
                        new RectangleF(viewport.X + S(52), rowY, viewport.Width - S(116), rowH), StringAlignment.Near, StringAlignment.Center);
                    string duration = TextUtil.FormatDuration(track.Duration);
                    Theme.DrawText(g, duration, Mono(10f), Theme.Muted2,
                        new RectangleF(viewport.Right - S(58), rowY, S(58), rowH), StringAlignment.Far, StringAlignment.Center);
                    if (playable)
                    {
                        RectangleF play = new RectangleF(viewport.Right - S(84), rowY + S(9), S(15), S(15));
                        Theme.IconPlay(g, play, hover ? Theme.Accent : Theme.Muted);
                        AddHotspot("trackPlay", track.Number.ToString(), row, true);
                    }
                }
            }
            finally { g.Restore(state); }
            Theme.DrawScrollbar(g, viewport, _trackContentH, _trackScroll);
        }

        private void RenderSettings(Graphics g)
        {
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(185, 0, 0, 0)))
                g.FillRectangle(dim, ClientRectangle);
            AddHotspot("settingsClose", "", new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), true);

            float w = Math.Min(S(620), ClientSize.Width - S(60));
            float h = Math.Min(S(520), ClientSize.Height - S(40));
            RectangleF card = new RectangleF((ClientSize.Width - w) / 2f, (ClientSize.Height - h) / 2f, w, h);
            Theme.FillRound(g, card, S(16), Theme.Surface);
            Theme.StrokeRound(g, card, S(16), Theme.Border, 1f);
            Theme.DrawText(g, "Impostazioni", Semi(20f), Theme.Text,
                new RectangleF(card.X + S(24), card.Y + S(18), card.Width - S(80), S(28)), StringAlignment.Near, StringAlignment.Center);
            RectangleF close = new RectangleF(card.Right - S(46), card.Y + S(18), S(26), S(26));
            UiUtil.DrawIconCircle(g, close, Hover("settingsClose", "") ? Theme.Surface3 : Theme.Surface2, Theme.BorderSoft);
            Theme.IconClose(g, new RectangleF(close.X + S(7), close.Y + S(7), S(12), S(12)), Theme.Text2, _scale);
            AddHotspot("settingsClose", "", close, true);

            float x = card.X + S(26);
            float width = card.Width - S(52);
            float y = card.Y + S(60);

            Theme.DrawText(g, "COMPORTAMENTO", Semi(9.5f), Theme.Muted2, new RectangleF(x, y, width, S(14)), StringAlignment.Near, StringAlignment.Center);
            y += S(18);
            RectangleF behaviorRow = new RectangleF(x, y, width, S(48));
            Theme.FillRound(g, behaviorRow, S(9), Theme.Surface2);
            Theme.DrawText(g, "Riduci nella tray alla chiusura", Body(12.5f), Theme.Text,
                new RectangleF(behaviorRow.X + S(12), behaviorRow.Y + S(7), behaviorRow.Width - S(90), S(18)), StringAlignment.Near, StringAlignment.Center);
            Theme.DrawText(g, "I dischi restano montati finché CueDeck è in esecuzione.", Body(10f), Theme.Muted,
                new RectangleF(behaviorRow.X + S(12), behaviorRow.Y + S(25), behaviorRow.Width - S(90), S(16)), StringAlignment.Near, StringAlignment.Center);
            RectangleF toggle = new RectangleF(behaviorRow.Right - S(58), behaviorRow.Y + S(13), S(44), S(22));
            UiUtil.DrawToggle(g, toggle, _settings.Data.CloseToTray, _scale);
            AddHotspot("toggleTray", "", behaviorRow, true);
            y += S(60);

            Theme.DrawText(g, "WINCDEMU (OPZIONALE)", Semi(9.5f), Theme.Muted2, new RectangleF(x, y, width, S(14)), StringAlignment.Near, StringAlignment.Center);
            y += S(18);
            Color winColor = _winCDEmuAvailable ? Theme.Green : Theme.Amber;
            using (SolidBrush dot = new SolidBrush(winColor))
                g.FillEllipse(dot, x, y + S(5), S(7), S(7));
            string winText = _winCDEmuAvailable ? "Rilevato: " + UiUtil.ShortenPath(_winCDEmuPath, 52) : "Non installato";
            Theme.DrawText(g, winText, Body(11f), _winCDEmuAvailable ? Theme.Text2 : Theme.Amber,
                new RectangleF(x + S(14), y, width - S(14), S(18)), StringAlignment.Near, StringAlignment.Center);
            y += S(21);
            Theme.DrawTextWrap(g, "Serve per montare tracce audio, CD protetti e immagini complete. CueDeck usa un'unità virtuale nativa per i CD dati, senza driver.",
                Body(10.3f), Theme.Muted, new RectangleF(x, y, width, S(30)), StringAlignment.Near);
            y += S(34);
            float bw = S(92);
            RectangleF browse = new RectangleF(x, y, bw, S(30));
            RectangleF detect = new RectangleF(x + bw + S(8), y, bw, S(30));
            RectangleF download = new RectangleF(x + (bw + S(8)) * 2, y, S(112), S(30));
            DrawOutlineButton(g, browse, "Sfoglia", "folder", Hover("browseWinCDEmu", ""), false);
            DrawOutlineButton(g, detect, "Rileva", "refresh", Hover("detectWinCDEmu", ""), false);
            DrawOutlineButton(g, download, "Scarica", "download", Hover("downloadWinCDEmu", ""), false);
            AddHotspot("browseWinCDEmu", "", browse, true);
            AddHotspot("detectWinCDEmu", "", detect, true);
            AddHotspot("downloadWinCDEmu", "", download, true);
            y += S(42);

            using (Pen line = new Pen(Theme.BorderSoft, 1f))
                g.DrawLine(line, x, y, x + width, y);
            y += S(12);

            Theme.DrawText(g, "CACHE E DATI", Semi(9.5f), Theme.Muted2, new RectangleF(x, y, width, S(14)), StringAlignment.Near, StringAlignment.Center);
            y += S(18);
            Theme.DrawText(g, "Cache ISO  " + TextUtil.FormatBytes(_cacheSize), Body(11.5f), Theme.Text2,
                new RectangleF(x, y, width, S(18)), StringAlignment.Near, StringAlignment.Center);
            y += S(20);
            Theme.DrawTextWrap(g, "Le tracce dati vengono convertite in ISO nella cache per il montaggio nativo. Le anteprime audio usano la cartella temporanea.",
                Body(10.3f), Theme.Muted, new RectangleF(x, y, width, S(30)), StringAlignment.Near);
            y += S(32);
            RectangleF openCache = new RectangleF(x, y, S(122), S(30));
            RectangleF clear = new RectangleF(x + S(130), y, S(112), S(30));
            DrawOutlineButton(g, openCache, "Apri cartella", "folder", Hover("openCache", ""), false);
            DrawOutlineButton(g, clear, "Svuota cache", "trash", Hover("clearCache", ""), false);
            AddHotspot("openCache", "", openCache, true);
            AddHotspot("clearCache", "", clear, true);

            RectangleF footer = new RectangleF(x, card.Bottom - S(36), width - S(180), S(22));
            Theme.DrawText(g, "CueDeck 1.0  ·  .NET Framework 4.8  ·  Nessun dato lascia il PC", Body(9.5f), Theme.Muted2,
                footer, StringAlignment.Near, StringAlignment.Center);
            RectangleF dataFolder = new RectangleF(card.Right - S(200), card.Bottom - S(38), S(174), S(26));
            Theme.DrawText(g, "Apri cartella dati", Body(10f), Hover("openDataFolder", "") ? Theme.Accent : Theme.Muted,
                dataFolder, StringAlignment.Far, StringAlignment.Center);
            AddHotspot("openDataFolder", "", dataFolder, true);
        }
        private void RenderConfirm(Graphics g)
        {
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(185, 0, 0, 0)))
                g.FillRectangle(dim, ClientRectangle);
            float w = Math.Min(S(500), ClientSize.Width - S(50));
            float h = S(238);
            RectangleF card = new RectangleF((ClientSize.Width - w) / 2f, (ClientSize.Height - h) / 2f, w, h);
            Theme.FillRound(g, card, S(16), Theme.Surface);
            Theme.StrokeRound(g, card, S(16), Theme.Border, 1f);
            RectangleF icon = new RectangleF(card.X + S(24), card.Y + S(24), S(34), S(34));
            UiUtil.DrawIconCircle(g, icon, Theme.Alpha(Theme.Amber, 30), Theme.Alpha(Theme.Amber, 70));
            Theme.IconWarning(g, new RectangleF(icon.X + S(8), icon.Y + S(8), S(18), S(18)), Theme.Amber, _scale);
            Theme.DrawText(g, _confirmTitle, Semi(16.5f), Theme.Text,
                new RectangleF(card.X + S(70), card.Y + S(23), card.Width - S(94), S(28)), StringAlignment.Near, StringAlignment.Center);
            Theme.DrawTextWrap(g, _confirmMessage, Body(11.5f), Theme.Muted,
                new RectangleF(card.X + S(28), card.Y + S(66), card.Width - S(56), S(78)), StringAlignment.Near);
            RectangleF cancel = new RectangleF(card.X + S(28), card.Bottom - S(58), S(116), S(36));
            RectangleF yes = new RectangleF(card.Right - S(166), card.Bottom - S(58), S(138), S(36));
            DrawOutlineButton(g, cancel, "Annulla", "", Hover("confirmNo", ""), false);
            bool danger = _confirmAction == "removeEntry" || _confirmAction == "clearCache";
            if (danger) DrawDangerButton(g, yes, _confirmYesLabel, Hover("confirmYes", ""));
            else DrawAccentButton(g, yes, _confirmYesLabel, "check", Hover("confirmYes", ""));
            AddHotspot("confirmNo", "", cancel, true);
            AddHotspot("confirmYes", "", yes, true);
        }

        private void RenderPrompt(Graphics g)
        {
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(185, 0, 0, 0)))
                g.FillRectangle(dim, ClientRectangle);
            RectangleF card = GetPromptCardRect();
            Theme.FillRound(g, card, S(16), Theme.Surface);
            Theme.StrokeRound(g, card, S(16), Theme.Border, 1f);
            Theme.DrawText(g, _promptTitle, Semi(16.5f), Theme.Text,
                new RectangleF(card.X + S(26), card.Y + S(22), card.Width - S(52), S(26)), StringAlignment.Near, StringAlignment.Center);
            if (!string.IsNullOrEmpty(_promptHint))
                Theme.DrawText(g, _promptHint, Body(10f), Theme.Muted,
                    new RectangleF(card.X + S(26), card.Y + S(52), card.Width - S(52), S(16)), StringAlignment.Near, StringAlignment.Center);
            RectangleF input = GetPromptInputRect(card);
            Theme.FillRound(g, input, S(8), Theme.Surface2);
            Theme.StrokeRound(g, input, S(8), Theme.Accent, 1f);
            RectangleF cancel = new RectangleF(card.X + S(26), card.Bottom - S(56), S(116), S(34));
            RectangleF save = new RectangleF(card.Right - S(146), card.Bottom - S(56), S(120), S(34));
            DrawOutlineButton(g, cancel, "Annulla", "", Hover("promptCancel", ""), false);
            DrawAccentButton(g, save, "Salva", "check", Hover("promptSave", ""));
            AddHotspot("promptCancel", "", cancel, true);
            AddHotspot("promptSave", "", save, true);
        }

        private void RenderBusy(Graphics g)
        {
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                g.FillRectangle(dim, ClientRectangle);
            float w = Math.Min(S(500), ClientSize.Width - S(50));
            float h = S(244);
            RectangleF card = new RectangleF((ClientSize.Width - w) / 2f, (ClientSize.Height - h) / 2f, w, h);
            Theme.FillRound(g, card, S(16), Theme.Surface);
            Theme.StrokeRound(g, card, S(16), Theme.Border, 1f);
            Theme.DrawText(g, _busyTitle, Semi(17f), Theme.Text,
                new RectangleF(card.X + S(28), card.Y + S(24), card.Width - S(56), S(28)), StringAlignment.Near, StringAlignment.Center);
            Theme.DrawTextWrap(g, _busyDetail, Body(11.3f), Theme.Muted,
                new RectangleF(card.X + S(28), card.Y + S(62), card.Width - S(56), S(54)), StringAlignment.Near);
            float barY = card.Y + S(132);
            if (_busyIndeterminate)
            {
                RectangleF spinner = new RectangleF(card.X + S(28), barY - S(3), S(24), S(24));
                UiUtil.DrawSpinner(g, spinner, _spinAngle, _scale);
                Theme.DrawText(g, "Operazione in corso...", Body(10.5f), Theme.Muted,
                    new RectangleF(card.X + S(64), barY, card.Width - S(100), S(24)), StringAlignment.Near, StringAlignment.Center);
            }
            else
            {
                RectangleF bar = new RectangleF(card.X + S(28), barY, card.Width - S(56), S(7));
                UiUtil.DrawProgress(g, bar, _busyProgress, _scale);
                Theme.DrawText(g, _busyProgress + "%", Mono(10.5f), Theme.Text2,
                    new RectangleF(card.X + S(28), barY + S(14), card.Width - S(56), S(16)), StringAlignment.Far, StringAlignment.Center);
            }
            if (_busyCanCancel)
            {
                RectangleF cancel = new RectangleF(card.X + S(28), card.Bottom - S(56), S(116), S(34));
                DrawOutlineButton(g, cancel, "Annulla", "", Hover("busyCancel", ""), false);
                AddHotspot("busyCancel", "", cancel, true);
            }
            else
            {
                Theme.DrawText(g, "Attendi...", Body(10.5f), Theme.Muted2,
                    new RectangleF(card.X + S(28), card.Bottom - S(48), card.Width - S(56), S(24)), StringAlignment.Near, StringAlignment.Center);
            }
        }

        private void RenderToast(Graphics g)
        {
            if (_toast == null) return;
            float width = Math.Min(S(382), ClientSize.Width - S(40));
            float height = string.IsNullOrEmpty(_toast.ActionLabel) ? S(72) : S(92);
            RectangleF rect = new RectangleF(ClientSize.Width - S(20) - width, ClientSize.Height - S(20) - height, width, height);
            Color accent = Theme.Accent;
            if (_toast.Kind == "success") accent = Theme.Green;
            else if (_toast.Kind == "error") accent = Theme.Red;
            else if (_toast.Kind == "warning") accent = Theme.Amber;
            Theme.FillRound(g, rect, S(12), Theme.Surface2);
            Theme.StrokeRound(g, rect, S(12), Theme.Border, 1f);
            using (SolidBrush bar = new SolidBrush(accent))
            {
                using (GraphicsPath path = Theme.RoundRect(new RectangleF(rect.X, rect.Y, S(4), rect.Height), S(2)))
                    g.FillPath(bar, path);
            }
            RectangleF icon = new RectangleF(rect.X + S(18), rect.Y + S(17), S(26), S(26));
            UiUtil.DrawIconCircle(g, icon, Theme.Alpha(accent, 35), Theme.Alpha(accent, 70));
            RectangleF iconGlyph = new RectangleF(icon.X + S(6), icon.Y + S(6), S(14), S(14));
            if (_toast.Kind == "success") Theme.IconCheck(g, iconGlyph, accent, _scale);
            else if (_toast.Kind == "warning") Theme.IconWarning(g, iconGlyph, accent, _scale);
            else if (_toast.Kind == "error") Theme.IconClose(g, iconGlyph, accent, _scale);
            else Theme.IconInfo(g, iconGlyph, accent, _scale);
            Theme.DrawText(g, _toast.Title, Semi(12.5f), Theme.Text,
                new RectangleF(rect.X + S(54), rect.Y + S(13), rect.Width - S(90), S(18)), StringAlignment.Near, StringAlignment.Center);
            Theme.DrawTextWrap(g, _toast.Message, Body(10.5f), Theme.Muted,
                new RectangleF(rect.X + S(54), rect.Y + S(31), rect.Width - S(74), string.IsNullOrEmpty(_toast.ActionLabel) ? S(30) : S(28)), StringAlignment.Near);
            RectangleF close = new RectangleF(rect.Right - S(28), rect.Y + S(12), S(18), S(18));
            Theme.IconClose(g, close, Theme.Muted, _scale);
            AddHotspot("toastClose", "", new RectangleF(rect.Right - S(34), rect.Y + S(6), S(28), S(28)), true);
            if (!string.IsNullOrEmpty(_toast.ActionLabel))
            {
                RectangleF action = new RectangleF(rect.X + S(54), rect.Bottom - S(26), rect.Width - S(80), S(18));
                Theme.DrawText(g, _toast.ActionLabel, Semi(10.5f), accent, action, StringAlignment.Near, StringAlignment.Center);
                AddHotspot("toastAction", "", action, true);
            }
        }

        private void RenderDrag(Graphics g)
        {
            using (SolidBrush overlay = new SolidBrush(Color.FromArgb(25, Theme.Accent)))
                g.FillRectangle(overlay, ClientRectangle);
            RectangleF border = new RectangleF(S(16), S(16), ClientSize.Width - S(32), ClientSize.Height - S(32));
            UiUtil.DrawDashedRoundRect(g, border, S(18), Theme.Alpha(Theme.Accent, 220), 2f);
            float cx = ClientSize.Width / 2f;
            float cy = ClientSize.Height / 2f;
            RectangleF art = new RectangleF(cx - S(44), cy - S(88), S(88), S(88));
            Theme.DrawCover(g, art, "drag", "CueDeck", "", null, _scale);
            Theme.DrawText(g, "Rilascia per aggiungere", Semi(20f), Theme.Text,
                new RectangleF(0, cy + S(8), ClientSize.Width, S(28)), StringAlignment.Center, StringAlignment.Center);
            Theme.DrawText(g, "File supportati: .cue  .bin  .iso  —  anche intere cartelle", Body(12f), Theme.Text2,
                new RectangleF(0, cy + S(40), ClientSize.Width, S(20)), StringAlignment.Center, StringAlignment.Center);
        }

        private void AddHotspot(string action, string arg, RectangleF rect, bool enabled)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            Hotspot h = new Hotspot();
            h.Action = action;
            h.Arg = arg ?? "";
            h.Rect = rect;
            h.Enabled = enabled;
            _hotspots.Add(h);
        }

        private bool Hover(string action, string arg)
        {
            return string.Equals(_hoverKey, action + "\u0001" + (arg ?? ""), StringComparison.Ordinal);
        }

        private Hotspot FindHotspot(Point point)
        {
            PointF p = new PointF(point.X, point.Y);
            for (int i = _hotspots.Count - 1; i >= 0; i--)
            {
                if (_hotspots[i].Contains(p)) return _hotspots[i];
            }
            return null;
        }

        private void DrawAccentButton(Graphics g, RectangleF rect, string label, string icon, bool hover)
        {
            Color a = hover ? Theme.AccentHover : Theme.Accent;
            Color b = hover ? Theme.Mix(Theme.AccentHover, Theme.Teal, 0.35f) : Theme.Mix(Theme.Accent, Theme.Teal, 0.20f);
            Theme.FillRoundGradient(g, rect, S(9), a, b, 20f);
            DrawButtonContent(g, rect, label, icon, Color.White, Semi(13.2f), true);
        }

        private void DrawAccentButtonSmall(Graphics g, RectangleF rect, string label, string icon, bool hover)
        {
            Theme.FillRound(g, rect, S(8), hover ? Theme.AccentHover : Theme.Accent);
            DrawButtonContent(g, rect, label, icon, Color.White, Semi(11f), true);
        }

        private void DrawOutlineButton(Graphics g, RectangleF rect, string label, string icon, bool hover)
        {
            DrawOutlineButton(g, rect, label, icon, hover, false);
        }

        private void DrawOutlineButton(Graphics g, RectangleF rect, string label, string icon, bool hover, bool green)
        {
            Color bg = hover ? Theme.Surface2 : Theme.Surface2;
            Theme.FillRound(g, rect, S(8), bg);
            Color border = green ? Theme.Alpha(Theme.Green, 150) : (hover ? Theme.Accent : Theme.Border);
            Theme.StrokeRound(g, rect, S(8), border, 1f);
            Color fg = green ? Theme.Green : (hover ? Theme.Text : Theme.Text2);
            DrawButtonContent(g, rect, label, icon, fg, Semi(11f), false);
        }

        private void DrawDisabledButton(Graphics g, RectangleF rect, string label, string icon)
        {
            Theme.FillRound(g, rect, S(9), Theme.Surface2);
            Theme.StrokeRound(g, rect, S(9), Theme.BorderSoft, 1f);
            DrawButtonContent(g, rect, label, icon, Theme.Muted2, Semi(13f), false);
        }

        private void DrawDangerButton(Graphics g, RectangleF rect, string label, bool hover)
        {
            Theme.FillRound(g, rect, S(9), hover ? Theme.Mix(Theme.Red, Color.White, 0.12f) : Theme.Red);
            DrawButtonContent(g, rect, label, "trash", Color.FromArgb(35, 8, 12), Semi(13f), true);
        }

        private void DrawButtonContent(Graphics g, RectangleF rect, string label, string icon, Color color, Font font, bool accent)
        {
            float iconSize = string.IsNullOrEmpty(icon) ? 0 : S(14);
            float gap = string.IsNullOrEmpty(icon) ? 0 : S(7);
            SizeF textSize = g.MeasureString(label, font);
            float total = iconSize + gap + textSize.Width;
            float start = rect.X + (rect.Width - total) / 2f;
            if (!string.IsNullOrEmpty(icon))
            {
                DrawMiniIcon(g, new RectangleF(start, rect.Y + (rect.Height - iconSize) / 2f, iconSize, iconSize), icon, color, _scale);
                start += iconSize + gap;
            }
            Theme.DrawText(g, label, font, color,
                new RectangleF(start, rect.Y, rect.Width - (start - rect.X) - S(6), rect.Height), StringAlignment.Near, StringAlignment.Center);
        }

        private void DrawIconButton(Graphics g, RectangleF rect, string icon, bool hover, bool active)
        {
            Theme.FillRound(g, rect, S(9), hover ? Theme.Surface2 : Theme.Surface);
            Theme.StrokeRound(g, rect, S(9), hover ? Theme.Border : Theme.BorderSoft, 1f);
            DrawMiniIcon(g, new RectangleF(rect.X + S(9), rect.Y + S(9), S(18), S(18)), icon, hover ? Theme.Text : Theme.Muted, _scale);
        }

        private void DrawMiniIcon(Graphics g, RectangleF rect, string icon, Color color, float scale)
        {
            if (icon == "plus") Theme.IconPlus(g, rect, color, scale);
            else if (icon == "settings") Theme.IconSettings(g, rect, color, scale);
            else if (icon == "folder") Theme.IconFolder(g, rect, color, scale);
            else if (icon == "play") Theme.IconPlay(g, rect, color);
            else if (icon == "pause") Theme.IconPause(g, rect, color);
            else if (icon == "eject") Theme.IconEject(g, rect, color, scale);
            else if (icon == "disc") Theme.IconDisc(g, rect, color, scale);
            else if (icon == "note") Theme.IconNote(g, rect, color, scale);
            else if (icon == "download") Theme.IconDownload(g, rect, color, scale);
            else if (icon == "trash") Theme.IconTrash(g, rect, color, scale);
            else if (icon == "external") Theme.IconExternal(g, rect, color, scale);
            else if (icon == "refresh") Theme.IconRefresh(g, rect, color, scale);
            else if (icon == "check") Theme.IconCheck(g, rect, color, scale);
            else if (icon == "warning") Theme.IconWarning(g, rect, color, scale);
            else if (icon == "info") Theme.IconInfo(g, rect, color, scale);
            else if (icon == "close") Theme.IconClose(g, rect, color, scale);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragOver) return;
            Hotspot hit = FindHotspot(e.Location);
            string key = hit != null ? hit.Action + "\u0001" + hit.Arg : "";
            if (!string.Equals(key, _hoverKey, StringComparison.Ordinal))
            {
                _hoverKey = key;
                Cursor = hit != null ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverKey.Length > 0)
            {
                _hoverKey = "";
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_settingsOpen || _confirmOpen || _promptOpen || _busy)
            {
                Hotspot overlayHit = FindHotspot(e.Location);
                _pressed = overlayHit;
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                CardLayout card = FindCardAt(e.Location);
                if (card != null)
                {
                    SelectEntryInternal(card.Entry);
                    ShowCardMenu(card.Entry, PointToScreen(e.Location));
                }
                else if (_selected != null && UiUtil.Contains(_detailRect, new PointF(e.X, e.Y)))
                {
                    ShowCardMenu(_selected, PointToScreen(e.Location));
                }
                return;
            }
            Hotspot hit = FindHotspot(e.Location);
            _pressed = hit;
            if (hit != null && hit.Action == "selectCard")
            {
                DiscEntry entry = _library.FindById(hit.Arg);
                if (entry != null) SelectEntry(entry);
            }
            if (hit != null && hit.Action == "focusSearch") FocusSearch();
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            Hotspot pressed = _pressed;
            _pressed = null;
            if (pressed == null) return;
            Hotspot hit = FindHotspot(e.Location);
            if (hit == null) return;
            if (string.Equals(hit.Action, pressed.Action, StringComparison.Ordinal) &&
                string.Equals(hit.Arg, pressed.Arg, StringComparison.Ordinal))
            {
                Dispatch(hit.Action, hit.Arg);
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left || _settingsOpen || _confirmOpen || _promptOpen || _busy) return;
            CardLayout card = FindCardAt(e.Location);
            if (card == null) return;
            Hotspot hit = FindHotspot(e.Location);
            if (hit != null && (hit.Action == "favorite" || hit.Action == "cardAction")) return;
            SelectEntryInternal(card.Entry);
            CueSheet sheet = GetSheet(card.Entry);
            if (sheet != null && sheet.IsAudioOnly) OpenAudioPlayer(sheet, 0);
            else MountEntry(card.Entry, false);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_settingsOpen || _confirmOpen || _promptOpen || _busy) return;
            PointF p = new PointF(e.X, e.Y);
            bool overTracks = UiUtil.Contains(_trackViewport, p) && _trackContentH > _trackViewport.Height;
            float delta = e.Delta > 0 ? -S(104) : S(104);
            if (overTracks)
            {
                _trackScroll = UiUtil.Clamp(_trackScroll + delta, 0, Math.Max(0, _trackContentH - _trackViewport.Height));
            }
            else if (UiUtil.Contains(_libViewport, p) || !UiUtil.Contains(_detailRect, p))
            {
                _libScroll = UiUtil.Clamp(_libScroll + delta, 0, Math.Max(0, _libContentH - _libViewport.Height));
            }
            else
            {
                _trackScroll = UiUtil.Clamp(_trackScroll + delta, 0, Math.Max(0, _trackContentH - _trackViewport.Height));
            }
            Invalidate();
        }

        private CardLayout FindCardAt(Point point)
        {
            PointF p = new PointF(point.X, point.Y);
            for (int i = _cards.Count - 1; i >= 0; i--)
                if (UiUtil.Contains(_cards[i].Rect, p)) return _cards[i];
            return null;
        }
    }
}
