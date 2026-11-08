using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace CueDeck
{
    public static class ImageConverter
    {
        private static readonly byte[] EccF = new byte[256];
        private static readonly byte[] EccB = new byte[256];
        private static readonly uint[] EdcLut = new uint[256];

        static ImageConverter()
        {
            for (int i = 0; i < 256; i++)
            {
                int j = (i << 1) ^ ((i & 0x80) != 0 ? 0x11D : 0);
                EccF[i] = (byte)j;
                EccB[i ^ j] = (byte)i;

                uint edc = (uint)i;
                for (int k = 0; k < 8; k++)
                    edc = (edc >> 1) ^ ((edc & 1) != 0 ? 0xD8018001u : 0u);
                EdcLut[i] = edc;
            }
        }

        public static void IsoToCueBin(string isoPath, string cuePath, IProgress<int> progress, CancellationToken token)
        {
            if (string.IsNullOrEmpty(isoPath) || !File.Exists(isoPath))
                throw new FileNotFoundException("File ISO non trovato.", isoPath);
            if (string.IsNullOrEmpty(cuePath))
                throw new ArgumentException("Percorso CUE non valido.", "cuePath");

            string fullIso = Path.GetFullPath(isoPath);
            string fullCue = Path.GetFullPath(cuePath);
            string binPath = Path.ChangeExtension(fullCue, ".bin");
            if (string.Equals(fullIso, binPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Il file di output non può coincidere con l'ISO di origine.");
            if (File.Exists(binPath))
                throw new IOException("Il file BIN di destinazione esiste già:\r\n" + binPath + "\r\nScegli un altro nome per il CUE.");

            FileInfo isoInfo = new FileInfo(fullIso);
            if (isoInfo.Length <= 0 || isoInfo.Length % 2048 != 0)
                throw new InvalidOperationException("La dimensione dell'ISO non è un multiplo di 2048 byte.");
            long sectors = isoInfo.Length / 2048;
            long outputBytes = checked(sectors * 2352L);
            EnsureFreeSpace(Path.GetDirectoryName(fullCue), outputBytes);

            string tempBin = binPath + ".part";
            string tempCue = fullCue + ".part";
            TryDelete(tempBin);
            TryDelete(tempCue);
            bool binMoved = false;
            try
            {
                using (FileStream input = new FileStream(fullIso, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 1024, FileOptions.SequentialScan))
                using (FileStream output = new FileStream(tempBin, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024))
                {
                    const int sectorsPerChunk = 128;
                    byte[] inBuffer = new byte[2048 * sectorsPerChunk];
                    byte[] outBuffer = new byte[2352 * sectorsPerChunk];
                    long done = 0;
                    int lastProgress = -1;

                    while (done < sectors)
                    {
                        token.ThrowIfCancellationRequested();
                        int count = (int)Math.Min(sectorsPerChunk, sectors - done);
                        int needed = count * 2048;
                        int read = ReadFull(input, inBuffer, 0, needed);
                        if (read < needed)
                            throw new IOException("L'ISO è troncata o illeggibile.");

                        for (int i = 0; i < count; i++)
                            BuildMode1Sector(outBuffer, i * 2352, inBuffer, i * 2048, done + i);

                        output.Write(outBuffer, 0, count * 2352);
                        done += count;

                        if (progress != null)
                        {
                            int percent = (int)Math.Min(99, done * 100L / Math.Max(1, sectors));
                            if (percent != lastProgress)
                            {
                                lastProgress = percent;
                                progress.Report(percent);
                            }
                        }
                    }
                    output.Flush();
                }

                WriteCue(tempCue, Path.GetFileName(binPath), "MODE1/2352");
                if (File.Exists(fullCue)) File.Delete(fullCue);
                File.Move(tempCue, fullCue);
                File.Move(tempBin, binPath);
                binMoved = true;
                if (progress != null) progress.Report(100);
            }
            catch
            {
                TryDelete(tempBin);
                TryDelete(tempCue);
                if (binMoved) TryDelete(binPath);
                TryDelete(fullCue);
                throw;
            }
        }

        private static void BuildMode1Sector(byte[] target, int targetOffset, byte[] isoData, int isoOffset, long lba)
        {
            target[targetOffset] = 0x00;
            for (int i = 1; i <= 10; i++) target[targetOffset + i] = 0xFF;
            target[targetOffset + 11] = 0x00;

            long absolute = lba + 150;
            int minutes = (int)(absolute / (75 * 60));
            int seconds = (int)((absolute / 75) % 60);
            int frames = (int)(absolute % 75);
            target[targetOffset + 12] = (byte)minutes;
            target[targetOffset + 13] = (byte)seconds;
            target[targetOffset + 14] = (byte)frames;
            target[targetOffset + 15] = 0x01;

            Buffer.BlockCopy(isoData, isoOffset, target, targetOffset + 16, 2048);
            Array.Clear(target, targetOffset + 2064, 2352 - 2064);

            EccGenerate(target, targetOffset);

            uint edc = EdcCompute(0, target, targetOffset, 2064);
            target[targetOffset + 2064] = (byte)edc;
            target[targetOffset + 2065] = (byte)(edc >> 8);
            target[targetOffset + 2066] = (byte)(edc >> 16);
            target[targetOffset + 2067] = (byte)(edc >> 24);
        }

        private static uint EdcCompute(uint edc, byte[] buffer, int offset, int length)
        {
            for (int i = 0; i < length; i++)
                edc = (edc >> 8) ^ EdcLut[(edc ^ buffer[offset + i]) & 0xFF];
            return edc;
        }

        private static void EccGenerate(byte[] sector, int offset)
        {
            uint savedEdc = (uint)(sector[offset + 2064] | (sector[offset + 2065] << 8) |
                                   (sector[offset + 2066] << 16) | (sector[offset + 2067] << 24));
            sector[offset + 2064] = 0;
            sector[offset + 2065] = 0;
            sector[offset + 2066] = 0;
            sector[offset + 2067] = 0;

            EccBlock(sector, offset, 86, 24, 2, 86, offset + 0x81C);
            EccBlock(sector, offset, 52, 43, 86, 88, offset + 0x8C8);

            sector[offset + 2064] = (byte)savedEdc;
            sector[offset + 2065] = (byte)(savedEdc >> 8);
            sector[offset + 2066] = (byte)(savedEdc >> 16);
            sector[offset + 2067] = (byte)(savedEdc >> 24);
        }

        private static void EccBlock(byte[] sector, int sectorOffset, int majorCount, int minorCount, int majorMult, int minorInc, int destOffset)
        {
            int size = majorCount * minorCount;
            for (int major = 0; major < majorCount; major++)
            {
                int index = (major >> 1) * majorMult + (major & 1);
                byte eccA = 0;
                byte eccB = 0;
                for (int minor = 0; minor < minorCount; minor++)
                {
                    byte temp = sector[sectorOffset + 12 + index];
                    index += minorInc;
                    if (index >= size) index -= size;
                    eccA ^= temp;
                    eccB ^= temp;
                    eccA = EccF[eccA];
                }
                eccA = EccB[EccF[eccA] ^ eccB];
                sector[destOffset + major] = eccA;
                sector[destOffset + major + majorCount] = (byte)(eccA ^ eccB);
            }
        }

        private static void WriteCue(string path, string binFileName, string trackMode)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("FILE \"").Append(binFileName).Append("\" BINARY\r\n");
            sb.Append("  TRACK 01 ").Append(trackMode).Append("\r\n");
            sb.Append("    INDEX 01 00:00:00\r\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static void EnsureFreeSpace(string directory, long requiredBytes)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(directory));
                if (string.IsNullOrEmpty(root)) return;
                DriveInfo drive = new DriveInfo(root);
                if (drive.AvailableFreeSpace < requiredBytes + 64L * 1024 * 1024)
                    throw new IOException("Spazio insufficiente nella destinazione. Servono " + TextUtil.FormatBytes(requiredBytes + 64L * 1024 * 1024) + " liberi.");
            }
            catch (IOException) { throw; }
            catch { }
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
