using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CueDeck
{
    public class MountException : Exception
    {
        public MountException(string message) : base(message) { }
        public MountException(string message, Exception inner) : base(message, inner) { }
    }

    public static class NativeMounter
    {
        private const uint VirtualStorageTypeDeviceIso = 1;
        private const uint VirtualDiskAccessAttachRo = 0x00010000;
        private const uint AttachVirtualDiskFlagNone = 0;
        private const uint AttachVirtualDiskFlagPermanentLifetime = 0x00000001;
        private static readonly Guid VendorMicrosoft = new Guid("ec984aec-a0f9-47e9-901f-71415a66345b");

        [StructLayout(LayoutKind.Sequential)]
        private struct VIRTUAL_STORAGE_TYPE
        {
            public uint DeviceId;
            public Guid VendorId;
        }

        [DllImport("virtdisk.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int OpenVirtualDisk(ref VIRTUAL_STORAGE_TYPE virtualStorageType, string path, uint accessMask, uint flags, IntPtr parameters, out IntPtr handle);

        [DllImport("virtdisk.dll", SetLastError = true)]
        private static extern int AttachVirtualDisk(IntPtr handle, IntPtr securityDescriptor, uint flags, uint providerSpecificFlags, IntPtr parameters, IntPtr overlapped);

        [DllImport("virtdisk.dll", SetLastError = true)]
        private static extern int DetachVirtualDisk(IntPtr handle, uint flags, uint providerSpecificFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        public static NativeMount Mount(string isoPath)
        {
            if (string.IsNullOrEmpty(isoPath) || !File.Exists(isoPath))
                throw new MountException("File ISO non trovato.");
            string fullPath = Path.GetFullPath(isoPath);
            string nativeError = null;

            try
            {
                NativeMount native = MountNative(fullPath);
                if (native != null) return native;
            }
            catch (Exception ex)
            {
                nativeError = ex.Message;
                Logger.Error("Native ISO mount: " + fullPath, ex);
            }

            try
            {
                NativeMount fallback = MountWithPowerShell(fullPath);
                if (fallback != null) return fallback;
            }
            catch (Exception ex)
            {
                Logger.Error("PowerShell ISO mount: " + fullPath, ex);
                string message = "Impossibile montare il disco.";
                if (!string.IsNullOrEmpty(nativeError)) message += "\r\n\r\nDettaglio: " + nativeError;
                message += "\r\n\r\n" + ex.Message;
                throw new MountException(message, ex);
            }

            throw new MountException("Impossibile montare il disco." + (nativeError != null ? "\r\n\r\n" + nativeError : ""));
        }

        private static NativeMount MountNative(string fullPath)
        {
            HashSet<string> before = GetCdromLetters();
            VIRTUAL_STORAGE_TYPE type = new VIRTUAL_STORAGE_TYPE();
            type.DeviceId = VirtualStorageTypeDeviceIso;
            type.VendorId = VendorMicrosoft;

            IntPtr handle;
            int rc = OpenVirtualDisk(ref type, fullPath, VirtualDiskAccessAttachRo, 0, IntPtr.Zero, out handle);
            if (rc != 0) throw new MountException("OpenVirtualDisk: " + FormatWin32(rc));

            try
            {
                rc = AttachVirtualDisk(handle, IntPtr.Zero, AttachVirtualDiskFlagPermanentLifetime, 0, IntPtr.Zero, IntPtr.Zero);
                if (rc != 0) throw new MountException("AttachVirtualDisk: " + FormatWin32(rc));

                string letter = WaitForNewCdrom(before, 12000);
                if (string.IsNullOrEmpty(letter))
                    throw new MountException("Il disco è stato collegato ma Windows non ha assegnato una lettera di unità.");

                NativeMount mount = new NativeMount();
                mount.Handle = handle;
                mount.ImagePath = fullPath;
                mount.DriveLetter = letter;
                mount.PowerShellFallback = false;
                mount.MountedUtc = DateTime.UtcNow;
                handle = IntPtr.Zero;
                return mount;
            }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    try { DetachVirtualDisk(handle, 0, 0); } catch { }
                    CloseHandle(handle);
                }
            }
        }

        private static NativeMount MountWithPowerShell(string fullPath)
        {
            string escaped = fullPath.Replace("'", "''");
            string script =
                "$ErrorActionPreference='Stop';" +
                "$img = Mount-DiskImage -ImagePath '" + escaped + "' -PassThru;" +
                "Start-Sleep -Milliseconds 400;" +
                "$v = $img | Get-Volume;" +
                "if ($v -and $v.DriveLetter) { [Console]::Out.Write($v.DriveLetter) } else { [Console]::Out.Write('NOLETTER') }";

            string output = RunPowerShell(script, 45000);
            string letter = ExtractDriveLetter(output);
            if (string.IsNullOrEmpty(letter))
                throw new MountException("Windows non ha assegnato una lettera al disco.");

            HashSet<string> before = GetCdromLetters();
            NativeMount mount = new NativeMount();
            mount.Handle = IntPtr.Zero;
            mount.ImagePath = fullPath;
            mount.DriveLetter = letter;
            mount.PowerShellFallback = true;
            mount.MountedUtc = DateTime.UtcNow;
            return mount;
        }

        public static void Unmount(NativeMount mount)
        {
            if (mount == null) return;
            if (mount.PowerShellFallback)
            {
                string escaped = mount.ImagePath.Replace("'", "''");
                string script = "$ErrorActionPreference='Stop'; Dismount-DiskImage -ImagePath '" + escaped + "' | Out-Null; [Console]::Out.Write('OK')";
                try { RunPowerShell(script, 30000); }
                catch (Exception ex) { Logger.Error("Dismount PowerShell " + mount.ImagePath, ex); throw; }
                WaitForDriveGone(mount.DriveLetter, 4000);
                return;
            }

            if (mount.Handle != IntPtr.Zero)
            {
                int rc = DetachVirtualDisk(mount.Handle, 0, 0);
                if (rc != 0)
                {
                    Logger.Info("DetachVirtualDisk returned " + FormatWin32(rc) + " for " + mount.ImagePath);
                }
                CloseHandle(mount.Handle);
                mount.Handle = IntPtr.Zero;
            }
            WaitForDriveGone(mount.DriveLetter, 5000);
        }

        private static string WaitForNewCdrom(HashSet<string> before, int timeoutMs)
        {
            int waited = 0;
            string last = null;
            while (waited < timeoutMs)
            {
                HashSet<string> now = GetCdromLetters();
                foreach (string letter in now)
                {
                    if (!before.Contains(letter))
                    {
                        last = letter;
                        try
                        {
                            DriveInfo info = new DriveInfo(letter + ":\\");
                            if (info.IsReady) return letter;
                        }
                        catch { }
                    }
                }
                Thread.Sleep(250);
                waited += 250;
            }
            return last;
        }

        private static void WaitForDriveGone(string letter, int timeoutMs)
        {
            if (string.IsNullOrEmpty(letter)) return;
            int waited = 0;
            while (waited < timeoutMs)
            {
                if (!GetCdromLetters().Contains(letter)) return;
                Thread.Sleep(200);
                waited += 200;
            }
        }

        public static HashSet<string> GetCdromLetters()
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                for (int i = 0; i < drives.Length; i++)
                {
                    try
                    {
                        if (drives[i].DriveType == DriveType.CDRom && drives[i].Name.Length >= 2)
                            result.Add(drives[i].Name.Substring(0, 1).ToUpperInvariant());
                    }
                    catch { }
                }
            }
            catch { }
            return result;
        }

        public static string RunPowerShell(string script, int timeoutMs)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe");
            if (!File.Exists(psi.FileName)) psi.FileName = "powershell.exe";
            psi.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"" + script.Replace("\"", "\\\"") + "\"";
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            using (Process p = new Process())
            {
                p.StartInfo = psi;
                p.Start();
                bool exited = p.WaitForExit(timeoutMs);
                if (!exited)
                {
                    try { p.Kill(); } catch { }
                    throw new MountException("Windows PowerShell non ha risposto in tempo.");
                }
                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();
                if (p.ExitCode != 0)
                {
                    string detail = (error + " " + output).Trim();
                    throw new MountException("Operazione di montaggio non riuscita." + (detail.Length > 0 ? "\r\n" + detail : ""));
                }
                return output.Trim();
            }
        }

        private static string ExtractDriveLetter(string output)
        {
            if (string.IsNullOrEmpty(output)) return null;
            string[] lines = output.Replace("\r", "").Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string line = lines[i].Trim();
                if (line.Length == 1 && line[0] >= 'A' && line[0] <= 'Z') return line;
                if (line.Length == 2 && line[1] == ':' && line[0] >= 'A' && line[0] <= 'Z') return line.Substring(0, 1);
            }
            return null;
        }

        private static string FormatWin32(int code)
        {
            try { return new Win32Exception(code).Message + " (0x" + code.ToString("X8", CultureInfo.InvariantCulture) + ")"; }
            catch { return "errore " + code.ToString(CultureInfo.InvariantCulture); }
        }
    }
}
