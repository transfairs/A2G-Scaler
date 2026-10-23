using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace A2GScaler
{
    /// <summary>Scaling modes, in the order of ScalerForm.ModeNames. Stored as numbers in settings.ini: append new modes, never reorder.</summary>
    enum ScaleMode { FitNearest, FitSmooth, IntegerNearest, FitSharp }

    /// <summary>Actions that have a global hotkey; the value + 1 is the hotkey id.</summary>
    enum HotkeyAction { Release, Fullscreen, Mode, Toolbar }

    /// <summary>Simple key=value file in %LocalAppData%\A2G-Scaler.</summary>
    class KeyValueFile
    {
        readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly string Path;

        public KeyValueFile(string path) { Path = path; }

        /// <summary>Tests point this at a temp folder so they never touch the real settings; null in the app.</summary>
        internal static string FolderOverride;

        /// <summary>%LocalAppData%\A2G-Scaler (created if missing): settings.ini, recovery.ini, scaler.log.</summary>
        public static string Folder
        {
            get
            {
                string dir = FolderOverride ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "A2G-Scaler");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public bool Exists { get { return File.Exists(Path); } }

        /// <summary>Replaces the values in memory by the file's content (empty if the file is missing). Lines starting with # and lines without a key are skipped.</summary>
        public KeyValueFile Load()
        {
            values.Clear();
            if (!File.Exists(Path)) return this;
            foreach (string line in File.ReadAllLines(Path, Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                // Trim() keeps U+FEFF since .NET 4: a stray extra BOM would hide the first key.
                if (eq > 0 && !line.StartsWith("#")) values[line.Substring(0, eq).Trim().TrimStart('﻿')] = line.Substring(eq + 1).Trim();
            }
            return this;
        }

        /// <summary>Writes all values as UTF-8 key=value lines, replacing the file.</summary>
        public void Save()
        {
            var sb = new StringBuilder();
            foreach (var kv in values) sb.Append(kv.Key).Append('=').Append(kv.Value).AppendLine();
            File.WriteAllText(Path, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>Deletes the file (if any) and forgets all values.</summary>
        public void Delete() { if (File.Exists(Path)) File.Delete(Path); values.Clear(); }

        public string Get(string key, string fallback) { string v; return values.TryGetValue(key, out v) ? v : fallback; }
        public int GetInt(string key, int fallback) { int v; return int.TryParse(Get(key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback; }
        public long GetLong(string key, long fallback) { long v; return long.TryParse(Get(key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback; }
        public bool GetBool(string key, bool fallback) { string v = Get(key, ""); return v == "1" ? true : v == "0" ? false : fallback; }
        public bool Has(string key) { return values.ContainsKey(key); }

        public void Set(string key, string value) { values[key] = value; }
        public void Set(string key, int value) { values[key] = value.ToString(CultureInfo.InvariantCulture); }
        public void Set(string key, long value) { values[key] = value.ToString(CultureInfo.InvariantCulture); }
        public void Set(string key, bool value) { values[key] = value ? "1" : "0"; }
        public void Remove(string key) { values.Remove(key); }
    }

    /// <summary>User settings, persisted between runs.</summary>
    class Settings
    {
        public ScaleMode Mode = ScaleMode.FitSharp;
        public bool ShowInfo = true, MovePopups = true, SpeedCompensation = true, Fullscreen, Maximized;
        public Rectangle WindowBounds = Rectangle.Empty;
        public string LastTarget = "";   // process name of the program last picked in the launch dialog
        public readonly Dictionary<HotkeyAction, Keys> Hotkeys = DefaultHotkeys();

        public static Dictionary<HotkeyAction, Keys> DefaultHotkeys()
        {
            var d = new Dictionary<HotkeyAction, Keys>();
            d[HotkeyAction.Release] = Keys.Control | Keys.Alt | Keys.S;
            d[HotkeyAction.Fullscreen] = Keys.Control | Keys.Alt | Keys.F;
            d[HotkeyAction.Mode] = Keys.Control | Keys.Alt | Keys.M;
            d[HotkeyAction.Toolbar] = Keys.Control | Keys.Alt | Keys.L;
            return d;
        }

        /// <summary>settings.ini in <see cref="KeyValueFile.Folder"/>.</summary>
        static KeyValueFile File() { return new KeyValueFile(System.IO.Path.Combine(KeyValueFile.Folder, "settings.ini")); }

        /// <summary>Reads settings.ini; missing, invalid or unreadable values keep their defaults. Window bounds are only used if large enough and on a screen.</summary>
        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                var f = File().Load();
                int mode = f.GetInt("Mode", (int)s.Mode);
                if (Enum.IsDefined(typeof(ScaleMode), mode)) s.Mode = (ScaleMode)mode;
                s.ShowInfo = f.GetBool("ShowInfo", s.ShowInfo);
                s.MovePopups = f.GetBool("MovePopups", s.MovePopups);
                s.SpeedCompensation = f.GetBool("SpeedCompensation", s.SpeedCompensation);
                s.Fullscreen = f.GetBool("Fullscreen", false);
                s.Maximized = f.GetBool("Maximized", false);
                var b = new Rectangle(f.GetInt("WindowX", 0), f.GetInt("WindowY", 0), f.GetInt("WindowW", 0), f.GetInt("WindowH", 0));
                if (b.Width >= 200 && b.Height >= 150 && IsOnScreen(b)) s.WindowBounds = b;
                s.LastTarget = f.Get("LastTarget", "");
                foreach (HotkeyAction a in Enum.GetValues(typeof(HotkeyAction)))
                    if (f.Has("Hotkey." + a)) s.Hotkeys[a] = (Keys)f.GetInt("Hotkey." + a, 0);
            }
            catch (Exception) { /* broken file: defaults */ }
            return s;
        }

        /// <summary>Writes all settings to settings.ini; failures are ignored (the next run then uses the old values).</summary>
        public void Save()
        {
            var f = File();
            f.Set("Mode", (int)Mode);
            f.Set("ShowInfo", ShowInfo);
            f.Set("MovePopups", MovePopups);
            f.Set("SpeedCompensation", SpeedCompensation);
            f.Set("Fullscreen", Fullscreen);
            f.Set("Maximized", Maximized);
            f.Set("WindowX", WindowBounds.X); f.Set("WindowY", WindowBounds.Y);
            f.Set("WindowW", WindowBounds.Width); f.Set("WindowH", WindowBounds.Height);
            f.Set("LastTarget", LastTarget);
            foreach (var kv in Hotkeys) f.Set("Hotkey." + kv.Key, (int)kv.Value);
            try { f.Save(); } catch (Exception) { }
        }

        /// <summary>True if at least 100×50 pixels of <paramref name="r"/> lie on some screen's working area.</summary>
        static bool IsOnScreen(Rectangle r)
        {
            foreach (Screen s in Screen.AllScreens)
            {
                Rectangle i = Rectangle.Intersect(s.WorkingArea, r);
                if (i.Width >= 100 && i.Height >= 50) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Records every system-wide change the scaler makes (mouse speed, game window position, hidden
    /// taskbar button, blanked system cursors), so a later run can undo them if this run crashed or was killed.
    /// </summary>
    static class Recovery
    {
        static KeyValueFile current;

        /// <summary>recovery.ini in <see cref="KeyValueFile.Folder"/>; follows a changed folder (tests).</summary>
        static KeyValueFile file
        {
            get
            {
                string path = System.IO.Path.Combine(KeyValueFile.Folder, "recovery.ini");
                if (current == null || current.Path != path) current = new KeyValueFile(path);
                return current;
            }
        }

        /// <summary>The mouse speed was lowered; keeps the first recorded original.</summary>
        public static void MouseSpeedChanged(int original) { Update(delegate { if (!file.Has("MouseSpeed")) file.Set("MouseSpeed", original); }); }
        public static void MouseSpeedRestored() { Update(delegate { file.Remove("MouseSpeed"); }); }

        /// <summary>The game window was moved; keeps the first original position per window.</summary>
        public static void GameMoved(IntPtr hwnd, Point original)
        {
            Update(delegate
            {
                if (file.GetLong("GameWindow", 0) == hwnd.ToInt64()) return;
                file.Set("GameWindow", hwnd.ToInt64()); file.Set("GameX", original.X); file.Set("GameY", original.Y);
            });
        }
        public static void GameRestored() { Update(delegate { file.Remove("GameWindow"); file.Remove("GameX"); file.Remove("GameY"); }); }

        /// <summary>The taskbar button of <paramref name="hwnd"/> was hidden.</summary>
        public static void TabHidden(IntPtr hwnd) { Update(delegate { file.Set("HiddenTab", hwnd.ToInt64()); }); }
        public static void TabRestored() { Update(delegate { file.Remove("HiddenTab"); }); }

        /// <summary>The system cursors were replaced by invisible ones (XP/Vista fallback for hiding the cursor).</summary>
        public static void CursorsBlanked() { Update(delegate { file.Set("SystemCursors", true); }); }
        public static void CursorsRestored() { Update(delegate { file.Remove("SystemCursors"); }); }

        /// <summary>Applies <paramref name="change"/> to recovery.ini (load, change, save); I/O errors are ignored.</summary>
        static void Update(Action change)
        {
            try { file.Load(); change(); file.Save(); } catch (Exception) { }
        }

        /// <summary>Undoes what a previous, crashed run left behind. Returns a description for the log, or null.</summary>
        public static string RepairLeftovers()
        {
            Native.ClipCursorNone(IntPtr.Zero);
            if (!file.Exists) return null;
            var done = new List<string>();
            try
            {
                file.Load();
                int speed = file.GetInt("MouseSpeed", -1);
                if (speed >= 1 && speed <= 20)
                {
                    Native.SystemParametersInfoSet(Native.SPI_SETMOUSESPEED, 0, (IntPtr)speed, 0);
                    done.Add("Mausgeschwindigkeit " + speed);
                }
                var game = new IntPtr(file.GetLong("GameWindow", 0));
                if (game != IntPtr.Zero && Native.IsWindow(game))
                {
                    Native.SetWindowPos(game, IntPtr.Zero, file.GetInt("GameX", 0), file.GetInt("GameY", 0), 0, 0,
                        Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                    done.Add("Spielfensterposition");
                }
                var tab = new IntPtr(file.GetLong("HiddenTab", 0));
                if (tab != IntPtr.Zero && Native.IsWindow(tab))
                {
                    var taskbar = (ITaskbarList)new TaskbarList();
                    taskbar.HrInit();
                    taskbar.AddTab(tab);
                    done.Add("Taskleisten-Eintrag");
                }
                if (file.GetBool("SystemCursors", false))
                {
                    Native.RestoreSystemCursors();
                    done.Add("Mauszeiger");
                }
            }
            catch (Exception ex) { done.Add("Fehler: " + ex.Message); }
            // Runs before the crash handler is installed: a locked file must not stop the start.
            try { file.Delete(); } catch (Exception ex) { done.Add("recovery.ini nicht löschbar: " + ex.Message); }
            return done.Count == 0 ? null : string.Join(", ", done.ToArray());
        }
    }

    /// <summary>Formatting and registration helpers for global hotkeys.</summary>
    static class HotkeyText
    {
        /// <summary>Display text such as "Strg+Alt+S", or "(keins)" for no hotkey.</summary>
        public static string Format(Keys k)
        {
            if ((k & Keys.KeyCode) == Keys.None) return "(keins)";
            var parts = new List<string>();
            if ((k & Keys.Control) != 0) parts.Add("Strg");
            if ((k & Keys.Alt) != 0) parts.Add("Alt");
            if ((k & Keys.Shift) != 0) parts.Add("Umschalt");
            Keys code = k & Keys.KeyCode;
            if (code >= Keys.D0 && code <= Keys.D9) parts.Add(((int)(code - Keys.D0)).ToString());
            else if (code >= Keys.NumPad0 && code <= Keys.NumPad9) parts.Add("Num " + (int)(code - Keys.NumPad0));
            else parts.Add(code.ToString());
            return string.Join("+", parts.ToArray());
        }

        /// <summary>A hotkey needs a modifier, unless it is a function key.</summary>
        public static bool IsValid(Keys k)
        {
            Keys code = k & Keys.KeyCode;
            if (code == Keys.None) return true; // "no hotkey"
            bool fkey = code >= Keys.F1 && code <= Keys.F24;
            return fkey || (k & (Keys.Control | Keys.Alt)) != 0;
        }

        /// <summary>MOD_NOREPEAT exists from Windows 7 on; older versions reject the whole registration with it.</summary>
        internal static bool NoRepeatSupported = Environment.OSVersion.Version >= new Version(6, 1);

        /// <summary>Registers <paramref name="k"/> as global hotkey <paramref name="id"/>; true for no hotkey, false if another program has it.</summary>
        public static bool Register(IntPtr hwnd, int id, Keys k)
        {
            Keys code = k & Keys.KeyCode;
            if (code == Keys.None) return true;
            uint mods = NoRepeatSupported ? Native.MOD_NOREPEAT : 0;
            if ((k & Keys.Control) != 0) mods |= Native.MOD_CONTROL;
            if ((k & Keys.Alt) != 0) mods |= Native.MOD_ALT;
            if ((k & Keys.Shift) != 0) mods |= Native.MOD_SHIFT;
            return Native.RegisterHotKey(hwnd, id, mods, (uint)code);
        }
    }
}
