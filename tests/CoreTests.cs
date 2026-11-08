using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace CueDeck
{
    internal static class CoreTests
    {
        private static int _failures;
        private static string _dir;

        private static void Check(bool condition, string message)
        {
            if (condition)
            {
                Console.WriteLine("  PASS  " + message);
            }
            else
            {
                _failures++;
                Console.WriteLine("  FAIL  " + message);
            }
        }

        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            _dir = args.Length > 0 ? args[0] : @"C:\Users\Administrator\AppData\Local\Temp\opencode\cuedeck-test\artifacts";
            Console.WriteLine("CueDeck core tests: " + _dir);

            try
            {
                TestParseData();
                TestConvert();
                TestMixedMode();
                TestAudioOnly();
                TestMultiFile();
                TestStandaloneBin();
                TestExportAudio();
                TestMount();
            }
            catch (Exception ex)
            {
                _failures++;
                Console.WriteLine("EXCEPTION: " + ex);
            }

            Console.WriteLine();
            Console.WriteLine(_failures == 0 ? "ALL TESTS PASSED" : _failures + " TEST(S) FAILED");
            return _failures == 0 ? 0 : 1;
        }

        private static void TestParseData()
        {
            Console.WriteLine("\n[parse data cue]");
            CueSheet s = CueParser.ParseCue(Path.Combine(_dir, "test.cue"));
            Check(s.Tracks.Count == 1, "one track");
            Check(s.HasData && !s.HasAudio, "data only");
            Check(s.NativeDataTrack != null, "native data track present");
            Check(s.NativeDataTrack.SectorSize == 2352, "sector size 2352");
            Check(s.NativeDataTrack.UserDataOffset == 16, "user data at +16");
            Check(s.NativeDataTrack.Sectors == 35, "35 sectors, got " + s.NativeDataTrack.Sectors);
            Check(s.NativeDataTrack.DurationSeconds > 0.46 && s.NativeDataTrack.DurationSeconds < 0.47, "duration ~0.466s");
        }

        private static void TestConvert()
        {
            Console.WriteLine("\n[convert to iso]");
            CueSheet s = CueParser.ParseCue(Path.Combine(_dir, "test.cue"));
            string iso = IsoBuilder.GetOrBuild(s, null, CancellationToken.None);
            Check(File.Exists(iso), "iso exists: " + iso);
            Check(new FileInfo(iso).Length == 35 * 2048, "iso size 71680");
            byte[] expected = File.ReadAllBytes(Path.Combine(_dir, "test.iso"));
            byte[] actual = File.ReadAllBytes(iso);
            bool same = expected.Length == actual.Length;
            if (same)
            {
                for (int i = 0; i < expected.Length; i++)
                {
                    if (expected[i] != actual[i]) { same = false; break; }
                }
            }
            Check(same, "converted bytes match source ISO");
            string iso2 = IsoBuilder.GetOrBuild(s, null, CancellationToken.None);
            Check(iso == iso2, "cache hit returns same path");
        }

        private static void TestMixedMode()
        {
            Console.WriteLine("\n[mixed mode cue]");
            CueSheet s = CueParser.ParseCue(Path.Combine(_dir, "mixed.cue"));
            Check(s.Tracks.Count == 2, "two tracks");
            Check(s.NativeDataTrack != null && s.NativeDataTrack.Number == 1, "data track 1");
            Check(s.NativeDataTrack.Sectors == 35, "data ends at audio index");
            CueTrack audio = s.AudioTracks[0];
            Check(audio.Number == 2, "audio is track 2");
            Check(audio.StartSector == 35, "audio starts at 35");
            Check(audio.Sectors == 5, "audio has 5 sectors, got " + audio.Sectors);
            Check(audio.DisplayTitle == "Traccia di prova", "audio title parsed");
            Check(s.Warnings.Count > 0, "mixed mode warning present");

            string wav = Path.Combine(AppPaths.PreviewDir, "coretest.wav");
            try
            {
                AudioTools.ExtractWave(audio, wav, null, CancellationToken.None);
                Check(new FileInfo(wav).Length == 44 + 5 * 2352, "wav size correct");
                byte[] header = new byte[12];
                using (FileStream fs = File.OpenRead(wav)) fs.Read(header, 0, 12);
                Check(Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && Encoding.ASCII.GetString(header, 8, 4) == "WAVE", "valid wav header");
            }
            finally { try { File.Delete(wav); } catch { } }
        }

        private static void TestAudioOnly()
        {
            Console.WriteLine("\n[audio only cue]");
            CueSheet s = CueParser.ParseCue(Path.Combine(_dir, "audio.cue"));
            Check(s.HasAudio && !s.HasData, "audio only");
            Check(s.NativeDataTrack == null, "nothing to mount natively");
            Check(s.IsAudioOnly, "IsAudioOnly");
            Check(s.AudioTracks.Count == 1 && s.AudioTracks[0].Sectors == 7, "7 audio sectors");
            Check(s.AudioTracks[0].DisplayTitle == "Silenzio", "title");
        }

        private static void TestMultiFile()
        {
            Console.WriteLine("\n[multi file cue]");
            CueSheet s = CueParser.ParseCue(Path.Combine(_dir, "multi.cue"));
            Check(s.Tracks.Count == 2, "two tracks");
            Check(s.Title == "Multi file", "global title");
            Check(s.Performer == "CueDeck", "global performer");
            Check(s.AudioTracks.Count == 2, "two audio tracks");
            Check(s.AudioTracks[0].Sectors == 3, "track 1 has 3 sectors");
            Check(s.AudioTracks[1].Sectors == 4, "track 2 has 4 sectors");
            Check(s.AudioTracks[1].DisplayTitle == "Due", "track 2 title");
            Check(s.TotalSourceBytes > 7 * 2352, "total source bytes");
        }

        private static void TestStandaloneBin()
        {
            Console.WriteLine("\n[standalone bins]");
            CueSheet data = CueParser.ParseBin(Path.Combine(_dir, "test.bin"));
            Check(data.NativeDataTrack != null, "data bin detected");
            Check(data.NativeDataTrack.Sectors == 35, "35 sectors");
            CueSheet audio = CueParser.ParseBin(Path.Combine(_dir, "audio.bin"));
            Check(audio.IsAudioOnly, "audio bin detected");
            Check(audio.AudioTracks.Count == 1 && audio.AudioTracks[0].Sectors == 7, "audio bin 7 sectors");
        }

        private static void TestExportAudio()
        {
            Console.WriteLine("\n[export audio]");
            CueSheet s = CueParser.ParseCue(Path.Combine(_dir, "mixed.cue"));
            string dir = Path.Combine(Path.GetTempPath(), "cuedeck-export-test");
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                List<string> files = AudioTools.ExportAllAudio(s, dir, null, CancellationToken.None);
                Check(files.Count == 1, "one wav exported");
                Check(files.Count > 0 && File.Exists(files[0]), "wav file exists");
                if (files.Count > 0)
                    Check(new FileInfo(files[0]).Length == 44 + 5 * 2352, "exported wav has expected size");
            }
            finally
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            }
        }

        private static void TestMount()
        {
            Console.WriteLine("\n[native mount]");
            string iso = Path.Combine(_dir, "test.iso");
            NativeMount mount = null;
            try
            {
                mount = NativeMounter.Mount(iso);
                Check(!string.IsNullOrEmpty(mount.DriveLetter), "got drive letter " + mount.DriveLetter);
                string root = mount.DriveLetter + ":\\";
                Check(File.Exists(Path.Combine(root, "HELLO.TXT")), "HELLO.TXT visible on mounted drive");
                if (File.Exists(Path.Combine(root, "HELLO.TXT")))
                {
                    string text = File.ReadAllText(Path.Combine(root, "HELLO.TXT"));
                    Check(text.StartsWith("Ciao dal disco", StringComparison.Ordinal), "file content readable");
                }
            }
            catch (Exception ex)
            {
                Check(false, "mount failed: " + ex.Message);
            }
            finally
            {
                if (mount != null)
                {
                    try
                    {
                        NativeMounter.Unmount(mount);
                        Check(!NativeMounter.GetCdromLetters().Contains(mount.DriveLetter), "drive removed after unmount");
                    }
                    catch (Exception ex)
                    {
                        Check(false, "unmount failed: " + ex.Message);
                    }
                }
            }
        }
    }
}
