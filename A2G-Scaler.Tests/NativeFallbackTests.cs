using System;
using Xunit;

namespace A2GScaler.Tests
{
    /// <summary>The wrappers that keep the scaler running on Windows versions lacking newer APIs.</summary>
    public class NativeFallbackTests
    {
        [Fact]
        public void TrySetProcessDPIAware_DoesNotThrow()
        {
            Native.TrySetProcessDPIAware();
        }

        [Fact]
        public void TryMagInitialize_SucceedsFromWindows7()
        {
            bool ok = Native.TryMagInitialize();
            try { Assert.Equal(Environment.OSVersion.Version >= new Version(6, 1), ok); }
            finally { if (ok) Native.MagUninitialize(); }
        }

        [Fact]
        public void UacExists_FromVista()
        {
            Assert.Equal(Environment.OSVersion.Version.Major >= 6, Native.UacExists);
        }

        [Fact]
        public void WithoutUac_NothingCountsAsElevated()
        {
            // As on XP: the query would fail with "access denied" and wrongly report elevation.
            bool original = Native.UacExists;
            Native.UacExists = false;
            try
            {
                Assert.False(Native.IsElevated(4));          // System process: never queryable by a normal user
                Assert.False(Native.IsElevated(0x7FFFFFF0)); // no such process
                Assert.False(Native.IsCurrentProcessElevated());
            }
            finally { Native.UacExists = original; }
        }

        [Fact]
        public void WithUac_OwnProcessIsQueryable()
        {
            Assert.Equal(Native.IsCurrentProcessElevated(), Native.IsElevated((uint)System.Diagnostics.Process.GetCurrentProcess().Id));
        }

        [Fact]
        public void BlankAndRestoreSystemCursors()
        {
            // Restores the user's scheme right away; the blank phase lasts a few milliseconds.
            try
            {
                Native.BlankSystemCursors();
                IntPtr arrow = Native.LoadCursor(IntPtr.Zero, (IntPtr)32512);
                Assert.NotEqual(IntPtr.Zero, arrow);
            }
            finally { Native.RestoreSystemCursors(); }
        }
    }
}
