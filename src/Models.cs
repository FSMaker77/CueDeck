using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace CueDeck
{
    public static class DiscKinds
    {
        public const string Cue = "cue";
        public const string Iso = "iso";
        public const string Bin = "bin";
    }

    public enum DiscSort
    {
        Name = 0,
        RecentlyAdded = 1,
        RecentlyMounted = 2,
        Size = 3
    }

    public class DiscEntry
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Path { get; set; }
        public string Kind { get; set; }
        public bool Favorite { get; set; }
        public long SizeBytes { get; set; }
        public DateTime AddedUtc { get; set; }
        public DateTime LastMountedUtc { get; set; }
        public int MountCount { get; set; }
        public string CoverPath { get; set; }
        public string Notes { get; set; }

        public DiscEntry()
        {
            Id = Guid.NewGuid().ToString("N");
            Title = "";
            Path = "";
            Kind = DiscKinds.Cue;
            AddedUtc = DateTime.UtcNow;
            LastMountedUtc = DateTime.MinValue;
            CoverPath = "";
            Notes = "";
        }

        public DiscEntry Clone()
        {
            return (DiscEntry)MemberwiseClone();
        }
    }

    public class LibraryFile
    {
        public int Version { get; set; }
        public List<DiscEntry> Discs { get; set; }

        public LibraryFile()
        {
            Version = 1;
            Discs = new List<DiscEntry>();
        }
    }

    public class AppSettings
    {
        public int Version { get; set; }
        public bool CloseToTray { get; set; }
        public bool TrayHintShown { get; set; }
        public bool WinCDEmuHintShown { get; set; }
        public string WinCDEmuPath { get; set; }
        public int SortMode { get; set; }
        public string LastFilter { get; set; }
        public string LastDiscId { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public int WindowX { get; set; }
        public int WindowY { get; set; }
        public bool Maximized { get; set; }

        public AppSettings()
        {
            Version = 1;
            CloseToTray = true;
            WinCDEmuPath = "";
            SortMode = (int)DiscSort.Name;
            LastFilter = "all";
            LastDiscId = "";
            WindowWidth = 1240;
            WindowHeight = 820;
            WindowX = int.MinValue;
            WindowY = int.MinValue;
        }

        public DiscSort GetSort()
        {
            if (SortMode < 0 || SortMode > 3) return DiscSort.Name;
            return (DiscSort)SortMode;
        }
    }

    public class CueFile
    {
        public string DeclaredPath { get; set; }
        public string FullPath { get; set; }
        public string FileType { get; set; }
        public long LengthBytes { get; set; }
        public long DataLengthBytes { get; set; }
        public long SectorCount { get; set; }
        public long DataOffsetBytes { get; set; }
        public bool Exists { get; set; }
        public bool IsWave { get; set; }

        public CueFile()
        {
            DeclaredPath = "";
            FullPath = "";
            FileType = "BINARY";
        }
    }

    public class CueTrack
    {
        public int Number { get; set; }
        public string CueType { get; set; }
        public int SectorSize { get; set; }
        public int UserDataOffset { get; set; }
        public int UserDataSize { get; set; }
        public bool IsAudio { get; set; }
        public bool NativeSupported { get; set; }
        public long Index00Sector { get; set; }
        public long Index01Sector { get; set; }
        public bool HasIndex00 { get; set; }
        public bool HasIndex01 { get; set; }
        public long PregapSectors { get; set; }
        public long PostgapSectors { get; set; }
        public long StartSector { get; set; }
        public long EndSector { get; set; }
        public long Sectors { get; set; }
        public string Title { get; set; }
        public string Performer { get; set; }
        public CueFile File { get; set; }

        public CueTrack()
        {
            CueType = "";
            SectorSize = 2352;
            UserDataSize = 2352;
            Index00Sector = -1;
            Index01Sector = -1;
            Title = "";
            Performer = "";
        }

        public double DurationSeconds
        {
            get { return Sectors / 75.0; }
        }

        public TimeSpan Duration
        {
            get
            {
                if (Sectors <= 0) return TimeSpan.Zero;
                return TimeSpan.FromSeconds(Sectors / 75.0);
            }
        }

        public string DisplayTitle
        {
            get
            {
                if (!string.IsNullOrEmpty(Title)) return Title;
                if (IsAudio) return "Traccia " + Number.ToString("00", CultureInfo.InvariantCulture);
                return "Dati";
            }
        }
    }

    public class CueSheet
    {
        public string SourcePath { get; set; }
        public string BaseDirectory { get; set; }
        public string Kind { get; set; }
        public bool StandaloneBin { get; set; }
        public string Title { get; set; }
        public string Performer { get; set; }
        public string Genre { get; set; }
        public string Date { get; set; }
        public List<CueFile> Files { get; set; }
        public List<CueTrack> Tracks { get; set; }
        public List<string> Warnings { get; set; }
        public long TotalSourceBytes { get; set; }

        public CueSheet()
        {
            SourcePath = "";
            BaseDirectory = "";
            Kind = DiscKinds.Cue;
            Title = "";
            Performer = "";
            Genre = "";
            Date = "";
            Files = new List<CueFile>();
            Tracks = new List<CueTrack>();
            Warnings = new List<string>();
        }

        public bool HasAudio
        {
            get
            {
                for (int i = 0; i < Tracks.Count; i++)
                    if (Tracks[i].IsAudio) return true;
                return false;
            }
        }

        public bool HasData
        {
            get
            {
                for (int i = 0; i < Tracks.Count; i++)
                    if (!Tracks[i].IsAudio) return true;
                return false;
            }
        }

        public List<CueTrack> AudioTracks
        {
            get
            {
                List<CueTrack> result = new List<CueTrack>();
                for (int i = 0; i < Tracks.Count; i++)
                    if (Tracks[i].IsAudio) result.Add(Tracks[i]);
                return result;
            }
        }

        public List<CueTrack> DataTracks
        {
            get
            {
                List<CueTrack> result = new List<CueTrack>();
                for (int i = 0; i < Tracks.Count; i++)
                    if (!Tracks[i].IsAudio) result.Add(Tracks[i]);
                return result;
            }
        }

        public CueTrack NativeDataTrack
        {
            get
            {
                for (int i = 0; i < Tracks.Count; i++)
                {
                    CueTrack t = Tracks[i];
                    if (!t.IsAudio && t.NativeSupported && t.UserDataSize == 2048 && t.Sectors > 0)
                        return t;
                }
                return null;
            }
        }

        public CueTrack FirstDataTrack
        {
            get
            {
                for (int i = 0; i < Tracks.Count; i++)
                    if (!Tracks[i].IsAudio) return Tracks[i];
                return null;
            }
        }

        public bool CanMountNative
        {
            get
            {
                if (!string.IsNullOrEmpty(SourcePath) && Kind == DiscKinds.Iso) return true;
                return NativeDataTrack != null;
            }
        }

        public bool IsAudioOnly
        {
            get { return HasAudio && !HasData; }
        }

        public long TotalSectors
        {
            get
            {
                long total = 0;
                for (int i = 0; i < Tracks.Count; i++) total += Math.Max(0, Tracks[i].Sectors);
                return total;
            }
        }

        public TimeSpan TotalDuration
        {
            get { return TimeSpan.FromSeconds(TotalSectors / 75.0); }
        }

        public string KindLabel
        {
            get
            {
                if (Kind == DiscKinds.Iso) return "ISO";
                if (StandaloneBin) return "BIN";
                return "CUE/BIN";
            }
        }

        public string TrackSummary
        {
            get
            {
                int audio = 0;
                int data = 0;
                for (int i = 0; i < Tracks.Count; i++)
                {
                    if (Tracks[i].IsAudio) audio++;
                    else data++;
                }
                List<string> parts = new List<string>();
                if (data > 0) parts.Add(data == 1 ? "1 traccia dati" : data + " tracce dati");
                if (audio > 0) parts.Add(audio == 1 ? "1 traccia audio" : audio + " tracce audio");
                if (parts.Count == 0) return "Nessuna traccia";
                return string.Join(" + ", parts.ToArray());
            }
        }

        public List<CueFile> GetSourceFiles()
        {
            List<CueFile> result = new List<CueFile>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Files.Count; i++)
            {
                CueFile f = Files[i];
                if (f == null || string.IsNullOrEmpty(f.FullPath)) continue;
                if (seen.ContainsKey(f.FullPath)) continue;
                seen[f.FullPath] = true;
                result.Add(f);
            }
            return result;
        }
    }

    public class MountedDisc
    {
        public string EntryId { get; set; }
        public string Title { get; set; }
        public string DriveLetter { get; set; }
        public string ImagePath { get; set; }
        public string SourcePath { get; set; }
        public bool FullMount { get; set; }
        public NativeMount Native { get; set; }
        public WinCDEmuMount WinCDEmu { get; set; }
        public DateTime MountedUtc { get; set; }

        public MountedDisc()
        {
            EntryId = "";
            Title = "";
            DriveLetter = "";
            ImagePath = "";
            SourcePath = "";
            MountedUtc = DateTime.UtcNow;
        }
    }

    public class NativeMount
    {
        public IntPtr Handle { get; set; }
        public string ImagePath { get; set; }
        public string DriveLetter { get; set; }
        public bool PowerShellFallback { get; set; }
        public DateTime MountedUtc { get; set; }

        public NativeMount()
        {
            ImagePath = "";
            DriveLetter = "";
            MountedUtc = DateTime.UtcNow;
        }
    }

    public class WinCDEmuMount
    {
        public string CuePath { get; set; }
        public string DriveLetter { get; set; }
        public string ExecutablePath { get; set; }

        public WinCDEmuMount()
        {
            CuePath = "";
            DriveLetter = "";
            ExecutablePath = "";
        }
    }

    public static class TextUtil
    {
        public static string CleanDiscTitle(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Disco senza nome";
            string s = name;
            try { s = Path.GetFileNameWithoutExtension(s); }
            catch { }
            s = s.Replace('_', ' ').Replace('.', ' ');
            s = Regex.Replace(s, "\\s+", " ").Trim();
            s = Regex.Replace(s, "\\s*[\\[{(]\\s*(disc|disk|cd|dvd)\\s*\\d+\\s*[\\]})]", "", RegexOptions.IgnoreCase).Trim();
            if (s.Length == 0) return "Disco senza nome";
            return s;
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            double value = bytes;
            string[] units = new string[] { "B", "KB", "MB", "GB", "TB" };
            int unit = 0;
            while (value >= 1024.0 && unit < units.Length - 1)
            {
                value /= 1024.0;
                unit++;
            }
            string format = value >= 100 || unit == 0 ? "0" : "0.0";
            return value.ToString(format, CultureInfo.GetCultureInfo("it-IT")) + " " + units[unit];
        }

        public static string FormatDuration(TimeSpan span)
        {
            if (span.TotalHours >= 1)
                return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", (int)span.TotalHours, span.Minutes, span.Seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", span.Minutes, span.Seconds);
        }

        public static string FormatDuration(double seconds)
        {
            if (seconds <= 0) return "0:00";
            return FormatDuration(TimeSpan.FromSeconds(seconds));
        }

        public static string TruncateMiddle(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text;
            if (maxChars < 8) return text.Substring(0, maxChars);
            int head = (maxChars - 3) / 2;
            int tail = maxChars - 3 - head;
            return text.Substring(0, head) + "..." + text.Substring(text.Length - tail);
        }

        public static string SanitizeFileName(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "track";
            string invalid = new string(Path.GetInvalidFileNameChars());
            char[] chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (invalid.IndexOf(chars[i]) >= 0) chars[i] = '_';
            string result = new string(chars).Trim().TrimEnd('.');
            result = Regex.Replace(result, "\\s+", " ");
            if (result.Length == 0) result = "track";
            if (maxLength > 0 && result.Length > maxLength) result = result.Substring(0, maxLength).Trim();
            return result;
        }
    }
}
