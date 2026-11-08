using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CueDeck
{
    public partial class MainForm : Form
    {
        private const string PlaceholderText = "Cerca un disco...";

        private readonly LibraryStore _library = new LibraryStore();
        private readonly SettingsStore _settings = new SettingsStore();
        private readonly List<MountedDisc> _mounted = new List<MountedDisc>();
        private readonly Dictionary<string, SheetCacheEntry> _sheetCache = new Dictionary<string, SheetCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly object _sheetLock = new object();
        private readonly List<DiscEntry> _visible = new List<DiscEntry>();
        private readonly List<CardLayout> _cards = new List<CardLayout>();
        private readonly List<Hotspot> _hotspots = new List<Hotspot>();
        private readonly ImageCache _imageCache = new ImageCache();
        private readonly Dictionary<string, string> _autoCovers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<StartupItem> _startupItems;

        private DiscEntry _selected;
        private CueSheet _selectedSheet;
        private string _selectedError = "";
        private string _filter = "all";
        private string _search = "";
        private DiscSort _sort = DiscSort.Name;
        private int _selectedIndex = -1;
        private float _scale = 1f;
        private float _libScroll;
        private float _trackScroll;
        private float _libContentH;
        private float _trackContentH;
        private int _columns = 2;
        private RectangleF _topRect;
        private RectangleF _sidebarRect;
        private RectangleF _libraryRect;
        private RectangleF _detailRect;
        private RectangleF _libViewport;
        private RectangleF _trackViewport;
        private RectangleF _searchRect;
        private RectangleF _addRect;
        private RectangleF _settingsRect;
        private RectangleF _sortRect;
        private RectangleF _scanRect;
        private RectangleF _coverRect;
        private float _cardW;
        private float _cardH;

        private bool _dragOver;
        private bool _busy;
        private bool _busyIndeterminate;
        private bool _busyCanCancel;
        private string _busyTitle = "";
        private string _busyDetail = "";
        private int _busyProgress;
        private CancellationTokenSource _busyCts;
        private System.Windows.Forms.Timer _spinTimer;
        private float _spinAngle;

        private AudioPlayerForm _audioPlayer;
        private ToastInfo _toast;
        private System.Windows.Forms.Timer _toastTimer;

        private bool _settingsOpen;
        private bool _confirmOpen;
        private string _confirmTitle = "";
        private string _confirmMessage = "";
        private string _confirmAction = "";
        private string _confirmArg = "";
        private string _confirmYesLabel = "Conferma";

        private bool _promptOpen;
        private string _promptTitle = "";
        private string _promptInitial = "";
        private string _promptHint = "";
        private TextBox _promptBox;
        private Action<string> _promptCallback;

        private TextBox _searchBox;
        private bool _placeholderActive;
        private bool _suppressSearchText;

        private NotifyIcon _trayIcon;
        private Icon _appIcon;
        private bool _reallyExit;
        private bool _startupHandled;

        private Hotspot _pressed;
        private string _hoverKey = "";

        private int _countAll;
        private int _countMounted;
        private int _countFavorites;
        private int _countData;
        private int _countAudio;

        private long _cacheSize;
        private string _winCDEmuPath;
        private bool _winCDEmuAvailable;
        private bool _driverInstalled;


        private class SheetCacheEntry
        {
            public long Stamp;
            public CueSheet Sheet;
            public string Error;
        }

        public MainForm(List<StartupItem> startupItems)
        {
            _startupItems = startupItems ?? new List<StartupItem>();
            InitializeWindow();
            _settings.Load();
            _library.Load();
            _scale = Math.Max(1f, DeviceDpi / 96f);
            _filter = NormalizeFilter(_settings.Data.LastFilter);
            _sort = _settings.Data.GetSort();
            ApplySavedBounds();
            InitializeSearchBox();
            InitializeTray();
            RefreshWinCDEmu();
            RefreshCacheSize();
            RebuildVisible();
            DiscEntry last = _library.FindById(_settings.Data.LastDiscId);
            if (last != null) SelectEntryInternal(last);
            else if (_visible.Count > 0) SelectEntryInternal(_visible[0]);
            WarmMetadataAsync();
            if (!string.IsNullOrEmpty(_library.LoadWarning))
                ShowToast("warning", "Libreria", _library.LoadWarning, "", "");
        }

        private void InitializeWindow()
        {
            Text = "CueDeck";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            ShowInTaskbar = true;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            AllowDrop = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            MinimumSize = new Size((int)S(960), (int)S(620));
            StartPosition = FormStartPosition.CenterScreen;
            _appIcon = LoadApplicationIcon();
            if (_appIcon != null) Icon = _appIcon;
            _spinTimer = new System.Windows.Forms.Timer();
            _spinTimer.Interval = 45;
            _spinTimer.Tick += delegate
            {
                _spinAngle = (_spinAngle + 11f) % 360f;
                if (_busy) Invalidate();
            };
            _toastTimer = new System.Windows.Forms.Timer();
            _toastTimer.Interval = 1000;
            _toastTimer.Tick += delegate
            {
                if (_toast != null && DateTime.UtcNow >= _toast.ExpiresUtc)
                {
                    _toast = null;
                    _toastTimer.Stop();
                    Invalidate();
                }
            };
        }

        private Icon LoadApplicationIcon()
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return icon;
            }
            catch { }
            try { return (Icon)SystemIcons.Application.Clone(); }
            catch { return SystemIcons.Application; }
        }

        private void ApplySavedBounds()
        {
            AppSettings s = _settings.Data;
            Rectangle working = Screen.PrimaryScreen.WorkingArea;
            int maxW = Math.Max((int)S(860), working.Width - (int)S(32));
            int maxH = Math.Max((int)S(560), working.Height - (int)S(32));
            int minW = Math.Min((int)S(960), maxW);
            int minH = Math.Min((int)S(620), maxH);
            int w = UiUtil.Clamp(s.WindowWidth, minW, maxW);
            int h = UiUtil.Clamp(s.WindowHeight, minH, maxH);

            bool useSavedPosition = s.WindowX != int.MinValue && s.WindowY != int.MinValue;
            if (useSavedPosition)
            {
                Rectangle desired = new Rectangle(s.WindowX, s.WindowY, w, h);
                if (IsOnAnyScreen(desired))
                {
                    StartPosition = FormStartPosition.Manual;
                    Bounds = desired;
                }
                else
                {
                    StartPosition = FormStartPosition.CenterScreen;
                    Size = new Size(w, h);
                }
            }
            else
            {
                StartPosition = FormStartPosition.CenterScreen;
                Size = new Size(w, h);
            }
            if (s.Maximized) WindowState = FormWindowState.Maximized;
        }
        private bool IsOnAnyScreen(Rectangle bounds)
        {
            Screen[] screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                Rectangle intersection = Rectangle.Intersect(bounds, screens[i].WorkingArea);
                if (intersection.Width > 180 && intersection.Height > 120) return true;
            }
            return false;
        }

        private void SaveWindowState()
        {
            try
            {
                Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                _settings.Data.WindowWidth = bounds.Width;
                _settings.Data.WindowHeight = bounds.Height;
                _settings.Data.WindowX = bounds.X;
                _settings.Data.WindowY = bounds.Y;
                _settings.Data.Maximized = WindowState == FormWindowState.Maximized;
                _settings.Data.LastDiscId = _selected != null ? _selected.Id : "";
            }
            catch { }
        }

        private void InitializeSearchBox()
        {
            _searchBox = new TextBox();
            _searchBox.BorderStyle = BorderStyle.None;
            _searchBox.BackColor = Theme.Surface;
            _searchBox.ForeColor = Theme.Muted;
            _searchBox.Font = Body(13.5f);
            _searchBox.HideSelection = false;
            _searchBox.TextAlign = HorizontalAlignment.Left;
            _searchBox.TextChanged += delegate
            {
                if (_suppressSearchText || _placeholderActive) return;
                _search = _searchBox.Text;
                RebuildVisible();
                Invalidate();
            };
            _searchBox.GotFocus += delegate
            {
                if (_placeholderActive || _searchBox.Text == PlaceholderText)
                {
                    _placeholderActive = false;
                    _suppressSearchText = true;
                    _searchBox.Text = "";
                    _suppressSearchText = false;
                    _searchBox.ForeColor = Theme.Text;
                    _search = "";
                }
                Invalidate();
            };
            _searchBox.LostFocus += delegate
            {
                if (_searchBox.Text.Trim().Length == 0) SetSearchPlaceholder();
                Invalidate();
            };
            _searchBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    if (_searchBox.Text.Length > 0) ClearSearch();
                    else BeginInvoke(new MethodInvoker(delegate { ActiveControl = null; }));
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Enter)
                {
                    BeginInvoke(new MethodInvoker(delegate { ActiveControl = null; }));
                    e.SuppressKeyPress = true;
                }
            };
            Controls.Add(_searchBox);
            SetSearchPlaceholder();
        }

        private void SetSearchPlaceholder()
        {
            if (_placeholderActive) return;
            if (_searchBox.Focused) return;
            if (_searchBox.Text.Length != 0) return;
            _placeholderActive = true;
            _suppressSearchText = true;
            _searchBox.ForeColor = Theme.Muted;
            _searchBox.Text = PlaceholderText;
            _suppressSearchText = false;
            _search = "";
        }

        private void ClearSearch()
        {
            _placeholderActive = false;
            _suppressSearchText = true;
            _searchBox.Text = "";
            _suppressSearchText = false;
            _search = "";
            if (!_searchBox.Focused) SetSearchPlaceholder();
            else _searchBox.ForeColor = Theme.Text;
            RebuildVisible();
            Invalidate();
        }

        private void FocusSearch()
        {
            ShowMainWindow();
            if (_settingsOpen || _confirmOpen || _promptOpen || _busy) return;
            UpdateChildControls();
            if (_placeholderActive)
            {
                _placeholderActive = false;
                _suppressSearchText = true;
                _searchBox.Text = "";
                _suppressSearchText = false;
                _searchBox.ForeColor = Theme.Text;
                _search = "";
            }
            _searchBox.Focus();
            _searchBox.SelectAll();
            Invalidate();
        }

        private void InitializeTray()
        {
            _trayIcon = new NotifyIcon();
            _trayIcon.Icon = _appIcon != null ? _appIcon : SystemIcons.Application;
            _trayIcon.Text = "CueDeck";
            _trayIcon.Visible = true;
            ContextMenuStrip menu = MakeMenu();
            ToolStripMenuItem open = MakeMenuItem("Apri CueDeck", delegate { ShowMainWindow(); });
            ToolStripMenuItem unmount = MakeMenuItem("Smonta tutti", delegate { ShowMainWindow(); RequestUnmountAll(); });
            ToolStripMenuItem settings = MakeMenuItem("Impostazioni...", delegate { ShowMainWindow(); OpenSettings(); });
            ToolStripMenuItem exit = MakeMenuItem("Esci", delegate { ExitApplication(); });
            menu.Items.Add(open);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(unmount);
            menu.Items.Add(settings);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exit);
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += delegate { ShowMainWindow(); };
            _trayIcon.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ShowMainWindow();
            };
        }

        public void HandleExternalCommand(string line)
        {
            try
            {
                if (!IsHandleCreated || IsDisposed) return;
                BeginInvoke(new MethodInvoker(delegate { ProcessExternalLine(line); }));
            }
            catch { }
        }

        private void ProcessExternalLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (line == "SHOW")
            {
                ShowMainWindow();
                return;
            }
            if (line == "UNMOUNTALL")
            {
                ShowMainWindow();
                RequestUnmountAll();
                return;
            }
            int tab = line.IndexOf('\t');
            if (tab <= 0) return;
            string command = line.Substring(0, tab);
            string encoded = line.Substring(tab + 1);
            string path;
            try { path = Encoding.UTF8.GetString(Convert.FromBase64String(encoded)); }
            catch { return; }
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                ShowToast("error", "Percorso non trovato", path, "", "");
                return;
            }
            AddSummary summary = _library.AddPaths(new string[] { path });
            RebuildVisible();
            DiscEntry entry = null;
            if (summary.NewEntries.Count > 0) entry = summary.NewEntries[0];
            else if (!string.IsNullOrEmpty(path)) entry = _library.FindByPath(path);
            if (entry != null) SelectEntryInternal(entry);
            ShowMainWindow();
            if (command == "MOUNT" && entry != null)
                BeginInvoke(new MethodInvoker(delegate { MountEntry(entry, false); }));
        }

        private void WarmMetadataAsync()
        {
            List<DiscEntry> snapshot = new List<DiscEntry>(_library.Data.Discs);
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    try
                    {
                        string error;
                        GetSheet(snapshot[i], out error);
                    }
                    catch { }
                }
                PostUi(delegate
                {
                    RebuildVisible();
                    Invalidate();
                });
            });
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

        private void RefreshWinCDEmu()
        {
            _winCDEmuPath = WinCDEmuMounter.FindExecutable(_settings.Data.WinCDEmuPath);
            _winCDEmuAvailable = !string.IsNullOrEmpty(_winCDEmuPath) && File.Exists(_winCDEmuPath);
            _driverInstalled = WinCDEmuMounter.DriverInstalled();
        }

        private void RefreshCacheSize()
        {
            _cacheSize = IsoBuilder.GetCacheSize();
        }

        private long ComputeStamp(string path)
        {
            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists) return 0;
                return (info.Length * 397L) ^ info.LastWriteTimeUtc.Ticks;
            }
            catch { return 0; }
        }

        private CueSheet GetSheet(DiscEntry entry, out string error)
        {
            error = "";
            if (entry == null) return null;
            string key = LibraryStore.PathKey(entry.Path);
            long stamp = ComputeStamp(entry.Path);
            lock (_sheetLock)
            {
                SheetCacheEntry cached;
                if (_sheetCache.TryGetValue(key, out cached) && cached.Stamp == stamp)
                {
                    error = cached.Error;
                    return cached.Sheet;
                }
            }
            CueSheet sheet = null;
            string parseError = "";
            try
            {
                sheet = CueParser.ParseAny(entry);
                if (sheet != null && sheet.TotalSourceBytes > 0 && entry.SizeBytes <= 0)
                    entry.SizeBytes = sheet.TotalSourceBytes;
            }
            catch (Exception ex)
            {
                parseError = ex.Message;
                Logger.Error("Parse disc " + entry.Path, ex);
            }
            lock (_sheetLock)
            {
                SheetCacheEntry cached = new SheetCacheEntry();
                cached.Stamp = stamp;
                cached.Sheet = sheet;
                cached.Error = parseError;
                _sheetCache[key] = cached;
            }
            error = parseError;
            return sheet;
        }

        private CueSheet GetSheet(DiscEntry entry)
        {
            string error;
            return GetSheet(entry, out error);
        }

        private CueSheet PeekSheet(DiscEntry entry)
        {
            if (entry == null) return null;
            string key = LibraryStore.PathKey(entry.Path);
            lock (_sheetLock)
            {
                SheetCacheEntry cached;
                if (_sheetCache.TryGetValue(key, out cached)) return cached.Sheet;
            }
            return null;
        }

        private long GetDisplaySize(DiscEntry entry)
        {
            if (entry == null) return 0;
            CueSheet sheet = PeekSheet(entry);
            if (sheet != null && sheet.TotalSourceBytes > 0) return sheet.TotalSourceBytes;
            if (entry.SizeBytes > 0) return entry.SizeBytes;
            try { if (File.Exists(entry.Path)) return new FileInfo(entry.Path).Length; }
            catch { }
            return 0;
        }

        private string GetCoverPath(DiscEntry entry)
        {
            if (entry == null) return null;
            if (!string.IsNullOrEmpty(entry.CoverPath) && File.Exists(entry.CoverPath)) return entry.CoverPath;
            string cached;
            if (_autoCovers.TryGetValue(entry.Id, out cached))
            {
                if (string.IsNullOrEmpty(cached) || File.Exists(cached)) return cached;
                _autoCovers.Remove(entry.Id);
            }
            string found = CoverStore.FindAutoCover(entry.Path);
            _autoCovers[entry.Id] = found ?? "";
            return found;
        }

        private void RefreshCounts()
        {
            _countAll = _library.Data.Discs.Count;
            _countMounted = _mounted.Count;
            _countFavorites = 0;
            _countData = 0;
            _countAudio = 0;
            for (int i = 0; i < _library.Data.Discs.Count; i++)
            {
                DiscEntry e = _library.Data.Discs[i];
                if (e.Favorite) _countFavorites++;
                CueSheet sheet = PeekSheet(e);
                if (sheet != null)
                {
                    if (sheet.HasData) _countData++;
                    if (sheet.IsAudioOnly) _countAudio++;
                }
                else if (e.Kind == DiscKinds.Iso)
                {
                    _countData++;
                }
            }
        }

        private void RebuildVisible()
        {
            string query = (_search ?? "").Trim();
            _visible.Clear();
            for (int i = 0; i < _library.Data.Discs.Count; i++)
            {
                DiscEntry entry = _library.Data.Discs[i];
                if (!MatchesFilter(entry)) continue;
                if (query.Length > 0 && !MatchesSearch(entry, query)) continue;
                _visible.Add(entry);
            }
            SortVisible();
            _selectedIndex = _selected != null ? _visible.IndexOf(_selected) : -1;
            if (_selected == null && _visible.Count > 0)
                SelectEntryInternal(_visible[0]);
            else if (_selected != null && _selectedIndex < 0)
            {
                if (_visible.Count > 0) SelectEntryInternal(_visible[0]);
                else
                {
                    _selected = null;
                    _selectedSheet = null;
                    _selectedError = "";
                    _selectedIndex = -1;
                }
            }
            _libScroll = UiUtil.Clamp(_libScroll, 0, Math.Max(0, _libContentH - _libViewport.Height));
            RefreshCounts();
            Invalidate();
        }

        private void SortVisible()
        {
            if (_sort == DiscSort.Name)
            {
                _visible.Sort(delegate(DiscEntry a, DiscEntry b)
                {
                    int result = string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
                    if (result == 0) result = string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
                    return result;
                });
            }
            else if (_sort == DiscSort.RecentlyAdded)
            {
                _visible.Sort(delegate(DiscEntry a, DiscEntry b) { return b.AddedUtc.CompareTo(a.AddedUtc); });
            }
            else if (_sort == DiscSort.RecentlyMounted)
            {
                _visible.Sort(delegate(DiscEntry a, DiscEntry b)
                {
                    int result = b.LastMountedUtc.CompareTo(a.LastMountedUtc);
                    if (result == 0) result = string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
                    return result;
                });
            }
            else if (_sort == DiscSort.Size)
            {
                _visible.Sort(delegate(DiscEntry a, DiscEntry b) { return GetDisplaySize(b).CompareTo(GetDisplaySize(a)); });
            }
        }

        private bool MatchesFilter(DiscEntry entry)
        {
            if (entry == null) return false;
            if (_filter == "mounted") return FindMountForEntry(entry.Id) != null;
            if (_filter == "favorites") return entry.Favorite;
            if (_filter == "data" || _filter == "audio")
            {
                CueSheet sheet = PeekSheet(entry);
                if (sheet == null)
                {
                    if (_filter == "data" && entry.Kind == DiscKinds.Iso) return true;
                    return _filter == "data" && entry.Kind != DiscKinds.Bin;
                }
                return _filter == "data" ? sheet.HasData : sheet.IsAudioOnly;
            }
            return true;
        }

        private bool MatchesSearch(DiscEntry entry, string query)
        {
            if (ContainsIgnoreCase(entry.Title, query)) return true;
            if (ContainsIgnoreCase(entry.Path, query)) return true;
            CueSheet sheet = PeekSheet(entry);
            if (sheet != null)
            {
                if (ContainsIgnoreCase(sheet.Title, query)) return true;
                if (ContainsIgnoreCase(sheet.Performer, query)) return true;
                if (ContainsIgnoreCase(sheet.Genre, query)) return true;
                for (int i = 0; i < sheet.Tracks.Count; i++)
                    if (ContainsIgnoreCase(sheet.Tracks[i].Title, query)) return true;
            }
            return false;
        }

        private static bool ContainsIgnoreCase(string text, string query)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private void SelectEntryInternal(DiscEntry entry)
        {
            _selected = entry;
            _selectedSheet = null;
            _selectedError = "";
            _trackScroll = 0;
            if (entry != null)
            {
                _selectedSheet = GetSheet(entry, out _selectedError);
                _settings.Data.LastDiscId = entry.Id;
            }
            _selectedIndex = _visible.IndexOf(entry);
            RefreshCounts();
            Invalidate();
        }

        private void SelectEntry(DiscEntry entry)
        {
            if (entry == null) return;
            SelectEntryInternal(entry);
            if (_selectedIndex >= 0) EnsureSelectedVisible();
        }

        private void EnsureSelectedVisible()
        {
            if (_selectedIndex < 0 || _visible.Count == 0) return;
            float gap = S(16);
            int row = _selectedIndex / Math.Max(1, _columns);
            float top = row * (_cardH + gap);
            float bottom = top + _cardH;
            float viewportH = _libViewport.Height;
            if (top < _libScroll) _libScroll = top;
            else if (bottom > _libScroll + viewportH) _libScroll = bottom - viewportH;
            _libScroll = UiUtil.Clamp(_libScroll, 0, Math.Max(0, _libContentH - viewportH));
        }

        private MountedDisc FindMountForEntry(string entryId)
        {
            for (int i = 0; i < _mounted.Count; i++)
                if (string.Equals(_mounted[i].EntryId, entryId, StringComparison.OrdinalIgnoreCase)) return _mounted[i];
            return null;
        }

        private MountedDisc FindMountByLetter(string letter)
        {
            for (int i = 0; i < _mounted.Count; i++)
                if (string.Equals(_mounted[i].DriveLetter, letter, StringComparison.OrdinalIgnoreCase)) return _mounted[i];
            return null;
        }

        private string GetDriveLetter(string entryId)
        {
            MountedDisc mount = FindMountForEntry(entryId);
            return mount != null ? mount.DriveLetter : "";
        }

        private void MountEntry(DiscEntry entry, bool forceFull)
        {
            if (entry == null) return;
            if (_busy)
            {
                ShowToast("info", "Operazione in corso", "Attendi il completamento dell'operazione attuale.", "", "");
                return;
            }
            MountedDisc existing = FindMountForEntry(entry.Id);
            if (existing != null && !forceFull)
            {
                OpenDrive(existing.DriveLetter);
                return;
            }
            string error;
            CueSheet sheet = GetSheet(entry, out error);
            if (sheet == null)
            {
                ShowToast("error", "Disco non leggibile", error, "", "");
                return;
            }
            if (!forceFull && sheet.IsAudioOnly)
            {
                OpenAudioPlayer(sheet, 0);
                return;
            }
            bool full = forceFull || !sheet.CanMountNative;
            if (full)
            {
                if (!_winCDEmuAvailable)
                {
                    ShowToast("warning", "WinCDEmu non installato", "Per montare tracce audio o formati non nativi serve WinCDEmu (gratuito).", "downloadWinCDEmu", "");
                    return;
                }
                if (!File.Exists(sheet.SourcePath))
                {
                    ShowToast("error", "File non trovato", sheet.SourcePath, "", "");
                    return;
                }
            }
            else if (!sheet.CanMountNative)
            {
                ShowToast("warning", "Formato non supportato", "Questo disco non ha una traccia dati montabile nativamente.", "", "");
                return;
            }

            StartBusy(full ? "Montaggio completo" : "Preparo il disco",
                full ? "Avvio WinCDEmu. Se appare una richiesta di autorizzazione, confermala." : "Preparazione dell'immagine dati...",
                full, !full);
            CancellationTokenSource cts = _busyCts;
            CancellationToken token = cts.Token;
            CueSheet sheetLocal = sheet;
            string winCDPath = _winCDEmuPath;

            Task.Run(delegate
            {
                try
                {
                    MountedDisc mounted = null;
                    if (full)
                    {
                        PostBusy(-1, "Avvio WinCDEmu...");
                        string cuePath = sheetLocal.SourcePath;
                        if (sheetLocal.StandaloneBin) cuePath = CreateSyntheticCue(sheetLocal);
                        WinCDEmuMount wm = WinCDEmuMounter.MountCue(cuePath, winCDPath);
                        mounted = new MountedDisc();
                        mounted.EntryId = entry.Id;
                        mounted.Title = entry.Title;
                        mounted.DriveLetter = wm.DriveLetter;
                        mounted.ImagePath = wm.CuePath;
                        mounted.SourcePath = entry.Path;
                        mounted.FullMount = true;
                        mounted.WinCDEmu = wm;
                        mounted.MountedUtc = DateTime.UtcNow;
                    }
                    else
                    {
                        Progress<int> progress = new Progress<int>(delegate(int percent)
                        {
                            BusyProgress(percent, percent < 100 ? "Preparo l'immagine ISO..." : "Collego l'unità virtuale...");
                        });
                        string iso = IsoBuilder.GetOrBuild(sheetLocal, progress, token);
                        token.ThrowIfCancellationRequested();
                        PostBusy(97, "Collego l'unità virtuale...");
                        NativeMount native = NativeMounter.Mount(iso);
                        mounted = new MountedDisc();
                        mounted.EntryId = entry.Id;
                        mounted.Title = entry.Title;
                        mounted.DriveLetter = native.DriveLetter;
                        mounted.ImagePath = iso;
                        mounted.SourcePath = entry.Path;
                        mounted.Native = native;
                        mounted.FullMount = false;
                        mounted.MountedUtc = DateTime.UtcNow;
                    }
                    PostUi(delegate { CompleteMount(mounted, entry); });
                }
                catch (OperationCanceledException)
                {
                    PostUi(delegate
                    {
                        EndBusy();
                        ShowToast("info", "Operazione annullata", "Il disco non è stato montato.", "", "");
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error("Mount " + entry.Path, ex);
                    PostUi(delegate
                    {
                        EndBusy();
                        ShowToast("error", "Montaggio non riuscito", ex.Message, "", "");
                    });
                }
            });
        }

        private void CompleteMount(MountedDisc mounted, DiscEntry entry)
        {
            EndBusy();
            if (mounted == null) return;
            _mounted.Add(mounted);
            _library.TouchMounted(entry);
            RefreshCacheSize();
            RebuildVisible();
            SelectEntryInternal(entry);
            ShowToast("success", "Disco montato",
                entry.Title + " è disponibile in " + mounted.DriveLetter + ":",
                "openDrive", mounted.DriveLetter);
            Invalidate();
        }

        private void UnmountMounted(MountedDisc mount)
        {
            if (mount == null || _busy) return;
            StartBusy("Smonto il disco", mount.Title + " (" + mount.DriveLetter + ":)", true, false);
            Task.Run(delegate
            {
                try
                {
                    if (mount.FullMount)
                        WinCDEmuMounter.Unmount(mount.WinCDEmu);
                    else
                        NativeMounter.Unmount(mount.Native);
                    PostUi(delegate
                    {
                        EndBusy();
                        _mounted.Remove(mount);
                        RebuildVisible();
                        ShowToast("info", "Disco smontato", mount.Title, "", "");
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error("Unmount " + mount.Title, ex);
                    PostUi(delegate
                    {
                        EndBusy();
                        ShowToast("error", "Smontaggio non riuscito", ex.Message, "", "");
                    });
                }
            });
        }

        private void RequestUnmountAll()
        {
            if (_mounted.Count == 0)
            {
                ShowToast("info", "Nessun disco montato", "Non ci sono unità virtuali da smontare.", "", "");
                return;
            }
            if (_busy) return;
            ShowConfirm("Smonta tutti i dischi",
                "Verranno smontate " + _mounted.Count + " unità virtuali.",
                "Smonta tutti", "unmountAll", "");
        }

        private void UnmountAllNow()
        {
            if (_busy) return;
            if (_mounted.Count == 0) return;
            StartBusy("Smonto tutti i dischi", "Attendi...", true, false);
            Task.Run(delegate
            {
                List<MountedDisc> snapshot = new List<MountedDisc>(_mounted);
                List<MountedDisc> succeeded = new List<MountedDisc>();
                List<string> errors = new List<string>();
                for (int i = 0; i < snapshot.Count; i++)
                {
                    try
                    {
                        if (snapshot[i].FullMount) WinCDEmuMounter.Unmount(snapshot[i].WinCDEmu);
                        else NativeMounter.Unmount(snapshot[i].Native);
                        succeeded.Add(snapshot[i]);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(snapshot[i].Title + ": " + ex.Message);
                        Logger.Error("Unmount all " + snapshot[i].Title, ex);
                    }
                }
                int finalRemoved = succeeded.Count;
                List<string> finalErrors = errors;
                PostUi(delegate
                {
                    EndBusy();
                    for (int i = 0; i < succeeded.Count; i++) _mounted.Remove(succeeded[i]);
                    RebuildVisible();
                    if (finalErrors.Count == 0)
                        ShowToast("success", "Smontaggio completato", finalRemoved + " dischi smontati.", "", "");
                    else
                        ShowToast("warning", "Smontaggio parziale", finalRemoved + " dischi smontati · " + finalErrors.Count + " errori: " + finalErrors[0], "", "");
                    Invalidate();
                });
            });
        }
        private void OpenDrive(string letter)
        {
            if (string.IsNullOrEmpty(letter)) return;
            try
            {
                DriveInfo info = new DriveInfo(letter + ":\\");
                if (!info.IsReady)
                {
                    ShowToast("warning", "Unità non pronta", "Windows sta ancora preparando " + letter + ":. Riprova tra un istante.", "", "");
                    return;
                }
            }
            catch { }
            try { Process.Start("explorer.exe", letter + ":\\"); }
            catch (Exception ex) { Logger.Error("Open drive " + letter, ex); }
        }

        private void OpenAudioPlayer(CueSheet sheet, int trackNumber)
        {
            if (sheet == null || !sheet.HasAudio)
            {
                ShowToast("info", "Nessuna traccia audio", "Questo disco non contiene tracce audio.", "", "");
                return;
            }
            try
            {
                if (_audioPlayer != null && !_audioPlayer.IsDisposed)
                {
                    AudioPlayerForm previous = _audioPlayer;
                    _audioPlayer = null;
                    try { previous.Close(); previous.Dispose(); } catch { }
                }
                _audioPlayer = new AudioPlayerForm(sheet, trackNumber);
                _audioPlayer.FormClosed += delegate { _audioPlayer = null; };
                _audioPlayer.Show(this);
            }
            catch (Exception ex)
            {
                Logger.Error("Open audio player", ex);
                ShowToast("error", "Player audio", ex.Message, "", "");
            }
        }

        private void RequestConvertToIso(DiscEntry entry)
        {
            if (_busy || entry == null) return;
            CueSheet sheet = GetSheet(entry);
            if (sheet == null)
            {
                ShowToast("error", "Disco non leggibile", "Impossibile leggere il CUE/BIN.", "", "");
                return;
            }
            if (sheet.Kind == DiscKinds.Iso)
            {
                ShowToast("info", "Formato già ISO", "Questo disco è già un file ISO.", "", "");
                return;
            }
            if (!sheet.CanMountNative)
            {
                ShowToast("warning", "Nessuna traccia dati", "Questo disco non contiene una traccia dati convertibile in ISO.", "", "");
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = sheet.HasAudio
                    ? "Converti in ISO (solo traccia dati, le tracce audio non saranno incluse)"
                    : "Converti in ISO";
                dialog.Filter = "Immagine ISO (*.iso)|*.iso";
                dialog.AddExtension = true;
                dialog.DefaultExt = "iso";
                dialog.OverwritePrompt = true;
                string safe = TextUtil.SanitizeFileName(entry.Title, 90);
                dialog.FileName = safe + ".iso";
                try
                {
                    string dir = Path.GetDirectoryName(entry.Path);
                    if (Directory.Exists(dir)) dialog.InitialDirectory = dir;
                }
                catch { }
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                StartConvertToIso(sheet, dialog.FileName);
            }
        }

        private void StartConvertToIso(CueSheet sheet, string outputPath)
        {
            if (_busy) return;
            StartBusy("Converto in ISO", "Estraggo la traccia dati...", false, true);
            CancellationTokenSource cts = _busyCts;
            CancellationToken token = cts.Token;
            CueSheet sheetLocal = sheet;
            bool hadAudio = sheet.HasAudio;

            Task.Run(delegate
            {
                try
                {
                    Progress<int> extraction = new Progress<int>(delegate(int percent)
                    {
                        BusyProgress((int)(percent * 0.85), "Estraggo la traccia dati...");
                    });
                    string cachedIso = IsoBuilder.GetOrBuild(sheetLocal, extraction, token);
                    token.ThrowIfCancellationRequested();
                    PostBusy(88, "Copio il file ISO...");
                    Progress<int> copy = new Progress<int>(delegate(int percent)
                    {
                        BusyProgress(88 + (int)(percent * 0.12), "Copio il file ISO...");
                    });
                    CopyFileWithProgress(cachedIso, outputPath, copy, token);
                    PostUi(delegate
                    {
                        EndBusy();
                        RefreshCacheSize();
                        string message = Path.GetFileName(outputPath);
                        if (hadAudio) message += " · tracce audio non incluse";
                        ShowToast("success", "ISO creata", message, "openFolder", Path.GetDirectoryName(outputPath));
                    });
                }
                catch (OperationCanceledException)
                {
                    PostUi(delegate
                    {
                        EndBusy();
                        ShowToast("info", "Conversione annullata", "Il file ISO non è stato creato.", "", "");
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error("Convert to ISO " + outputPath, ex);
                    PostUi(delegate
                    {
                        EndBusy();
                        ShowToast("error", "Conversione non riuscita", ex.Message, "", "");
                    });
                }
            });
        }

        private void RequestConvertToCueBin(DiscEntry entry)
        {
            if (_busy || entry == null) return;
            CueSheet sheet = GetSheet(entry);
            if (sheet == null || sheet.Kind != DiscKinds.Iso)
            {
                ShowToast("info", "Conversione non disponibile", "Questa funzione converte un file ISO in CUE/BIN.", "", "");
                return;
            }
            if (!File.Exists(sheet.SourcePath))
            {
                ShowToast("error", "File non trovato", sheet.SourcePath, "", "");
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Converti ISO in CUE/BIN (MODE1/2352, solo dati)";
                dialog.Filter = "CUE sheet (*.cue)|*.cue";
                dialog.AddExtension = true;
                dialog.DefaultExt = "cue";
                dialog.OverwritePrompt = true;
                string safe = TextUtil.SanitizeFileName(entry.Title, 90);
                dialog.FileName = safe + ".cue";
                try
                {
                    string dir = Path.GetDirectoryName(entry.Path);
                    if (Directory.Exists(dir)) dialog.InitialDirectory = dir;
                }
                catch { }
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                string cuePath = dialog.FileName;
                string binPath = Path.ChangeExtension(cuePath, ".bin");
                if (File.Exists(binPath))
                {
                    ShowToast("warning", "File BIN già esistente",
                        "Scegli un altro nome per il CUE: il BIN corrispondente esiste già.", "", "");
                    return;
                }
                StartConvertToCueBin(sheet.SourcePath, cuePath);
            }
        }

        private void StartConvertToCueBin(string isoPath, string cuePath)
        {
            if (_busy) return;
            StartBusy("Converto in CUE/BIN", "Genero i settori MODE1/2352...", false, true);
            CancellationTokenSource cts = _busyCts;
            CancellationToken token = cts.Token;

            Task.Run(delegate
            {
                try
                {
                    Progress<int> progress = new Progress<int>(delegate(int percent)
                    {
                        BusyProgress(percent, "Genero i settori MODE1/2352...");
                    });
                    ImageConverter.IsoToCueBin(isoPath, cuePath, progress, token);
                    PostUi(delegate
                    {
                        EndBusy();
                        ShowToast("success", "CUE/BIN creati",
                            Path.GetFileName(cuePath) + " + " + Path.GetFileName(Path.ChangeExtension(cuePath, ".bin")),
                            "openFolder", Path.GetDirectoryName(cuePath));
                    });
                }
                catch (OperationCanceledException)
                {
                    PostUi(delegate
                    {
                        EndBusy();
                        ShowToast("info", "Conversione annullata", "I file non sono stati creati.", "", "");
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error("Convert to CUE/BIN " + cuePath, ex);
                    PostUi(delegate
                    {
                        EndBusy();
                        ShowToast("error", "Conversione non riuscita", ex.Message, "", "");
                    });
                }
            });
        }

        private static void CopyFileWithProgress(string source, string destination, IProgress<int> progress, CancellationToken token)
        {
            string temp = destination + ".part";
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            try
            {
                long length = new FileInfo(source).Length;
                long done = 0;
                int lastPercent = -1;
                using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 1024, FileOptions.SequentialScan))
                using (FileStream output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024))
                {
                    byte[] buffer = new byte[1024 * 1024];
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        int read = input.Read(buffer, 0, buffer.Length);
                        if (read <= 0) break;
                        output.Write(buffer, 0, read);
                        done += read;
                        if (progress != null)
                        {
                            int percent = (int)Math.Min(99, done * 100L / Math.Max(1, length));
                            if (percent != lastPercent) { lastPercent = percent; progress.Report(percent); }
                        }
                    }
                    output.Flush();
                }
                token.ThrowIfCancellationRequested();
                if (File.Exists(destination))
                {
                    File.SetAttributes(destination, FileAttributes.Normal);
                    File.Delete(destination);
                }
                File.Move(temp, destination);
                if (progress != null) progress.Report(100);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                throw;
            }
        }

        private void RequestExportAudio(CueSheet sheet)
        {
            if (sheet == null || !sheet.HasAudio)
            {
                ShowToast("info", "Nessuna traccia audio", "Questo disco non contiene tracce audio.", "", "");
                return;
            }
            if (_busy) return;
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Scegli la cartella in cui esportare le tracce audio (WAV)";
                dialog.ShowNewFolderButton = true;
                try { if (Directory.Exists(sheet.BaseDirectory)) dialog.SelectedPath = sheet.BaseDirectory; } catch { }
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string folder = dialog.SelectedPath;
                StartBusy("Esporto le tracce audio", "Preparazione...", false, true);
                CancellationTokenSource cts = _busyCts;
                CancellationToken token = cts.Token;
                CueSheet sheetLocal = sheet;
                Progress<AudioExtractProgress> progress = new Progress<AudioExtractProgress>(delegate(AudioExtractProgress p)
                {
                    BusyProgress(p.Percent, "Traccia " + p.TrackNumber.ToString("00") + ": " + p.FileName);
                });
                Task.Run(delegate
                {
                    try
                    {
                        List<string> files = AudioTools.ExportAllAudio(sheetLocal, folder, progress, token);
                        PostUi(delegate
                        {
                            EndBusy();
                            ShowToast("success", "Esportazione completata", files.Count + " tracce WAV esportate.", "openFolder", folder);
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        PostUi(delegate { EndBusy(); ShowToast("info", "Esportazione annullata", "Le tracce già scritte sono state mantenute.", "", ""); });
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Export audio", ex);
                        PostUi(delegate { EndBusy(); ShowToast("error", "Esportazione non riuscita", ex.Message, "", ""); });
                    }
                });
            }
        }

        private string CreateSyntheticCue(CueSheet sheet)
        {
            if (sheet == null || sheet.Kind != DiscKinds.Bin) return sheet != null ? sheet.SourcePath : "";
            try
            {
                AppPaths.Ensure();
                string sourceKey = sheet.SourcePath + "|" + new FileInfo(sheet.SourcePath).Length + "|" + new FileInfo(sheet.SourcePath).LastWriteTimeUtc.Ticks;
                uint sourceHash = 2166136261;
                for (int i = 0; i < sourceKey.Length; i++) { sourceHash ^= sourceKey[i]; sourceHash *= 16777619; }
                string name = "synthetic_" + sourceHash.ToString("x8") + ".cue";
                string path = Path.Combine(AppPaths.CacheDir, name);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("FILE \"" + sheet.SourcePath.Replace("\"", "\"\"") + "\" BINARY");
                for (int i = 0; i < sheet.Tracks.Count; i++)
                {
                    CueTrack t = sheet.Tracks[i];
                    sb.AppendLine("  TRACK " + t.Number.ToString("00") + " " + (string.IsNullOrEmpty(t.CueType) ? "MODE1/2352" : t.CueType));
                    long start = t.StartSector;
                    long m = start / (75 * 60);
                    long rem = start % (75 * 60);
                    long s = rem / 75;
                    long f = rem % 75;
                    sb.AppendLine("    INDEX 01 " + m.ToString("00") + ":" + s.ToString("00") + ":" + f.ToString("00"));
                }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                return path;
            }
            catch (Exception ex)
            {
                Logger.Error("Create synthetic cue", ex);
                return sheet.SourcePath;
            }
        }

        private void StartBusy(string title, string detail, bool indeterminate, bool canCancel)
        {
            _busy = true;
            _busyTitle = title;
            _busyDetail = detail;
            _busyProgress = 0;
            _busyIndeterminate = indeterminate;
            _busyCanCancel = canCancel;
            if (_busyCts != null)
            {
                try { _busyCts.Dispose(); } catch { }
            }
            _busyCts = new CancellationTokenSource();
            _settingsOpen = false;
            _confirmOpen = false;
            _promptOpen = false;
            if (_spinTimer != null)
            {
                if (indeterminate) _spinTimer.Start();
                else _spinTimer.Stop();
            }
            UpdateChildControls();
            Invalidate();
        }

        private void EndBusy()
        {
            _busy = false;
            _busyCanCancel = false;
            _busyIndeterminate = false;
            if (_spinTimer != null) _spinTimer.Stop();
            CancellationTokenSource cts = _busyCts;
            _busyCts = null;
            if (cts != null)
            {
                try { cts.Dispose(); } catch { }
            }
            UpdateChildControls();
            Invalidate();
        }

        private void PostBusy(int percent, string detail)
        {
            PostUi(delegate
            {
                if (percent >= 0) _busyProgress = percent;
                if (!string.IsNullOrEmpty(detail)) _busyDetail = detail;
                Invalidate();
            });
        }

        private void BusyProgress(int percent, string detail)
        {
            if (IsDisposed) return;
            _busyProgress = percent;
            if (!string.IsNullOrEmpty(detail)) _busyDetail = detail;
            Invalidate();
        }

        private void CancelBusy()
        {
            if (_busyCts != null && _busyCanCancel)
            {
                try { _busyCts.Cancel(); } catch { }
                _busyDetail = "Annullamento in corso...";
                Invalidate();
            }
        }

        private void ShowToast(string kind, string title, string message, string action, string arg)
        {
            ShowToast(kind, title, message, action, arg, "");
        }

        private void ShowToast(string kind, string title, string message, string action, string arg, string actionLabel)
        {
            _toast = new ToastInfo();
            _toast.Kind = kind;
            _toast.Title = title ?? "";
            _toast.Message = message ?? "";
            _toast.Action = action ?? "";
            _toast.Arg = arg ?? "";
            _toast.ActionLabel = string.IsNullOrEmpty(actionLabel) ? DefaultActionLabel(action) : actionLabel;
            _toast.ExpiresUtc = DateTime.UtcNow.AddSeconds(kind == "error" ? 8 : 5);
            if (_toastTimer != null) _toastTimer.Start();
            Invalidate();
        }

        private string DefaultActionLabel(string action)
        {
            if (action == "downloadWinCDEmu") return "Scarica WinCDEmu";
            if (action == "openDrive") return "Apri unità";
            if (action == "openFolder") return "Apri cartella";
            if (action == "openSettings") return "Impostazioni";
            return "";
        }

        private void DismissToast()
        {
            _toast = null;
            if (_toastTimer != null) _toastTimer.Stop();
            Invalidate();
        }

        private void ShowConfirm(string title, string message, string yesLabel, string action, string arg)
        {
            _confirmTitle = title;
            _confirmMessage = message;
            _confirmYesLabel = yesLabel;
            _confirmAction = action;
            _confirmArg = arg;
            _confirmOpen = true;
            UpdateChildControls();
            Invalidate();
        }

        private void CloseConfirm()
        {
            _confirmOpen = false;
            _confirmAction = "";
            _confirmArg = "";
            UpdateChildControls();
            Invalidate();
        }

        private void ShowRename(DiscEntry entry)
        {
            if (entry == null) return;
            _promptTitle = "Rinomina disco";
            _promptInitial = entry.Title;
            _promptHint = "Il nome viene salvato solo nella libreria di CueDeck.";
            _promptCallback = delegate(string value)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    entry.Title = value.Trim();
                    _library.Update(entry);
                    RebuildVisible();
                    ShowToast("info", "Titolo aggiornato", entry.Title, "", "");
                }
            };
            _promptOpen = true;
            EnsurePromptBox();
            _promptBox.Text = _promptInitial;
            _promptBox.Visible = true;
            UpdateChildControls();
            _promptBox.Focus();
            _promptBox.SelectAll();
            Invalidate();
        }

        private void EnsurePromptBox()
        {
            if (_promptBox != null) return;
            _promptBox = new TextBox();
            _promptBox.BorderStyle = BorderStyle.None;
            _promptBox.BackColor = Theme.Surface2;
            _promptBox.ForeColor = Theme.Text;
            _promptBox.Font = Body(14f);
            _promptBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    CommitPrompt();
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    ClosePrompt();
                    e.SuppressKeyPress = true;
                }
            };
            Controls.Add(_promptBox);
            _promptBox.Visible = false;
        }

        private void CommitPrompt()
        {
            string value = _promptBox != null ? _promptBox.Text.Trim() : "";
            Action<string> callback = _promptCallback;
            ClosePrompt();
            if (callback != null) callback(value);
        }

        private void ClosePrompt()
        {
            _promptOpen = false;
            _promptCallback = null;
            if (_promptBox != null) _promptBox.Visible = false;
            UpdateChildControls();
            Invalidate();
        }

        private void OpenSettings()
        {
            if (_busy) return;
            RefreshCacheSize();
            RefreshWinCDEmu();
            _settingsOpen = true;
            UpdateChildControls();
            Invalidate();
        }

        private void CloseSettings()
        {
            _settingsOpen = false;
            UpdateChildControls();
            Invalidate();
        }

        private void SetCover(DiscEntry entry)
        {
            if (entry == null) return;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Scegli una copertina";
                dialog.Filter = "Immagini|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Tutti i file|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    CoverStore.SetCustom(entry, dialog.FileName);
                    _library.Update(entry);
                    _autoCovers.Remove(entry.Id);
                    Invalidate();
                    ShowToast("success", "Copertina aggiornata", Path.GetFileName(dialog.FileName), "", "");
                }
                catch (Exception ex)
                {
                    ShowToast("error", "Copertina non valida", ex.Message, "", "");
                }
            }
        }

        private void RemoveCover(DiscEntry entry)
        {
            if (entry == null) return;
            CoverStore.RemoveCustom(entry);
            _library.Update(entry);
            _autoCovers.Remove(entry.Id);
            Invalidate();
        }

        private void AddFiles()
        {
            if (_busy) return;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Aggiungi dischi";
                dialog.Multiselect = true;
                dialog.Filter = "Immagini disco|*.cue;*.bin;*.iso|CUE sheet|*.cue|BIN raw|*.bin|ISO|*.iso|Tutti i file|*.*";
                if (dialog.ShowDialog(this) == DialogResult.OK) PerformAdd(dialog.FileNames);
            }
        }

        private void AddFolder()
        {
            if (_busy) return;
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Scegli una cartella da scansionare per file CUE, BIN e ISO";
                dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog(this) == DialogResult.OK) PerformAdd(new string[] { dialog.SelectedPath });
            }
        }

        private void PerformAdd(IEnumerable<string> paths)
        {
            if (paths == null) return;
            Cursor previous = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                AddSummary summary = _library.AddPaths(paths);
                RebuildVisible();
                if (summary.NewEntries.Count > 0)
                {
                    SelectEntryInternal(summary.NewEntries[0]);
                    EnsureSelectedVisible();
                }
                if (summary.Added > 0)
                {
                    string message = summary.Added + (summary.Added == 1 ? " disco aggiunto" : " dischi aggiunti");
                    if (summary.Existing > 0) message += " · " + summary.Existing + " già presenti";
                    if (summary.Errors.Count > 0) message += " · " + summary.Errors.Count + " errori";
                    ShowToast(summary.Errors.Count > 0 ? "warning" : "success", "Libreria aggiornata", message, "", "");
                }
                else if (summary.Existing > 0)
                {
                    ShowToast("info", "Già in libreria", summary.Existing + " dischi erano già presenti.", "", "");
                }
                else if (summary.Errors.Count > 0)
                {
                    ShowToast("error", "Aggiunta non riuscita", summary.Errors[0], "", "");
                }
                else
                {
                    ShowToast("warning", "Nessun disco trovato", "Seleziona file .cue, .bin o .iso.", "", "");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Perform add", ex);
                ShowToast("error", "Errore", ex.Message, "", "");
            }
            finally
            {
                Cursor = previous;
                Invalidate();
            }
        }

        private void RequestRemove(DiscEntry entry)
        {
            if (entry == null) return;
            string extra = FindMountForEntry(entry.Id) != null ? "\r\n\r\nIl disco è montato: rimarrà montato fino a quando non lo smonti." : "";
            ShowConfirm("Rimuovi dalla libreria",
                "Vuoi rimuovere \"" + entry.Title + "\"?" + extra,
                "Rimuovi", "removeEntry", entry.Id);
        }

        private void RemoveEntryById(string id)
        {
            DiscEntry entry = _library.FindById(id);
            if (entry == null) return;
            _library.Remove(entry);
            if (_selected != null && _selected.Id == id)
            {
                _selected = null;
                _selectedSheet = null;
                _selectedError = "";
                _selectedIndex = -1;
            }
            RebuildVisible();
            ShowToast("info", "Disco rimosso", entry.Title, "", "");
        }

        private void SetFilter(string filter)
        {
            _filter = NormalizeFilter(filter);
            _settings.Data.LastFilter = _filter;
            _libScroll = 0;
            RebuildVisible();
        }

        private static string NormalizeFilter(string filter)
        {
            if (filter == "mounted" || filter == "favorites" || filter == "data" || filter == "audio") return filter;
            return "all";
        }

        private string FilterTitle()
        {
            if (_filter == "mounted") return "Dischi montati";
            if (_filter == "favorites") return "Preferiti";
            if (_filter == "data") return "CD dati";
            if (_filter == "audio") return "CD audio";
            return "Tutti i dischi";
        }

        private int FilterCount()
        {
            if (_filter == "mounted") return _countMounted;
            if (_filter == "favorites") return _countFavorites;
            if (_filter == "data") return _countData;
            if (_filter == "audio") return _countAudio;
            return _countAll;
        }

        private void SetSort(DiscSort sort)
        {
            _sort = sort;
            _settings.Data.SortMode = (int)sort;
            SortVisible();
            _selectedIndex = _selected != null ? _visible.IndexOf(_selected) : -1;
            Invalidate();
        }

        private string SortLabel()
        {
            if (_sort == DiscSort.RecentlyAdded) return "Recenti";
            if (_sort == DiscSort.RecentlyMounted) return "Ultimi montati";
            if (_sort == DiscSort.Size) return "Dimensione";
            return "Nome";
        }

        private void NavigateSelection(int delta)
        {
            if (_visible.Count == 0) return;
            int index = _selectedIndex;
            if (index < 0) index = 0;
            else index = UiUtil.Clamp(index + delta, 0, _visible.Count - 1);
            DiscEntry entry = _visible[index];
            SelectEntry(entry);
            EnsureSelectedVisible();
        }

        private void ToggleFavorite(DiscEntry entry)
        {
            if (entry == null) return;
            _library.SetFavorite(entry, !entry.Favorite);
            RefreshCounts();
            Invalidate();
            ShowToast("info", entry.Favorite ? "Aggiunto ai preferiti" : "Rimosso dai preferiti", entry.Title, "", "");
        }

        private void PrimaryAction(DiscEntry entry)
        {
            if (entry == null) return;
            MountedDisc mount = FindMountForEntry(entry.Id);
            if (mount != null)
            {
                OpenDrive(mount.DriveLetter);
                return;
            }
            CueSheet sheet = GetSheet(entry);
            if (sheet == null) return;
            if (sheet.IsAudioOnly)
            {
                OpenAudioPlayer(sheet, 0);
                return;
            }
            MountEntry(entry, false);
        }

        private void DetailPrimaryAction()
        {
            if (_selected == null) return;
            MountedDisc mount = FindMountForEntry(_selected.Id);
            if (mount != null)
            {
                UnmountMounted(mount);
                return;
            }
            if (_selectedSheet != null && _selectedSheet.IsAudioOnly)
            {
                OpenAudioPlayer(_selectedSheet, 0);
                return;
            }
            if (_selectedSheet != null && !_selectedSheet.CanMountNative && !_winCDEmuAvailable)
            {
                ShowToast("warning", "WinCDEmu non installato", "Serve WinCDEmu per montare questo formato o le tracce audio.", "downloadWinCDEmu", "");
                return;
            }
            MountEntry(_selected, _selectedSheet != null && !_selectedSheet.CanMountNative);
        }

        private void HandleStartupItems()
        {
            if (_startupHandled) return;
            _startupHandled = true;
            if (_startupItems.Count == 0) return;
            List<string> paths = new List<string>();
            for (int i = 0; i < _startupItems.Count; i++) paths.Add(_startupItems[i].Path);
            AddSummary summary = _library.AddPaths(paths);
            RebuildVisible();
            DiscEntry toSelect = summary.NewEntries.Count > 0 ? summary.NewEntries[0] : null;
            DiscEntry toMount = null;
            for (int i = 0; i < _startupItems.Count; i++)
            {
                if (!_startupItems[i].Mount) continue;
                DiscEntry e = _library.FindByPath(_startupItems[i].Path);
                if (e != null) { toMount = e; break; }
            }
            if (toMount == null) toMount = toSelect;
            if (toMount != null) SelectEntryInternal(toMount);
            else if (toSelect != null) SelectEntryInternal(toSelect);
            if (toMount != null && _startupItems.Exists(delegate(StartupItem item) { return item.Mount; }))
            {
                DiscEntry mountEntry = toMount;
                BeginInvoke(new MethodInvoker(delegate { MountEntry(mountEntry, false); }));
            }
        }

        private ContextMenuStrip MakeMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = new DarkMenuRenderer();
            menu.BackColor = Theme.Surface;
            menu.ForeColor = Theme.Text;
            menu.Font = Body(13f);
            menu.ShowImageMargin = false;
            menu.Padding = new Padding(4);
            return menu;
        }

        private ToolStripMenuItem MakeMenuItem(string text, EventHandler handler)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.ForeColor = Theme.Text;
            item.BackColor = Theme.Surface;
            item.Padding = new Padding(8, 3, 8, 3);
            if (handler != null) item.Click += handler;
            return item;
        }

        private void ShowSortMenu()
        {
            ContextMenuStrip menu = MakeMenu();
            ToolStripMenuItem name = MakeMenuItem("Nome (A-Z)", delegate { SetSort(DiscSort.Name); });
            ToolStripMenuItem added = MakeMenuItem("Aggiunti di recente", delegate { SetSort(DiscSort.RecentlyAdded); });
            ToolStripMenuItem mounted = MakeMenuItem("Montati di recente", delegate { SetSort(DiscSort.RecentlyMounted); });
            ToolStripMenuItem size = MakeMenuItem("Dimensione", delegate { SetSort(DiscSort.Size); });
            name.Checked = _sort == DiscSort.Name;
            added.Checked = _sort == DiscSort.RecentlyAdded;
            mounted.Checked = _sort == DiscSort.RecentlyMounted;
            size.Checked = _sort == DiscSort.Size;
            menu.Items.Add(name);
            menu.Items.Add(added);
            menu.Items.Add(mounted);
            menu.Items.Add(size);
            menu.Show(this, new Point((int)_sortRect.X, (int)_sortRect.Bottom + 4));
        }

        private void ShowAddMenu()
        {
            ContextMenuStrip menu = MakeMenu();
            ToolStripMenuItem addFile = MakeMenuItem("Aggiungi file...", delegate { AddFiles(); });
            addFile.ShortcutKeyDisplayString = "Ctrl+O";
            menu.Items.Add(addFile);
            menu.Items.Add(MakeMenuItem("Aggiungi cartella...", delegate { AddFolder(); }));
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem unmount = MakeMenuItem("Smonta tutti", delegate { RequestUnmountAll(); });
            unmount.Enabled = _mounted.Count > 0;
            menu.Items.Add(unmount);
            menu.Items.Add(MakeMenuItem("Impostazioni...", delegate { OpenSettings(); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MakeMenuItem("Esci", delegate { ExitApplication(); }));
            menu.Show(this, new Point((int)_addRect.X, (int)_addRect.Bottom + 6));
        }

        private void ShowCardMenu(DiscEntry entry, Point screenPoint)
        {
            if (entry == null) return;
            ContextMenuStrip menu = MakeMenu();
            MountedDisc mount = FindMountForEntry(entry.Id);
            CueSheet sheet = GetSheet(entry);
            if (mount != null)
            {
                menu.Items.Add(MakeMenuItem("Apri unità " + mount.DriveLetter + ":", delegate { OpenDrive(mount.DriveLetter); }));
                menu.Items.Add(MakeMenuItem(mount.FullMount ? "Smonta (WinCDEmu)" : "Smonta", delegate { UnmountMounted(mount); }));
            }
            else if (sheet != null && sheet.IsAudioOnly)
            {
                menu.Items.Add(MakeMenuItem("Riproduci CD audio", delegate { OpenAudioPlayer(sheet, 0); }));
                ToolStripMenuItem virtualDisc = MakeMenuItem("Monta come CD virtuale", delegate { MountEntry(entry, true); });
                virtualDisc.Enabled = _winCDEmuAvailable;
                menu.Items.Add(virtualDisc);
            }
            else if (sheet != null && sheet.HasData && !sheet.CanMountNative)
            {
                ToolStripMenuItem full = MakeMenuItem("Monta con WinCDEmu", delegate { MountEntry(entry, true); });
                full.Enabled = _winCDEmuAvailable;
                menu.Items.Add(full);
            }
            else
            {
                menu.Items.Add(MakeMenuItem("Monta disco", delegate { MountEntry(entry, false); }));
                if (sheet != null && sheet.HasAudio)
                {
                    ToolStripMenuItem full = MakeMenuItem("Monta completo con WinCDEmu", delegate { MountEntry(entry, true); });
                    full.Enabled = _winCDEmuAvailable;
                    menu.Items.Add(full);
                }
            }
            if (sheet != null && sheet.HasAudio)
            {
                if (!(sheet.IsAudioOnly && mount == null))
                    menu.Items.Add(MakeMenuItem("Riproduci audio", delegate { OpenAudioPlayer(sheet, 0); }));
                menu.Items.Add(MakeMenuItem("Esporta tracce audio...", delegate { RequestExportAudio(sheet); }));
            }
            if (sheet != null && sheet.Kind != DiscKinds.Iso && sheet.CanMountNative)
            {
                menu.Items.Add(MakeMenuItem("Converti in ISO...", delegate { RequestConvertToIso(entry); }));
            }
            if (sheet != null && sheet.Kind == DiscKinds.Iso)
            {
                menu.Items.Add(MakeMenuItem("Converti in CUE/BIN...", delegate { RequestConvertToCueBin(entry); }));
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MakeMenuItem(entry.Favorite ? "Rimuovi dai preferiti" : "Aggiungi ai preferiti", delegate { ToggleFavorite(entry); }));
            menu.Items.Add(MakeMenuItem("Imposta copertina...", delegate { SetCover(entry); }));
            if (!string.IsNullOrEmpty(entry.CoverPath))
                menu.Items.Add(MakeMenuItem("Rimuovi copertina", delegate { RemoveCover(entry); }));
            menu.Items.Add(MakeMenuItem("Rinomina...", delegate { ShowRename(entry); }));
            menu.Items.Add(MakeMenuItem("Rivela nel file system", delegate { UiUtil.OpenFolder(entry.Path); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MakeMenuItem("Rimuovi dalla libreria", delegate { RequestRemove(entry); }));
            menu.Show(screenPoint);
        }
        private void ApplyDarkTitleBar()
        {
            try
            {
                if (!IsHandleCreated) return;
                int one = 1;
                int result = DwmSetWindowAttribute(Handle, 20, ref one, 4);
                if (result != 0) DwmSetWindowAttribute(Handle, 19, ref one, 4);
                int caption = 0x00150F0C;
                int textColor = 0x00FCF7F4;
                DwmSetWindowAttribute(Handle, 35, ref caption, 4);
                DwmSetWindowAttribute(Handle, 36, ref textColor, 4);
                int corners = 2;
                DwmSetWindowAttribute(Handle, 33, ref corners, 4);
            }
            catch { }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private void ShowMainWindow()
        {
            try
            {
                ShowInTaskbar = true;
                Show();
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
                BringToFront();
                UpdateChildControls();
            }
            catch { }
        }

        private void HideToTray()
        {
            SaveWindowState();
            _settings.Save();
            ShowInTaskbar = false;
            Hide();
            if (!_settings.Data.TrayHintShown)
            {
                _settings.Data.TrayHintShown = true;
                _settings.Save();
                try
                {
                    _trayIcon.ShowBalloonTip(3500, "CueDeck è ancora attivo",
                        "I dischi montati restano disponibili. Fai doppio clic sull'icona per riaprire.",
                        ToolTipIcon.Info);
                }
                catch { }
            }
        }

        private void ExitApplication()
        {
            _reallyExit = true;
            Close();
        }

        private void UnmountOnExit()
        {
            for (int i = 0; i < _mounted.Count; i++)
            {
                try
                {
                    if (!_mounted[i].FullMount) NativeMounter.Unmount(_mounted[i].Native);
                }
                catch (Exception ex) { Logger.Error("Unmount on exit", ex); }
            }
            _mounted.Clear();
        }

        private void DisposeTray()
        {
            try
            {
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    if (_trayIcon.ContextMenuStrip != null) _trayIcon.ContextMenuStrip.Dispose();
                    _trayIcon.Dispose();
                    _trayIcon = null;
                }
            }
            catch { }
        }

        private void HandleConfirmAction()
        {
            string action = _confirmAction;
            string arg = _confirmArg;
            CloseConfirm();
            if (action == "unmountAll") UnmountAllNow();
            else if (action == "removeEntry") RemoveEntryById(arg);
            else if (action == "clearCache") ClearCacheNow();
            else if (action == "removeCover") { DiscEntry e = _library.FindById(arg); if (e != null) RemoveCover(e); }
        }

        private void ClearCacheNow()
        {
            for (int i = 0; i < _mounted.Count; i++)
            {
                if (!_mounted[i].FullMount)
                {
                    ShowToast("warning", "Cache in uso", "Smonta prima i dischi montati nella modalità nativa.", "", "");
                    return;
                }
            }
            long freed;
            int count = IsoBuilder.ClearCache(out freed);
            try
            {
                if (Directory.Exists(AppPaths.PreviewDir))
                {
                    string[] previews = Directory.GetFiles(AppPaths.PreviewDir, "*", SearchOption.TopDirectoryOnly);
                    for (int i = 0; i < previews.Length; i++)
                    {
                        try { File.Delete(previews[i]); } catch { }
                    }
                }
            }
            catch { }
            RefreshCacheSize();
            ShowToast("success", "Cache svuotata", count + " file rimossi · " + TextUtil.FormatBytes(freed) + " liberati.", "", "");
            Invalidate();
        }

        private void OpenWinCDEmuDownload()
        {
            UiUtil.OpenUrl(WinCDEmuMounter.DownloadUrl);
        }

        private void BrowseWinCDEmu()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Seleziona l'eseguibile di WinCDEmu";
                dialog.Filter = "WinCDEmu|vmnt64.exe;vmnt.exe;BatchMnt.exe;batchmnt.exe;PortableWinCDEmu.exe|Eseguibili|*.exe|Tutti i file|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _settings.Data.WinCDEmuPath = dialog.FileName;
                _settings.Save();
                RefreshWinCDEmu();
                Invalidate();
                ShowToast(_winCDEmuAvailable ? "success" : "warning", "WinCDEmu",
                    _winCDEmuAvailable ? "Percorso salvato." : "Eseguibile non valido.", "", "");
            }
        }

        private void Dispatch(string action, string arg)
        {
            if (string.IsNullOrEmpty(action)) return;
            if (action == "nav") { SetFilter(arg); return; }
            if (action == "add") { ShowAddMenu(); return; }
            if (action == "settings") { OpenSettings(); return; }
            if (action == "sort") { ShowSortMenu(); return; }
            if (action == "scan") { AddFolder(); return; }
            if (action == "emptyAdd") { AddFiles(); return; }
            if (action == "emptyScan") { AddFolder(); return; }
            if (action == "resetFilters") { _filter = "all"; _settings.Data.LastFilter = "all"; _search = ""; ClearSearch(); return; }
            if (action == "selectCard")
            {
                DiscEntry e = _library.FindById(arg);
                if (e != null) SelectEntry(e);
                return;
            }
            if (action == "cardAction")
            {
                DiscEntry e = _library.FindById(arg);
                if (e == null) return;
                SelectEntryInternal(e);
                MountedDisc m = FindMountForEntry(e.Id);
                if (m != null) OpenDrive(m.DriveLetter);
                else
                {
                    CueSheet s = GetSheet(e);
                    if (s != null && s.IsAudioOnly) OpenAudioPlayer(s, 0);
                    else MountEntry(e, false);
                }
                return;
            }
            if (action == "favorite")
            {
                DiscEntry favoriteEntry = _library.FindById(arg);
                if (favoriteEntry != null) SelectEntryInternal(favoriteEntry);
                ToggleFavorite(favoriteEntry);
                return;
            }
            if (action == "convertToIso")
            {
                RequestConvertToIso(_library.FindById(arg));
                return;
            }
            if (action == "convertToCueBin")
            {
                RequestConvertToCueBin(_library.FindById(arg));
                return;
            }
            if (action == "detailPrimary") { DetailPrimaryAction(); return; }
            if (action == "detailFullMount")
            {
                if (_selected != null) MountEntry(_selected, true);
                return;
            }
            if (action == "detailOpen")
            {
                if (_selected != null)
                {
                    MountedDisc m = FindMountForEntry(_selected.Id);
                    if (m != null) OpenDrive(m.DriveLetter);
                }
                return;
            }
            if (action == "detailAudio")
            {
                if (_selectedSheet != null) OpenAudioPlayer(_selectedSheet, 0);
                return;
            }
            if (action == "detailExport")
            {
                if (_selectedSheet != null) RequestExportAudio(_selectedSheet);
                return;
            }
            if (action == "detailReveal")
            {
                if (_selected != null) UiUtil.OpenFolder(_selected.Path);
                return;
            }
            if (action == "detailCover")
            {
                if (_selected != null) SetCover(_selected);
                return;
            }
            if (action == "detailFavorite")
            {
                if (_selected != null) ToggleFavorite(_selected);
                return;
            }
            if (action == "trackPlay")
            {
                if (_selectedSheet != null)
                {
                    int number;
                    int.TryParse(arg, out number);
                    OpenAudioPlayer(_selectedSheet, number);
                }
                return;
            }
            if (action == "eject")
            {
                MountedDisc m = FindMountByLetter(arg);
                if (m != null) UnmountMounted(m);
                return;
            }
            if (action == "driveOpen") { OpenDrive(arg); return; }
            if (action == "openMounted") { SetFilter("mounted"); return; }
            if (action == "openSettings") { OpenSettings(); return; }
            if (action == "settingsClose") { CloseSettings(); return; }
            if (action == "toggleTray")
            {
                _settings.Data.CloseToTray = !_settings.Data.CloseToTray;
                _settings.Save();
                Invalidate();
                return;
            }
            if (action == "browseWinCDEmu") { BrowseWinCDEmu(); return; }
            if (action == "detectWinCDEmu")
            {
                RefreshWinCDEmu();
                ShowToast(_winCDEmuAvailable ? "success" : "warning", "WinCDEmu",
                    _winCDEmuAvailable ? "Rilevato: " + _winCDEmuPath : "Non trovato. Scarica la versione portable dal sito ufficiale.", "", "");
                Invalidate();
                return;
            }
            if (action == "downloadWinCDEmu") { OpenWinCDEmuDownload(); return; }
            if (action == "openCache") { UiUtil.OpenFolder(AppPaths.CacheDir); return; }
            if (action == "openDataFolder") { UiUtil.OpenFolder(AppPaths.Root); return; }
            if (action == "clearCache")
            {
                ShowConfirm("Svuota la cache ISO", "Verranno eliminati i file ISO temporanei e le anteprime audio. I dischi originali non vengono toccati.", "Svuota cache", "clearCache", "");
                return;
            }
            if (action == "confirmYes") { HandleConfirmAction(); return; }
            if (action == "confirmNo") { CloseConfirm(); return; }
            if (action == "promptSave") { CommitPrompt(); return; }
            if (action == "promptCancel") { ClosePrompt(); return; }
            if (action == "busyCancel") { CancelBusy(); return; }
            if (action == "toastClose") { DismissToast(); return; }
            if (action == "toastAction")
            {
                ToastInfo toast = _toast;
                DismissToast();
                if (toast == null) return;
                if (toast.Action == "openDrive") OpenDrive(toast.Arg);
                else if (toast.Action == "openFolder") UiUtil.OpenFolder(toast.Arg);
                else if (toast.Action == "downloadWinCDEmu") OpenWinCDEmuDownload();
                else if (toast.Action == "openSettings") OpenSettings();
                else Dispatch(toast.Action, toast.Arg);
                return;
            }
            if (action == "removeEntry") { RequestRemove(_library.FindById(arg)); return; }
            if (action == "rename") { ShowRename(_library.FindById(arg)); return; }
            if (action == "unmountAll") { RequestUnmountAll(); return; }
            if (action == "exit") { ExitApplication(); return; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _scale = Math.Max(1f, DeviceDpi / 96f);
            RecalcLayout();
            MinimumSize = new Size((int)S(960), (int)S(620));
            UpdateChildControls();
            if (!IsOnAnyScreen(Bounds))
            {
                StartPosition = FormStartPosition.CenterScreen;
                Location = new Point(0, 0);
                CenterToScreen();
            }
            HandleStartupItems();
            ActiveControl = null;
            if (!_searchBox.Focused && _searchBox.Text.Length == 0) SetSearchPlaceholder();
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyDarkTitleBar();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            _scale = Math.Max(1f, DeviceDpi / 96f);
            MinimumSize = new Size((int)S(960), (int)S(620));
            if (_searchBox != null) _searchBox.Font = Body(13.5f);
            if (_promptBox != null) _promptBox.Font = Body(14f);
            UpdateChildControls();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateChildControls();
            Invalidate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_reallyExit && _settings.Data.CloseToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            SaveWindowState();
            _settings.Data.LastFilter = _filter;
            _settings.Data.SortMode = (int)_sort;
            _settings.Save();
            UnmountOnExit();
            DisposeTray();
            try { _imageCache.Dispose(); } catch { }
            base.OnFormClosing(e);
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                _dragOver = true;
                e.Effect = DragDropEffects.Copy;
                Invalidate();
            }
        }

        protected override void OnDragLeave(EventArgs e)
        {
            base.OnDragLeave(e);
            _dragOver = false;
            Invalidate();
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            _dragOver = false;
            Invalidate();
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null) PerformAdd(files);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F))
            {
                FocusSearch();
                return true;
            }
            if (keyData == (Keys.Control | Keys.O))
            {
                AddFiles();
                return true;
            }
            if (keyData == Keys.Escape)
            {
                if (_busy && _busyCanCancel) { CancelBusy(); return true; }
                if (_promptOpen) { ClosePrompt(); return true; }
                if (_confirmOpen) { CloseConfirm(); return true; }
                if (_settingsOpen) { CloseSettings(); return true; }
                if (_searchBox != null && _searchBox.Focused)
                {
                    if (_searchBox.Text.Length > 0) ClearSearch();
                    else BeginInvoke(new MethodInvoker(delegate { ActiveControl = null; }));
                    return true;
                }
                if (_toast != null) { DismissToast(); return true; }
                return true;
            }
            if (_promptOpen || _busy || _confirmOpen || _settingsOpen) return base.ProcessCmdKey(ref msg, keyData);
            bool searchFocused = _searchBox != null && _searchBox.Focused;
            if (!searchFocused)
            {
                if (keyData == Keys.Enter || keyData == Keys.Space)
                {
                    if (_selected != null)
                    {
                        if (keyData == Keys.Space) ToggleFavorite(_selected);
                        else PrimaryAction(_selected);
                    }
                    return true;
                }
                if (keyData == Keys.Left) { NavigateSelection(-1); return true; }
                if (keyData == Keys.Right) { NavigateSelection(1); return true; }
                if (keyData == Keys.Up) { NavigateSelection(-Math.Max(1, _columns)); return true; }
                if (keyData == Keys.Down) { NavigateSelection(Math.Max(1, _columns)); return true; }
                if (keyData == Keys.Delete && _selected != null)
                {
                    RequestRemove(_selected);
                    return true;
                }
                if (keyData == Keys.F2 && _selected != null)
                {
                    ShowRename(_selected);
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private Font Body(float px)
        {
            return Theme.Font(px * _scale, FontStyle.Regular);
        }

        private Font Semi(float px)
        {
            return Theme.DisplayFont(px * _scale, FontStyle.Bold);
        }

        private Font Bold(float px)
        {
            return Theme.DisplayFont(px * _scale, FontStyle.Bold);
        }

        private Font Mono(float px)
        {
            return Theme.MonoFont(px * _scale, FontStyle.Regular);
        }

        private float S(float logical)
        {
            return logical * _scale;
        }
    }
}
