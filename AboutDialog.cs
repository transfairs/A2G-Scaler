using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace A2GScaler
{
    /// <summary>Version, project links and a link to the folder holding settings and log.</summary>
    class AboutDialog : Form
    {
        const string GithubUrl = "https://github.com/transfairs/A2G-Scaler";
        const string TrainerUrl = "https://github.com/transfairs/A2G-Trainer";

        readonly ToolTip tip = new ToolTip();

        /// <summary>Opens a link target (URL or folder) with the shell; tests replace it to avoid opening a browser.</summary>
        internal static Action<string> Open = delegate (string target) { Process.Start(target); };

        /// <summary>The 64 px frame of the embedded logo (the exe icon only yields 32 px); null if unreadable.</summary>
        static Bitmap LoadLogo()
        {
            try
            {
                using (var stream = typeof(AboutDialog).Assembly.GetManifestResourceStream("A2GScaler.Logo.ico"))
                using (var icon = new Icon(stream, 64, 64))
                    return icon.ToBitmap();
            }
            catch (Exception) { return null; }
        }

        /// <summary>Builds the dialog for <paramref name="appName"/> in <paramref name="version"/> (shown as "v" + version).</summary>
        public AboutDialog(string appName, string version)
        {
            Text = "Über " + appName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(460, 228);

            int x = 12;
            Bitmap logo = LoadLogo();
            if (logo != null)
            {
                var picture = new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.CenterImage };
                picture.SetBounds(12, 12, 64, 64);
                Controls.Add(picture);
                x = 88;
            }
            int w = ClientSize.Width - x - 12;

            var title = new Label { Text = appName, Font = new Font(Font.FontFamily, Font.Size * 1.5f, FontStyle.Bold), AutoSize = false };
            title.SetBounds(x, 10, w - 110, 30);
            var versionLabel = new Label { Text = "v" + version, AutoSize = false, TextAlign = ContentAlignment.MiddleRight, ForeColor = SystemColors.GrayText };
            versionLabel.SetBounds(ClientSize.Width - 122, 14, 110, 22);
            var description = new Label { Text = "Skaliert Anstoss 2 Gold (640×480) auf beliebige Fenster- und Bildschirmgrößen und leitet die Maus ins Spiel weiter.", AutoSize = false };
            description.SetBounds(x, 44, w, 36);
            Controls.Add(title); Controls.Add(versionLabel); Controls.Add(description);

            AddLink(x, 88, w, "GitHub: ", GithubUrl, GithubUrl, GithubUrl);
            AddLink(x, 112, w, "Schwesterprojekt: ", "A2G-Trainer", TrainerUrl, TrainerUrl);
            AddLink(x, 144, w, "Einstellungen und Protokoll:\n", KeyValueFile.Folder, KeyValueFile.Folder, "Ordner im Explorer öffnen");

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
            ok.SetBounds(ClientSize.Width - 102, 188, 90, 28);
            Controls.Add(ok);
            AcceptButton = CancelButton = ok;
        }

        /// <summary>Label "prefix + linkText" where only linkText is clickable and opens target.</summary>
        void AddLink(int x, int y, int w, string prefix, string linkText, string target, string tooltip)
        {
            var link = new LinkLabel { Text = prefix + linkText, AutoSize = false, AutoEllipsis = true, LinkArea = new LinkArea(prefix.Length, linkText.Length) };
            link.SetBounds(x, y, w, prefix.EndsWith("\n") ? 36 : 20);
            link.LinkClicked += delegate
            {
                try { Open(target); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            tip.SetToolTip(link, tooltip);
            Controls.Add(link);
        }

        /// <summary>Disposes the tooltip component along with the form.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
