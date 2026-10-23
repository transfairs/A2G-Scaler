# Technical notes and pitfalls

How A2G-Scaler works, and which obvious simplifications do not work. Read before changing capture,
mouse handling or z-order.

## Starting point

According to the decompiled code, the game renders at a fixed 640×480:
- The default is a window (DirectDraw `DDSCL_NORMAL` plus GDI).
- `/f`, `/F`, `-f` or `-F` switch the desktop to 640×480×16 via `ChangeDisplaySettingsA`. The same
  happens if the desktop already runs at 640×480.
- Only 8 or 16 bit colour depth is supported.

A real higher resolution is therefore out of reach; what remains is scaling the window.

## Picture

- **Capture:** `GetDCEx(hwnd, DCX_WINDOW|DCX_CACHE)` + `BitBlt` returns the complete window
  content even when the game is covered (about 4–5 ms per window). **Not** the screen DC: that
  shows whatever is on top. `PrintWindow` takes 33–54 ms, too slow for every frame.
- **Popups** (goal, card, fan chant, half time, calendar) are separate `#32770` top-level windows
  of the same process, owned by the main window, some without a title bar. So all visible windows
  of the process have to be composed in z-order.
- **The game centres popups on the screen's work area**, not over the game window (it assumes
  640×480 full screen). Fix: move new popups outside the main window once, via `SetWindowPos`, to
  its centre. They stay usable.
- Main window outer size 642×486, client area 636×457. The client area is what gets scaled.
- **Title bars** are drawn by DWM and are missing from the window DC (it holds game content
  there). Fix: frame and title bar come from a `PrintWindow(hwnd, 0)` cache, refreshed only when
  title, size or active state change, at the latest every 2 s.
- **Windows scaling above 100 % (without DPI override):** the game is not DPI-aware, the scaler
  is. DWM bitmap-stretches the game; at 125 % the scaler sees a client area of 795×571, but the
  window DC only holds the 636×457 game pixels. A canvas in screen pixels gives a picture that is
  too small, wrong mouse mapping and cursor trails in the never-overwritten rest. So the canvas has
  game-pixel size, factor = `GetDpiForWindow(scaler) / GetDpiForWindow(game)`. Screen offsets used
  for composing, for the frame cache and for the game cursor are divided by that factor.
  `srcScreen`, `ClipCursor` and `SetCursorPos` stay in screen pixels.
- **Scaling filter:** at a non-integer factor `COLORONCOLOR` looks uneven (game pixels alternately
  1 and 2 screen pixels wide), `HALFTONE` looks washed out. The default is therefore sharp bilinear
  (`SharpScaler.cs`): every game pixel stays a sharp block, only the boundary pixel is blended.
  **Not via GDI** (nearest to an integer multiple + `HALFTONE` down): 18 ms at 1000×718, 118 ms at
  4K. The own C# implementation needs 3 and 11 ms. Pixel-art upscalers (xBRZ/hqx) make text and
  photos look like comics. GPU filters (FSR/Lanczos) would need D3D11 and at least .NET 4.6.1.

## Mouse (same principle as Magpie, no code taken)

- The game window sits centred behind the scaler.
- When the cursor enters the picture, the scaler becomes click-through
  (`WS_EX_LAYERED|WS_EX_TRANSPARENT`). The cursor is moved to the matching spot in the game via
  `SetCursorPos` and held there via `ClipCursor`. `SetCursorPos` must come before `ClipCursor`
  and may need to be repeated once.
- The system cursor is hidden via `MagShowSystemCursor` (works from x86 too; fallback
  `SetSystemCursor`). The game's cursor is drawn into the picture via `DrawIconEx`.
- Speed compensation: `SPI_SETMOUSESPEED` divided by the scale factor (→ `Recovery`).
- **Release only by clearly pushing out at an edge:** raw input deltas with `RIDEV_INPUTSINK`,
  threshold about 100 counts. Merely touching the edge would make the controls at the top of the
  game unusable. Otherwise via hotkey or on focus loss.

## Z-order and taskbar

- **Context menus (`#32768`) and tooltips** have no owner and are always on top. The main window
  is therefore the largest unowned `#32770`. While the mouse is in the game, the scaler moves
  itself above these windows, but only if a game window lies above it. Otherwise it fights with
  the taskbar and flickers.
- "Always on top" only while the game or the scaler has the focus. When giving it up, set
  `HWND_NOTOPMOST` and then sort in behind the foreground window (not the `TopMost` property).
- The game's title and icon are mirrored. The game's taskbar button is hidden via
  `ITaskbarList.DeleteTab` and restored on exit (→ `Recovery`). Minimising also minimises the game.
- The toolbar needs `WM_MOUSEACTIVATE` → `MA_ACTIVATE`, otherwise the first click is lost.

## Data editor and launcher

- The game folder contains `anstoss2.exe`, `Editor.exe` (process name `Editor`) and `verl.exe`.
- **GOG release:** the game is `run.exe` (no `anstoss2.exe`; `vrun.exe` is its `verl.exe`), same main
  window class `#32770`. `Target.ExeIn` falls back to `run.exe` when there is no `anstoss2.exe`.
  Without that, the scaler found no game, and in a GOG folder the launcher only saw `Editor.exe` and
  started the editor without asking.
- **2007 release:** has both. Its `anstoss2.exe` is a **windowless starter** that runs the game as
  `run.exe` and stays alive meanwhile. Attaching to it left the scaler waiting for a window forever.
- Hence for the game, `Target.FindRunning` only accepts a process (`anstoss2` or `run`) whose main
  window title starts with "ANSTOSS 2", as the trainer does; `run` is also a generic name.
  `Target.IsStarted` (launcher: "already running, just attach") still counts a windowless
  `anstoss2`. If the launched process has no game window, `TrackGameProcess` searches for the one
  that has; when the launched starter ends after the game was seen (or 30 s without any game window),
  the scaler closes as it does with a launched game.
- The editor is not a dialog window: main window class `ASCARON ANSTOSS 2 DATENEDITOR`. The search
  for the largest `#32770` does not find it, so the class is attached to the target
  (`Target.MainWindowClass`).
- Editor outer size 640×480 without sizing border, client area 634×431, plus a **menu bar**.
  USER32 draws it into the non-client area, so it is in the window DC (unlike the title bar).
  `GetSourceArea` extends the captured area upwards by `GetMenuBarInfo(OBJID_MENU).rcBar`.
- Launcher: if `A2G-Scaler.exe` lies in the game folder, it asks "game or editor?" (only if both
  exist) and starts it without `/f` via shell execute (so compatibility settings such as "run as
  administrator" apply). If one is already running, it only attaches. When the program it started
  ends, the scaler exits too, unless a process of the same name keeps running.
- `GetProcessesByName` still lists an exited process for about a second. `Target.FindRunning`
  therefore filters via `HasExited`, otherwise the scaler stays open after the game ends.

## dsound proxy (`dsound-proxy/`)

A DLL in the game folder that starts the scaler when it is loaded.

- **Why dsound and not winmm:** game and editor import `WINMM.dll` directly, but a `winmm.dll` in
  the program folder is never loaded: `winmm` is preloaded from System32 via the API sets
  (`api-ms-win-mm-*`, imports of `dsound`/`avifil32`/`msvfw32`). Nothing pulls in `dsound.dll`
  early and both game and editor import it, so the folder copy wins.
- `dsound.c` (x86) exports all 12 dsound functions as `jmp` stubs to the real system `dsound.dll`
  and starts `A2G-Scaler.exe --attach <PID>`. `/f`/`-f` suppresses the start.
- **Loader lock:** start the scaler **from a separate thread** (`CreateProcess` in `DllMain`
  hangs). The real `dsound.dll` is loaded **lazily on the first call** (`EnsureLoaded()`), not in
  `DllMain`.
- Scaler side: `Program.AttachPid()` reads `--attach <pid>`, `Target.ForProcess()` derives the
  target from the process name. The attached host counts as "started by us". In attach mode the
  single-instance check suppresses the "already running" box.
- The scaler deliberately runs as a separate process: it makes itself DPI-aware and changes the
  timer resolution; inside the game process that would change the game.
- Building needs the Visual Studio workload "Desktop development with C++".
- Risk: an unsigned `dsound.dll` in the game folder is a typical DLL-hijacking pattern, virus
  scanners may flag it. Sign it for distribution.

## Older Windows versions

Target: Windows XP SP3 and later (.NET Framework 4.0 Client Profile, x86). Day-to-day testing happens
on Windows 10/11; everything below is designed for older versions but **not yet verified there**.

| API | Available from | Without it |
|---|---|---|
| `SetProcessDPIAware` | Vista | `Native.TrySetProcessDPIAware` does nothing (XP has no per-process DPI virtualisation anyway) |
| `GetDpiForWindow` | Windows 10 1607 | `Native.DpiOf` returns 0, game scale stays 1 |
| `MagInitialize` (Magnification.dll) | Vista | `Native.TryMagInitialize` returns false |
| `MagShowSystemCursor` | Windows 7 | same, probed right after `MagInitialize` |
| `MOD_NOREPEAT` | Windows 7 | `HotkeyText.NoRepeatSupported` false: hotkeys registered without it (older versions reject the whole registration with the flag) |
| `TokenElevation`, `PROCESS_QUERY_LIMITED_INFORMATION` | Vista | `Native.UacExists` false: no elevation check at all. Without it, XP answered the unknown access right with "access denied", which counts as elevated, and the scaler reported the game as running as administrator even when both ran as admin (found on real XP) |

- **Hiding the cursor without `MagShowSystemCursor`:** the standard system cursors (`OCR_*`) are
  replaced by an invisible cursor via `SetSystemCursor` while the mouse is in the game, and the
  user's scheme is reloaded via `SPI_SETCURSORS` on leaving. That is a system-wide change, so it is
  recorded in `Recovery` (`SystemCursors`). The game only uses the standard arrow and hourglass
  (`LoadCursorA(NULL, IDC_ARROW / IDC_WAIT)` in the decompiled code), so this hides it completely.
  `GetCursorInfo` then reports the shared, now blank handle; `DrawGameCursor` draws a copy of the
  original cursor (`CopyIcon` before blanking) instead.
- **Capture without desktop composition** (XP; Vista/7 without Aero): there are no redirection
  surfaces, a window covered by another one does not keep its pixels. The scaler is a layered window
  (`WS_EX_LAYERED`, alpha 255), and layered windows are composited as sprites on top of the screen,
  so windows below still paint to the screen and a window-DC `BitBlt` of the game reads them (screen
  reads without `CAPTUREBLT` exclude layered windows). This is the expected behaviour, not yet seen on
  real XP. Other, non-layered windows over the game would still spoil the capture.
- **dsound proxy on XP:** current MSVC toolsets no longer target XP, and their static CRT imports
  functions XP lacks. `dsound.c` therefore uses no CRT at all (`/Zl /GS- /NODEFAULTLIB`, entry point
  `DllMain`, own `memset`) and is linked with `/SUBSYSTEM:WINDOWS,5.01`. Its only imports are
  kernel32/user32 functions that exist since XP; check with `dumpbin /imports` after changes.
- **Icon:** `A2G-Scaler.ico` holds DIB images up to 64 px plus a PNG-compressed 256 px image. XP
  cannot decode the PNG one and uses the DIB sizes.
- **Fonts:** the waiting/admin message uses "Segoe UI", which XP does not ship; GDI+ falls back to
  Microsoft Sans Serif. Dialogs use `SystemFonts.MessageBoxFont` and look native everywhere.
