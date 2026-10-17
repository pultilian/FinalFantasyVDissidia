using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Dissidia.Common;

namespace DissidiaRosterEditor
{
    // Install (package folder -> game) or Update (older build in the game + package waiting), what is
    // installed, the save cleaner, and Uninstall (game -> package folder).
    public class InstallPage : UserControl
    {
        private readonly ModFiles files;
        private readonly Action<string, Color> status;
        private readonly Action afterChange;
        private TableLayoutPanel table; private Button installBtn, uninstallBtn; private Label installNote;
        private bool updateMode;

        public InstallPage(ModFiles files, Action<string, Color> status, Action afterChange)
        {
            this.files = files; this.status = status; this.afterChange = afterChange;
            Dock = DockStyle.Fill; BackColor = Theme.Bg; AutoScroll = true;
            Build();
            Refresh_();
        }

        private void Build()
        {
            var body = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(22, 14, 22, 20), BackColor = Theme.Bg };

            body.Controls.Add(Section("INSTALL"));
            body.Controls.Add(new Label { Text = "Moves the mod's files from the \"" + Installer.PackageFolderName + "\" folder into the game. Uninstall moves them back into that folder, so the mod can be installed again at any time. If an older build is already installed, the button reads Update: it replaces the old files and keeps your roster, unlock record and settings.", AutoSize = true, MaximumSize = new Size(820, 0), ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 8) });
            installNote = Note("", 0);
            body.Controls.Add(installNote);
            installBtn = Ui.Btn(Font, "Install FFV: Dissidia", true); installBtn.Margin = new Padding(0, 2, 0, 0);
            installBtn.Click += (s, e) => { if (updateMode) Update(); else Install(); };
            body.Controls.Add(installBtn);

            body.Controls.Add(Section("INSTALLED COMPONENTS", 18));
            body.Controls.Add(Note("Game folder: " + files.GameDir));
            table = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 4, 0, 8), BackColor = Theme.Surface, Padding = new Padding(8, 6, 8, 6) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            body.Controls.Add(table);
            body.Controls.Add(Note("BepInEx, Memoria and Magicite are separate projects the mod runs on. Install, Update and Uninstall never touch them."));

            var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 0), WrapContents = false };
            var refresh = Ui.Btn(Font, "Refresh", false); refresh.Click += (s, e) => Refresh_();
            var open = Ui.Btn(Font, "Open game folder", false); open.Margin = new Padding(10, 0, 0, 0); open.Click += (s, e) => { try { Process.Start("explorer.exe", files.GameDir); } catch { } };
            row.Controls.Add(refresh); row.Controls.Add(open);
            body.Controls.Add(row);

            body.Controls.Add(Section("SAVES", 18));
            body.Controls.Add(Note("Crystal Shards are key items in your saves. Without the mod installed, a save that still holds one will not load. Uninstall removes them from every save automatically; this button does the same on its own, for example before sharing a save with someone who does not have the mod. Each save is backed up to Documents first."));
            var cleanBtn = Ui.Btn(Font, "Remove Crystal Shards from saves...", false); cleanBtn.Margin = new Padding(0, 4, 0, 0);
            cleanBtn.Click += (s, e) => CleanSaves();
            body.Controls.Add(cleanBtn);

            body.Controls.Add(Section("UNINSTALL", 18));
            body.Controls.Add(new Label { Text = "Removes all Final Fantasy V: Dissidia components from the game. It will retain BepInEx, Memoria and Magicite.", AutoSize = true, MaximumSize = new Size(820, 0), ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 8) });
            body.Controls.Add(Note("The files are moved into the \"" + Installer.PackageFolderName + "\" folder, ready to be installed again; your roster and settings go with them. Crystal Shards are removed from your saves first, with the original saves copied to Documents.", 2));
            uninstallBtn = Ui.Btn(Font, "Uninstall FFV: Dissidia...", false);
            uninstallBtn.ForeColor = Theme.Error; uninstallBtn.Margin = new Padding(0, 6, 0, 0);
            uninstallBtn.Click += (s, e) => Uninstall();
            body.Controls.Add(uninstallBtn);
            Controls.Add(body);
        }

        public void Refresh_()
        {
            table.SuspendLayout();
            table.Controls.Clear(); table.RowStyles.Clear();
            var items = Installer.Status(files);
            int r = 0;
            foreach (var it in items)
            {
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
                table.Controls.Add(new Label { Text = it.Present ? "✓" : it.Optional ? "-" : "✗", AutoSize = true, ForeColor = it.Present ? Theme.Ok : it.Optional ? Theme.TextDim : Theme.Error, Font = new Font("Segoe UI Semibold", 10f), Margin = new Padding(0, 3, 0, 0) }, 0, r);
                table.Controls.Add(new Label { Text = it.Name, AutoSize = true, ForeColor = it.ThirdParty ? Theme.TextDim : Theme.Text, Margin = new Padding(0, 4, 0, 0) }, 1, r);
                table.Controls.Add(new Label { Text = it.Detail, AutoSize = true, ForeColor = Theme.TextDim, Margin = new Padding(0, 4, 0, 0) }, 2, r);
                r++;
            }
            table.ResumeLayout();
            bool gameDir = ModFiles.LooksLikeGameDir(files.GameDir);
            bool installed = gameDir && Installer.IsInstalled(files), partly = gameDir && Installer.PartlyInstalled(files), ready = gameDir && Installer.PackageReady(files);
            updateMode = ready && partly;
            installBtn.Text = updateMode ? "Update FFV: Dissidia" : "Install FFV: Dissidia";
            installBtn.Enabled = ready;
            uninstallBtn.Enabled = partly;
            if (!gameDir) { installNote.Text = "Choose the game folder (the one with FINAL FANTASY V.exe) at the top first."; installNote.ForeColor = Theme.Warn; }
            else if (updateMode) { installNote.Text = "A build of FFV: Dissidia is installed and another is waiting in the package folder. Update replaces the installed files with the package and keeps your roster, unlock record and settings."; installNote.ForeColor = Theme.Warn; }
            else if (installed) { installNote.Text = "FFV: Dissidia is installed."; installNote.ForeColor = Theme.Ok; }
            else if (ready) { installNote.Text = "Ready to install from: " + Installer.PackageDir(files); installNote.ForeColor = Theme.TextDim; }
            else if (Installer.PackagePartial(files)) { installNote.Text = "The package folder is incomplete: " + Installer.PackageDir(files) + ". Extract the download again."; installNote.ForeColor = Theme.Warn; }
            else { installNote.Text = "No mod package found. Extract the download into the game folder so that \"" + Installer.PackageFolderName + "\" sits next to this program, then click Refresh."; installNote.ForeColor = Theme.Warn; }
        }

        private void Install()
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                string log = Installer.Install(files);
                Cursor = Cursors.Default;
                status("FFV: Dissidia installed - start the game to play.", Theme.Ok);
                MessageBox.Show(this, log + "\nInstalled. Start the game to play with the default line-up, or use the Roster page to change who is who.", "Installed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                afterChange?.Invoke();
            }
            catch (UnauthorizedAccessException ex) { Cursor = Cursors.Default; MessageBox.Show(this, "Windows refused to write to the game folder:\n" + ex.Message + "\n\nRight-click the Mod Manager, choose 'Run as administrator' and try again.", "Install failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            catch (Exception ex) { Cursor = Cursors.Default; MessageBox.Show(this, ex.Message, "Install failed", MessageBoxButtons.OK, MessageBoxIcon.Error); Refresh_(); }
        }

        private void Update()
        {
            var confirm = MessageBox.Show(this,
                "Update FFV: Dissidia in\n" + files.GameDir + "?\n\nThe installed files are replaced by the build in the \"" + Installer.PackageFolderName + "\" folder. Your roster, unlock record and settings are kept, with copies saved to Documents. Saves are not touched. BepInEx, Memoria and Magicite stay.\n\nClose the game before continuing.",
                "Update FFV: Dissidia", MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
            if (confirm != DialogResult.OK) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                string log = Installer.Update(files, out string backup);
                Cursor = Cursors.Default;
                status("FFV: Dissidia updated - start the game to play.", Theme.Ok);
                MessageBox.Show(this, log + (backup != null ? "\nCopies of your previous roster and settings:\n" + backup : ""), "Updated", MessageBoxButtons.OK, MessageBoxIcon.Information);
                afterChange?.Invoke();
            }
            catch (UnauthorizedAccessException ex) { Cursor = Cursors.Default; MessageBox.Show(this, "Windows refused to write to the game folder:\n" + ex.Message + "\n\nRight-click the Mod Manager, choose 'Run as administrator' and try again.", "Update failed", MessageBoxButtons.OK, MessageBoxIcon.Error); Refresh_(); }
            catch (Exception ex) { Cursor = Cursors.Default; MessageBox.Show(this, ex.Message, "Update failed", MessageBoxButtons.OK, MessageBoxIcon.Error); Refresh_(); }
        }

        private void CleanSaves()
        {
            if (MessageBox.Show(this, "Remove every Crystal Shard key item from all FFV save files?\n\nEach save is copied to a backup folder in Documents before it is changed. Close the game before continuing.", "Remove Crystal Shards from saves", MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                string backup = Path.Combine(Installer.NewBackupDir(), "saves");
                var log = new List<string>();
                var r = SaveReader.RemoveForgedItemsEverywhere(backup, log);
                Cursor = Cursors.Default;
                string msg = r.Files == 0 ? "No save held Crystal Shards. Nothing was changed." : $"Removed {r.Items} Crystal Shard{(r.Items == 1 ? "" : "s")} from {r.Files} save{(r.Files == 1 ? "" : "s")}.\n\n{string.Join("\n", log)}\n\nOriginals copied to:\n{backup}";
                status(r.Files == 0 ? "No Crystal Shards found in any save." : $"Crystal Shards removed from {r.Files} save(s).", Theme.Ok);
                MessageBox.Show(this, msg, "Remove Crystal Shards from saves", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { Cursor = Cursors.Default; MessageBox.Show(this, ex.Message, "Could not clean the saves", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void Uninstall()
        {
            var confirm = MessageBox.Show(this,
                "Remove FFV: Dissidia from\n" + files.GameDir + "?\n\nThe mod's files are moved into the \"" + Installer.PackageFolderName + "\" folder next to the Mod Manager, so Install can put them back later. Crystal Shards are removed from your saves so they load without the mod; the original saves are copied to Documents first. BepInEx, Memoria and Magicite stay.\n\nClose the game before continuing.",
                "Uninstall FFV: Dissidia", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.OK) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                string log = Installer.Uninstall(files, out string backup);
                Cursor = Cursors.Default;
                status("FFV: Dissidia uninstalled - the files are in the \"" + Installer.PackageFolderName + "\" folder.", Theme.Ok);
                MessageBox.Show(this, log + (backup != null ? "\nSave backups:\n" + backup : ""), "Uninstalled", MessageBoxButtons.OK, MessageBoxIcon.Information);
                afterChange?.Invoke();
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MessageBox.Show(this, ex.Message, "Uninstall failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Refresh_();
            }
        }

        private static Label Section(string text, int top = 0) => new Label { Text = text, AutoSize = true, ForeColor = Theme.Accent, Font = new Font("Segoe UI Semibold", 9f), Margin = new Padding(0, top, 0, 6) };
        private static Label Note(string text, int top = 0) => new Label { Text = text, AutoSize = true, MaximumSize = new Size(820, 0), ForeColor = Theme.TextDim, Margin = new Padding(0, top, 0, 8) };
    }
}
