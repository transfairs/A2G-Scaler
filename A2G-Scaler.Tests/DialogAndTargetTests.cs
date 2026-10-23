using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Xunit;

namespace A2GScaler.Tests
{
    public class AboutDialogTests : TempFolderTest
    {
        [Fact]
        public void ShowsNameVersionLogoAndLinks()
        {
            StaThread.Run(delegate
            {
                using (var d = new AboutDialog("A2G-Scaler", "1.2.3"))
                {
                    Assert.Equal("Über A2G-Scaler", d.Text);
                    Assert.Contains(d.Controls.OfType<Label>(), l => l.Text == "v1.2.3");
                    var logo = d.Controls.OfType<PictureBox>().Single();
                    Assert.Equal(64, logo.Image.Width);
                    var links = d.Controls.OfType<LinkLabel>().ToList();
                    Assert.Equal(3, links.Count);
                    Assert.Contains(links, l => l.Text.EndsWith(Folder));
                    Assert.Same(d.AcceptButton, d.CancelButton);
                }
            });
        }

        [Fact]
        public void LinksOpenTheirTargets()
        {
            var opened = new List<string>();
            Action<string> original = AboutDialog.Open;
            AboutDialog.Open = opened.Add;
            try
            {
                StaThread.Run(delegate
                {
                    using (var d = new AboutDialog("A2G-Scaler", "1.2.3"))
                    {
                        MethodInfo onClick = typeof(LinkLabel).GetMethod("OnLinkClicked", BindingFlags.Instance | BindingFlags.NonPublic);
                        foreach (LinkLabel l in d.Controls.OfType<LinkLabel>())
                            onClick.Invoke(l, new object[] { new LinkLabelLinkClickedEventArgs(l.Links[0]) });
                    }
                });
            }
            finally { AboutDialog.Open = original; }

            Assert.Equal(new[] { "https://github.com/transfairs/A2G-Scaler", "https://github.com/transfairs/A2G-Trainer", Folder }, opened);
        }
    }

    public class TargetTests : IDisposable
    {
        readonly string dir = Path.Combine(Path.GetTempPath(), "A2G-Scaler.Tests", Guid.NewGuid().ToString("N"));
        readonly List<Process> started = new List<Process>();

        public TargetTests() { Directory.CreateDirectory(dir); }

        public void Dispose()
        {
            foreach (Process p in started)
                using (p) { try { if (!p.HasExited) { p.Kill(); p.WaitForExit(5000); } } catch (Exception) { } }
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }

        /// <summary>Runs a renamed copy of cmd.exe that waits a while, so it shows up under that process name.</summary>
        Process StartAs(string name)
        {
            string exe = Path.Combine(dir, name + ".exe");
            File.Copy(Environment.GetEnvironmentVariable("ComSpec"), exe, true);
            var p = Process.Start(new ProcessStartInfo(exe, "/c ping -n 30 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false });
            started.Add(p);
            return p;
        }

        [Fact]
        public void KnownTargets()
        {
            Assert.Equal("anstoss2", Target.Game.ProcessName);
            Assert.Equal("Anstoss 2 Gold", Target.Game.DisplayName);
            Assert.Equal("Editor", Target.Editor.ProcessName);
            Assert.Equal("Dateneditor", Target.Editor.DisplayName);
            Assert.Equal(Path.Combine(dir, "anstoss2.exe"), Target.Game.ExeIn(dir));
        }

        [Fact]
        public void ForProcess_RecognisesGameAndEditor()
        {
            Assert.Same(Target.Game, Target.ForProcess(StartAs("anstoss2")));
            Assert.Same(Target.Editor, Target.ForProcess(StartAs("editor"))); // case-insensitive
        }

        [Fact]
        public void ForProcess_GogRunExe_IsTheGame()
        {
            Assert.Same(Target.Game, Target.ForProcess(StartAs("run")));
        }

        [Theory]
        [InlineData("ANSTOSS 2 - Persönliche Erfolge", true)]
        [InlineData("Anstoss 2 Gold", true)]
        [InlineData("ANSTOSS 2", true)]
        [InlineData("Run", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsGameTitle(string title, bool expected)
        {
            Assert.Equal(expected, Target.IsGameTitle(title));
        }

        [Fact]
        public void ExeIn_FallsBackToGogRunExe()
        {
            Assert.Equal(Path.Combine(dir, "anstoss2.exe"), Target.Game.ExeIn(dir)); // neither exists: usual name
            File.WriteAllText(Path.Combine(dir, "run.exe"), "");
            Assert.Equal(Path.Combine(dir, "run.exe"), Target.Game.ExeIn(dir));
            File.WriteAllText(Path.Combine(dir, "anstoss2.exe"), "");
            Assert.Equal(Path.Combine(dir, "anstoss2.exe"), Target.Game.ExeIn(dir)); // original wins
            Assert.Equal(Path.Combine(dir, "Editor.exe"), Target.Editor.ExeIn(dir));
        }

        [Fact]
        public void FindRunning_IgnoresRunExeWithoutGameWindow()
        {
            // A real GOG game may be running on the test machine; it must just never be our dummy.
            Process dummy = StartAs("run");
            using (Process found = Target.Game.FindRunning())
                Assert.True(found == null || found.Id != dummy.Id);
        }

        [Fact]
        public void ForProcess_UnknownName_IsTreatedAsGame()
        {
            Target t = Target.ForProcess(StartAs("a2gunknown"));
            Assert.Equal("a2gunknown", t.ProcessName);
            Assert.Equal(Target.Game.DisplayName, t.DisplayName);
            Assert.Equal(Target.Game.MainWindowClass, t.MainWindowClass);
        }

        [Fact]
        public void ForProcess_UnreadableName_FallsBackToGameName()
        {
            using (var p = new Process()) // no process behind it: ProcessName throws
            {
                Target t = Target.ForProcess(p);
                Assert.Equal("anstoss2", t.ProcessName);
            }
        }

        [Fact]
        public void FindRunning_FindsLiveProcess_AndSkipsExcluded()
        {
            var t = new Target("a2gtestdummy", "Dummy", "#32770", "a2gtestdummy2", false); // no window needed
            Process p = StartAs("a2gtestdummy");
            using (Process found = t.FindRunning())
                Assert.Equal(p.Id, found.Id);
            Assert.Null(t.FindRunning(p.Id));
            p.Kill(); p.WaitForExit();
            Assert.Null(t.FindRunning());

            Process second = StartAs("a2gtestdummy2"); // the second name is searched too
            using (Process found = t.FindRunning())
                Assert.Equal(second.Id, found.Id);
        }

        [Fact]
        public void Game_IgnoresWindowlessStarter_ButCountsItAsStarted()
        {
            // Like the 2007 release's anstoss2.exe: running, but without the game window.
            Process starter = StartAs("anstoss2");
            Assert.False(Target.Game.HasGameWindow(starter));
            using (Process found = Target.Game.FindRunning())
                Assert.True(found == null || found.Id != starter.Id); // a real game may run on the test machine
            Assert.True(Target.Game.IsStarted());
        }

        [Fact]
        public void IsStarted_FalseWhenNothingRuns()
        {
            var t = new Target("a2gnotrunning", "Dummy", "#32770", "a2gnotrunning2", true);
            Assert.False(t.IsStarted());
        }

        [Fact]
        public void HasGameWindow_EditorNeedsNone_EndedProcessHasNone()
        {
            Process p = StartAs("a2gtestdummy");
            Assert.True(Target.Editor.HasGameWindow(p));
            p.Kill(); p.WaitForExit();
            Assert.False(Target.Game.HasGameWindow(p));
        }

        [Fact]
        public void ImageIn_PrefersIcoFile_ThenExeIcon_ElseNull()
        {
            Assert.Null(Target.Game.ImageIn(dir)); // neither ico nor exe

            File.Copy(Environment.GetEnvironmentVariable("ComSpec"), Path.Combine(dir, "anstoss2.exe"));
            using (var fromExe = Target.Game.ImageIn(dir)) Assert.NotNull(fromExe);

            string logo = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "A2G-Scaler.ico");
            File.Copy(logo, Path.Combine(dir, "anstoss2.ico"));
            using (var fromIco = Target.Game.ImageIn(dir)) Assert.Equal(32, fromIco.Width);
        }
    }
}
