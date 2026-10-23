# dsound proxy: start the scaler together with the game

A small helper DLL that makes A2G-Scaler start by itself as soon as Anstoss 2 Gold or the editor
runs, without clicking `A2G-Scaler.exe`.

## How it works

Game and editor load `dsound.dll` (DirectSound) at startup. It normally lives in the Windows system
folder, but `dsound` is not one of the "KnownDLLs" and nothing pulls it in early. A `dsound.dll`
right in the program folder is therefore loaded first, just like the game's own folder-local
`ddraw.dll` (Wine). This proxy DLL

1. forwards **every** DirectSound call unchanged to the real `dsound.dll` from the system folder
   (game and editor need `DirectSoundCreate` and `DirectSoundEnumerateA`; all exports are
   forwarded), and
2. on load starts `A2G-Scaler.exe --attach <PID>`, i.e. the scaler, attached to exactly the process
   that loaded the DLL.

Because the scaler gets the process ID, the game's file name doesn't matter, which also covers the
GOG version (`run.exe`). When started in full screen (`/f`) the scaler is not started, since scaling
makes no sense then.

Two details that are easy to miss when building and are documented as bugs:

- The real `dsound.dll` is **not loaded in `DllMain`** but on the first forwarded call. `LoadLibrary`
  under the loader lock messes up the binding of other modules being loaded at the same time (showed
  up as a bogus error "entry point `mciSendStringW` not found in `MSVFW32.dll`" at game start).
- The scaler is started **from a separate thread**, not directly in `DllMain`: `CreateProcess`
  under the loader lock hangs.

The DLL reads no game data and changes nothing in the game process. It forwards and starts, nothing
more.

## Why dsound and not winmm

The first attempt was `winmm.dll` (game and editor import it directly). That does **not** work here:
`winmm` is already preloaded from System32 via the DirectSound/video API sets before the program
folder is searched. The loaded system DLL wins, the folder copy is never used. `dsound` doesn't
have this problem.

## Building

Needs Visual Studio (or Build Tools) with the **"Desktop development with C++"** workload (the
.NET-only install is not enough). Then:

```
dsound-proxy\build.cmd
```

Output: `dsound-proxy\dsound.dll` (x86, matching the 32-bit game). The script finds Visual Studio
via `vswhere`; to use a specific install, set `VCVARS` to its `vcvarsall.bat` first. The release
workflow builds the DLL too; the built file is not committed.

## Install / remove

- **Install:** put `dsound.dll` into the game folder next to `anstoss2.exe` and `A2G-Scaler.exe`.
- **Remove:** delete `dsound.dll` again. The game then loads the system DLL as before.

## Things to consider

- **Virus scanners:** a planted `dsound.dll` is a pattern malware uses too (DLL hijacking).
  Defender & co. may flag the unprotected, unsigned DLL or quarantine it. For distribution this is
  the weak spot; whoever distributes the DLL should know this and ideally sign it.
- **x86 only.** The 32-bit game does not load a 64-bit DLL.
- If a scaler is already running, the automatically started one quietly steps aside (single
  instance).
