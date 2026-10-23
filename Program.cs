// Build: msbuild A2G-Scaler.csproj /restore /t:Rebuild /p:Configuration=Release /p:Platform=x86
using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace A2GScaler
{
    /// <summary>Entry point: single instance, crash recovery, target selection, then the scaler window.</summary>
    static class Program
    {
        static ScalerForm form;

        /// <summary>Starts the scaler: steps aside if one already runs, repairs leftovers of a crashed run, installs the crash handler, picks or attaches to the target and runs the form.</summary>
        [STAThread]
        static void Main()
        {
            // The dsound proxy (see dsound-proxy/) starts us with --attach <pid> when the game or editor launches.
            int attachPid = AttachPid(Environment.GetCommandLineArgs());

            bool first;
            using (new Mutex(true, "A2G-Scaler-SingleInstance", out first))
            {
                // Started automatically for a game that a running scaler already shows: just step aside, no popup.
                if (!first) { if (attachPid == 0) MessageBox.Show("A2G-Scaler läuft bereits.", "A2G-Scaler"); return; }

                Native.TrySetProcessDPIAware();
                Native.timeBeginPeriod(1);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // Undo anything a crashed or killed earlier run left behind (mouse speed, game position, taskbar, cursors).
                string repaired = Recovery.RepairLeftovers();

                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e) { Crash(e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { Crash(e.ExceptionObject as Exception); };

                Settings settings = Settings.Load();
                Target target;
                Process started;
                if (attachPid != 0)
                {
                    // Attach straight to the process the proxy named; it is our launched process, so we close with it.
                    try { started = Process.GetProcessById(attachPid); }
                    catch (ArgumentException) { return; } // host already gone
                    target = Target.ForProcess(started);
                }
                else if (!Launcher.Choose(settings, out target, out started)) return;

                form = new ScalerForm(settings, repaired, target, started);
                Application.Run(form);
            }
        }

        /// <summary>The PID from a "--attach &lt;pid&gt;" command line (args[0] is the program), or 0.</summary>
        internal static int AttachPid(string[] args)
        {
            for (int i = 1; i < args.Length - 1; i++)
            {
                int pid;
                if (args[i] == "--attach" && int.TryParse(args[i + 1], out pid) && pid > 0) return pid;
            }
            return 0;
        }

        /// <summary>Last resort for unhandled exceptions: undoes every system-wide change, shows the error and exits.</summary>
        static void Crash(Exception ex)
        {
            try { if (form != null) form.RestoreEverything(); } catch (Exception) { }
            Native.ClipCursorNone(IntPtr.Zero);
            try { Native.MagShowSystemCursor(true); } catch (Exception) { }
            MessageBox.Show("A2G-Scaler wurde wegen eines Fehlers beendet:\n\n" + (ex != null ? ex.ToString() : "unbekannt"), "A2G-Scaler",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.Exit(1);
        }
    }
}
