using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace A2GScaler
{
    /// <summary>A program of the game folder that the scaler can show: the game itself or the data editor.</summary>
    class Target
    {
        /// <summary>
        /// The game: anstoss2.exe, in the GOG release run.exe. The 2007 release has both: its anstoss2.exe is a
        /// windowless starter that runs the game as run.exe and stays alive meanwhile.
        /// </summary>
        public static readonly Target Game = new Target("anstoss2", "Anstoss 2 Gold", "#32770", "run", true);
        public static readonly Target Editor = new Target("Editor", "Dateneditor", "ASCARON ANSTOSS 2 DATENEDITOR", null, false);

        public readonly string ProcessName, DisplayName, MainWindowClass;

        /// <summary>Second process name of the same program (run.exe of the GOG and 2007 releases), or null.</summary>
        public readonly string GogProcessName;

        /// <summary>
        /// Only a process showing the game's window counts (<see cref="IsGameTitle"/>): run is a generic name, and
        /// the 2007 starter anstoss2.exe has no window at all.
        /// </summary>
        readonly bool needsGameWindow;

        /// <summary>Internal rather than private so tests can build targets with dummy process names.</summary>
        internal Target(string processName, string displayName, string mainWindowClass, string gogProcessName, bool needsGameWindow)
        {
            ProcessName = processName; DisplayName = displayName; MainWindowClass = mainWindowClass; GogProcessName = gogProcessName;
            this.needsGameWindow = needsGameWindow;
        }

        /// <summary>True if <paramref name="p"/> shows this target's window (always true for targets that need none).</summary>
        public bool HasGameWindow(Process p)
        {
            if (!needsGameWindow) return true;
            try { p.Refresh(); return IsGameTitle(p.MainWindowTitle); } // Refresh: Process caches the title
            catch (Exception) { return false; }
        }

        /// <summary>True for the game's window titles ("ANSTOSS 2 - ..."), as the trainer recognises the game.</summary>
        internal static bool IsGameTitle(string title)
        {
            return title != null && title.StartsWith("ANSTOSS 2", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The target that matches a running process, for the proxy's --attach. Unknown names are treated like the game.</summary>
        public static Target ForProcess(Process p)
        {
            string name;
            try { name = p.ProcessName; } catch (Exception) { name = ""; }
            if (name.Equals(Game.ProcessName, StringComparison.OrdinalIgnoreCase)) return Game;
            if (name.Equals(Game.GogProcessName, StringComparison.OrdinalIgnoreCase)) return Game;
            if (name.Equals(Editor.ProcessName, StringComparison.OrdinalIgnoreCase)) return Editor;
            return new Target(name.Length > 0 ? name : Game.ProcessName, Game.DisplayName, Game.MainWindowClass, null, true);
        }

        /// <summary>The program's exe in <paramref name="folder"/>: the usual name, or the GOG one if only that exists.</summary>
        public string ExeIn(string folder)
        {
            string exe = Path.Combine(folder, ProcessName + ".exe");
            if (GogProcessName == null || File.Exists(exe)) return exe;
            string gog = Path.Combine(folder, GogProcessName + ".exe");
            return File.Exists(gog) ? gog : exe;
        }

        /// <summary>
        /// A running process of this target that shows its window, or null. Windows keeps listing a process for a
        /// while after it ended (the game takes a second or so to go away); such exited processes do not count.
        /// </summary>
        public Process FindRunning(int exceptPid = 0)
        {
            Process found = Pick(Process.GetProcessesByName(ProcessName), exceptPid, needsGameWindow);
            if (found == null && GogProcessName != null) found = Pick(Process.GetProcessesByName(GogProcessName), exceptPid, needsGameWindow);
            return found;
        }

        /// <summary>
        /// True if this target is running in any state, also still without its window (a game just starting, the
        /// 2007 starter). The generic GOG name only counts with the game window.
        /// </summary>
        public bool IsStarted()
        {
            using (Process p = Pick(Process.GetProcessesByName(ProcessName), 0, false)) if (p != null) return true;
            using (Process p = FindRunning()) return p != null;
        }

        /// <summary>The first live process of <paramref name="candidates"/> (optionally only one with the game window); disposes the rest.</summary>
        Process Pick(Process[] candidates, int exceptPid, bool needWindow)
        {
            Process found = null;
            foreach (Process p in candidates)
            {
                bool live;
                try { live = p.Id != exceptPid && !p.HasExited; }
                catch (Exception) { live = p.Id != exceptPid; } // elevated, cannot be queried: assume it runs
                if (live && needWindow) live = HasGameWindow(p);
                if (live && found == null) found = p; else p.Dispose();
            }
            return found;
        }

        /// <summary>Icon for the launch dialog: anstoss2.exe has no icon resource, but anstoss2.ico lies next to it.</summary>
        public Image ImageIn(string folder)
        {
            string ico = Path.Combine(folder, ProcessName + ".ico");
            try
            {
                using (Icon icon = File.Exists(ico) ? new Icon(ico, 32, 32) : Icon.ExtractAssociatedIcon(ExeIn(folder)))
                    return icon.ToBitmap();
            }
            catch (Exception) { return null; }
        }
    }

    /// <summary>
    /// When the scaler sits in the game folder, starts the game or the editor (asking if both are there).
    /// Elsewhere it starts nothing and the scaler waits for the game as before.
    /// </summary>
    static class Launcher
    {
        /// <summary>Picks the target; <paramref name="started"/> is the process the scaler launched itself, if any. False: user cancelled.</summary>
        public static bool Choose(Settings settings, out Target target, out Process started)
        {
            target = Target.Game;
            started = null;
            string folder = Path.GetDirectoryName(Application.ExecutablePath);
            bool hasGame = File.Exists(Target.Game.ExeIn(folder)), hasEditor = File.Exists(Target.Editor.ExeIn(folder));
            if (!hasGame && !hasEditor) return true;

            // Already running: just attach, never start a second instance.
            if (IsRunning(Target.Game)) return true;
            if (IsRunning(Target.Editor)) { target = Target.Editor; return true; }

            if (hasGame && hasEditor)
            {
                using (var dialog = new LaunchDialog(folder, settings.LastTarget == Target.Editor.ProcessName))
                {
                    if (dialog.ShowDialog() != DialogResult.OK) return false;
                    target = dialog.Result;
                }
                settings.LastTarget = target.ProcessName;
            }
            else if (hasEditor) target = Target.Editor;

            try
            {
                // Without /f: the game must run windowed. Shell execute so compatibility settings (run as admin) apply.
                started = Process.Start(new ProcessStartInfo(target.ExeIn(folder)) { WorkingDirectory = folder, UseShellExecute = true });
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) return false; // UAC prompt declined
                MessageBox.Show(target.DisplayName + " ließ sich nicht starten:\n\n" + ex.Message, "A2G-Scaler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            return true;
        }

        /// <summary>True if a live process of <paramref name="t"/> exists.</summary>
        static bool IsRunning(Target t)
        {
            return t.IsStarted();
        }
    }

    /// <summary>Asks whether to start the game or the data editor.</summary>
    class LaunchDialog : Form
    {
        /// <summary>The target the user picked; set when the dialog closes with OK.</summary>
        public Target Result { get; private set; }

        /// <summary>One button per program found in <paramref name="folder"/>; <paramref name="editorFirst"/> preselects the editor (last choice).</summary>
        public LaunchDialog(string folder, bool editorFirst)
        {
            Text = "A2G-Scaler";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = SystemFonts.MessageBoxFont;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            ClientSize = new Size(380, 150);

            var intro = new Label { Text = "Was soll gestartet werden?", AutoSize = false };
            intro.SetBounds(12, 12, 356, 20);
            Controls.Add(intro);

            Button game = TargetButton(folder, Target.Game, "Anstoss 2 Gold", 12);
            Button editor = TargetButton(folder, Target.Editor, "Editor", 196);
            var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel };
            cancel.SetBounds(278, 112, 90, 28);
            Controls.Add(cancel);
            CancelButton = cancel;
            AcceptButton = editorFirst ? editor : game;
            ActiveControl = editorFirst ? editor : game;
        }

        /// <summary>Adds a button with the program's icon that closes the dialog with <paramref name="t"/> as result.</summary>
        Button TargetButton(string folder, Target t, string text, int x)
        {
            var b = new Button { Text = text, TextImageRelation = TextImageRelation.ImageAboveText };
            b.Image = t.ImageIn(folder);
            b.SetBounds(x, 40, 172, 64);
            b.Click += delegate { Result = t; DialogResult = DialogResult.OK; };
            Controls.Add(b);
            return b;
        }
    }
}
