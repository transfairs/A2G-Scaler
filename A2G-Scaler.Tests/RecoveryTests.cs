using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Xunit;

namespace A2GScaler.Tests
{
    public class RecoveryTests : TempFolderTest
    {
        string IniPath { get { return Path.Combine(Folder, "recovery.ini"); } }
        KeyValueFile Ini() { return new KeyValueFile(IniPath).Load(); }

        static readonly IntPtr NoWindow = new IntPtr(0x7FFFFFF0); // never a valid HWND

        [Fact]
        public void MouseSpeed_RecordsFirstOriginalOnly_AndClears()
        {
            Recovery.MouseSpeedChanged(10);
            Recovery.MouseSpeedChanged(3); // already reduced: the original stays
            Assert.Equal(10, Ini().GetInt("MouseSpeed", 0));
            Recovery.MouseSpeedRestored();
            Assert.False(Ini().Has("MouseSpeed"));
        }

        [Fact]
        public void GameMoved_KeepsFirstPositionPerWindow_AndClears()
        {
            Recovery.GameMoved(new IntPtr(100), new Point(5, 6));
            Recovery.GameMoved(new IntPtr(100), new Point(50, 60)); // same window moved again
            Assert.Equal(5, Ini().GetInt("GameX", 0));
            Assert.Equal(6, Ini().GetInt("GameY", 0));

            Recovery.GameMoved(new IntPtr(200), new Point(7, 8)); // new game window
            Assert.Equal(200, Ini().GetLong("GameWindow", 0));
            Assert.Equal(7, Ini().GetInt("GameX", 0));

            Recovery.GameRestored();
            var f = Ini();
            Assert.False(f.Has("GameWindow") || f.Has("GameX") || f.Has("GameY"));
        }

        [Fact]
        public void Tab_RecordsAndClears()
        {
            Recovery.TabHidden(new IntPtr(300));
            Assert.Equal(300, Ini().GetLong("HiddenTab", 0));
            Recovery.TabRestored();
            Assert.False(Ini().Has("HiddenTab"));
        }

        [Fact]
        public void Cursors_RecordsAndClears()
        {
            Recovery.CursorsBlanked();
            Assert.True(Ini().GetBool("SystemCursors", false));
            Recovery.CursorsRestored();
            Assert.False(Ini().Has("SystemCursors"));
        }

        [Fact]
        public void Repair_ReloadsSystemCursors()
        {
            // SPI_SETCURSORS only reloads the user's own scheme, so this is harmless on the test machine.
            var f = new KeyValueFile(IniPath);
            f.Set("SystemCursors", true);
            f.Save();
            Assert.Equal("Mauszeiger", Recovery.RepairLeftovers());
        }

        [Fact]
        public void Update_LockedFile_IsSilent()
        {
            File.WriteAllText(IniPath, "");
            using (new FileStream(IniPath, FileMode.Open, FileAccess.Read, FileShare.None))
                Recovery.MouseSpeedChanged(10);
        }

        [Fact]
        public void File_FollowsFolderChange()
        {
            Recovery.TabHidden(new IntPtr(1));
            string other = Path.Combine(Folder, "other");
            KeyValueFile.FolderOverride = other;
            Recovery.TabHidden(new IntPtr(2));
            Assert.Equal(2, new KeyValueFile(Path.Combine(other, "recovery.ini")).Load().GetLong("HiddenTab", 0));
            Assert.Equal(1, Ini().GetLong("HiddenTab", 0));
            KeyValueFile.FolderOverride = Folder;
        }

        [Fact]
        public void Repair_NoFile_ReturnsNull()
        {
            Assert.Null(Recovery.RepairLeftovers());
        }

        [Fact]
        public void Repair_StaleEntries_DoNothingButDeleteFile()
        {
            var f = new KeyValueFile(IniPath);
            f.Set("MouseSpeed", 0);           // out of range
            f.Set("GameWindow", NoWindow.ToInt64());
            f.Set("HiddenTab", NoWindow.ToInt64());
            f.Save();
            Assert.Null(Recovery.RepairLeftovers());
            Assert.False(File.Exists(IniPath));
        }

        [Fact]
        public void Repair_LockedFile_ReportsInsteadOfThrowing()
        {
            File.WriteAllText(IniPath, "MouseSpeed=10\n");
            string done;
            using (new FileStream(IniPath, FileMode.Open, FileAccess.Read, FileShare.None))
                done = Recovery.RepairLeftovers();
            Assert.StartsWith("Fehler: ", done);
            Assert.Contains(", recovery.ini nicht löschbar: ", done);
            Assert.True(File.Exists(IniPath)); // left for the next start
        }

        [Fact]
        public void Repair_MouseSpeedAboveRange_IsIgnored()
        {
            var f = new KeyValueFile(IniPath);
            f.Set("MouseSpeed", 21);
            f.Save();
            Assert.Null(Recovery.RepairLeftovers());
        }

        [Fact]
        public void Repair_RestoresSpeedWindowAndTab()
        {
            // Writes back the current mouse speed, so the test leaves the system as it was.
            int speed = 0;
            Native.SystemParametersInfo(Native.SPI_GETMOUSESPEED, 0, ref speed, 0);

            StaThread.Run(delegate
            {
                using (var form = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(10, 10), ShowInTaskbar = false })
                {
                    IntPtr h = form.Handle;
                    var f = new KeyValueFile(IniPath);
                    f.Set("MouseSpeed", speed);
                    f.Set("GameWindow", h.ToInt64()); f.Set("GameX", 123); f.Set("GameY", 45);
                    f.Set("HiddenTab", h.ToInt64());
                    f.Save();

                    string done = Recovery.RepairLeftovers();

                    Assert.Equal("Mausgeschwindigkeit " + speed + ", Spielfensterposition, Taskleisten-Eintrag", done);
                    Native.RECT r; Native.GetWindowRect(h, out r);
                    Assert.Equal(123, r.L);
                    Assert.Equal(45, r.T);
                    Assert.False(File.Exists(IniPath));
                }
            });
        }
    }
}
