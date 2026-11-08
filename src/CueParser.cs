using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CueDeck
{
    public class CueParseException : Exception
    {
        public CueParseException(string message) : base(message) { }
        public CueParseException(string message, Exception inner) : base(message, inner) { }
    }

    public static class CueParser
    {
        private static readonly Regex LineRegex = new Regex(@"^\s*([A-Za-z]+)\s*(.*)$", RegexOptions.Compiled);
        private static readonly Regex FileRegex = new Regex(@"^\s*FILE\s+(.+?)\s+(BINARY|MOTOROLA|AIFF|WAVE|MP3|FLAC)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TrackRegex = new Regex(@"^\s*TRACK\s+(\d+)\s+(\S+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex IndexRegex = new Regex(@"^\s*INDEX\s+(\d+)\s+(\d+):(\d+):(\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MsfRegex = new Regex(@"^\s*(\d+):(\d+):(\d+)\s*$", RegexOptions.Compiled);

        public static CueSheet ParseAny(DiscEntry entry)
        {
            if (entry == null) throw new CueParseException("Disco non valido.");
            if (entry.Kind == DiscKinds.Iso) return ParseIso(entry.Path);
            if (entry.Kind == DiscKinds.Bin) return ParseBin(entry.Path);
            return ParseCue(entry.Path);
        }

        public static CueSheet ParseCue(string cuePath)
        {
            if (string.IsNullOrEmpty(cuePath)) throw new CueParseException("Percorso CUE vuoto.");
            string fullCue = Path.GetFullPath(cuePath);
            if (!File.Exists(fullCue)) throw new CueParseException("File CUE non trovato:\r\n" + fullCue);

            string text = ReadAllTextSmart(fullCue);
            string baseDir = Path.GetDirectoryName(fullCue);
            CueSheet sheet = new CueSheet();
            sheet.SourcePath = fullCue;
            sheet.BaseDirectory = baseDir;
            sheet.Kind = DiscKinds.Cue;

            CueFile currentFile = null;
            CueTrack currentTrack = null;
            Dictionary<string, CueFile> fileCache = new Dictionary<string, CueFile>(StringComparer.OrdinalIgnoreCase);

            string[] lines = Regex.Split(text.TrimStart('\uFEFF'), "\r\n|\n|\r");
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string rawLine = lines[lineIndex];
                string line = rawLine.Trim();
                if (line.Length == 0) continue;
                if (line[0] == ';' || line.StartsWith("//", StringComparison.Ordinal)) continue;

                Match lineMatch = LineRegex.Match(line);
                if (!lineMatch.Success) continue;
                string command = lineMatch.Groups[1].Value.ToUpperInvariant();
                string rest = lineMatch.Groups[2].Value.Trim();

                if (command == "FILE")
                {
                    string fileName;
                    string fileType;
                    if (!TryParseFileCommand(rest, out fileName, out fileType))
                    {
                        sheet.Warnings.Add("Riga FILE non valida alla riga " + (lineIndex + 1) + ".");
                        continue;
                    }
                    string resolved = ResolveFilePath(baseDir, fileName);
                    CueFile file;
                    if (!fileCache.TryGetValue(resolved, out file))
                    {
                        file = BuildCueFile(fileName, resolved, fileType);
                        fileCache[resolved] = file;
                        sheet.Files.Add(file);
                        if (!file.Exists)
                            sheet.Warnings.Add("File immagine non trovato: " + fileName);
                    }
                    currentFile = file;
                    currentTrack = null;
                    continue;
                }

                if (command == "TRACK")
                {
                    Match tm = TrackRegex.Match(line);
                    if (!tm.Success)
                    {
                        sheet.Warnings.Add("Riga TRACK non valida alla riga " + (lineIndex + 1) + ".");
                        continue;
                    }
                    int number = 0;
                    int.TryParse(tm.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
                    string type = tm.Groups[2].Value.Trim().ToUpperInvariant();
                    CueTrack track = new CueTrack();
                    track.Number = number;
                    track.CueType = type;
                    track.File = currentFile;
                    ApplySectorInfo(track);
                    sheet.Tracks.Add(track);
                    currentTrack = track;
                    if (currentFile == null)
                    {
                        sheet.Warnings.Add("La traccia " + number.ToString("00") + " non ha un file associato.");
                    }
                    continue;
                }

                if (command == "INDEX")
                {
                    Match im = IndexRegex.Match(line);
                    if (im.Success && currentTrack != null)
                    {
                        int indexNumber;
                        int.TryParse(im.Groups[1].Value, out indexNumber);
                        long sector = MakeMsf(im.Groups[2].Value, im.Groups[3].Value, im.Groups[4].Value);
                        if (indexNumber == 0)
                        {
                            currentTrack.HasIndex00 = true;
                            currentTrack.Index00Sector = sector;
                        }
                        else if (indexNumber == 1)
                        {
                            currentTrack.HasIndex01 = true;
                            currentTrack.Index01Sector = sector;
                        }
                    }
                    continue;
                }

                if (command == "TITLE")
                {
                    string value = Unquote(rest);
                    if (currentTrack != null) currentTrack.Title = value;
                    else if (string.IsNullOrEmpty(sheet.Title)) sheet.Title = value;
                    continue;
                }

                if (command == "PERFORMER")
                {
                    string value = Unquote(rest);
                    if (currentTrack != null) currentTrack.Performer = value;
                    else if (string.IsNullOrEmpty(sheet.Performer)) sheet.Performer = value;
                    continue;
                }

                if (command == "PREGAP" || command == "POSTGAP")
                {
                    if (currentTrack != null)
                    {
                        long sectors = ParseMsfLoose(rest);
                        if (command == "PREGAP") currentTrack.PregapSectors = sectors;
                        else currentTrack.PostgapSectors = sectors;
                    }
                    continue;
                }

                if (command == "REM")
                {
                    Match rm = Regex.Match(rest, @"^\s*(GENRE|DATE|COMMENT|DISCID|CATALOG)\s+(.+)$", RegexOptions.IgnoreCase);
                    if (rm.Success)
                    {
                        string key = rm.Groups[1].Value.ToUpperInvariant();
                        string value = Unquote(rm.Groups[2].Value.Trim());
                        if (key == "GENRE") sheet.Genre = value;
                        else if (key == "DATE") sheet.Date = value;
                    }
                    continue;
                }
            }

            if (sheet.Tracks.Count == 0)
                throw new CueParseException("Il file CUE non contiene tracce.");

            if (string.IsNullOrEmpty(sheet.Title))
                sheet.Title = TextUtil.CleanDiscTitle(fullCue);

            ComputeLayout(sheet);

            try { sheet.TotalSourceBytes += new FileInfo(fullCue).Length; }
            catch { }

            for (int i = 0; i < sheet.Files.Count; i++)
                sheet.TotalSourceBytes += Math.Max(0, sheet.Files[i].LengthBytes);

            ValidateSheet(sheet);
            return sheet;
        }

        public static CueSheet ParseBin(string binPath)
        {
            if (string.IsNullOrEmpty(binPath)) throw new CueParseException("Percorso BIN vuoto.");
            string fullBin = Path.GetFullPath(binPath);
            if (!File.Exists(fullBin)) throw new CueParseException("File BIN non trovato:\r\n" + fullBin);

            CueSheet sheet = new CueSheet();
            sheet.SourcePath = fullBin;
            sheet.BaseDirectory = Path.GetDirectoryName(fullBin);
            sheet.Kind = DiscKinds.Bin;
            sheet.StandaloneBin = true;
            sheet.Title = TextUtil.CleanDiscTitle(fullBin);

            CueFile file = BuildCueFile(Path.GetFileName(fullBin), fullBin, "BINARY");
            sheet.Files.Add(file);
            sheet.TotalSourceBytes = file.LengthBytes;

            long size = file.LengthBytes;
            bool is2352 = size > 0 && size % 2352 == 0;
            bool is2048 = size > 0 && size % 2048 == 0;
            CueTrack track = new CueTrack();
            track.Number = 1;
            track.File = file;

            bool madeData = false;
            if (is2352)
            {
                long totalSectors = size / 2352;
                long dataStart = FindFirstDataSector(file.FullPath, totalSectors, 240);
                int mode = 0;
                if (dataStart >= 0) mode = ReadSectorMode(file.FullPath, dataStart);
                if (dataStart >= 0 && (mode == 1 || mode == 2))
                {
                    track.CueType = mode == 1 ? "MODE1/2352" : "MODE2/2352";
                    track.SectorSize = 2352;
                    track.UserDataOffset = mode == 1 ? 16 : 24;
                    track.UserDataSize = 2048;
                    track.IsAudio = false;
                    track.NativeSupported = true;
                    track.HasIndex01 = true;
                    track.Index01Sector = dataStart;
                    track.StartSector = dataStart;
                    track.EndSector = totalSectors;
                    track.Sectors = totalSectors - dataStart;
                    madeData = true;
                    if (dataStart > 0)
                        sheet.Warnings.Add("Trovato un pregap iniziale di " + dataStart.ToString(CultureInfo.InvariantCulture) + " settori: verrà ignorato nel montaggio.");
                }
            }

            if (!madeData && is2048 && LooksLikeIso2048(file.FullPath))
            {
                long totalSectors = size / 2048;
                track.CueType = "MODE1/2048";
                track.SectorSize = 2048;
                track.UserDataOffset = 0;
                track.UserDataSize = 2048;
                track.IsAudio = false;
                track.NativeSupported = true;
                track.HasIndex01 = true;
                track.Index01Sector = 0;
                track.StartSector = 0;
                track.EndSector = totalSectors;
                track.Sectors = totalSectors;
                madeData = true;
            }

            if (!madeData)
            {
                if (is2352)
                {
                    long totalSectors = size / 2352;
                    track.CueType = "AUDIO";
                    track.SectorSize = 2352;
                    track.UserDataOffset = 0;
                    track.UserDataSize = 2352;
                    track.IsAudio = true;
                    track.NativeSupported = false;
                    track.HasIndex01 = true;
                    track.Index01Sector = 0;
                    track.StartSector = 0;
                    track.EndSector = totalSectors;
                    track.Sectors = totalSectors;
                    sheet.Warnings.Add("Il file BIN sembra contenere audio CD e non un file system dati.");
                }
                else
                {
                    throw new CueParseException("Dimensioni BIN non riconosciute (" + TextUtil.FormatBytes(size) + "). Il file non sembra un'immagine CD settore-per-settore valida.");
                }
            }

            sheet.Tracks.Add(track);
            file.SectorCount = track.SectorSize > 0 ? file.LengthBytes / track.SectorSize : 0;
            ValidateSheet(sheet);
            return sheet;
        }

        public static CueSheet ParseIso(string isoPath)
        {
            if (string.IsNullOrEmpty(isoPath)) throw new CueParseException("Percorso ISO vuoto.");
            string fullIso = Path.GetFullPath(isoPath);
            if (!File.Exists(fullIso)) throw new CueParseException("File ISO non trovato:\r\n" + fullIso);

            long length = new FileInfo(fullIso).Length;
            long sectors = length / 2048;
            CueSheet sheet = new CueSheet();
            sheet.SourcePath = fullIso;
            sheet.BaseDirectory = Path.GetDirectoryName(fullIso);
            sheet.Kind = DiscKinds.Iso;
            sheet.TotalSourceBytes = length;

            CueFile file = BuildCueFile(Path.GetFileName(fullIso), fullIso, "BINARY");
            file.SectorCount = sectors;
            sheet.Files.Add(file);

            string label = TryReadIsoVolumeLabel(fullIso);
            sheet.Title = !string.IsNullOrEmpty(label) ? label : TextUtil.CleanDiscTitle(fullIso);

            CueTrack track = new CueTrack();
            track.Number = 1;
            track.CueType = "MODE1/2048";
            track.SectorSize = 2048;
            track.UserDataOffset = 0;
            track.UserDataSize = 2048;
            track.IsAudio = false;
            track.NativeSupported = true;
            track.HasIndex01 = true;
            track.Index01Sector = 0;
            track.StartSector = 0;
            track.EndSector = sectors;
            track.Sectors = sectors;
            track.File = file;
            sheet.Tracks.Add(track);

            if (length % 2048 != 0)
                sheet.Warnings.Add("La dimensione ISO non è un multiplo di 2048 byte.");
            if (length < 32768)
                sheet.Warnings.Add("Il file ISO è molto piccolo e potrebbe non essere valido.");

            return sheet;
        }

        public static string FindReferencingCue(string binPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(binPath));
                string binName = Path.GetFileName(binPath);
                if (!Directory.Exists(dir)) return null;
                string[] cues = Directory.GetFiles(dir, "*.cue", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < cues.Length; i++)
                {
                    try
                    {
                        string text = ReadAllTextSmart(cues[i]);
                        MatchCollection matches = Regex.Matches(text, @"(?im)^\s*FILE\s+(.+?)\s+(BINARY|MOTOROLA|AIFF|WAVE|MP3|FLAC)\s*$");
                        for (int m = 0; m < matches.Count; m++)
                        {
                            string declared = Unquote(matches[m].Groups[1].Value.Trim());
                            if (string.Equals(Path.GetFileName(declared), binName, StringComparison.OrdinalIgnoreCase))
                                return Path.GetFullPath(cues[i]);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        public static string TryReadIsoVolumeLabel(string isoPath)
        {
            try
            {
                using (FileStream fs = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] descriptor = new byte[2048];
                    for (int sector = 16; sector < 24; sector++)
                    {
                        if (fs.Length < (long)(sector + 1) * 2048) break;
                        fs.Position = (long)sector * 2048;
                        int read = ReadFull(fs, descriptor, 0, descriptor.Length);
                        if (read < 2048) break;
                        if (descriptor[1] != (byte)'C' || descriptor[2] != (byte)'D' || descriptor[3] != (byte)'0' ||
                            descriptor[4] != (byte)'0' || descriptor[5] != (byte)'1') break;
                        byte type = descriptor[0];
                        if (type == 1 || type == 2)
                        {
                            byte[] raw = new byte[32];
                            Array.Copy(descriptor, 40, raw, 0, 32);
                            string label;
                            if (type == 2)
                                label = Encoding.BigEndianUnicode.GetString(raw);
                            else
                                label = Encoding.ASCII.GetString(raw);
                            label = label.Trim('\0', ' ').Trim();
                            if (label.Length > 0) return label;
                        }
                        if (type == 255) break;
                    }
                }
            }
            catch { }
            return null;
        }

        private static void ValidateSheet(CueSheet sheet)
        {
            bool hasNativeData = false;
            bool hasUnsupportedData = false;
            for (int i = 0; i < sheet.Tracks.Count; i++)
            {
                CueTrack track = sheet.Tracks[i];
                if (track.Sectors <= 0)
                    sheet.Warnings.Add("La traccia " + track.Number.ToString("00", CultureInfo.InvariantCulture) + " non contiene settori validi.");
                if (track.IsAudio)
                {
                    if (track.File != null && (track.File.FileType == "MP3" || track.File.FileType == "AIFF" || track.File.FileType == "MOTOROLA"))
                        sheet.Warnings.Add("La traccia " + track.Number.ToString("00", CultureInfo.InvariantCulture) + " usa un formato audio esterno non supportato (" + track.File.FileType + ").");
                }
                if (!track.IsAudio)
                {
                    if (track.NativeSupported && track.UserDataSize == 2048) hasNativeData = true;
                    else hasUnsupportedData = true;
                }
            }
            if (!sheet.HasData)
            {
                if (!sheet.HasAudio)
                    sheet.Warnings.Add("Nessuna traccia audio o dati riconosciuta.");
                else
                    sheet.Warnings.Add("CD audio: non contiene un file system da montare. Puoi ascoltarlo o esportarlo.");
            }
            else if (!hasNativeData)
            {
                if (hasUnsupportedData)
                    sheet.Warnings.Add("Il formato dati di questo disco non è supportato dal montaggio nativo.");
            }
            if (hasNativeData && sheet.HasAudio)
                sheet.Warnings.Add("Le tracce audio non saranno disponibili nella modalità nativa (solo dati).");
        }

        private static void ComputeLayout(CueSheet sheet)
        {
            Dictionary<CueFile, List<CueTrack>> byFile = new Dictionary<CueFile, List<CueTrack>>();
            for (int i = 0; i < sheet.Tracks.Count; i++)
            {
                CueTrack t = sheet.Tracks[i];
                if (t.File == null) continue;
                List<CueTrack> list;
                if (!byFile.TryGetValue(t.File, out list))
                {
                    list = new List<CueTrack>();
                    byFile[t.File] = list;
                }
                list.Add(t);
            }

            foreach (KeyValuePair<CueFile, List<CueTrack>> pair in byFile)
            {
                CueFile file = pair.Key;
                List<CueTrack> tracks = pair.Value;
                long sectorSize = tracks.Count > 0 && tracks[0].SectorSize > 0 ? tracks[0].SectorSize : 2352;
                long effectiveLength = file.IsWave && file.DataLengthBytes > 0 ? file.DataLengthBytes : file.LengthBytes;
                if (file.IsWave)
                    file.SectorCount = Math.Max(0, effectiveLength / 2352);
                else
                    file.SectorCount = sectorSize > 0 ? effectiveLength / sectorSize : 0;

                if (file.Exists && sectorSize > 0 && file.LengthBytes % sectorSize != 0)
                    sheet.Warnings.Add("La dimensione di " + Path.GetFileName(file.FullPath) + " non è allineata ai settori da " + sectorSize + " byte.");

                long previousEnd = 0;
                for (int i = 0; i < tracks.Count; i++)
                {
                    CueTrack track = tracks[i];
                    long start;
                    if (track.HasIndex01) start = Math.Max(0, track.Index01Sector);
                    else if (track.HasIndex00) start = Math.Max(0, track.Index00Sector);
                    else start = previousEnd;

                    long end = file.SectorCount;
                    if (i + 1 < tracks.Count)
                    {
                        CueTrack next = tracks[i + 1];
                        long boundary;
                        if (next.HasIndex00) boundary = next.Index00Sector;
                        else if (next.HasIndex01) boundary = next.Index01Sector;
                        else boundary = next.Index01Sector;
                        if (boundary > start && boundary <= file.SectorCount) end = boundary;
                    }

                    if (!file.Exists) end = start;
                    if (start > file.SectorCount) start = file.SectorCount;
                    if (end < start) end = start;
                    track.StartSector = start;
                    track.EndSector = end;
                    track.Sectors = end - start;
                    previousEnd = end;

                    if (!track.HasIndex01)
                        sheet.Warnings.Add("La traccia " + track.Number.ToString("00", CultureInfo.InvariantCulture) + " non ha INDEX 01.");
                }
            }
        }

        private static CueFile BuildCueFile(string declaredPath, string resolvedPath, string fileType)
        {
            CueFile file = new CueFile();
            file.DeclaredPath = declaredPath;
            file.FullPath = resolvedPath;
            file.FileType = string.IsNullOrEmpty(fileType) ? "BINARY" : fileType.ToUpperInvariant();
            file.Exists = File.Exists(resolvedPath);
            if (file.Exists)
            {
                FileInfo info = new FileInfo(resolvedPath);
                file.LengthBytes = info.Length;
                if (file.FileType == "WAVE")
                {
                    file.IsWave = true;
                    long dataOffset;
                    long dataLength;
                    if (TryReadWaveData(resolvedPath, out dataOffset, out dataLength))
                    {
                        file.DataOffsetBytes = dataOffset;
                        file.DataLengthBytes = dataLength;
                    }
                    else
                    {
                        file.DataLengthBytes = file.LengthBytes;
                    }
                }
            }
            return file;
        }

        private static bool TryReadWaveData(string path, out long dataOffset, out long dataLength)
        {
            dataOffset = 44;
            dataLength = 0;
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] header = new byte[12];
                    if (ReadFull(fs, header, 0, 12) < 12) return false;
                    if (header[0] != 'R' || header[1] != 'I' || header[2] != 'F' || header[3] != 'F') return false;
                    if (header[8] != 'W' || header[9] != 'A' || header[10] != 'V' || header[11] != 'E') return false;
                    while (fs.Position + 8 <= fs.Length)
                    {
                        byte[] chunk = new byte[8];
                        if (ReadFull(fs, chunk, 0, 8) < 8) break;
                        string id = Encoding.ASCII.GetString(chunk, 0, 4);
                        uint size = BitConverter.ToUInt32(chunk, 4);
                        if (id == "data")
                        {
                            dataOffset = fs.Position;
                            dataLength = Math.Min(size, fs.Length - fs.Position);
                            return true;
                        }
                        long next = fs.Position + size + (size % 2);
                        if (next <= fs.Position || next > fs.Length) break;
                        fs.Position = next;
                    }
                }
            }
            catch { }
            return false;
        }

        private static long FindFirstDataSector(string path, long totalSectors, int scanLimit)
        {
            try
            {
                int limit = (int)Math.Min(totalSectors, scanLimit);
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] sector = new byte[2352];
                    for (int i = 0; i < limit; i++)
                    {
                        fs.Position = (long)i * 2352;
                        if (ReadFull(fs, sector, 0, 2352) < 2352) break;
                        if (HasSyncPattern(sector))
                        {
                            int mode = sector[15];
                            if (mode == 1 || mode == 2) return i;
                        }
                    }
                }
            }
            catch { }
            return -1;
        }

        private static int ReadSectorMode(string path, long sectorIndex)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] sector = new byte[2352];
                    fs.Position = sectorIndex * 2352;
                    if (ReadFull(fs, sector, 0, 2352) < 2352) return 0;
                    if (!HasSyncPattern(sector)) return 0;
                    return sector[15];
                }
            }
            catch { return 0; }
        }

        private static bool HasSyncPattern(byte[] sector)
        {
            if (sector.Length < 12) return false;
            if (sector[0] != 0x00 || sector[11] != 0x00) return false;
            for (int i = 1; i <= 10; i++)
                if (sector[i] != 0xFF) return false;
            return true;
        }

        private static bool LooksLikeIso2048(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fs.Length < 17 * 2048) return false;
                    byte[] buf = new byte[2048];
                    fs.Position = 16 * 2048;
                    if (ReadFull(fs, buf, 0, 2048) < 2048) return false;
                    return buf[0] == 1 && buf[1] == 'C' && buf[2] == 'D' && buf[3] == '0' && buf[4] == '0' && buf[5] == '1';
                }
            }
            catch { return false; }
        }

        private static void ApplySectorInfo(CueTrack track)
        {
            string type = track.CueType;
            track.IsAudio = type == "AUDIO";
            track.UserDataOffset = 0;
            track.UserDataSize = 2352;
            track.SectorSize = 2352;
            track.NativeSupported = false;

            if (track.IsAudio)
            {
                track.SectorSize = 2352;
                track.UserDataSize = 2352;
                track.UserDataOffset = 0;
                return;
            }

            if (type == "MODE1/2048")
            {
                track.SectorSize = 2048;
                track.UserDataOffset = 0;
                track.UserDataSize = 2048;
                track.NativeSupported = true;
            }
            else if (type == "MODE2/2048")
            {
                track.SectorSize = 2048;
                track.UserDataOffset = 0;
                track.UserDataSize = 2048;
                track.NativeSupported = true;
            }
            else if (type == "MODE1/2352")
            {
                track.SectorSize = 2352;
                track.UserDataOffset = 16;
                track.UserDataSize = 2048;
                track.NativeSupported = true;
            }
            else if (type == "MODE2/2352" || type == "CDI/2352")
            {
                track.SectorSize = 2352;
                track.UserDataOffset = 24;
                track.UserDataSize = 2048;
                track.NativeSupported = true;
            }
            else if (type == "MODE2/2336" || type == "CDI/2336")
            {
                track.SectorSize = 2336;
                track.UserDataOffset = 8;
                track.UserDataSize = 2048;
                track.NativeSupported = true;
            }
            else if (type.StartsWith("MODE1/") || type.StartsWith("MODE2/"))
            {
                string[] parts = type.Split('/');
                int size;
                if (parts.Length == 2 && int.TryParse(parts[1], out size) && (size == 2352 || size == 2336 || size == 2048))
                {
                    track.SectorSize = size;
                    if (size == 2352)
                    {
                        track.UserDataOffset = type.StartsWith("MODE2") ? 24 : 16;
                        track.UserDataSize = 2048;
                        track.NativeSupported = true;
                    }
                    else if (size == 2336)
                    {
                        track.UserDataOffset = 8;
                        track.UserDataSize = 2048;
                        track.NativeSupported = true;
                    }
                    else
                    {
                        track.UserDataOffset = 0;
                        track.UserDataSize = 2048;
                        track.NativeSupported = true;
                    }
                }
            }
        }

        private static bool TryParseFileCommand(string rest, out string fileName, out string fileType)
        {
            fileName = "";
            fileType = "BINARY";
            if (string.IsNullOrEmpty(rest)) return false;
            if (rest[0] == '"')
            {
                int close = rest.IndexOf('"', 1);
                if (close <= 0) return false;
                fileName = rest.Substring(1, close - 1);
                string after = rest.Substring(close + 1).Trim();
                if (after.Length > 0)
                {
                    int space = after.IndexOf(' ');
                    fileType = (space >= 0 ? after.Substring(0, space) : after).Trim().ToUpperInvariant();
                }
                return fileName.Length > 0;
            }
            Match m = FileRegex.Match("FILE " + rest);
            if (m.Success)
            {
                fileName = Unquote(m.Groups[1].Value.Trim());
                fileType = m.Groups[2].Value.ToUpperInvariant();
                return fileName.Length > 0;
            }
            int lastSpace = rest.LastIndexOf(' ');
            if (lastSpace > 0)
            {
                fileName = rest.Substring(0, lastSpace).Trim();
                fileType = rest.Substring(lastSpace + 1).Trim().ToUpperInvariant();
                return fileName.Length > 0;
            }
            fileName = rest.Trim();
            return fileName.Length > 0;
        }

        private static string ResolveFilePath(string baseDir, string declared)
        {
            if (string.IsNullOrEmpty(declared)) return "";
            string normalized = declared.Replace('/', '\\');
            try
            {
                if (Path.IsPathRooted(normalized))
                {
                    if (File.Exists(normalized)) return Path.GetFullPath(normalized);
                }
                else
                {
                    string candidate = Path.GetFullPath(Path.Combine(baseDir, normalized));
                    if (File.Exists(candidate)) return candidate;
                }

                string baseName = Path.GetFileName(normalized);
                if (!string.IsNullOrEmpty(baseName))
                {
                    string candidate = Path.GetFullPath(Path.Combine(baseDir, baseName));
                    if (File.Exists(candidate)) return candidate;

                    string[] files = Directory.GetFiles(baseDir, "*", SearchOption.TopDirectoryOnly);
                    for (int i = 0; i < files.Length; i++)
                    {
                        if (string.Equals(Path.GetFileName(files[i]), baseName, StringComparison.OrdinalIgnoreCase))
                            return Path.GetFullPath(files[i]);
                    }
                }
                return Path.GetFullPath(Path.Combine(baseDir, normalized));
            }
            catch
            {
                return normalized;
            }
        }

        public static string ReadAllTextSmart(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            try
            {
                UTF8Encoding strict = new UTF8Encoding(false, true);
                return strict.GetString(bytes);
            }
            catch { }

            try { return Encoding.GetEncoding(1252).GetString(bytes); }
            catch { return Encoding.Default.GetString(bytes); }
        }

        private static string Unquote(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string s = text.Trim();
            if (s.Length >= 2)
            {
                char first = s[0];
                char last = s[s.Length - 1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                    return s.Substring(1, s.Length - 2);
            }
            return s;
        }

        private static long MakeMsf(string minutes, string seconds, string frames)
        {
            long m, s, f;
            long.TryParse(minutes, NumberStyles.Integer, CultureInfo.InvariantCulture, out m);
            long.TryParse(seconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out s);
            long.TryParse(frames, NumberStyles.Integer, CultureInfo.InvariantCulture, out f);
            return (m * 60 + s) * 75 + f;
        }

        private static long ParseMsfLoose(string text)
        {
            Match m = MsfRegex.Match(text);
            if (!m.Success) return 0;
            return MakeMsf(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
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
    }
}
