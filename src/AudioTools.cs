using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CueDeck
{
    public class AudioExtractProgress
    {
        public int TrackNumber { get; set; }
        public int TrackIndex { get; set; }
        public int TrackCount { get; set; }
        public int Percent { get; set; }
        public string FileName { get; set; }

        public AudioExtractProgress()
        {
            FileName = "";
        }
    }

    public static class AudioTools
    {
        public static bool CanExtract(CueTrack track)
        {
            if (track == null || !track.IsAudio || track.File == null) return false;
            if (!File.Exists(track.File.FullPath)) return false;
            if (track.File.FileType == "MP3" || track.File.FileType == "AIFF" || track.File.FileType == "MOTOROLA") return false;
            return track.SectorSize == 2352 && track.Sectors > 0;
        }

        public static string EnsurePreviewWave(CueSheet sheet, CueTrack track, IProgress<int> progress, CancellationToken token)
        {
            AppPaths.Ensure();
            string path = GetPreviewPath(sheet, track);
            long expected = track.Sectors * 2352L + 44L;
            if (File.Exists(path))
            {
                try
                {
                    if (new FileInfo(path).Length == expected)
                    {
                        if (progress != null) progress.Report(100);
                        return path;
                    }
                }
                catch { }
                TryDelete(path);
            }
            string temp = path + ".part";
            TryDelete(temp);
            try
            {
                ExtractWave(track, temp, progress, token);
                if (progress != null) progress.Report(100);
                if (File.Exists(path)) TryDelete(path);
                File.Move(temp, path);
                return path;
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }

        public static string GetPreviewPath(CueSheet sheet, CueTrack track)
        {
            AppPaths.Ensure();
            string key = (sheet.SourcePath ?? "").ToLowerInvariant() + "|" + track.Number + "|" + track.StartSector + "|" + track.Sectors;
            if (track.File != null)
            {
                key += "|" + track.File.FullPath.ToLowerInvariant();
                try { key += "|" + new FileInfo(track.File.FullPath).LastWriteTimeUtc.Ticks; }
                catch { }
            }
            uint hash = 2166136261;
            for (int i = 0; i < key.Length; i++)
            {
                hash ^= key[i];
                hash *= 16777619;
            }
            return Path.Combine(AppPaths.PreviewDir, "preview_" + hash.ToString("x8", CultureInfo.InvariantCulture) + ".wav");
        }

        public static void ExtractWave(CueTrack track, string outputPath, IProgress<int> progress, CancellationToken token)
        {
            if (!CanExtract(track))
                throw new InvalidOperationException("Questa traccia non può essere esportata come WAV.");

            long available = track.File.IsWave && track.File.DataLengthBytes > 0
                ? track.File.DataLengthBytes
                : track.File.LengthBytes;
            long sourceOffset = track.File.DataOffsetBytes + track.StartSector * 2352L;
            long dataBytes = track.Sectors * 2352L;
            if (sourceOffset + dataBytes > track.File.DataOffsetBytes + available)
                dataBytes = Math.Max(0, track.File.DataOffsetBytes + available - sourceOffset);
            if (dataBytes <= 0) throw new InvalidOperationException("La traccia audio è vuota.");

            using (FileStream input = new FileStream(track.File.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
            using (FileStream output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024))
            {
                WriteWaveHeader(output, dataBytes);
                input.Position = sourceOffset;
                byte[] buffer = new byte[1024 * 1024];
                long remaining = dataBytes;
                long done = 0;
                int lastPercent = -1;
                while (remaining > 0)
                {
                    token.ThrowIfCancellationRequested();
                    int count = (int)Math.Min(buffer.Length, remaining);
                    int read = input.Read(buffer, 0, count);
                    if (read <= 0) break;
                    output.Write(buffer, 0, read);
                    remaining -= read;
                    done += read;
                    if (progress != null)
                    {
                        int percent = (int)Math.Min(99, done * 100L / Math.Max(1, dataBytes));
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            progress.Report(percent);
                        }
                    }
                }
                output.Flush();
            }
            if (progress != null) progress.Report(100);
        }

        private static void WriteWaveHeader(Stream output, long dataBytes)
        {
            long size = Math.Min(dataBytes, 0x7FFFFFF0L);
            int dataSize = (int)size;
            byte[] header = new byte[44];
            WriteAscii(header, 0, "RIFF");
            WriteInt32(header, 4, dataSize + 36);
            WriteAscii(header, 8, "WAVE");
            WriteAscii(header, 12, "fmt ");
            WriteInt32(header, 16, 16);
            WriteInt16(header, 20, 1);
            WriteInt16(header, 22, 2);
            WriteInt32(header, 24, 44100);
            WriteInt32(header, 28, 44100 * 2 * 2);
            WriteInt16(header, 32, 4);
            WriteInt16(header, 34, 16);
            WriteAscii(header, 36, "data");
            WriteInt32(header, 40, dataSize);
            output.Write(header, 0, header.Length);
        }

        private static void WriteAscii(byte[] buffer, int offset, string text)
        {
            for (int i = 0; i < text.Length; i++) buffer[offset + i] = (byte)text[i];
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteInt16(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        public static List<string> ExportAllAudio(CueSheet sheet, string outputFolder, IProgress<AudioExtractProgress> progress, CancellationToken token)
        {
            if (sheet == null) throw new ArgumentNullException("sheet");
            if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);
            List<CueTrack> audio = sheet.AudioTracks;
            List<string> written = new List<string>();
            for (int i = 0; i < audio.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                CueTrack track = audio[i];
                if (!CanExtract(track)) continue;
                string name = TextUtil.SanitizeFileName(track.Number.ToString("00", CultureInfo.InvariantCulture) + " - " + track.DisplayTitle, 120) + ".wav";
                string path = Path.Combine(outputFolder, name);
                int index = i;
                Progress<int> inner = new Progress<int>(delegate(int p)
                {
                    if (progress == null) return;
                    AudioExtractProgress ap = new AudioExtractProgress();
                    ap.TrackNumber = track.Number;
                    ap.TrackIndex = index;
                    ap.TrackCount = audio.Count;
                    ap.Percent = (int)((index * 100L + p) / Math.Max(1, audio.Count));
                    ap.FileName = name;
                    progress.Report(ap);
                });
                ExtractWave(track, path, inner, token);
                written.Add(path);
            }
            return written;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }

    public class MciPlayer : IDisposable
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int mciSendString(string command, StringBuilder returnValue, int returnLength, IntPtr hwndCallback);

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern bool mciGetErrorString(int errorCode, StringBuilder errorText, int errorTextSize);

        private static int _counter;
        private readonly string _alias;
        private bool _open;

        public string LastError { get; private set; }

        public MciPlayer()
        {
            _alias = "cuedeck" + (++_counter).ToString(CultureInfo.InvariantCulture);
            LastError = "";
        }

        public bool IsOpen { get { return _open; } }

        public bool Open(string path)
        {
            Close();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                LastError = "File audio non trovato.";
                return false;
            }
            int rc = Send("open \"" + path + "\" type waveaudio alias " + _alias);
            if (rc != 0)
            {
                LastError = GetError(rc);
                return false;
            }
            _open = true;
            return true;
        }

        public bool Play()
        {
            if (!_open) return false;
            int rc = Send("play " + _alias);
            LastError = rc == 0 ? "" : GetError(rc);
            return rc == 0;
        }

        public bool Pause()
        {
            if (!_open) return false;
            int rc = Send("pause " + _alias);
            LastError = rc == 0 ? "" : GetError(rc);
            return rc == 0;
        }

        public bool Resume()
        {
            if (!_open) return false;
            int rc = Send("resume " + _alias);
            LastError = rc == 0 ? "" : GetError(rc);
            return rc == 0;
        }

        public bool Stop()
        {
            if (!_open) return false;
            int rc = Send("stop " + _alias);
            LastError = rc == 0 ? "" : GetError(rc);
            return rc == 0;
        }

        public bool Seek(int milliseconds)
        {
            if (!_open) return false;
            int rc = Send("seek " + _alias + " to " + Math.Max(0, milliseconds).ToString(CultureInfo.InvariantCulture));
            LastError = rc == 0 ? "" : GetError(rc);
            return rc == 0;
        }

        public int GetPosition()
        {
            if (!_open) return 0;
            return ReadInt("status " + _alias + " position");
        }

        public int GetLength()
        {
            if (!_open) return 0;
            return ReadInt("status " + _alias + " length");
        }

        public string GetMode()
        {
            if (!_open) return "closed";
            return ReadString("status " + _alias + " mode");
        }

        public void SetVolume(int percent)
        {
            if (!_open) return;
            int volume = Math.Max(0, Math.Min(100, percent)) * 10;
            Send("setaudio " + _alias + " volume to " + volume.ToString(CultureInfo.InvariantCulture));
        }

        public void Close()
        {
            if (!_open) return;
            Send("close " + _alias);
            _open = false;
        }

        public void Dispose()
        {
            Close();
        }

        private int Send(string command)
        {
            try { return mciSendString(command, null, 0, IntPtr.Zero); }
            catch { return -1; }
        }

        private int ReadInt(string command)
        {
            StringBuilder sb = new StringBuilder(64);
            int rc = mciSendString(command, sb, sb.Capacity, IntPtr.Zero);
            if (rc != 0) return 0;
            int value;
            if (int.TryParse(sb.ToString().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return value;
            return 0;
        }

        private string ReadString(string command)
        {
            StringBuilder sb = new StringBuilder(64);
            int rc = mciSendString(command, sb, sb.Capacity, IntPtr.Zero);
            if (rc != 0) return "";
            return sb.ToString().Trim();
        }

        private string GetError(int code)
        {
            try
            {
                StringBuilder sb = new StringBuilder(256);
                if (mciGetErrorString(code, sb, sb.Capacity)) return sb.ToString();
            }
            catch { }
            return "Errore audio (" + code.ToString(CultureInfo.InvariantCulture) + ").";
        }
    }
}
