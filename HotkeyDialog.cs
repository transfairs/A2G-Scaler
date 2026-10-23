using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace A2GScaler
{
    /// <summary>Read-only text box that records the key combination pressed into it.</summary>
    class HotkeyBox : TextBox
    {
        Keys keys;
        public event EventHandler KeysChanged;

        public HotkeyBox() { ReadOnly = true; BackColor = SystemColors.Window; ShortcutsEnabled = false; }

        public Keys Keys
        {
            get { return keys; }
            set { keys = value; Text = HotkeyText.Format(value); if (KeysChanged != null) KeysChanged(this, EventArgs.Empty); }
        }

        /// <summary>Records the pressed combination. Back/Delete clear it, lone modifiers are ignored, plain Tab and Esc keep their dialog meaning.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            Keys mods = keyData & Keys.Modifiers;
            if (code == Keys.Tab && mods == Keys.None) return base.ProcessCmdKey(ref msg, keyData);
            if (code == Keys.Escape && mods == Keys.None) return base.ProcessCmdKey(ref msg, keyData);
            if ((code == Keys.Back || code == Keys.Delete) && mods == Keys.None) { Keys = Keys.None; return true; }
            if (code == Keys.ControlKey || code == Keys.Menu || code == Keys.ShiftKey || code == Keys.LWin || code == Keys.RWin) return true;
            Keys = keyData;
            return true;
        }
    }

    /// <summary>Lets the user change the global hotkeys.</summary>
    class HotkeyDialog : Form
    {
        static readonly string[] Labels = { "Maus aus dem Spiel lösen", "Vollbild ein/aus", "Nächster Skalierungsmodus", "Werkzeugleiste ein/aus" };

        readonly Dictionary<HotkeyAction, HotkeyBox> boxes = new Dictionary<HotkeyAction, HotkeyBox>();
        readonly Label hint = new Label();
        readonly Button ok = new Button();

        public Dictionary<HotkeyAction, Keys> Result { get; private set; }

        /// <summary>Builds one input box per action, pre-filled with <paramref name="current"/>.</summary>
        public HotkeyDialog(Dictionary<HotkeyAction, Keys> current)
        {
            Text = "Tastenkürzel";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(440, 250);

            var intro = new Label { Text = "In ein Feld klicken und die neue Tastenkombination drücken. Entf löscht ein Kürzel.\nDie Kürzel gelten global, also auch während das Spiel die Tastatur hat.", AutoSize = false };
            intro.SetBounds(12, 10, 416, 40);
            Controls.Add(intro);

            int y = 58;
            foreach (HotkeyAction a in Enum.GetValues(typeof(HotkeyAction)))
            {
                var label = new Label { Text = Labels[(int)a], AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
                label.SetBounds(12, y, 190, 24);
                var box = new HotkeyBox();
                box.SetBounds(208, y, 220, 24);
                box.Keys = current[a];
                box.KeysChanged += delegate { Validate(); };
                Controls.Add(label); Controls.Add(box);
                boxes[a] = box;
                y += 32;
            }

            hint.SetBounds(12, y + 2, 416, 20);
            hint.ForeColor = Color.Firebrick;
            Controls.Add(hint);

            var defaults = new Button { Text = "Standard" };
            defaults.SetBounds(12, 210, 100, 28);
            defaults.Click += delegate
            {
                var d = Settings.DefaultHotkeys();
                foreach (var kv in boxes) kv.Value.Keys = d[kv.Key];
            };
            ok.Text = "OK";
            ok.SetBounds(240, 210, 90, 28);
            ok.Click += delegate
            {
                Result = new Dictionary<HotkeyAction, Keys>();
                foreach (var kv in boxes) Result[kv.Key] = kv.Value.Keys;
                DialogResult = DialogResult.OK;
            };
            var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel };
            cancel.SetBounds(338, 210, 90, 28);
            Controls.Add(defaults); Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            Validate();
        }

        /// <summary>Disables OK and shows the first problem: a key without Ctrl/Alt (except F-keys) or a combination used twice.</summary>
        new void Validate()
        {
            string problem = null;
            var seen = new Dictionary<Keys, HotkeyAction>();
            foreach (var kv in boxes)
            {
                Keys k = kv.Value.Keys;
                if (!HotkeyText.IsValid(k)) { problem = Labels[(int)kv.Key] + ": bitte mit Strg oder Alt kombinieren (oder eine F-Taste)."; break; }
                if ((k & Keys.KeyCode) == Keys.None) continue;
                HotkeyAction other;
                if (seen.TryGetValue(k, out other)) { problem = HotkeyText.Format(k) + " ist doppelt vergeben."; break; }
                seen[k] = kv.Key;
            }
            hint.Text = problem ?? "";
            ok.Enabled = problem == null;
        }
    }
}
