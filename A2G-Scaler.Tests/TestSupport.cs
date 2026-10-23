using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using Xunit;

// Settings/Recovery and AboutDialog.Open are static; parallel tests would race on them.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace A2GScaler.Tests
{
    /// <summary>
    /// Runs an action on a dedicated STA thread and rethrows any exception on the calling thread, for tests
    /// that touch WinForms controls.
    /// </summary>
    internal static class StaThread
    {
        public static void Run(Action action)
        {
            ExceptionDispatchInfo captured = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { captured = ExceptionDispatchInfo.Capture(ex); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (captured != null) captured.Throw();
        }
    }

    /// <summary>
    /// Points <see cref="KeyValueFile.Folder"/> at a fresh temp folder for the lifetime of the test, so tests
    /// never read or overwrite the real settings in %LocalAppData%\A2G-Scaler.
    /// </summary>
    public abstract class TempFolderTest : IDisposable
    {
        protected readonly string Folder;

        protected TempFolderTest()
        {
            Folder = Path.Combine(Path.GetTempPath(), "A2G-Scaler.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            KeyValueFile.FolderOverride = Folder;
        }

        public void Dispose()
        {
            KeyValueFile.FolderOverride = null;
            try { Directory.Delete(Folder, true); } catch (IOException) { }
        }
    }
}
