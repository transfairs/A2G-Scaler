using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Xunit;

namespace A2GScaler.Tests
{
    public class HotkeyTextTests
    {
        [Theory]
        [InlineData(Keys.None, "(keins)")]
        [InlineData(Keys.Control, "(keins)")]
        [InlineData(Keys.Control | Keys.Alt | Keys.S, "Strg+Alt+S")]
        [InlineData(Keys.Shift | Keys.F5, "Umschalt+F5")]
        [InlineData(Keys.Control | Keys.Alt | Keys.Shift | Keys.D7, "Strg+Alt+Umschalt+7")]
        [InlineData(Keys.Alt | Keys.NumPad3, "Alt+Num 3")]
        [InlineData(Keys.F12, "F12")]
        public void Format(Keys k, string expected)
        {
            Assert.Equal(expected, HotkeyText.Format(k));
        }

        [Theory]
        [InlineData(Keys.None, true)]
        [InlineData(Keys.F1, true)]
        [InlineData(Keys.F24, true)]
        [InlineData(Keys.Control | Keys.A, true)]
        [InlineData(Keys.Alt | Keys.A, true)]
        [InlineData(Keys.A, false)]
        [InlineData(Keys.Shift | Keys.A, false)]
        public void IsValid(Keys k, bool expected)
        {
            Assert.Equal(expected, HotkeyText.IsValid(k));
        }

        [Fact]
        public void Register_None_SucceedsWithoutRegistering()
        {
            Assert.True(HotkeyText.Register(IntPtr.Zero, 0xBEE0, Keys.None));
        }

        [Theory]
        [InlineData(Keys.Control | Keys.Alt | Keys.Shift | Keys.F24)]
        [InlineData(Keys.F23)]
        public void Register_RealHotkey(Keys k)
        {
            // Unusual keys, so a combination the user has bound elsewhere is not in the way.
            const int id = 0xBEE1;
            Assert.True(HotkeyText.Register(IntPtr.Zero, id, k));
            try { Assert.False(HotkeyText.Register(IntPtr.Zero, id + 1, k)); } // taken now
            finally { Native.UnregisterHotKey(IntPtr.Zero, id); }
        }

        [Fact]
        public void NoRepeat_DependsOnWindows7()
        {
            Assert.Equal(Environment.OSVersion.Version >= new Version(6, 1), HotkeyText.NoRepeatSupported);
        }

        [Fact]
        public void Register_WithoutNoRepeat_AsOnXpAndVista()
        {
            bool original = HotkeyText.NoRepeatSupported;
            HotkeyText.NoRepeatSupported = false;
            const int id = 0xBEE3;
            try { Assert.True(HotkeyText.Register(IntPtr.Zero, id, Keys.Control | Keys.Alt | Keys.Shift | Keys.F22)); }
            finally { Native.UnregisterHotKey(IntPtr.Zero, id); HotkeyText.NoRepeatSupported = original; }
        }
    }

    public class HotkeyBoxTests
    {
        /// <summary>Exposes the protected key handler.</summary>
        class Box : HotkeyBox
        {
            public bool Press(Keys k) { var m = new Message(); return ProcessCmdKey(ref m, k); }
        }

        [Fact]
        public void KeysSetter_UpdatesTextAndRaisesEvent()
        {
            StaThread.Run(delegate
            {
                using (var b = new Box())
                {
                    int raised = 0;
                    b.KeysChanged += delegate { raised++; };
                    b.Keys = Keys.Control | Keys.Alt | Keys.X;
                    Assert.Equal("Strg+Alt+X", b.Text);
                    Assert.Equal(1, raised);
                    Assert.True(b.ReadOnly);
                }
            });
        }

        [Fact]
        public void KeysSetter_WithoutHandler()
        {
            StaThread.Run(delegate { using (var b = new Box()) b.Keys = Keys.F2; });
        }

        [Fact]
        public void Press_RecordsCombination()
        {
            StaThread.Run(delegate
            {
                using (var b = new Box())
                {
                    Assert.True(b.Press(Keys.Control | Keys.Alt | Keys.K));
                    Assert.Equal(Keys.Control | Keys.Alt | Keys.K, b.Keys);
                    Assert.True(b.Press(Keys.Shift | Keys.Tab)); // Tab with a modifier is a hotkey, not navigation
                    Assert.Equal(Keys.Shift | Keys.Tab, b.Keys);
                    Assert.True(b.Press(Keys.Control | Keys.Delete));
                    Assert.Equal(Keys.Control | Keys.Delete, b.Keys);
                }
            });
        }

        [Theory]
        [InlineData(Keys.Back)]
        [InlineData(Keys.Delete)]
        public void Press_BackOrDelete_Clears(Keys k)
        {
            StaThread.Run(delegate
            {
                using (var b = new Box { Keys = Keys.F3 })
                {
                    Assert.True(b.Press(k));
                    Assert.Equal(Keys.None, b.Keys);
                }
            });
        }

        [Theory]
        [InlineData(Keys.ControlKey | Keys.Control)]
        [InlineData(Keys.Menu | Keys.Alt)]
        [InlineData(Keys.ShiftKey | Keys.Shift)]
        [InlineData(Keys.LWin)]
        [InlineData(Keys.RWin)]
        public void Press_ModifierAlone_IsIgnored(Keys k)
        {
            StaThread.Run(delegate
            {
                using (var b = new Box { Keys = Keys.F3 })
                {
                    Assert.True(b.Press(k));
                    Assert.Equal(Keys.F3, b.Keys);
                }
            });
        }

        [Theory]
        [InlineData(Keys.Tab)]
        [InlineData(Keys.Escape)]
        public void Press_TabAndEscape_PassThrough(Keys k)
        {
            StaThread.Run(delegate
            {
                using (var b = new Box { Keys = Keys.F3 })
                {
                    Assert.False(b.Press(k)); // not handled: dialog navigation / cancel
                    Assert.Equal(Keys.F3, b.Keys);
                }
            });
        }
    }

    public class HotkeyDialogTests
    {
        static List<HotkeyBox> Boxes(Form f) { return f.Controls.OfType<HotkeyBox>().ToList(); }
        static Button ButtonOf(Form f, string text) { return f.Controls.OfType<Button>().Single(b => b.Text == text); }
        static Label Hint(Form f) { return f.Controls.OfType<Label>().Single(l => l.ForeColor == System.Drawing.Color.Firebrick); }
        static void Click(Button b) { typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(b, new object[] { EventArgs.Empty }); }

        [Fact]
        public void ShowsCurrentKeysAndIsValid()
        {
            StaThread.Run(delegate
            {
                using (var d = new HotkeyDialog(Settings.DefaultHotkeys()))
                {
                    var boxes = Boxes(d);
                    Assert.Equal(4, boxes.Count);
                    Assert.Equal(Keys.Control | Keys.Alt | Keys.S, boxes[0].Keys);
                    Assert.True(ButtonOf(d, "OK").Enabled);
                    Assert.Equal("", Hint(d).Text);
                    Assert.Same(ButtonOf(d, "OK"), d.AcceptButton);
                    Assert.Same(ButtonOf(d, "Abbrechen"), d.CancelButton);
                }
            });
        }

        [Fact]
        public void KeyWithoutModifier_IsRejected()
        {
            StaThread.Run(delegate
            {
                using (var d = new HotkeyDialog(Settings.DefaultHotkeys()))
                {
                    Boxes(d)[1].Keys = Keys.A;
                    Assert.False(ButtonOf(d, "OK").Enabled);
                    Assert.Equal("Vollbild ein/aus: bitte mit Strg oder Alt kombinieren (oder eine F-Taste).", Hint(d).Text);
                }
            });
        }

        [Fact]
        public void Duplicate_IsRejected_ButSeveralEmptyAreFine()
        {
            StaThread.Run(delegate
            {
                using (var d = new HotkeyDialog(Settings.DefaultHotkeys()))
                {
                    var boxes = Boxes(d);
                    boxes[2].Keys = boxes[0].Keys;
                    Assert.False(ButtonOf(d, "OK").Enabled);
                    Assert.Equal("Strg+Alt+S ist doppelt vergeben.", Hint(d).Text);

                    boxes[0].Keys = Keys.None;
                    boxes[2].Keys = Keys.None;
                    Assert.True(ButtonOf(d, "OK").Enabled);
                    Assert.Equal("", Hint(d).Text);
                }
            });
        }

        [Fact]
        public void Standard_RestoresDefaults()
        {
            StaThread.Run(delegate
            {
                var custom = Settings.DefaultHotkeys();
                custom[HotkeyAction.Mode] = Keys.F7;
                using (var d = new HotkeyDialog(custom))
                {
                    Assert.Equal(Keys.F7, Boxes(d)[2].Keys);
                    Click(ButtonOf(d, "Standard"));
                    Assert.Equal(Keys.Control | Keys.Alt | Keys.M, Boxes(d)[2].Keys);
                }
            });
        }

        [Fact]
        public void Ok_ReturnsEditedKeys()
        {
            StaThread.Run(delegate
            {
                using (var d = new HotkeyDialog(Settings.DefaultHotkeys()))
                {
                    Assert.Null(d.Result);
                    Boxes(d)[3].Keys = Keys.F8;
                    Click(ButtonOf(d, "OK"));
                    Assert.Equal(DialogResult.OK, d.DialogResult);
                    Assert.Equal(Keys.F8, d.Result[HotkeyAction.Toolbar]);
                    Assert.Equal(Keys.Control | Keys.Alt | Keys.S, d.Result[HotkeyAction.Release]);
                }
            });
        }
    }
}
