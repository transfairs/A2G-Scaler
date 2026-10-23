// A2G-Scaler: shows Anstoss 2 Gold (fixed 640x480) or its data editor scaled up, with mouse handling in the
// style of Magpie.
//
// Display: composites all visible top-level windows of anstoss2.exe or Editor.exe (main window + owned popups) via
// their window DCs and stretches the result into this window. Title bars of game windows (drawn by DWM,
// missing from the window DC) come from a PrintWindow cache. Popups that the game centres on the screen
// are moved over the main window.
//
// Mouse: when the cursor enters the scaled image, this window becomes click-through, the real cursor is
// moved to the matching spot of the game window (kept hidden behind this one) and clipped to it, the
// system cursor is hidden and the game's cursor is drawn into the scaled image. Pushing firmly outward at
// an edge (raw input, no button held) or the release hotkey frees the cursor again.
//
// Every system-wide change (mouse speed, game window position, hidden taskbar button, blanked system
// cursors on XP/Vista) is recorded in recovery.ini and undone on the next start if this process dies.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace A2GScaler
{
    /// <summary>The scaler window: composes the game's windows, scales them into this window and forwards the mouse.</summary>
    class ScalerForm : Form
    {
        const string AppName = "A2G-Scaler";
        static readonly string[] ModeNames = { "Einpassen (pixelgenau)", "Einpassen (weich)", "Integer (pixelgenau)", "Einpassen (scharf)" };

        readonly Settings settings;
        readonly Timer timer = new Timer();
        readonly Stopwatch fpsWatch = Stopwatch.StartNew();
        readonly string logPath;
        bool fullscreen;
        Rectangle normalBounds;        // window bounds outside fullscreen/maximised, for saving
        FormWindowState prevState;

        IntPtr canvasDC = IntPtr.Zero, canvasBmp = IntPtr.Zero, oldBmp = IntPtr.Zero;
        Size canvasSize;
        readonly SharpScaler sharp = new SharpScaler();
        int frames;
        double fps, captureMs;
        string lastWindowSet = "", status = "", shownMessage;

        // ------------------------------------------------------------ game tracking
        readonly Target target;        // game or data editor
        Process launched;              // started by the launcher; when it ends, the scaler closes too
        bool gameSeen;                 // a game process was attached at least once (a starter ending before that is no reason to close)
        readonly Stopwatch launchedGone = new Stopwatch();   // since the launched process ended without a game window seen
        Process gameProcess;
        uint gamePid;
        readonly Stopwatch processSearch = new Stopwatch();
        bool gameElevatedBlocked;
        IntPtr gameMain = IntPtr.Zero;
        bool gameMainIconic;
        Rectangle srcScreen;           // game client area, screen coordinates
        double gameScale = 1;          // screen pixels per game pixel (> 1 when Windows bitmap-stretches the DPI-unaware game)
        Rectangle destScreen;          // scaled image, screen coordinates
        Point gameOriginalPos;
        bool gameMoved;
        readonly HashSet<IntPtr> movedPopups = new HashSet<IntPtr>();
        IntPtr iconSource = IntPtr.Zero;
        ITaskbarList taskbar;
        IntPtr hiddenTab = IntPtr.Zero;
        readonly Stopwatch tabWatch = new Stopwatch();
        IntPtr minimizedGame = IntPtr.Zero;

        // ------------------------------------------------------------ mouse
        bool inside, magOk, cursorHidden, transparent, topMost;
        int savedSpeed = -1;
        Point? pendingCursor;
        readonly Stopwatch leaveWatch = new Stopwatch();
        readonly Dictionary<IntPtr, Point> hotspots = new Dictionary<IntPtr, Point>();
        // Without MagShowSystemCursor (XP, Vista) the system cursors are blanked; these copies of the
        // originals (shared system handle → copy) are what DrawGameCursor then draws into the picture.
        readonly Dictionary<IntPtr, IntPtr> cursorCopies = new Dictionary<IntPtr, IntPtr>();
        const int PushThreshold = 100;   // raw mouse counts (roughly 3 mm with an 800 dpi mouse)
        int pushLeft, pushRight, pushUp, pushDown;
        readonly byte[] rawBuffer = new byte[64];

        // ------------------------------------------------------------ toolbar
        readonly ToolStrip toolbar = new FirstClickToolStrip();
        bool toolbarHidden; // deliberately not persisted: every start shows the toolbar, so nobody is stuck without it
        readonly ToolStripButton hideButton = new ToolStripButton("✕");
        readonly ToolStripDropDownButton modeButton = new ToolStripDropDownButton();
        readonly ToolStripButton fullscreenButton = new ToolStripButton();
        readonly ToolStripButton statsButton = new ToolStripButton("Statistik");
        readonly ToolStripButton popupButton = new ToolStripButton("Popups zentrieren");
        readonly ToolStripButton speedButton = new ToolStripButton("Tempo-Ausgleich");
        readonly ToolStripButton hotkeyButton = new ToolStripButton("Tastenkürzel …");
        readonly ToolStripButton aboutButton = new ToolStripButton("Über");
        readonly ToolStripLabel statsLabel = new ToolStripLabel();

        /// <summary>A ToolStrip in an inactive window swallows the first click; this one does not.</summary>
        class FirstClickToolStrip : ToolStrip
        {
            /// <summary>Turns "activate and swallow the click" into plain "activate".</summary>
            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                const int WM_MOUSEACTIVATE = 0x21, MA_ACTIVATE = 1, MA_ACTIVATEANDEAT = 2;
                if (m.Msg == WM_MOUSEACTIVATE && m.Result == (IntPtr)MA_ACTIVATEANDEAT) m.Result = (IntPtr)MA_ACTIVATE;
            }
        }

        /// <summary>PrintWindow copy of a game window, for its title bar and frame (DWM draws them, so they are missing from the window DC).</summary>
        class FrameCache { public IntPtr DC, Bmp, Old; public Size Size; public string Title; public bool Active; public Stopwatch Age; }
        readonly Dictionary<IntPtr, FrameCache> frameCaches = new Dictionary<IntPtr, FrameCache>();

        /// <summary>Creates the window for <paramref name="target"/>; <paramref name="launched"/> is the process the launcher started (the scaler closes with it), <paramref name="repaired"/> the recovery report for the log.</summary>
        public ScalerForm(Settings settings, string repaired, Target target, Process launched)
        {
            this.settings = settings;
            this.target = target;
            this.launched = launched;
            Text = AppName;
            BackColor = Color.Black;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            KeyPreview = true;
            logPath = Path.Combine(KeyValueFile.Folder, "scaler.log");
            File.WriteAllText(logPath, "");
            if (repaired != null) Log("Reste eines abgestürzten Laufs repariert: " + repaired);
            Log("Ziel: " + target.DisplayName + (launched != null ? ", gestartet als PID " + launched.Id : ""));

            if (settings.WindowBounds.IsEmpty)
            {
                ClientSize = new Size(1272, 914 + 25);
                StartPosition = FormStartPosition.CenterScreen;
            }
            else
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = settings.WindowBounds;
            }
            if (settings.Maximized) WindowState = FormWindowState.Maximized;
            normalBounds = Bounds;

            BuildToolbar();
            timer.Interval = 10;
            timer.Tick += delegate { Frame(); };
            DoubleClick += delegate { ToggleFullscreen(); };
            Resize += delegate { RememberBounds(); SyncMinimize(); ClearBackground(); };
            Move += delegate { RememberBounds(); };
            Shown += delegate { if (settings.Fullscreen) ToggleFullscreen(); };
        }

        /// <summary>Appends a timestamped line to scaler.log (emptied at every start).</summary>
        void Log(string line)
        {
            try { File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss.fff ") + line + Environment.NewLine); } catch (IOException) { }
        }

        // ================================================================ toolbar and settings

        /// <summary>Creates the toolbar items and wires them to the settings.</summary>
        void BuildToolbar()
        {
            toolbar.GripStyle = ToolStripGripStyle.Hidden;
            toolbar.Dock = DockStyle.Top;
            for (int i = 0; i < ModeNames.Length; i++)
            {
                int index = i;
                modeButton.DropDownItems.Add(ModeNames[i], null, delegate { SetMode((ScaleMode)index); });
            }
            hideButton.Click += delegate { ToggleToolbar(); };
            fullscreenButton.Click += delegate { ToggleFullscreen(); };
            statsButton.CheckOnClick = true;
            statsButton.CheckedChanged += delegate { settings.ShowInfo = statsButton.Checked; statsLabel.Visible = settings.ShowInfo; };
            popupButton.CheckOnClick = true;
            popupButton.CheckedChanged += delegate { settings.MovePopups = popupButton.Checked; movedPopups.Clear(); };
            speedButton.CheckOnClick = true;
            speedButton.CheckedChanged += delegate { settings.SpeedCompensation = speedButton.Checked; };
            hotkeyButton.Click += delegate { EditHotkeys(); };
            aboutButton.Click += delegate { ShowAbout(); };
            statsLabel.Alignment = ToolStripItemAlignment.Right;
            toolbar.Items.AddRange(new ToolStripItem[] { hideButton, new ToolStripSeparator(), modeButton, new ToolStripSeparator(), fullscreenButton, new ToolStripSeparator(),
                statsButton, popupButton, speedButton, new ToolStripSeparator(), hotkeyButton, aboutButton, statsLabel });
            toolbar.SizeChanged += delegate { ClearBackground(); };
            toolbar.VisibleChanged += delegate { shownMessage = null; ClearBackground(); };
            toolbar.Visible = !toolbarHidden;
            Controls.Add(toolbar);
            SyncToolbar();
        }

        /// <summary>Updates toolbar texts, check marks and tooltips from the settings and current hotkeys.</summary>
        void SyncToolbar()
        {
            modeButton.Text = "Modus: " + ModeNames[(int)settings.Mode];
            modeButton.ToolTipText = "Nächster Modus: " + HotkeyText.Format(settings.Hotkeys[HotkeyAction.Mode]);
            for (int i = 0; i < modeButton.DropDownItems.Count; i++)
                ((ToolStripMenuItem)modeButton.DropDownItems[i]).Checked = i == (int)settings.Mode;
            fullscreenButton.Text = (fullscreen ? "Fenster" : "Vollbild") + " (" + HotkeyText.Format(settings.Hotkeys[HotkeyAction.Fullscreen]) + ")";
            statsButton.Checked = settings.ShowInfo;
            popupButton.Checked = settings.MovePopups;
            popupButton.ToolTipText = "Popups, die das Spiel in der Bildschirmmitte öffnet, über das Spiel verschieben";
            speedButton.Checked = settings.SpeedCompensation;
            speedButton.ToolTipText = "Mausgeschwindigkeit im Spiel an die Vergrößerung anpassen";
            hotkeyButton.ToolTipText = "Werkzeugleiste ein/aus: " + HotkeyText.Format(settings.Hotkeys[HotkeyAction.Toolbar]);
            hideButton.ToolTipText = "Werkzeugleiste ausblenden (wieder einblenden mit " + HotkeyText.Format(settings.Hotkeys[HotkeyAction.Toolbar]) + ")";
            statsLabel.Visible = settings.ShowInfo;
        }

        /// <summary>Switches the scaling mode.</summary>
        void SetMode(ScaleMode m) { settings.Mode = m; SyncToolbar(); ClearBackground(); }

        /// <summary>Version as stamped into the assembly (by the release pipeline, from the git tag).</summary>
        public static string Version
        {
            get
            {
                var attrs = typeof(ScalerForm).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
                return attrs.Length > 0 ? ((System.Reflection.AssemblyInformationalVersionAttribute)attrs[0]).InformationalVersion : "?";
            }
        }

        /// <summary>Frees the mouse and shows the about dialog.</summary>
        void ShowAbout()
        {
            if (inside) LeaveGame(null, "Über-Dialog");
            using (var dlg = new AboutDialog(AppName, Version))
                dlg.ShowDialog(this);
        }

        /// <summary>Shows or hides the toolbar for this session (not persisted).</summary>
        void ToggleToolbar()
        {
            toolbarHidden = !toolbarHidden;
            toolbar.Visible = !toolbarHidden && !fullscreen;
        }

        /// <summary>Remembers the normal window bounds while neither fullscreen nor maximised.</summary>
        void RememberBounds()
        {
            if (!fullscreen && WindowState == FormWindowState.Normal) normalBounds = Bounds;
        }

        /// <summary>Stores window state and bounds in the settings and saves them.</summary>
        void SaveSettings()
        {
            settings.Fullscreen = fullscreen;
            settings.Maximized = fullscreen ? prevState == FormWindowState.Maximized : WindowState == FormWindowState.Maximized;
            settings.WindowBounds = normalBounds;
            settings.Save();
        }

        // ================================================================ hotkeys

        /// <summary>Registers all hotkeys; returns the combinations another program has already taken.</summary>
        List<string> RegisterHotkeys()
        {
            var failed = new List<string>();
            foreach (var kv in settings.Hotkeys)
                if (!HotkeyText.Register(Handle, (int)kv.Key + 1, kv.Value)) failed.Add(HotkeyText.Format(kv.Value));
            Log("Tastenkürzel registriert" + (failed.Count > 0 ? ", belegt: " + string.Join(", ", failed.ToArray()) : ""));
            if (failed.Count > 0)
                statsLabel.Text = "Tastenkürzel belegt: " + string.Join(", ", failed.ToArray());
            return failed;
        }

        /// <summary>Releases all global hotkeys of this window.</summary>
        void UnregisterHotkeys()
        {
            foreach (HotkeyAction a in Enum.GetValues(typeof(HotkeyAction))) Native.UnregisterHotKey(Handle, (int)a + 1);
        }

        /// <summary>Shows the hotkey dialog (with the hotkeys released meanwhile), saves the result and re-registers.</summary>
        void EditHotkeys()
        {
            if (inside) LeaveGame(null, "Tastenkürzel-Dialog");
            UnregisterHotkeys(); // so pressing the current combinations in the dialog does not trigger them
            using (var dlg = new HotkeyDialog(settings.Hotkeys))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    foreach (var kv in dlg.Result) settings.Hotkeys[kv.Key] = kv.Value;
                    settings.Save();
                }
            }
            List<string> failed = RegisterHotkeys();
            if (failed.Count > 0)
                MessageBox.Show(this, "Diese Tastenkürzel sind bereits von einem anderen Programm belegt und funktionieren nicht:\n\n"
                    + string.Join("\n", failed.ToArray()), AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            SyncToolbar();
        }

        // ================================================================ window plumbing

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_LAYERED; return cp; }
        }

        /// <summary>Makes the window opaque-layered, registers hotkeys and raw mouse input, sets up cursor hiding and starts the frame timer.</summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetLayeredWindowAttributes(Handle, 0, 255, Native.LWA_ALPHA);
            RegisterHotkeys();
            magOk = Native.TryMagInitialize();
            var rid = new Native.RAWINPUTDEVICE { usUsagePage = 1, usUsage = 2, dwFlags = Native.RIDEV_INPUTSINK, hwndTarget = Handle };
            bool rawOk = Native.RegisterRawInputDevices(new[] { rid }, 1, (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICE)));
            Log("MagInitialize=" + magOk + ", RawInput=" + rawOk + ", Scaler als Admin=" + Native.IsCurrentProcessElevated());
            timer.Start();
        }

        /// <summary>Dispatches global hotkeys and raw mouse input.</summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                switch ((HotkeyAction)(m.WParam.ToInt32() - 1))
                {
                    case HotkeyAction.Release: if (inside) { LeaveGame(null, "Tastenkürzel"); Activate(); } break;
                    case HotkeyAction.Fullscreen: ToggleFullscreen(); break;
                    case HotkeyAction.Mode: SetMode((ScaleMode)(((int)settings.Mode + 1) % ModeNames.Length)); break;
                    case HotkeyAction.Toolbar: ToggleToolbar(); break;
                }
            }
            else if (m.Msg == Native.WM_INPUT) ReadRawMouse(m.LParam);
            base.WndProc(ref m);
        }

        /// <summary>Esc leaves fullscreen or closes, F11 toggles fullscreen.</summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { if (fullscreen) ToggleFullscreen(); else Close(); }
            else if (e.KeyCode == Keys.F11) ToggleFullscreen();
        }

        /// <summary>Repaints the background; the image follows with the next frame.</summary>
        protected override void OnPaint(PaintEventArgs e) { shownMessage = null; ClearBackground(); }

        /// <summary>Switches between a borderless window covering the current screen and the previous window state.</summary>
        void ToggleFullscreen()
        {
            if (inside) LeaveGame(null, "Vollbildwechsel");
            if (!fullscreen)
            {
                prevState = WindowState;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                Bounds = Screen.FromControl(this).Bounds;
                toolbar.Visible = false; // auto-shows when the free cursor touches the top screen edge
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                Bounds = normalBounds;
                WindowState = prevState;
                toolbar.Visible = !toolbarHidden;
            }
            fullscreen = !fullscreen;
            SyncToolbar();
            ClearBackground();
        }

        /// <summary>In fullscreen: shows the toolbar when the free cursor touches the top screen edge, hides it again once the cursor moves away or enters the game.</summary>
        void UpdateToolbarAutoHide(Native.POINT p)
        {
            if (!fullscreen) return;
            Rectangle screen = Screen.FromControl(this).Bounds;
            if (!toolbar.Visible && !inside && !toolbarHidden && p.Y <= screen.Top + 1) toolbar.Visible = true;
            else if (toolbar.Visible && (inside || p.Y > screen.Top + toolbar.Height + 30) && !AnyDropDownOpen())
                toolbar.Visible = false;
        }

        /// <summary>True while a toolbar drop-down (the mode menu) is open.</summary>
        bool AnyDropDownOpen()
        {
            foreach (ToolStripItem item in toolbar.Items)
            {
                var dd = item as ToolStripDropDownItem;
                if (dd != null && dd.DropDown.Visible) return true;
            }
            return false;
        }

        Rectangle ViewArea
        {
            get
            {
                int top = toolbar.Visible ? toolbar.Height : 0;
                return new Rectangle(0, top, ClientSize.Width, Math.Max(1, ClientSize.Height - top));
            }
        }

        Rectangle lastDest;   // client coordinates of the last scaled image

        /// <summary>Paints the bars around the scaled image black; the image is repainted by the next frame.</summary>
        void ClearBackground()
        {
            Rectangle va = ViewArea;
            Rectangle d = Rectangle.Intersect(lastDest, va);
            IntPtr dc = Native.GetDC(Handle);
            if (d.IsEmpty) Native.PatBlt(dc, va.X, va.Y, va.Width, va.Height, Native.BLACKNESS);
            else
            {
                Native.PatBlt(dc, va.X, va.Y, va.Width, d.Top - va.Y, Native.BLACKNESS);
                Native.PatBlt(dc, va.X, d.Bottom, va.Width, va.Bottom - d.Bottom, Native.BLACKNESS);
                Native.PatBlt(dc, va.X, d.Top, d.Left - va.X, d.Height, Native.BLACKNESS);
                Native.PatBlt(dc, d.Right, d.Top, va.Right - d.Right, d.Height, Native.BLACKNESS);
            }
            Native.ReleaseDC(Handle, dc);
        }

        /// <summary>Shows a centred message instead of the game image (no game, minimised, elevated).</summary>
        void ShowMessage(string message)
        {
            if (message == shownMessage) return;
            shownMessage = message;
            lastDest = Rectangle.Empty;
            ClearBackground();
            using (var g = CreateGraphics())
            using (var font = new Font("Segoe UI", 14f))
                TextRenderer.DrawText(g, message, font, ViewArea, Color.Gainsboro,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }

        // ================================================================ game tracking

        /// <summary>Finds the target process; the process list is only searched once per second while none runs.</summary>
        bool TrackGameProcess()
        {
            if (gameProcess != null)
            {
                if (!HasExited(gameProcess)) return true;
                Log(target.DisplayName + " beendet");
                bool wasLaunched = gameProcess == launched;
                int exitedPid = (int)gamePid;
                OnGameLost();
                if (wasLaunched)
                {
                    launched = null;
                    using (Process other = target.FindRunning(exitedPid))
                        if (other == null) { BeginInvoke(new MethodInvoker(Close)); return false; }
                }
            }
            // The 2007 release's anstoss2.exe is only a windowless starter for run.exe: if what we launched has no
            // game window, look for the process that has. Once the starter is gone too, close as with the game.
            Process found = null;
            if (launched != null && target.HasGameWindow(launched)) found = launched;
            else
            {
                if (launched != null && HasExited(launched))
                {
                    // Before any game window was seen, a starter may have just handed over: give the game 30 s.
                    if (!launchedGone.IsRunning) launchedGone.Start();
                    if (gameSeen || launchedGone.ElapsedMilliseconds > 30000)
                    {
                        Log("Gestarteter Prozess beendet");
                        launched = null;
                        BeginInvoke(new MethodInvoker(Close));
                        return false;
                    }
                }
                if (processSearch.IsRunning && processSearch.ElapsedMilliseconds < 1000) return false;
                processSearch.Restart();
                found = target.FindRunning();
            }
            if (found == null) return false;
            gameSeen = true;
            gameProcess = found;
            gamePid = (uint)gameProcess.Id;
            gameElevatedBlocked = Native.IsElevated(gamePid) && !Native.IsCurrentProcessElevated();
            Log("Spiel gefunden, PID " + gamePid + (gameElevatedBlocked ? ", läuft als Administrator" : ""));
            return true;
        }

        /// <summary>True if <paramref name="p"/> has ended (or can no longer be queried).</summary>
        static bool HasExited(Process p)
        {
            try { return p.HasExited; } catch (Exception) { return true; }
        }

        /// <summary>Resets all per-game state when the game exits; we wait for the next start.</summary>
        void OnGameLost()
        {
            LeaveGame(null, "Spiel beendet");
            if (gameProcess != null) gameProcess.Dispose();
            gameProcess = null;
            gamePid = 0;
            gameMain = IntPtr.Zero;
            gameMoved = false;
            Recovery.GameRestored();
            movedPopups.Clear();
            ReleaseFrameCaches();
            iconSource = IntPtr.Zero;
            if (hiddenTab != IntPtr.Zero) { hiddenTab = IntPtr.Zero; Recovery.TabRestored(); }
            minimizedGame = IntPtr.Zero;
            hotspots.Clear();
            Text = AppName;
            processSearch.Reset();
        }

        /// <summary>Collects the visible, non-minimised windows of the game process in z-order and picks the main window (largest unowned window of the target's main class).</summary>
        void FindGameWindows(out List<IntPtr> bottomToTop)
        {
            gameMain = IntPtr.Zero;
            gameMainIconic = false;
            var topToBottom = new List<IntPtr>();
            bottomToTop = topToBottom;
            uint pid = gamePid;
            // Main window = largest unowned window of the target's main class (the game: a dialog). Context
            // menus (#32768) and tooltips are unowned too, but must only be composited on top, never become the canvas.
            IntPtr found = IntPtr.Zero;
            long foundArea = -1;
            bool foundIconic = false;
            Native.EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                uint p; Native.GetWindowThreadProcessId(h, out p);
                if (p != pid || !Native.IsWindowVisible(h)) return true;
                bool iconic = Native.IsIconic(h);
                if (!iconic) topToBottom.Add(h);
                if (Native.GetWindow(h, Native.GW_OWNER) == IntPtr.Zero && Native.ClassOf(h) == target.MainWindowClass)
                {
                    Native.RECT r; Native.GetWindowRect(h, out r);
                    long area = iconic ? 0 : (long)(r.R - r.L) * (r.B - r.T);
                    if (area > foundArea) { found = h; foundArea = area; foundIconic = iconic; }
                }
                return true;
            }, IntPtr.Zero);
            gameMain = foundIconic ? IntPtr.Zero : found;
            gameMainIconic = foundIconic;
            topToBottom.Reverse();
        }

        /// <summary>True if <paramref name="h"/> belongs to the game process.</summary>
        bool IsGameWindow(IntPtr h)
        {
            if (h == IntPtr.Zero || gamePid == 0) return false;
            uint p; Native.GetWindowThreadProcessId(h, out p);
            return p == gamePid;
        }

        /// <summary>True if a visible game window lies above this one in z-order (e.g. a context menu).</summary>
        bool GameWindowAboveUs()
        {
            for (IntPtr h = Native.GetWindow(Handle, Native.GW_HWNDPREV); h != IntPtr.Zero; h = Native.GetWindow(h, Native.GW_HWNDPREV))
                if (Native.IsWindowVisible(h) && IsGameWindow(h)) return true;
            return false;
        }

        /// <summary>Copies the game's title and icon to this window, so its taskbar button looks like the game's.</summary>
        void MirrorGameIdentity()
        {
            string title = Native.TextOf(gameMain);
            if (title.Length > 0 && Text != title) Text = title;
            if (iconSource == gameMain) return;
            iconSource = gameMain;
            Icon icon = null;
            IntPtr result;
            foreach (int kind in new[] { Native.ICON_BIG, Native.ICON_SMALL2, Native.ICON_SMALL })
            {
                if (Native.SendMessageTimeout(gameMain, Native.WM_GETICON, (IntPtr)kind, IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 200, out result) != IntPtr.Zero
                    && result != IntPtr.Zero)
                {
                    icon = (Icon)Icon.FromHandle(result).Clone();
                    break;
                }
            }
            if (icon == null)
            {
                try { icon = Icon.ExtractAssociatedIcon(gameProcess.MainModule.FileName); }
                catch (Exception ex) { Log("Icon nicht lesbar: " + ex.Message); }
            }
            if (icon != null) Icon = icon;
        }

        /// <summary>Hides the game's taskbar button (repeated every 3 s, Windows may bring it back) and records it in Recovery.</summary>
        void HideGameTaskbarTab()
        {
            if (hiddenTab == gameMain && tabWatch.ElapsedMilliseconds < 3000) return;
            try
            {
                if (taskbar == null) { taskbar = (ITaskbarList)new TaskbarList(); taskbar.HrInit(); }
                if (hiddenTab != IntPtr.Zero && hiddenTab != gameMain && Native.IsWindow(hiddenTab)) taskbar.AddTab(hiddenTab);
                taskbar.DeleteTab(gameMain);
                if (hiddenTab != gameMain) { Recovery.TabHidden(gameMain); Log("Taskleisten-Eintrag des Spiels ausgeblendet"); }
            }
            catch (Exception ex) { Log("Taskleiste: " + ex.Message); }
            hiddenTab = gameMain;
            tabWatch.Restart();
        }

        /// <summary>Brings the game's taskbar button back.</summary>
        void RestoreGameTaskbarTab()
        {
            if (hiddenTab == IntPtr.Zero) return;
            try { if (taskbar != null && Native.IsWindow(hiddenTab)) taskbar.AddTab(hiddenTab); }
            catch (Exception ex) { Log("Taskleiste: " + ex.Message); }
            hiddenTab = IntPtr.Zero;
            Recovery.TabRestored();
        }

        /// <summary>Minimises the game with this window and restores it again.</summary>
        void SyncMinimize()
        {
            if (WindowState == FormWindowState.Minimized)
            {
                if (minimizedGame == IntPtr.Zero && gameMain != IntPtr.Zero)
                {
                    if (inside) LeaveGame(null, "minimiert");
                    minimizedGame = gameMain;
                    Native.ShowWindow(minimizedGame, Native.SW_SHOWMINNOACTIVE);
                }
            }
            else if (minimizedGame != IntPtr.Zero)
            {
                if (Native.IsWindow(minimizedGame)) Native.ShowWindow(minimizedGame, Native.SW_SHOWNOACTIVATE);
                minimizedGame = IntPtr.Zero;
                tabWatch.Reset(); // restoring may bring the taskbar button back
                hiddenTab = IntPtr.Zero;
            }
        }

        /// <summary>Keeps the game's client area centred under the scaled image, hidden behind this window.</summary>
        bool KeepGameBehind(Rectangle mainWindow, Native.POINT clientOrigin, Size clientSize)
        {
            if (WindowState == FormWindowState.Minimized || destScreen.Width < clientSize.Width) return false;
            int wantX = destScreen.X + (destScreen.Width - clientSize.Width) / 2;
            int wantY = destScreen.Y + (destScreen.Height - clientSize.Height) / 2;
            if (Math.Abs(wantX - clientOrigin.X) <= 1 && Math.Abs(wantY - clientOrigin.Y) <= 1) return false;
            if (!gameMoved)
            {
                gameOriginalPos = mainWindow.Location;
                gameMoved = true;
                Recovery.GameMoved(gameMain, gameOriginalPos);
            }
            int nx = mainWindow.X + (wantX - clientOrigin.X), ny = mainWindow.Y + (wantY - clientOrigin.Y);
            Native.SetWindowPos(gameMain, IntPtr.Zero, nx, ny, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            movedPopups.Clear();
            return true;
        }

        /// <summary>Moves each new dialog popup that lies outside the main window to its centre, once.</summary>
        void CentrePopups(List<IntPtr> windows, Rectangle mainRect)
        {
            foreach (IntPtr h in windows)
            {
                if (!settings.MovePopups || h == gameMain || movedPopups.Contains(h)) continue;
                if (Native.ClassOf(h) != "#32770") continue; // menus and tooltips already sit at the cursor
                movedPopups.Add(h);
                Native.RECT pr; Native.GetWindowRect(h, out pr);
                var popup = Rectangle.FromLTRB(pr.L, pr.T, pr.R, pr.B);
                if (mainRect.Contains(popup)) continue;
                int nx = mainRect.X + (mainRect.Width - popup.Width) / 2;
                int ny = mainRect.Y + (mainRect.Height - popup.Height) / 2;
                Native.SetWindowPos(h, IntPtr.Zero, nx, ny, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            }
            movedPopups.IntersectWith(windows);
        }

        // ================================================================ canvas

        /// <summary>Creates the off-screen canvas in game pixels, when the size changes.</summary>
        void EnsureCanvas(Size size)
        {
            if (canvasDC != IntPtr.Zero && size == canvasSize) return;
            ReleaseCanvas();
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            canvasDC = Native.CreateCompatibleDC(screen);
            canvasBmp = Native.CreateCompatibleBitmap(screen, size.Width, size.Height);
            oldBmp = Native.SelectObject(canvasDC, canvasBmp);
            Native.ReleaseDC(IntPtr.Zero, screen);
            canvasSize = size;
            ClearBackground();
        }

        /// <summary>Frees the canvas and the sharp scaler's buffers.</summary>
        void ReleaseCanvas()
        {
            sharp.Dispose();
            if (canvasDC == IntPtr.Zero) return;
            Native.SelectObject(canvasDC, oldBmp);
            Native.DeleteObject(canvasBmp);
            Native.DeleteDC(canvasDC);
            canvasDC = IntPtr.Zero;
        }

        // ================================================================ frame

        /// <summary>One timer tick: tracks the game, positions it behind the scaler, composes its windows into the canvas, handles the mouse and draws the scaled image.</summary>
        void Frame()
        {
            Native.POINT cursor; Native.GetCursorPos(out cursor);
            UpdateToolbarAutoHide(cursor);

            if (!TrackGameProcess())
            {
                SetTopMost(false);
                ShowMessage("Warte auf " + target.DisplayName + " …"
                    + (target == Target.Game ? "\n\nBitte das Spiel im Fenstermodus starten (ohne /f)." : ""));
                return;
            }
            if (gameElevatedBlocked)
            {
                SetTopMost(false);
                ShowMessage(target.DisplayName + " läuft als Administrator.\n\nWindows lässt dann keine Steuerung durch ein normales Programm zu. "
                    + "Bitte A2G-Scaler ebenfalls als Administrator starten (Rechtsklick → Als Administrator ausführen) "
                    + "oder das Spiel ohne Administratorrechte starten.");
                return;
            }

            List<IntPtr> windows;
            FindGameWindows(out windows);
            if (gameMain == IntPtr.Zero)
            {
                if (inside) LeaveGame(null, "Spielfenster weg");
                SetTopMost(false);
                if (WindowState != FormWindowState.Minimized)
                    ShowMessage(gameMainIconic ? target.DisplayName + " ist minimiert." : "Warte auf das Fenster von " + target.DisplayName + " …");
                return;
            }
            shownMessage = null;

            MirrorGameIdentity();
            HideGameTaskbarTab();

            // At Windows scaling above 100 % the DPI-unaware game is bitmap-stretched by DWM: we (DPI-aware) see
            // its rectangles in screen pixels, but its window DCs only hold the unstretched game pixels. The
            // canvas therefore uses game pixels, all screen offsets are divided by gameScale.
            UpdateGameScale();
            Native.POINT origin; Size screenSize;
            GetSourceArea(out origin, out screenSize);
            var size = new Size(G(screenSize.Width), G(screenSize.Height));
            if (size.Width <= 0 || size.Height <= 0) return;
            EnsureCanvas(size);

            // Target rectangle (client coordinates of this form) with preserved aspect ratio.
            var dest = FitRect(ViewArea, size, settings.Mode == ScaleMode.IntegerNearest);
            int dx = dest.X, dy = dest.Y, dw = dest.Width, dh = dest.Height;
            if (dest != lastDest) { lastDest = dest; ClearBackground(); }
            destScreen = new Rectangle(PointToScreen(new Point(dx, dy)), new Size(dw, dh));

            Native.RECT mr; Native.GetWindowRect(gameMain, out mr);
            var mainRect = Rectangle.FromLTRB(mr.L, mr.T, mr.R, mr.B);
            if (!inside && KeepGameBehind(mainRect, origin, screenSize))
            {
                Native.GetWindowRect(gameMain, out mr);
                mainRect = Rectangle.FromLTRB(mr.L, mr.T, mr.R, mr.B);
                GetSourceArea(out origin, out screenSize);
            }
            srcScreen = new Rectangle(origin.X, origin.Y, screenSize.Width, screenSize.Height);
            CentrePopups(windows, mainRect);

            // Composite all game windows, bottom to top.
            var sw = Stopwatch.StartNew();
            var set = new StringBuilder();
            foreach (IntPtr h in windows)
            {
                Native.RECT wr; Native.GetWindowRect(h, out wr);
                IntPtr src = Native.GetDCEx(h, IntPtr.Zero, Native.DCX_WINDOW | Native.DCX_CACHE);
                Native.BitBlt(canvasDC, G(wr.L - origin.X), G(wr.T - origin.Y), G(wr.R - wr.L), G(wr.B - wr.T), src, 0, 0, Native.SRCCOPY);
                Native.ReleaseDC(h, src);
                if (h != gameMain) DrawNonClient(h, wr, origin);
                set.Append(h.ToInt64()).Append(' ');
            }
            DropStaleFrameCaches(windows);
            captureMs = captureMs * 0.9 + sw.Elapsed.TotalMilliseconds * 0.1;
            string s = set.ToString();
            if (s != lastWindowSet) { Log(windows.Count + " Spielfenster sichtbar"); lastWindowSet = s; }

            UpdateMouse(cursor);
            if (inside)
            {
                // Context menus and tooltips are topmost and would show unscaled above us. Stay on top;
                // being click-through, the clicks still reach the real menu below.
                if (GameWindowAboveUs())
                    Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOMOVE | Native.SWP_NOACTIVATE);
                DrawGameCursor();
            }

            IntPtr dc = Native.GetDC(Handle);
            Native.SetBrushOrgEx(dc, 0, 0, IntPtr.Zero);
            if (settings.Mode == ScaleMode.FitSharp)
                sharp.Draw(dc, dx, dy, dw, dh, canvasDC, size);
            else
            {
                Native.SetStretchBltMode(dc, settings.Mode == ScaleMode.FitSmooth ? Native.HALFTONE : Native.COLORONCOLOR);
                Native.StretchBlt(dc, dx, dy, dw, dh, canvasDC, 0, 0, size.Width, size.Height, Native.SRCCOPY);
            }
            Native.ReleaseDC(Handle, dc);

            frames++;
            if (fpsWatch.ElapsedMilliseconds >= 1000)
            {
                fps = frames * 1000.0 / fpsWatch.ElapsedMilliseconds; frames = 0; fpsWatch.Restart();
                status = string.Format("{0}x{1} → {2}x{3} | {4:N0} fps | {5:N1} ms | Maus: {6}",
                    size.Width, size.Height, dw, dh, fps, captureMs,
                    inside ? "im Spiel (" + HotkeyText.Format(settings.Hotkeys[HotkeyAction.Release]) + " löst)" : "frei");
                if (settings.ShowInfo && statsLabel.Text != status) statsLabel.Text = status;
            }
        }

        /// <summary>
        /// Part of the main window that is shown scaled, in screen coordinates: the client area plus the menu bar
        /// if there is one (the editor has one, the game does not). USER draws the menu bar, so it is in the window DC.
        /// </summary>
        void GetSourceArea(out Native.POINT origin, out Size screenSize)
        {
            Native.RECT cr; Native.GetClientRect(gameMain, out cr);
            origin = new Native.POINT(); Native.ClientToScreen(gameMain, ref origin);
            screenSize = new Size(cr.R - cr.L, cr.B - cr.T);
            Native.RECT bar;
            if (Native.MenuBarOf(gameMain, out bar) && bar.T < origin.Y && bar.B <= origin.Y + 1)
            {
                screenSize.Height += origin.Y - bar.T;
                origin.Y = bar.T;
            }
        }

        /// <summary>Converts screen pixels (as this DPI-aware process sees the game) into game pixels.</summary>
        int G(int screenPixels) { return (int)Math.Round(screenPixels / gameScale); }

        /// <summary>Updates the screen-to-game pixel factor from both windows' DPI (1 where the API is missing).</summary>
        void UpdateGameScale()
        {
            uint ours = Native.DpiOf(Handle), game = Native.DpiOf(gameMain);
            double s = ours > 0 && game > 0 ? (double)ours / game : 1;
            if (s == gameScale) return;
            gameScale = s;
            Log("DPI Scaler " + ours + ", Spiel " + game + " → Spielpixel = " + s.ToString("0.##") + " Bildschirmpixel");
            ReleaseFrameCaches();
        }

        // ---------------------------------------------------------------- title bars of game windows

        /// <summary>Draws frame and title bar of popup <paramref name="h"/> from its PrintWindow cache (refreshed on change or after 2 s).</summary>
        void DrawNonClient(IntPtr h, Native.RECT wr, Native.POINT canvasOrigin)
        {
            if ((Native.GetWindowLong(h, Native.GWL_STYLE) & Native.WS_CAPTION) != Native.WS_CAPTION) return;
            var size = new Size(G(wr.R - wr.L), G(wr.B - wr.T));
            if (size.Width <= 0 || size.Height <= 0) return;

            string title = Native.TextOf(h);
            bool active = Native.GetForegroundWindow() == h;
            FrameCache c;
            if (!frameCaches.TryGetValue(h, out c) || c.Size != size || c.Title != title || c.Active != active || c.Age.ElapsedMilliseconds > 2000)
            {
                if (c == null || c.Size != size)
                {
                    if (c != null) ReleaseFrameCache(c);
                    c = new FrameCache { Size = size, Age = new Stopwatch() };
                    IntPtr screen = Native.GetDC(IntPtr.Zero);
                    c.DC = Native.CreateCompatibleDC(screen);
                    c.Bmp = Native.CreateCompatibleBitmap(screen, size.Width, size.Height);
                    c.Old = Native.SelectObject(c.DC, c.Bmp);
                    Native.ReleaseDC(IntPtr.Zero, screen);
                    frameCaches[h] = c;
                }
                Native.PrintWindow(h, c.DC, 0);
                c.Title = title; c.Active = active; c.Age.Restart();
            }

            Native.RECT cr; Native.GetClientRect(h, out cr);
            var co = new Native.POINT(); Native.ClientToScreen(h, ref co);
            int cl = G(co.X - wr.L), ct = G(co.Y - wr.T), cw = G(cr.R), ch = G(cr.B);
            int x = G(wr.L - canvasOrigin.X), y = G(wr.T - canvasOrigin.Y);
            Native.BitBlt(canvasDC, x, y, size.Width, ct, c.DC, 0, 0, Native.SRCCOPY);                                     // title bar + top
            Native.BitBlt(canvasDC, x, y + ct, cl, ch, c.DC, 0, ct, Native.SRCCOPY);                                        // left
            Native.BitBlt(canvasDC, x + cl + cw, y + ct, size.Width - cl - cw, ch, c.DC, cl + cw, ct, Native.SRCCOPY);      // right
            Native.BitBlt(canvasDC, x, y + ct + ch, size.Width, size.Height - ct - ch, c.DC, 0, ct + ch, Native.SRCCOPY);   // bottom
        }

        /// <summary>Frees the title-bar caches of windows that are gone.</summary>
        void DropStaleFrameCaches(List<IntPtr> windows)
        {
            if (frameCaches.Count == 0) return;
            var gone = new List<IntPtr>();
            foreach (IntPtr h in frameCaches.Keys) if (!windows.Contains(h)) gone.Add(h);
            foreach (IntPtr h in gone) { ReleaseFrameCache(frameCaches[h]); frameCaches.Remove(h); }
        }

        /// <summary>Frees all title-bar caches.</summary>
        void ReleaseFrameCaches()
        {
            foreach (FrameCache c in frameCaches.Values) ReleaseFrameCache(c);
            frameCaches.Clear();
        }

        /// <summary>Frees one title-bar cache's bitmap and DC.</summary>
        static void ReleaseFrameCache(FrameCache c)
        {
            Native.SelectObject(c.DC, c.Old);
            Native.DeleteObject(c.Bmp);
            Native.DeleteDC(c.DC);
        }

        // ================================================================ mouse

        /// <summary>Scaled image pixels per game client pixel.</summary>
        double ScaleFactor { get { return (double)destScreen.Width / srcScreen.Width; } }

        /// <summary>Keeps the topmost state in line with the focus, lets the cursor into the game when it enters the image and frees it on focus loss.</summary>
        void UpdateMouse(Native.POINT p)
        {
            IntPtr fg = Native.GetForegroundWindow();
            bool fgOk = fg == Handle || IsGameWindow(fg);
            SetTopMost(fgOk);

            if (!inside)
            {
                if (leaveWatch.IsRunning && leaveWatch.ElapsedMilliseconds < 300) return;
                if (!fgOk || !destScreen.Contains(p.X, p.Y)) return;
                if (Native.GetAncestor(Native.WindowFromPoint(p), Native.GA_ROOT) != Handle) return;
                if ((Native.GetAsyncKeyState(0x01) & 0x8000) != 0) return; // not while a button is held
                EnterGame(p);
                return;
            }

            if (!fgOk) { LeaveGame(null, "Fokus verloren"); return; }

            // Retry the jump into the game area once if it did not land (the user may already be moving).
            if (pendingCursor.HasValue)
            {
                Point t = pendingCursor.Value;
                if (Math.Abs(p.X - t.X) > 40 || Math.Abs(p.Y - t.Y) > 40) Native.SetCursorPos(t.X, t.Y);
                pendingCursor = null;
            }
        }

        /// <summary>Extracts the relative movement from a WM_INPUT mouse packet.</summary>
        void ReadRawMouse(IntPtr hRawInput)
        {
            int header = 8 + 2 * IntPtr.Size;            // RAWINPUTHEADER
            uint size = (uint)rawBuffer.Length;
            if (Native.GetRawInputData(hRawInput, Native.RID_INPUT, rawBuffer, ref size, (uint)header) == unchecked((uint)-1)) return;
            if (BitConverter.ToInt32(rawBuffer, 0) != 0) return;   // RIM_TYPEMOUSE
            ushort flags = BitConverter.ToUInt16(rawBuffer, header);
            if ((flags & 1) != 0) return;                          // MOUSE_MOVE_ABSOLUTE (tablet / remote desktop)
            int dx = BitConverter.ToInt32(rawBuffer, header + 12);
            int dy = BitConverter.ToInt32(rawBuffer, header + 16);
            if (dx != 0 || dy != 0) OnRawMouse(dx, dy);
        }

        /// <summary>Pushing outward against an edge of the game area builds up pressure; past the threshold the mouse is freed.</summary>
        void OnRawMouse(int dx, int dy)
        {
            if (!inside || pendingCursor.HasValue) return;
            if (AnyButtonDown()) { pushLeft = pushRight = pushUp = pushDown = 0; return; } // never release during a drag

            Native.POINT p; Native.GetCursorPos(out p);
            bool atLeft = p.X <= srcScreen.Left, atRight = p.X >= srcScreen.Right - 1;
            bool atTop = p.Y <= srcScreen.Top, atBottom = p.Y >= srcScreen.Bottom - 1;
            pushLeft = Push(pushLeft, atLeft, -dx);
            pushRight = Push(pushRight, atRight, dx);
            pushUp = Push(pushUp, atTop, -dy);
            pushDown = Push(pushDown, atBottom, dy);

            // Only release towards a side where there is screen space outside the scaled image.
            Rectangle screen = Screen.FromRectangle(destScreen).Bounds;
            Point v = ToDest(p);
            if (pushLeft > PushThreshold && destScreen.Left > screen.Left) LeaveGame(new Point(destScreen.Left - 2, v.Y), "links");
            else if (pushRight > PushThreshold && destScreen.Right < screen.Right) LeaveGame(new Point(destScreen.Right + 1, v.Y), "rechts");
            else if (pushUp > PushThreshold && destScreen.Top > screen.Top) LeaveGame(new Point(v.X, destScreen.Top - 2), "oben");
            else if (pushDown > PushThreshold && destScreen.Bottom < screen.Bottom) LeaveGame(new Point(v.X, destScreen.Bottom + 1), "unten");
        }

        /// <summary>True while the left, right or middle mouse button is held.</summary>
        static bool AnyButtonDown()
        {
            return (Native.GetAsyncKeyState(0x01) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x02) & 0x8000) != 0
                || (Native.GetAsyncKeyState(0x04) & 0x8000) != 0;
        }

        /// <summary>Screen point in the scaled image → game pixel.</summary>
        Point ToSource(Native.POINT p) { return ToSource(new Point(p.X, p.Y), srcScreen, destScreen); }

        /// <summary>Game pixel → screen point in the scaled image.</summary>
        Point ToDest(Native.POINT p) { return ToDest(new Point(p.X, p.Y), srcScreen, destScreen); }

        /// <summary>Point in the scaled image → matching game pixel, clamped into the game area.</summary>
        internal static Point ToSource(Point p, Rectangle src, Rectangle dest)
        {
            double f = (double)dest.Width / src.Width;
            int x = src.X + (int)((p.X - dest.X) / f);
            int y = src.Y + (int)((p.Y - dest.Y) / f);
            return new Point(Math.Max(src.Left, Math.Min(src.Right - 1, x)), Math.Max(src.Top, Math.Min(src.Bottom - 1, y)));
        }

        /// <summary>Game pixel → matching point in the scaled image (not clamped).</summary>
        internal static Point ToDest(Point p, Rectangle src, Rectangle dest)
        {
            double f = (double)dest.Width / src.Width;
            return new Point(dest.X + (int)((p.X - src.X) * f), dest.Y + (int)((p.Y - src.Y) * f));
        }

        /// <summary>
        /// Largest rectangle of <paramref name="size"/>'s aspect ratio centred in <paramref name="view"/>; with
        /// <paramref name="integer"/> only whole multiples (at least 1×, overflowing a view that is too small).
        /// </summary>
        internal static Rectangle FitRect(Rectangle view, Size size, bool integer)
        {
            int dw, dh;
            if (integer)
            {
                int f = Math.Max(1, Math.Min(view.Width / size.Width, view.Height / size.Height));
                dw = size.Width * f; dh = size.Height * f;
            }
            else
            {
                double f = Math.Min((double)view.Width / size.Width, (double)view.Height / size.Height);
                dw = Math.Max(1, (int)(size.Width * f)); dh = Math.Max(1, (int)(size.Height * f));
            }
            return new Rectangle(view.X + (view.Width - dw) / 2, view.Y + (view.Height - dh) / 2, dw, dh);
        }

        /// <summary>
        /// New push pressure against one edge: grows by an outward move while the cursor sits at that edge, is
        /// kept on moves along it, and drops to 0 on an inward move or away from the edge.
        /// </summary>
        internal static int Push(int pressure, bool atEdge, int outward)
        {
            if (atEdge && outward > 0) return pressure + outward;
            return outward < 0 || !atEdge ? 0 : pressure;
        }

        /// <summary>Moves the cursor into the game at the matching spot, clips it there, makes this window click-through, hides the system cursor and lowers the mouse speed if enabled.</summary>
        void EnterGame(Native.POINT p)
        {
            Point s = ToSource(p);
            Native.SetCursorPos(s.X, s.Y);
            pendingCursor = s;
            if (Native.GetForegroundWindow() == Handle) Native.SetForegroundWindow(gameMain);
            SetTopMost(true);
            SetTransparent(true);
            var clip = new Native.RECT { L = srcScreen.Left, T = srcScreen.Top, R = srcScreen.Right, B = srcScreen.Bottom };
            Native.ClipCursor(ref clip);
            HideSystemCursor(true);
            if (settings.SpeedCompensation)
            {
                int speed = 0;
                if (Native.SystemParametersInfo(Native.SPI_GETMOUSESPEED, 0, ref speed, 0))
                {
                    savedSpeed = speed;
                    Recovery.MouseSpeedChanged(speed);
                    int reduced = Math.Max(1, Math.Min(20, (int)Math.Round(speed / ScaleFactor)));
                    Native.SystemParametersInfoSet(Native.SPI_SETMOUSESPEED, 0, (IntPtr)reduced, 0);
                }
            }
            inside = true;
        }

        /// <summary>Undoes EnterGame and puts the cursor at <paramref name="target"/> (or the matching spot in the image).</summary>
        void LeaveGame(Point? target, string reason)
        {
            if (!inside) return;
            inside = false;
            pendingCursor = null;
            pushLeft = pushRight = pushUp = pushDown = 0;
            leaveWatch.Restart();
            Native.ClipCursorNone(IntPtr.Zero);
            RestoreSpeed();
            if (target.HasValue) Native.SetCursorPos(target.Value.X, target.Value.Y);
            else
            {
                Native.POINT p; Native.GetCursorPos(out p);
                Point v = ToDest(p);
                Native.SetCursorPos(v.X, v.Y);
            }
            HideSystemCursor(false);
            SetTransparent(false);
            Log("Maus frei: " + reason);
        }

        /// <summary>Restores the mouse speed EnterGame lowered.</summary>
        void RestoreSpeed()
        {
            if (savedSpeed < 0) return;
            Native.SystemParametersInfoSet(Native.SPI_SETMOUSESPEED, 0, (IntPtr)savedSpeed, 0);
            savedSpeed = -1;
            Recovery.MouseSpeedRestored();
        }

        /// <summary>Hides or shows the system cursor: via the magnification API, or (XP/Vista) by blanking the system cursors, recorded in Recovery.</summary>
        void HideSystemCursor(bool hide)
        {
            if (cursorHidden == hide) return;
            if (magOk) Native.MagShowSystemCursor(!hide);
            else if (hide)
            {
                if (cursorCopies.Count == 0)
                    foreach (uint id in Native.SystemCursorIds)
                    {
                        IntPtr shared = Native.LoadCursor(IntPtr.Zero, (IntPtr)id);
                        IntPtr copy = shared == IntPtr.Zero ? IntPtr.Zero : Native.CopyIcon(shared);
                        if (copy != IntPtr.Zero) cursorCopies[shared] = copy;
                    }
                Recovery.CursorsBlanked();
                Native.BlankSystemCursors();
            }
            else
            {
                Native.RestoreSystemCursors();
                Recovery.CursorsRestored();
            }
            cursorHidden = hide;
        }

        /// <summary>Makes this window click-through (WS_EX_TRANSPARENT) or not.</summary>
        void SetTransparent(bool on)
        {
            if (transparent == on) return;
            int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
            ex = on ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT;
            Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex);
            transparent = on;
        }

        /// <summary>Keeps the scaler topmost only while it or the game has the focus.</summary>
        void SetTopMost(bool on)
        {
            if (topMost == on) return;
            const uint flags = Native.SWP_NOSIZE | Native.SWP_NOMOVE | Native.SWP_NOACTIVATE;
            if (on) Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, flags);
            else
            {
                // Dropping topmost puts a window at the top of the normal band, i.e. above the window the
                // user just clicked. Slot in right behind the new foreground window instead.
                Native.SetWindowPos(Handle, Native.HWND_NOTOPMOST, 0, 0, 0, 0, flags);
                IntPtr fg = Native.GetForegroundWindow();
                if (fg != IntPtr.Zero && fg != Handle) Native.SetWindowPos(Handle, fg, 0, 0, 0, 0, flags);
            }
            topMost = on;
        }

        /// <summary>Draws the game's current cursor into the canvas, so it is scaled with the image.</summary>
        void DrawGameCursor()
        {
            var ci = new Native.CURSORINFO(); ci.cbSize = Marshal.SizeOf(typeof(Native.CURSORINFO));
            if (!Native.GetCursorInfo(ref ci) || ci.hCursor == IntPtr.Zero) return;
            IntPtr cursor = ci.hCursor, copy;
            if (cursorHidden && !magOk && cursorCopies.TryGetValue(cursor, out copy)) cursor = copy; // the shared one is blank now
            Point hot;
            if (!hotspots.TryGetValue(cursor, out hot))
            {
                Native.ICONINFO ii;
                if (Native.GetIconInfo(cursor, out ii))
                {
                    hot = new Point(ii.xHotspot, ii.yHotspot);
                    if (ii.hbmMask != IntPtr.Zero) Native.DeleteObject(ii.hbmMask);
                    if (ii.hbmColor != IntPtr.Zero) Native.DeleteObject(ii.hbmColor);
                }
                hotspots[cursor] = hot;
            }
            Native.POINT p; Native.GetCursorPos(out p);
            Native.DrawIconEx(canvasDC, G(p.X - srcScreen.X) - hot.X, G(p.Y - srcScreen.Y) - hot.Y, cursor, 0, 0, 0, IntPtr.Zero, Native.DI_NORMAL);
        }

        // ================================================================ shutdown

        /// <summary>Undoes every system-wide change. Safe to call more than once.</summary>
        public void RestoreEverything()
        {
            LeaveGame(null, "Ende");
            Native.ClipCursorNone(IntPtr.Zero);
            RestoreSpeed();
            HideSystemCursor(false);
            if (gameMoved && gameMain != IntPtr.Zero && Native.IsWindow(gameMain))
                Native.SetWindowPos(gameMain, IntPtr.Zero, gameOriginalPos.X, gameOriginalPos.Y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            gameMoved = false;
            Recovery.GameRestored();
            if (minimizedGame != IntPtr.Zero && Native.IsWindow(minimizedGame)) Native.ShowWindow(minimizedGame, Native.SW_SHOWNOACTIVATE);
            minimizedGame = IntPtr.Zero;
            RestoreGameTaskbarTab();
        }

        /// <summary>Saves the settings.</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSettings();
            base.OnFormClosing(e);
        }

        /// <summary>Undoes all system-wide changes and frees every resource.</summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            RestoreEverything();
            if (magOk) Native.MagUninitialize();
            foreach (IntPtr copy in cursorCopies.Values) Native.DestroyIcon(copy);
            cursorCopies.Clear();
            UnregisterHotkeys();
            ReleaseCanvas();
            ReleaseFrameCaches();
            base.OnFormClosed(e);
        }
    }
}
