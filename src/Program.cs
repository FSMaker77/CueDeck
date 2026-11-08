using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CueDeck
{
    public class StartupItem
    {
        public string Path;
        public bool Mount;

        public StartupItem(string path, bool mount)
        {
            Path = path;
            Mount = mount;
        }
    }

    internal static class Program
    {
        private const string MutexName = @"Local\CueDeck.SingleInstance.v1";
        private static Mutex _mutex;
        private static string _pipeName;

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                Logger.Error("UI thread exception", e.Exception);
                try { MessageBox.Show("Si e verificato un errore:\r\n\r\n" + e.Exception.Message, "CueDeck", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Logger.Error("Unhandled exception", e.ExceptionObject as Exception);
            };

            List<StartupItem> items = ParseArguments(args);
            _pipeName = "CueDeck.Pipe." + Process.GetCurrentProcess().SessionId.ToString();

            bool created;
            _mutex = new Mutex(true, MutexName, out created);
            if (!created)
            {
                bool ok = SendToRunningInstance(items);
                if (!ok)
                {
                    try
                    {
                        MessageBox.Show("CueDeck è già in esecuzione, ma non è stato possibile contattare la finestra principale.\r\n\r\nChiudi l'altra istanza e riprova.",
                            "CueDeck", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch { }
                }
                return;
            }

            try
            {
                AppPaths.Ensure();
                MainForm form = new MainForm(items);
                IntPtr handle = form.Handle;
                PipeServer.Start(_pipeName, form);
                Application.Run(form);
            }
            catch (Exception ex)
            {
                Logger.Error("Fatal startup", ex);
                try { MessageBox.Show("Impossibile avviare CueDeck:\r\n\r\n" + ex.Message, "CueDeck", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                catch { }
            }
            finally
            {
                try { if (_mutex != null) _mutex.ReleaseMutex(); } catch { }
                try { if (_mutex != null) _mutex.Dispose(); } catch { }
            }
        }

        private static List<StartupItem> ParseArguments(string[] args)
        {
            List<StartupItem> items = new List<StartupItem>();
            bool mountNext = false;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.IsNullOrEmpty(arg)) continue;
                if (string.Equals(arg, "--mount", StringComparison.OrdinalIgnoreCase))
                {
                    mountNext = true;
                    continue;
                }
                if (string.Equals(arg, "--add", StringComparison.OrdinalIgnoreCase))
                {
                    mountNext = false;
                    continue;
                }
                if (arg.StartsWith("--", StringComparison.Ordinal)) continue;
                if (arg.StartsWith("-", StringComparison.Ordinal)) continue;
                items.Add(new StartupItem(arg, mountNext));
                mountNext = false;
            }
            return items;
        }

        private static bool SendToRunningInstance(List<StartupItem> items)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    using (NamedPipeClientStream client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out))
                    {
                        client.Connect(1200);
                        using (StreamWriter writer = new StreamWriter(client, new UTF8Encoding(false)))
                        {
                            writer.AutoFlush = true;
                            if (items == null || items.Count == 0)
                            {
                                writer.WriteLine("SHOW");
                            }
                            else
                            {
                                for (int i = 0; i < items.Count; i++)
                                {
                                    string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(items[i].Path));
                                    writer.WriteLine((items[i].Mount ? "MOUNT\t" : "ADD\t") + encoded);
                                }
                            }
                        }
                    }
                    return true;
                }
                catch
                {
                    Thread.Sleep(250);
                }
            }
            return false;
        }
    }

    internal static class PipeServer
    {
        public static void Start(string pipeName, MainForm form)
        {
            Thread thread = new Thread(delegate()
            {
                while (true)
                {
                    try
                    {
                        using (NamedPipeServerStream server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None))
                        {
                            server.WaitForConnection();
                            using (StreamReader reader = new StreamReader(server, Encoding.UTF8))
                            {
                                string line;
                                while ((line = reader.ReadLine()) != null)
                                {
                                    if (line.Length == 0) continue;
                                    form.HandleExternalCommand(line);
                                }
                            }
                        }
                    }
                    catch
                    {
                        try { Thread.Sleep(400); } catch { }
                    }
                }
            });
            thread.IsBackground = true;
            thread.Name = "CueDeck single-instance pipe";
            thread.Start();
        }
    }
}
