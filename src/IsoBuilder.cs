using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CueDeck
{
    public static class IsoBuilder
    {
        private const int ConverterVersion = 3;
        private const int SectorsPerChunk = 256;

        public static string GetOrBuild(CueSheet sheet, IProgress<int> progress, CancellationToken token)
        {
            if (sheet == null) throw new ArgumentNullException("sheet");
            if (sheet.Kind == DiscKinds.Iso)
            {
                if (!File.Exists(sheet.SourcePath)) throw new CueParseException("File ISO non trovato.");
                if (progress != null) progress.Report(100);
                return sheet.SourcePath;
            }

            CueTrack track = sheet.NativeDataTrack;
            if (track == null)
                throw new CueParseException("Questo disco non ha una traccia dati montabile nella modalità nativa.");

            AppPaths.Ensure();
            string cachePath = GetCachePath(sheet);
            long expectedSize = checked(track.Sectors * 2048L);

            if (File.Exists(cachePath) && new FileInfo(cachePath).Length == expectedSize)
            {
                if (progress != null) progress.Report(100);
                return cachePath;
            }
            if (File.Exists(cachePath)) TryDelete(cachePath);

            EnsureFreeSpace(expectedSize);
            string tempPath = cachePath + ".part-" + System.Diagnostics.Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
            TryDelete(tempPath);

            try
            {
                ConvertTrackToIso(track, tempPath, progress, token);
                token.ThrowIfCancellationRequested();
                if (new FileInfo(tempPath).Length != expectedSize)
                    throw new IOException("La conversione ISO ha prodotto una dimensione inattesa.");

                if (File.Exists(cachePath)) TryDelete(cachePath);
                File.Move(tempPath, cachePath);
                if (progress != null) progress.Report(100);
                return cachePath;
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
        }

        public static string GetCachePath(CueSheet sheet)
        {
            AppPaths.Ensure();
            string key = ComputeCacheKey(sheet);
            return Path.Combine(AppPaths.CacheDir, "CueDeck_" + key + ".iso");
        }

        public static string ComputeCacheKey(CueSheet sheet)
        {
            CueTrack track = sheet.NativeDataTrack;
            if (track == null) return "nodata";
            StringBuilder sb = new StringBuilder();
            sb.Append("v").Append(ConverterVersion).Append('|');
            sb.Append(sheet.SourcePath.ToLowerInvariant()).Append('|');
            sb.Append(track.StartSector).Append('|').Append(track.Sectors).Append('|');
            sb.Append(track.SectorSize).Append('|').Append(track.UserDataOffset).Append('|').Append(track.UserDataSize).Append('|');
            if (track.File != null)
            {
                sb.Append(track.File.FullPath.ToLowerInvariant()).Append('|');
                try
                {
                    FileInfo info = new FileInfo(track.File.FullPath);
                    if (info.Exists)
                    {
                        sb.Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks);
                    }
                }
                catch { }
            }
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                StringBuilder hex = new StringBuilder();
                for (int i = 0; i < 12; i++) hex.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        private static void ConvertTrackToIso(CueTrack track, string outputPath, IProgress<int> progress, CancellationToken token)
        {
            if (track.File == null || !File.Exists(track.File.FullPath))
                throw new CueParseException("File immagine mancante per la traccia dati.");

            int sectorSize = track.SectorSize;
            int dataOffset = track.UserDataOffset;
            if (sectorSize <= 0 || track.UserDataSize != 2048)
                throw new CueParseException("Formato settore non supportato.");
            if (dataOffset < 0 || dataOffset + 2048 > sectorSize)
                throw new CueParseException("Offset dati non valido nel formato CUE.");

            long sourceStart = track.File.DataOffsetBytes + track.StartSector * (long)sectorSize;
            long remaining = track.Sectors;
            long done = 0;

            using (FileStream input = new FileStream(track.File.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
            using (FileStream output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024))
            {
                if (sourceStart < 0 || sourceStart > input.Length)
                    throw new CueParseException("La traccia dati punta oltre la fine del file immagine.");

                input.Position = sourceStart;
                byte[] inBuffer = new byte[SectorsPerChunk * sectorSize];
                byte[] outBuffer = new byte[SectorsPerChunk * 2048];
                int lastReported = -1;

                while (remaining > 0)
                {
                    token.ThrowIfCancellationRequested();
                    int count = (int)Math.Min(SectorsPerChunk, remaining);
                    int needed = count * sectorSize;
                    int read = ReadFull(input, inBuffer, 0, needed);
                    if (read < needed)
                        throw new IOException("Il file immagine è troncato: mancano dati nella traccia selezionata.");

                    if (sectorSize == 2048 && dataOffset == 0)
                    {
                        Buffer.BlockCopy(inBuffer, 0, outBuffer, 0, count * 2048);
                    }
                    else
                    {
                        for (int i = 0; i < count; i++)
                        {
                            Buffer.BlockCopy(inBuffer, i * sectorSize + dataOffset, outBuffer, i * 2048, 2048);
                        }
                    }
                    output.Write(outBuffer, 0, count * 2048);

                    remaining -= count;
                    done += count;
                    if (progress != null)
                    {
                        int percent = (int)Math.Min(99, (done * 100L) / Math.Max(1, track.Sectors));
                        if (percent != lastReported)
                        {
                            lastReported = percent;
                            progress.Report(percent);
                        }
                    }
                }
                output.Flush();
            }
            if (progress != null) progress.Report(100);
        }

        private static void EnsureFreeSpace(long requiredBytes)
        {
            try
            {
                string root = Path.GetPathRoot(AppPaths.CacheDir);
                if (string.IsNullOrEmpty(root)) return;
                DriveInfo drive = new DriveInfo(root);
                long free = drive.AvailableFreeSpace;
                if (free < requiredBytes + 64L * 1024 * 1024)
                    throw new IOException("Spazio insufficiente nella cache. Servono " + TextUtil.FormatBytes(requiredBytes + 64L * 1024 * 1024) + " liberi.");
            }
            catch (IOException) { throw; }
            catch { }
        }

        public static long GetCacheSize()
        {
            long total = 0;
            try
            {
                if (!Directory.Exists(AppPaths.CacheDir)) return 0;
                string[] files = Directory.GetFiles(AppPaths.CacheDir, "*", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < files.Length; i++)
                {
                    try { total += new FileInfo(files[i]).Length; }
                    catch { }
                }
            }
            catch { }
            return total;
        }

        public static int ClearCache(out long freedBytes)
        {
            int deleted = 0;
            freedBytes = 0;
            try
            {
                if (!Directory.Exists(AppPaths.CacheDir)) return 0;
                string[] files = Directory.GetFiles(AppPaths.CacheDir, "*", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        long length = new FileInfo(files[i]).Length;
                        File.SetAttributes(files[i], FileAttributes.Normal);
                        File.Delete(files[i]);
                        freedBytes += length;
                        deleted++;
                    }
                    catch { }
                }
            }
            catch { }
            return deleted;
        }

        public static int PruneCache(TimeSpan maxAge)
        {
            int deleted = 0;
            try
            {
                if (!Directory.Exists(AppPaths.CacheDir)) return 0;
                DateTime threshold = DateTime.UtcNow - maxAge;
                string[] files = Directory.GetFiles(AppPaths.CacheDir, "CueDeck_*.iso", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        FileInfo info = new FileInfo(files[i]);
                        if (info.LastAccessTimeUtc < threshold && info.LastWriteTimeUtc < threshold)
                        {
                            info.Delete();
                            deleted++;
                        }
                    }
                    catch { }
                }
                string[] parts = Directory.GetFiles(AppPaths.CacheDir, "*.part-*", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < parts.Length; i++)
                {
                    try
                    {
                        FileInfo info = new FileInfo(parts[i]);
                        if (info.LastWriteTimeUtc < DateTime.UtcNow - TimeSpan.FromHours(6))
                        {
                            info.Delete();
                            deleted++;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return deleted;
        }

        private static int ReadFull(Stream stream, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, offset + total, count - total);
                if (read <= 0) break;
                total += read;
            }
            return total;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }
}
