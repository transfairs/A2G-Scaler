// A2G-Scaler dsound proxy (x86).
//
// Dropped next to anstoss2.exe / Editor.exe, this file is loaded in place of the system dsound.dll:
// dsound is not a KnownDLL and nothing pulls it in before the game's own import, so the copy in the
// program folder wins the loader search (winmm, the first attempt, does NOT work this way: it is
// pre-loaded from System32 via the DirectSound/AVI apiset chain before the folder is searched).
//
// It forwards every DirectSound entry point on to the real system dsound.dll, and on load starts
// A2G-Scaler.exe attached to the host process (A2G-Scaler.exe --attach <pid>). The scaler then shows
// that very process, whatever it is called (so this also covers the GOG run.exe), and closes again when
// it ends. The launch runs on its own thread, because CreateProcess under the loader lock in DllMain
// deadlocks. On fullscreen start (/f) the scaler is not launched.
//
// It reads no game data and changes nothing in the host. Remove the scaler by deleting this dsound.dll.
//
// Build: run build.cmd (needs Visual Studio's "Desktop development with C++" workload). Must be x86.
// Built without the C runtime (the static CRT of current toolsets needs APIs Windows XP lacks), so only
// kernel32/user32 functions that exist since XP may be used here.

#include <windows.h>

// The compiler may emit memset calls (e.g. for ZeroMemory); without the CRT this file provides it.
#pragma function(memset)
void *memset(void *dst, int value, size_t count)
{
    unsigned char *p = (unsigned char *)dst;
    while (count--) *p++ = (unsigned char)value;
    return dst;
}

// Every export of the real dsound.dll, so nothing the game (or anything else) expects goes missing.
// The game needs DirectSoundCreate + DirectSoundEnumerateA, the editor DirectSoundCreate.
#define DSOUND_FUNCS(X) \
    X(DirectSoundCreate) X(DirectSoundEnumerateA) X(DirectSoundEnumerateW) \
    X(DirectSoundCaptureCreate) X(DirectSoundCaptureEnumerateA) X(DirectSoundCaptureEnumerateW) \
    X(GetDeviceID) X(DirectSoundFullDuplexCreate) X(DirectSoundCreate8) \
    X(DirectSoundCaptureCreate8) X(DllCanUnloadNow) X(DllGetClassObject)

#define DECLARE_PTR(name) static FARPROC p_##name;
DSOUND_FUNCS(DECLARE_PTR)
#undef DECLARE_PTR

static CRITICAL_SECTION g_lock;
static volatile LONG g_loaded;

// Loads the real system dsound.dll and resolves the forward pointers. Done lazily on the first
// forwarded call, never from DllMain: LoadLibrary under the loader lock corrupts the binding of other
// modules still being loaded (observed as a bogus "mciSendStringW missing in MSVFW32.dll" on game start).
static void EnsureLoaded(void)
{
    if (g_loaded) return;
    EnterCriticalSection(&g_lock);
    if (!g_loaded)
    {
        wchar_t path[MAX_PATH];
        UINT n = GetSystemDirectoryW(path, MAX_PATH);
        if (n != 0 && n <= MAX_PATH - 13)
        {
            lstrcpyW(path + n, L"\\dsound.dll");
            HMODULE real = LoadLibraryW(path);
            if (real)
            {
                #define RESOLVE(name) p_##name = GetProcAddress(real, #name);
                DSOUND_FUNCS(RESOLVE)
                #undef RESOLVE
            }
        }
        g_loaded = 1;
    }
    LeaveCriticalSection(&g_lock);
}

// Internal names (w_*) so they do not clash with any prototype in the SDK headers; dsound.def re-exports
// each under its plain DirectSound name. Each ensures the real dsound is loaded (first call only), then
// jumps straight into the real function with the caller's arguments and stack untouched.
#define DEFINE_STUB(name) \
    __declspec(naked) void w_##name(void) \
    { \
        __asm { pushad } \
        __asm { call EnsureLoaded } \
        __asm { popad } \
        __asm { jmp dword ptr [p_##name] } \
    }
DSOUND_FUNCS(DEFINE_STUB)
#undef DEFINE_STUB

// ---- launching the scaler -------------------------------------------------------------------------

// True if the game was started in fullscreen: /f, -f, /F or -F as a separate argument.
static BOOL StartedFullscreen(void)
{
    LPCWSTR c = GetCommandLineW();
    for (; *c; c++)
    {
        if ((*c == L'/' || *c == L'-') && (c[1] == L'f' || c[1] == L'F')
            && (c[2] == 0 || c[2] == L' ' || c[2] == L'\t')
            && (c == GetCommandLineW() || c[-1] == L' ' || c[-1] == L'\t' || c[-1] == L'"'))
            return TRUE;
    }
    return FALSE;
}

static void LaunchScaler(HINSTANCE self)
{
    if (StartedFullscreen()) return;

    wchar_t dir[MAX_PATH];
    DWORD len = GetModuleFileNameW(self, dir, MAX_PATH);   // path of this DLL => the program folder
    if (len == 0 || len >= MAX_PATH) return;
    wchar_t *slash = NULL;
    for (wchar_t *c = dir; *c; c++) if (*c == L'\\') slash = c;
    if (!slash) return;
    *slash = 0;

    wchar_t exe[MAX_PATH];
    if (wsprintfW(exe, L"%s\\A2G-Scaler.exe", dir) < 0) return;
    if (GetFileAttributesW(exe) == INVALID_FILE_ATTRIBUTES) return;

    wchar_t cmd[MAX_PATH + 64];
    wsprintfW(cmd, L"\"%s\" --attach %lu", exe, GetCurrentProcessId());

    STARTUPINFOW si; PROCESS_INFORMATION pi;
    ZeroMemory(&si, sizeof si); si.cb = sizeof si;
    if (CreateProcessW(NULL, cmd, NULL, NULL, FALSE, 0, NULL, dir, &si, &pi))
    {
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
    }
}

// Runs after DllMain returns and the loader lock is released; CreateProcess is unsafe before then.
static DWORD WINAPI LauncherThread(LPVOID param)
{
    LaunchScaler((HINSTANCE)param);
    return 0;
}

BOOL WINAPI DllMain(HINSTANCE self, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(self);
        InitializeCriticalSection(&g_lock);    // real dsound is loaded lazily on the first forwarded call
        HANDLE t = CreateThread(NULL, 0, LauncherThread, self, 0, NULL);
        if (t) CloseHandle(t);
    }
    return TRUE;
}
