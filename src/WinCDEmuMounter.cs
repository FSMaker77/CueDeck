using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace CueDeck
{
    public static class WinCDEmuMounter
    {
        public const string DownloadUrl = "https://wincdemu.sysprogs.org/portable/";

        public static string FindExecutable(string preferredPath)
        {
            if (!string.IsNullOrEmpty(preferredPath))
            {
                try
                {
                    if (Directory.Exists(preferredPath))
                    {
                        string inside = FindInDirectory(preferredPath);
                        if (!string.IsNullOrEmpty(inside)) return inside;
                    }
                    else if (File.Exists(preferredPath))
                    {
                        return Path.GetFullPath(preferredPath);
                    }
                }
                catch { }
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string[] directories = new string[]
            {
                Path.Combine(programFiles, "WinCDEmu"),
                Path.Combine(programFilesX86, "WinCDEmu"),
                Path.Combine(programFiles, "WinCDEmu (Portable)"),
                Path.Combine(programFilesX86, "WinCDEmu (Portable)")
            };
            for (int i = 0; i < directories.Length; i++)
            {
                string exe = FindInDirectory(directories[i]);
                if (!string.IsNullOrEmpty(exe)) return exe;
            }

            string[] keyPaths = new string[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WinCDEmu"
            };
            RegistryHive[] hives = new RegistryHive[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
            for (int h = 0; h < hives.Length; h++)
            {
                for (int k = 0; k < keyPaths.Length; k++)
                {
                    string fromRegistry = FindFromRegistry(hives[h], keyPaths[k]);
                    if (!string.IsNullOrEmpty(fromRegistry)) return fromRegistry;
                }
            }
            return null;
        }

        private static string FindFromRegistry(RegistryHive hive, string subKey)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default))
                using (RegistryKey root = baseKey.OpenSubKey(subKey))
                {
                    if (root == null) return null;
                    string[] names = root.GetSubKeyNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        using (RegistryKey sub = root.OpenSubKey(names[i]))
                        {
                            if (sub == null) continue;
                            object displayName = sub.GetValue("DisplayName");
                            if (displayName == null || displayName.ToString().IndexOf("WinCDEmu", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            object install = sub.GetValue("InstallLocation");
                            if (install != null)
                            {
                                string inside = FindInDirectory(install.ToString());
                                if (!string.IsNullOrEmpty(inside)) return inside;
                            }
                            object icon = sub.GetValue("DisplayIcon");
                            if (icon != null)
                            {
                                string iconPath = icon.ToString().Trim('"');
                                int comma = iconPath.LastIndexOf(',');
                                if (comma > 2) iconPath = iconPath.Substring(0, comma);
                                if (File.Exists(iconPath))
                                {
                                    string dir = Path.GetDirectoryName(iconPath);
                                    string inside = FindInDirectory(dir);
                                    if (!string.IsNullOrEmpty(inside)) return inside;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static string FindInDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return null;
            string[] names = new string[] { "vmnt64.exe", "vmnt.exe", "BatchMnt.exe", "batchmnt.exe", "PortableWinCDEmu.exe" };
            for (int i = 0; i < names.Length; i++)
            {
                string candidate = Path.Combine(directory, names[i]);
                if (File.Exists(candidate)) return candidate;
            }
            try
            {
                string[] files = Directory.GetFiles(directory, "*.exe", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < files.Length; i++)
                {
                    string name = Path.GetFileName(files[i]).ToLowerInvariant();
                    if (name.Contains("vmnt") || name.Contains("batchmnt") || name.Contains("wincdemu"))
                        return files[i];
                }
            }
            catch { }
            return null;
        }

        public static bool DriverInstalled()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\BazisVirtualCDBus"))
                    return key != null;
            }
            catch { return false; }
        }

        public static WinCDEmuMount MountCue(string cuePath, string executablePath)
        {
            if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
                throw new MountException("WinCDEmu non è installato o il percorso configurato non è valido.");
            if (string.IsNullOrEmpty(cuePath) || !File.Exists(cuePath))
                throw new MountException("File CUE non trovato.");

            HashSet<string> before = NativeMounter.GetCdromLetters();
            string exeName = Path.GetFileName(executablePath).ToLowerInvariant();
            bool batch = exeName.Contains("batchmnt");
            string arguments = "\"" + cuePath + "\"" + (batch ? " /wait" : "");
            int exitCode;
            string output;
            string error;
            RunTool(executablePath, arguments, 90000, out exitCode, out output, out error);

            string letter = WaitForNewCdrom(before, 15000);
            if (string.IsNullOrEmpty(letter))
            {
                string detail = (output + " " + error).Trim();
                throw new MountException("WinCDEmu non ha creato un'unità virtuale." + (detail.Length > 0 ? "\r\n" + detail : ""));
            }

            WinCDEmuMount mount = new WinCDEmuMount();
            mount.CuePath = Path.GetFullPath(cuePath);
            mount.DriveLetter = letter;
            mount.ExecutablePath = executablePath;
            return mount;
        }

        public static void Unmount(WinCDEmuMount mount)
        {
            if (mount == null || string.IsNullOrEmpty(mount.CuePath)) return;
            string letter = mount.DriveLetter;
            if (!string.IsNullOrEmpty(letter) && !NativeMounter.GetCdromLetters().Contains(letter))
                return;

            string exeName = Path.GetFileName(mount.ExecutablePath).ToLowerInvariant();
            bool batch = exeName.Contains("batchmnt");
            string arguments = batch
                ? "/unmount \"" + mount.CuePath + "\""
                : "\"" + mount.CuePath + "\"";
            int exitCode;
            string output;
            string error;
            RunTool(mount.ExecutablePath, arguments, 90000, out exitCode, out output, out error);
            if (!string.IsNullOrEmpty(letter)) WaitForDriveGone(letter, 8000);
        }

        private static string WaitForNewCdrom(HashSet<string> before, int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                HashSet<string> now = NativeMounter.GetCdromLetters();
                foreach (string letter in now)
                {
                    if (!before.Contains(letter))
                    {
                        try
                        {
                            DriveInfo info = new DriveInfo(letter + ":\\");
                            if (info.IsReady) return letter;
                        }
                        catch { return letter; }
                    }
                }
                Thread.Sleep(300);
                waited += 300;
            }
            foreach (string letter in NativeMounter.GetCdromLetters())
                if (!before.Contains(letter)) return letter;
            return null;
        }

        private static void WaitForDriveGone(string letter, int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                if (!NativeMounter.GetCdromLetters().Contains(letter)) return;
                Thread.Sleep(250);
                waited += 250;
            }
        }

        private static void RunTool(string exe, string arguments, int timeoutMs, out int exitCode, out string output, out string error)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exe;
            psi.Arguments = arguments;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.WorkingDirectory = Path.GetDirectoryName(exe);

            using (Process process = new Process())
            {
                process.StartInfo = psi;
                process.Start();
                bool exited = process.WaitForExit(timeoutMs);
                if (!exited)
                {
                    try { process.Kill(); } catch { }
                    throw new MountException("WinCDEmu non ha risposto in tempo. Se richiesto, conferma il prompt di Windows.");
                }
                output = process.StandardOutput.ReadToEnd().Trim();
                error = process.StandardError.ReadToEnd().Trim();
                exitCode = process.ExitCode;
            }
        }
    }
}
