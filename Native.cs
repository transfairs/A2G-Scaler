using System;
using System.Runtime.InteropServices;
using System.Text;

namespace A2GScaler
{
    /// <summary>Win32 imports used by the scaler.</summary>
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT pt; }
        [StructLayout(LayoutKind.Sequential)] public struct ICONINFO { public bool fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }
        [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTDEVICE { public ushort usUsagePage; public ushort usUsage; public uint dwFlags; public IntPtr hwndTarget; }
        public delegate bool EnumProc(IntPtr h, IntPtr l);

        // Windows and window state
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);

        /// <summary>Declares the process DPI-aware; does nothing on Windows XP, which has no such call.</summary>
        public static void TrySetProcessDPIAware()
        {
            try { SetProcessDPIAware(); } catch (EntryPointNotFoundException) { }
        }

        /// <summary>DPI of a window as Windows sees it (96 for DPI-unaware windows); 0 before Windows 10 1607.</summary>
        public static uint DpiOf(IntPtr h)
        {
            try { return GetDpiForWindow(h); } catch (EntryPointNotFoundException) { return 0; }
        }
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int idx, int val);
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);

        // Cursor and input
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern bool ClipCursor(ref RECT r);
        [DllImport("user32.dll", EntryPoint = "ClipCursor")] public static extern bool ClipCursorNone(IntPtr zero);
        [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO ci);
        [DllImport("user32.dll")] public static extern bool GetIconInfo(IntPtr h, out ICONINFO ii);
        [DllImport("user32.dll")] public static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int w, int h, int step, IntPtr brush, int flags);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint param, ref int pv, uint ini);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfo")] public static extern bool SystemParametersInfoSet(uint action, uint param, IntPtr pv, uint ini);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
        [DllImport("user32.dll")] public static extern uint GetRawInputData(IntPtr hRawInput, uint command, byte[] data, ref uint size, uint headerSize);

        // GDI
        [DllImport("user32.dll")] public static extern IntPtr GetDCEx(IntPtr h, IntPtr clip, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, uint rop);
        [DllImport("gdi32.dll")] public static extern bool StretchBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int sw, int sh, uint rop);
        [DllImport("gdi32.dll")] public static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] public static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr old);
        [DllImport("gdi32.dll")] public static extern bool PatBlt(IntPtr dc, int x, int y, int w, int h, uint rop);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] public static extern int SetDIBitsToDevice(IntPtr dc, int x, int y, uint w, uint h, int sx, int sy, uint startScan, uint lines, int[] bits, ref BITMAPINFOHEADER bmi, uint usage);
        [DllImport("gdi32.dll")] public static extern bool GdiFlush();

        /// <summary>BITMAPINFO for 32-bit BI_RGB (no colour table needed).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int Size, Width, Height;
            public short Planes, BitCount;
            public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;

            public static BITMAPINFOHEADER TopDown32(int w, int h)
            {
                return new BITMAPINFOHEADER { Size = Marshal.SizeOf(typeof(BITMAPINFOHEADER)), Width = w, Height = -h, Planes = 1, BitCount = 32 };
            }
        }

        // Process elevation
        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern bool GetTokenInformation(IntPtr token, int cls, out int info, int size, out int returned);

        // Misc
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
        [DllImport("Magnification.dll")] static extern bool MagInitialize();
        [DllImport("Magnification.dll")] public static extern bool MagUninitialize();
        [DllImport("Magnification.dll")] public static extern bool MagShowSystemCursor(bool show);

        /// <summary>
        /// Initialises the magnification API and checks that it can hide the system cursor (Windows 7+).
        /// False on XP (no Magnification.dll) and Vista (no MagShowSystemCursor): use <see cref="BlankSystemCursors"/>.
        /// </summary>
        public static bool TryMagInitialize()
        {
            try
            {
                if (!MagInitialize()) return false;
                MagShowSystemCursor(true); // probe only: showing is the normal state
                return true;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException)
            {
                MagUninitialize();
                return false;
            }
        }

        // Fallback cursor hiding for systems without MagShowSystemCursor: temporarily replace the standard
        // system cursors (the game only uses those) by an invisible one, undone via SPI_SETCURSORS.
        [DllImport("user32.dll")] static extern bool SetSystemCursor(IntPtr cursor, uint id);
        [DllImport("user32.dll")] static extern IntPtr CreateCursor(IntPtr inst, int xHot, int yHot, int w, int h, byte[] andMask, byte[] xorMask);
        [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr inst, IntPtr id);
        [DllImport("user32.dll")] public static extern IntPtr CopyIcon(IntPtr icon);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);

        /// <summary>OCR_* ids of the standard system cursors (equal to the IDC_* resource ids).</summary>
        public static readonly uint[] SystemCursorIds = { 32512, 32513, 32514, 32515, 32516, 32642, 32643, 32644, 32645, 32646, 32648, 32649, 32650, 32651 };

        /// <summary>Replaces every standard system cursor by an invisible one. Undo with <see cref="RestoreSystemCursors"/>.</summary>
        public static void BlankSystemCursors()
        {
            var and = new byte[32 * 32 / 8];
            for (int i = 0; i < and.Length; i++) and[i] = 0xFF; // AND 1 + XOR 0 = screen unchanged
            var xor = new byte[and.Length];
            foreach (uint id in SystemCursorIds)
            {
                IntPtr blank = CreateCursor(IntPtr.Zero, 0, 0, 32, 32, and, xor);
                if (blank != IntPtr.Zero && !SetSystemCursor(blank, id)) DestroyIcon(blank); // on success the system owns it
            }
        }

        /// <summary>Reloads the user's cursor scheme from the registry, undoing <see cref="BlankSystemCursors"/>.</summary>
        public static void RestoreSystemCursors() { SystemParametersInfoSet(SPI_SETCURSORS, 0, IntPtr.Zero, 0); }

        public const uint SRCCOPY = 0x00CC0020, BLACKNESS = 0x00000042;
        public const uint DCX_WINDOW = 0x1, DCX_CACHE = 0x2;
        public const uint GW_HWNDPREV = 3, GW_OWNER = 4, GA_ROOT = 2;
        public const int COLORONCOLOR = 3, HALFTONE = 4;
        public const uint DIB_RGB_COLORS = 0;
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_NOTOPMOST = new IntPtr(-2);
        public const int SW_SHOWNOACTIVATE = 4, SW_SHOWMINNOACTIVE = 7;
        public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        public const int WS_CAPTION = 0x00C00000, WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20;
        public const uint LWA_ALPHA = 0x2;
        public const int WM_GETICON = 0x7F, ICON_SMALL = 0, ICON_BIG = 1, ICON_SMALL2 = 2;
        public const uint SMTO_ABORTIFHUNG = 0x2;
        public const int DI_NORMAL = 0x3;
        public const uint SPI_GETMOUSESPEED = 0x70, SPI_SETMOUSESPEED = 0x71, SPI_SETCURSORS = 0x57;
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;
        public const int WM_HOTKEY = 0x312, WM_INPUT = 0x00FF;
        public const uint RIDEV_INPUTSINK = 0x100, RID_INPUT = 0x10000003;
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, TOKEN_QUERY = 0x8;
        public const int TokenElevation = 20, ERROR_ACCESS_DENIED = 5;

        /// <summary>Window class name of <paramref name="h"/>.</summary>
        public static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
        /// <summary>Title of <paramref name="h"/>.</summary>
        public static string TextOf(IntPtr h) { var sb = new StringBuilder(512); GetWindowText(h, sb, sb.Capacity); return sb.ToString(); }

        [StructLayout(LayoutKind.Sequential)] struct MENUBARINFO { public int cbSize; public RECT rcBar; public IntPtr hMenu, hwndMenu; public int flags; }
        [DllImport("user32.dll")] static extern IntPtr GetMenu(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetMenuBarInfo(IntPtr h, int idObject, int idItem, ref MENUBARINFO mbi);
        const int OBJID_MENU = unchecked((int)0xFFFFFFFD);

        /// <summary>Screen rectangle of the window's menu bar; false if it has none.</summary>
        public static bool MenuBarOf(IntPtr h, out RECT bar)
        {
            bar = new RECT();
            if (GetMenu(h) == IntPtr.Zero) return false;
            var mbi = new MENUBARINFO { cbSize = Marshal.SizeOf(typeof(MENUBARINFO)) };
            if (!GetMenuBarInfo(h, OBJID_MENU, 0, ref mbi) || mbi.rcBar.B <= mbi.rcBar.T) return false;
            bar = mbi.rcBar;
            return true;
        }

        /// <summary>
        /// UAC (and with it elevation and UIPI) exists from Vista on. XP has neither: any process may control any
        /// other's windows, and the queries below would fail there (unknown access right, unknown token class).
        /// </summary>
        internal static bool UacExists = Environment.OSVersion.Version.Major >= 6;

        /// <summary>True if the process runs elevated. A process we may not even query counts as elevated. Always false without UAC.</summary>
        public static bool IsElevated(uint pid)
        {
            if (!UacExists) return false;
            IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero) return Marshal.GetLastWin32Error() == ERROR_ACCESS_DENIED;
            try { return IsElevatedProcess(process); }
            finally { CloseHandle(process); }
        }

        /// <summary>True if the scaler itself runs elevated. Always false without UAC.</summary>
        public static bool IsCurrentProcessElevated() { return UacExists && IsElevatedProcess(GetCurrentProcess()); }

        /// <summary>Reads TokenElevation; a token we may not open counts as elevated, any other failure as not elevated.</summary>
        static bool IsElevatedProcess(IntPtr process)
        {
            IntPtr token;
            if (!OpenProcessToken(process, TOKEN_QUERY, out token)) return Marshal.GetLastWin32Error() == ERROR_ACCESS_DENIED;
            try
            {
                int elevated, returned;
                return GetTokenInformation(token, TokenElevation, out elevated, 4, out returned) && elevated != 0;
            }
            finally { CloseHandle(token); }
        }
    }

    /// <summary>Shell taskbar interface, used to hide the game's own taskbar button while the scaler shows it.</summary>
    [ComImport, Guid("56FDF342-FD6D-11d0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ITaskbarList
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
    }

    /// <summary>COM class of the shell taskbar list (CLSID_TaskbarList).</summary>
    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    class TaskbarList { }
}
