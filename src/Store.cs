using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CueDeck
{
    public static class AppPaths
    {
        public static readonly string Root;
        public static readonly string CacheDir;
        public static readonly string PreviewDir;
        public static readonly string CoversDir;
        public static readonly string LogDir;
        public static readonly string LibraryPath;
        public static readonly string SettingsPath;
        public static readonly string LogPath;

        static AppPaths()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Root = Path.Combine(local, "CueDeck");
            CacheDir = Path.Combine(Root, "Cache");
            PreviewDir = Path.Combine(Root, "Preview");
            CoversDir = Path.Combine(Root, "Covers");
            LogDir = Path.Combine(Root, "Logs");
            LibraryPath = Path.Combine(Root, "library.json");
            SettingsPath = Path.Combine(Root, "settings.json");
            LogPath = Path.Combine(LogDir, "cuedeck.log");
        }

        public static void Ensure()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(CacheDir);
            Directory.CreateDirectory(PreviewDir);
            Directory.CreateDirectory(CoversDir);
            Directory.CreateDirectory(LogDir);
        }
    }

    public static class Logger
    {
        private static readonly object Sync = new object();

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Error(string context, Exception ex)
        {
            Write("ERROR", context, ex);
        }

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                lock (Sync)
                {
                    AppPaths.Ensure();
                    Rotate();
                    StringBuilder sb = new StringBuilder();
                    sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                    sb.Append(" [").Append(level).Append("] ").Append(message);
                    if (ex != null)
                    {
                        sb.AppendLine();
                        sb.Append(ex.GetType().FullName).Append(": ").Append(ex.Message);
                        sb.AppendLine();
                        sb.Append(ex.StackTrace);
                    }
                    sb.AppendLine();
                    File.AppendAllText(AppPaths.LogPath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch { }
        }

        private static void Rotate()
        {
            try
            {
                FileInfo info = new FileInfo(AppPaths.LogPath);
                if (info.Exists && info.Length > 1024 * 1024)
                {
                    string old = AppPaths.LogPath + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(AppPaths.LogPath, old);
                }
            }
            catch { }
        }
    }

    public class AddSummary
    {
        public int Added { get; set; }
        public int Existing { get; set; }
        public int Unsupported { get; set; }
        public List<string> Errors { get; set; }
        public List<DiscEntry> NewEntries { get; set; }

        public AddSummary()
        {
            Errors = new List<string>();
            NewEntries = new List<DiscEntry>();
        }
    }

    public class LibraryStore
    {
        private readonly object _sync = new object();
        private readonly JavaScriptSerializer _json;
        public LibraryFile Data { get; private set; }
        public string LoadWarning { get; private set; }
        public bool Dirty { get; private set; }

        public LibraryStore()
        {
            _json = new JavaScriptSerializer();
            _json.MaxJsonLength = 32 * 1024 * 1024;
            Data = new LibraryFile();
            LoadWarning = "";
        }

        public void Load()
        {
            lock (_sync)
            {
                AppPaths.Ensure();
                if (!File.Exists(AppPaths.LibraryPath))
                {
                    Data = new LibraryFile();
                    return;
                }
                try
                {
                    string json = File.ReadAllText(AppPaths.LibraryPath, Encoding.UTF8);
                    LibraryFile file = _json.Deserialize<LibraryFile>(json);
                    if (file == null) file = new LibraryFile();
                    if (file.Discs == null) file.Discs = new List<DiscEntry>();
                    Normalize(file);
                    Data = file;
                }
                catch (Exception ex)
                {
                    LoadWarning = "La libreria non poteva essere letta. E stato creato un elenco vuoto.";
                    Logger.Error("Load library", ex);
                    try
                    {
                        string corrupt = AppPaths.LibraryPath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                        File.Copy(AppPaths.LibraryPath, corrupt, true);
                    }
                    catch { }
                    Data = new LibraryFile();
                }
            }
        }

        private void Normalize(LibraryFile file)
        {
            List<DiscEntry> clean = new List<DiscEntry>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < file.Discs.Count; i++)
            {
                DiscEntry e = file.Discs[i];
                if (e == null || string.IsNullOrEmpty(e.Path)) continue;
                string key = PathKey(e.Path);
                if (seen.ContainsKey(key)) continue;
                seen[key] = true;
                if (string.IsNullOrEmpty(e.Id)) e.Id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrEmpty(e.Kind)) e.Kind = KindFromExtension(e.Path);
                if (string.IsNullOrEmpty(e.Title)) e.Title = TextUtil.CleanDiscTitle(e.Path);
                if (e.AddedUtc == DateTime.MinValue) e.AddedUtc = DateTime.UtcNow;
                if (e.CoverPath == null) e.CoverPath = "";
                if (e.Notes == null) e.Notes = "";
                clean.Add(e);
            }
            file.Discs = clean;
        }

        public void Save()
        {
            lock (_sync)
            {
                try
                {
                    AppPaths.Ensure();
                    string json = _json.Serialize(Data);
                    string temp = AppPaths.LibraryPath + ".tmp";
                    File.WriteAllText(temp, json, new UTF8Encoding(false));
                    if (File.Exists(AppPaths.LibraryPath))
                    {
                        string backup = AppPaths.LibraryPath + ".bak";
                        try { File.Replace(temp, AppPaths.LibraryPath, backup); }
                        catch
                        {
                            File.Delete(AppPaths.LibraryPath);
                            File.Move(temp, AppPaths.LibraryPath);
                        }
                    }
                    else
                    {
                        File.Move(temp, AppPaths.LibraryPath);
                    }
                    Dirty = false;
                }
                catch (Exception ex)
                {
                    Logger.Error("Save library", ex);
                }
            }
        }

        public DiscEntry FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Data.Discs.Count; i++)
                if (string.Equals(Data.Discs[i].Id, id, StringComparison.OrdinalIgnoreCase)) return Data.Discs[i];
            return null;
        }

        public DiscEntry FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string key = PathKey(path);
            for (int i = 0; i < Data.Discs.Count; i++)
                if (PathKey(Data.Discs[i].Path) == key) return Data.Discs[i];
            return null;
        }

        public DiscEntry AddPath(string path, AddSummary summary)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string full;
            try { full = Path.GetFullPath(path); }
            catch { full = path; }
            string ext = Path.GetExtension(full).ToLowerInvariant();

            if (ext == ".bin")
            {
                string cue = CueParser.FindReferencingCue(full);
                if (!string.IsNullOrEmpty(cue)) full = cue;
            }

            ext = Path.GetExtension(full).ToLowerInvariant();
            string kind;
            if (ext == ".cue") kind = DiscKinds.Cue;
            else if (ext == ".iso") kind = DiscKinds.Iso;
            else if (ext == ".bin") kind = DiscKinds.Bin;
            else
            {
                if (summary != null) summary.Unsupported++;
                return null;
            }

            DiscEntry existing = FindByPath(full);
            if (existing != null)
            {
                if (summary != null) summary.Existing++;
                return existing;
            }

            DiscEntry entry = new DiscEntry();
            entry.Path = full;
            entry.Kind = kind;
            entry.AddedUtc = DateTime.UtcNow;
            entry.Title = TextUtil.CleanDiscTitle(full);
            entry.SizeBytes = 0;

            try
            {
                CueSheet sheet = CueParser.ParseAny(entry);
                if (!string.IsNullOrEmpty(sheet.Title)) entry.Title = sheet.Title;
                entry.SizeBytes = sheet.TotalSourceBytes;
            }
            catch (Exception ex)
            {
                try { entry.SizeBytes = new FileInfo(full).Length; }
                catch { }
                if (summary != null) summary.Errors.Add(Path.GetFileName(full) + ": " + ex.Message);
                Logger.Error("Add disc " + full, ex);
            }

            lock (_sync)
            {
                Data.Discs.Add(entry);
                Dirty = true;
            }
            if (summary != null)
            {
                summary.Added++;
                summary.NewEntries.Add(entry);
            }
            Save();
            return entry;
        }

        public AddSummary AddPaths(IEnumerable<string> paths)
        {
            AddSummary summary = new AddSummary();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in paths)
            {
                if (string.IsNullOrEmpty(p) || !seen.Add(p)) continue;
                try
                {
                    if (Directory.Exists(p)) ScanFolderInternal(p, true, 5, summary, seen);
                    else if (File.Exists(p)) AddPath(p, summary);
                    else summary.Errors.Add("Percorso non trovato: " + p);
                }
                catch (Exception ex)
                {
                    summary.Errors.Add(p + ": " + ex.Message);
                    Logger.Error("Add path " + p, ex);
                }
            }
            return summary;
        }

        public AddSummary ScanFolder(string folder)
        {
            AddSummary summary = new AddSummary();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(folder)) ScanFolderInternal(folder, true, 5, summary, seen);
            return summary;
        }

        private void ScanFolderInternal(string folder, bool recursive, int depthLeft, AddSummary summary, HashSet<string> seen)
        {
            List<string> cues = new List<string>();
            List<string> isos = new List<string>();
            List<string> bins = new List<string>();
            HashSet<string> referencedBins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            CollectFiles(folder, recursive, depthLeft, cues, isos, bins);
            for (int i = 0; i < cues.Count; i++)
            {
                if (!seen.Add(cues[i])) continue;
                try
                {
                    CueSheet sheet = CueParser.ParseCue(cues[i]);
                    for (int f = 0; f < sheet.Files.Count; f++)
                    {
                        if (!string.IsNullOrEmpty(sheet.Files[f].FullPath))
                            referencedBins.Add(PathKey(sheet.Files[f].FullPath));
                    }
                }
                catch { }
                AddPath(cues[i], summary);
            }
            for (int i = 0; i < isos.Count; i++)
            {
                if (!seen.Add(isos[i])) continue;
                AddPath(isos[i], summary);
            }
            for (int i = 0; i < bins.Count; i++)
            {
                if (!seen.Add(bins[i])) continue;
                if (referencedBins.Contains(PathKey(bins[i]))) continue;
                AddPath(bins[i], summary);
            }
        }

        private void CollectFiles(string folder, bool recursive, int depthLeft, List<string> cues, List<string> isos, List<string> bins)
        {
            try
            {
                string[] files = Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < files.Length; i++)
                {
                    string ext = Path.GetExtension(files[i]).ToLowerInvariant();
                    if (ext == ".cue") cues.Add(Path.GetFullPath(files[i]));
                    else if (ext == ".iso") isos.Add(Path.GetFullPath(files[i]));
                    else if (ext == ".bin") bins.Add(Path.GetFullPath(files[i]));
                }
                if (recursive && depthLeft > 0)
                {
                    string[] dirs = Directory.GetDirectories(folder, "*", SearchOption.TopDirectoryOnly);
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        FileAttributes attrs = File.GetAttributes(dirs[i]);
                        if ((attrs & FileAttributes.Hidden) != 0 || (attrs & FileAttributes.System) != 0) continue;
                        if (string.Equals(Path.GetFullPath(dirs[i]), Path.GetFullPath(AppPaths.CacheDir), StringComparison.OrdinalIgnoreCase)) continue;
                        CollectFiles(dirs[i], true, depthLeft - 1, cues, isos, bins);
                    }
                }
            }
            catch (Exception ex)
            {
                summarySafeLog(folder, ex);
            }
        }

        private void summarySafeLog(string folder, Exception ex)
        {
            Logger.Error("Scan folder " + folder, ex);
        }

        public void Remove(DiscEntry entry)
        {
            if (entry == null) return;
            lock (_sync) { Data.Discs.Remove(entry); Dirty = true; }
            Save();
        }

        public void TouchMounted(DiscEntry entry)
        {
            if (entry == null) return;
            entry.LastMountedUtc = DateTime.UtcNow;
            entry.MountCount++;
            Dirty = true;
            Save();
        }

        public void SetFavorite(DiscEntry entry, bool value)
        {
            if (entry == null) return;
            entry.Favorite = value;
            Dirty = true;
            Save();
        }

        public void Update(DiscEntry entry)
        {
            if (entry == null) return;
            Dirty = true;
            Save();
        }

        public void RefreshSizes()
        {
            bool changed = false;
            for (int i = 0; i < Data.Discs.Count; i++)
            {
                DiscEntry e = Data.Discs[i];
                if (e.SizeBytes > 0) continue;
                try
                {
                    long size = 0;
                    if (e.Kind == DiscKinds.Cue || e.Kind == DiscKinds.Bin || e.Kind == DiscKinds.Iso)
                    {
                        CueSheet sheet = CueParser.ParseAny(e);
                        size = sheet.TotalSourceBytes;
                        if (string.IsNullOrEmpty(e.Title) || e.Title == TextUtil.CleanDiscTitle(e.Path))
                            e.Title = sheet.Title;
                    }
                    if (size <= 0 && File.Exists(e.Path)) size = new FileInfo(e.Path).Length;
                    e.SizeBytes = size;
                    changed = true;
                }
                catch { }
            }
            if (changed) Save();
        }

        public static string KindFromExtension(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".iso") return DiscKinds.Iso;
            if (ext == ".bin") return DiscKinds.Bin;
            return DiscKinds.Cue;
        }

        public static string PathKey(string path)
        {
            try { return Path.GetFullPath(path).TrimEnd('\\').ToLowerInvariant(); }
            catch { return (path ?? "").Trim().ToLowerInvariant(); }
        }
    }

    public class SettingsStore
    {
        private readonly JavaScriptSerializer _json;
        public AppSettings Data { get; private set; }

        public SettingsStore()
        {
            _json = new JavaScriptSerializer();
            _json.MaxJsonLength = 4 * 1024 * 1024;
            Data = new AppSettings();
        }

        public void Load()
        {
            try
            {
                AppPaths.Ensure();
                if (!File.Exists(AppPaths.SettingsPath)) return;
                string json = File.ReadAllText(AppPaths.SettingsPath, Encoding.UTF8);
                AppSettings s = _json.Deserialize<AppSettings>(json);
                if (s != null)
                {
                    if (string.IsNullOrEmpty(s.WinCDEmuPath)) s.WinCDEmuPath = "";
                    if (string.IsNullOrEmpty(s.LastFilter)) s.LastFilter = "all";
                    if (string.IsNullOrEmpty(s.LastDiscId)) s.LastDiscId = "";
                    if (s.WindowWidth < 900) s.WindowWidth = 1240;
                    if (s.WindowHeight < 600) s.WindowHeight = 820;
                    Data = s;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Load settings", ex);
                Data = new AppSettings();
            }
        }

        public void Save()
        {
            try
            {
                AppPaths.Ensure();
                string json = _json.Serialize(Data);
                string temp = AppPaths.SettingsPath + ".tmp";
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                if (File.Exists(AppPaths.SettingsPath))
                {
                    try { File.Replace(temp, AppPaths.SettingsPath, null); }
                    catch
                    {
                        File.Delete(AppPaths.SettingsPath);
                        File.Move(temp, AppPaths.SettingsPath);
                    }
                }
                else File.Move(temp, AppPaths.SettingsPath);
            }
            catch (Exception ex)
            {
                Logger.Error("Save settings", ex);
            }
        }
    }

    public static class CoverStore
    {
        private static readonly string[] Extensions = new string[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

        public static string FindAutoCover(string sourcePath)
        {
            try
            {
                string dir = Path.GetDirectoryName(sourcePath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
                string baseName = Path.GetFileNameWithoutExtension(sourcePath);
                List<string> candidates = new List<string>();
                for (int i = 0; i < Extensions.Length; i++)
                {
                    candidates.Add(Path.Combine(dir, baseName + Extensions[i]));
                    candidates.Add(Path.Combine(dir, "cover" + Extensions[i]));
                    candidates.Add(Path.Combine(dir, "folder" + Extensions[i]));
                    candidates.Add(Path.Combine(dir, "front" + Extensions[i]));
                    candidates.Add(Path.Combine(dir, "artwork" + Extensions[i]));
                }
                for (int i = 0; i < candidates.Count; i++)
                    if (File.Exists(candidates[i])) return candidates[i];
            }
            catch { }
            return null;
        }

        public static string Resolve(DiscEntry entry)
        {
            if (entry == null) return null;
            if (!string.IsNullOrEmpty(entry.CoverPath) && File.Exists(entry.CoverPath)) return entry.CoverPath;
            return FindAutoCover(entry.Path);
        }

        public static string SetCustom(DiscEntry entry, string imagePath)
        {
            if (entry == null) throw new ArgumentNullException("entry");
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                throw new FileNotFoundException("Immagine non trovata.");
            string ext = Path.GetExtension(imagePath).ToLowerInvariant();
            bool ok = false;
            for (int i = 0; i < Extensions.Length; i++)
                if (Extensions[i] == ext) { ok = true; break; }
            if (!ok) throw new InvalidOperationException("Formato immagine non supportato. Usa JPG, PNG, BMP o GIF.");

            AppPaths.Ensure();
            string target = Path.Combine(AppPaths.CoversDir, entry.Id + ext);
            File.Copy(imagePath, target, true);
            if (!string.IsNullOrEmpty(entry.CoverPath) && File.Exists(entry.CoverPath) &&
                !string.Equals(Path.GetFullPath(entry.CoverPath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(entry.CoverPath); } catch { }
            }
            entry.CoverPath = target;
            return target;
        }

        public static void RemoveCustom(DiscEntry entry)
        {
            if (entry == null) return;
            if (!string.IsNullOrEmpty(entry.CoverPath))
            {
                try { if (File.Exists(entry.CoverPath)) File.Delete(entry.CoverPath); }
                catch { }
            }
            entry.CoverPath = "";
        }
    }
}
