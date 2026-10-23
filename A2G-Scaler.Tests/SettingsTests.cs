using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Xunit;

namespace A2GScaler.Tests
{
    public class SettingsTests : TempFolderTest
    {
        string IniPath { get { return Path.Combine(Folder, "settings.ini"); } }

        [Fact]
        public void Defaults_WhenNoFile()
        {
            var s = Settings.Load();
            Assert.Equal(ScaleMode.FitSharp, s.Mode);
            Assert.True(s.ShowInfo);
            Assert.True(s.MovePopups);
            Assert.True(s.SpeedCompensation);
            Assert.False(s.Fullscreen);
            Assert.False(s.Maximized);
            Assert.Equal(Rectangle.Empty, s.WindowBounds);
            Assert.Equal("", s.LastTarget);
            Assert.Equal(Settings.DefaultHotkeys(), s.Hotkeys);
        }

        [Fact]
        public void DefaultHotkeys_AreCtrlAltCombinations()
        {
            var d = Settings.DefaultHotkeys();
            Assert.Equal(Keys.Control | Keys.Alt | Keys.S, d[HotkeyAction.Release]);
            Assert.Equal(Keys.Control | Keys.Alt | Keys.F, d[HotkeyAction.Fullscreen]);
            Assert.Equal(Keys.Control | Keys.Alt | Keys.M, d[HotkeyAction.Mode]);
            Assert.Equal(Keys.Control | Keys.Alt | Keys.L, d[HotkeyAction.Toolbar]);
        }

        [Fact]
        public void SaveAndLoad_RoundTrip()
        {
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            var s = new Settings
            {
                Mode = ScaleMode.IntegerNearest,
                ShowInfo = false,
                MovePopups = false,
                SpeedCompensation = false,
                Fullscreen = true,
                Maximized = true,
                WindowBounds = new Rectangle(work.X + 10, work.Y + 10, 400, 300),
                LastTarget = "Editor",
            };
            s.Hotkeys[HotkeyAction.Mode] = Keys.F9;
            s.Hotkeys[HotkeyAction.Toolbar] = Keys.None;
            s.Save();

            var t = Settings.Load();
            Assert.Equal(ScaleMode.IntegerNearest, t.Mode);
            Assert.False(t.ShowInfo);
            Assert.False(t.MovePopups);
            Assert.False(t.SpeedCompensation);
            Assert.True(t.Fullscreen);
            Assert.True(t.Maximized);
            Assert.Equal(s.WindowBounds, t.WindowBounds);
            Assert.Equal("Editor", t.LastTarget);
            Assert.Equal(Keys.F9, t.Hotkeys[HotkeyAction.Mode]);
            Assert.Equal(Keys.None, t.Hotkeys[HotkeyAction.Toolbar]);
            Assert.Equal(Keys.Control | Keys.Alt | Keys.S, t.Hotkeys[HotkeyAction.Release]);
        }

        [Fact]
        public void Load_UnknownMode_KeepsDefault()
        {
            File.WriteAllText(IniPath, "Mode=99\n");
            Assert.Equal(ScaleMode.FitSharp, Settings.Load().Mode);
        }

        [Theory]
        [InlineData(0, 0, 199, 300)]          // too narrow
        [InlineData(0, 0, 400, 149)]          // too low
        [InlineData(-100000, -100000, 400, 300)] // on no screen
        public void Load_RejectsUnusableWindowBounds(int x, int y, int w, int h)
        {
            File.WriteAllText(IniPath, "WindowX=" + x + "\nWindowY=" + y + "\nWindowW=" + w + "\nWindowH=" + h + "\n");
            Assert.Equal(Rectangle.Empty, Settings.Load().WindowBounds);
        }

        [Fact]
        public void Save_DropsObsoleteToolbarHidden()
        {
            File.WriteAllText(IniPath, "ToolbarHidden=1\n");
            new Settings().Save();
            Assert.DoesNotContain("ToolbarHidden", File.ReadAllText(IniPath));
        }

        [Fact]
        public void Load_UnreadableFile_GivesDefaults()
        {
            File.WriteAllText(IniPath, "Mode=0\n");
            using (new FileStream(IniPath, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Equal(ScaleMode.FitSharp, Settings.Load().Mode);
        }

        [Fact]
        public void Save_UnwritableFile_IsSilent()
        {
            File.WriteAllText(IniPath, "");
            using (new FileStream(IniPath, FileMode.Open, FileAccess.Read, FileShare.None))
                new Settings().Save();
        }
    }
}
