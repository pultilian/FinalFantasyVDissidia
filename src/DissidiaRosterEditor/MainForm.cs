using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Dissidia.Common;

namespace DissidiaRosterEditor
{
    // FFV: Dissidia Mod Manager - roster, limit settings, limit list, hero unlocks and install/uninstall.
    public class MainForm : Form
    {
        private static readonly string[] GameOrder =
        {
            "FF I", "FF II", "FF III", "FF IV", "FF V", "FF VI", "FF VII", "FF VIII", "FF IX", "FF X", "FF X-2",
            "FF XI", "FF XII", "FF XIII", "FF XIII-2", "FF XIV", "FF XV", "FF XVI",
            "FF Tactics", "FF Type-0", "FF Resonance", "Mobius FF", "World of FF", "Kingdom Hearts",
        };
        private static readonly (string Key, string Title)[] Pages =
        {
            ("roster", "Roster"), ("limits", "Limit Breaks"), ("limitlist", "Limit List"), ("unlocks", "Unlocks"), ("install", "Install / Uninstall"),
        };
        private const string RosterDescription =
            "Bartz and party have the ability to use Crystal Shards which allow them to take on the persona of Final Fantasy characters from across " +
            "the series. Here you can choose what Crystal Shard to assign to each Job. This in effect allows you to play as that character. If playing " +
            "with Crystal Shard unlocks, you must unlock the job, or buy the Crystal Shards within the game before you can select them here. " +
            "The Roster can be changed for any save and at any time.";

        private ModFiles files;
        private List<Hero> heroes = new List<Hero>();
        private List<string> games = new List<string>();
        private Dictionary<string, List<Hero>> heroesByGame = new Dictionary<string, List<Hero>>();
        private readonly Dictionary<(string, int), SlotCard> cells = new Dictionary<(string, int), SlotCard>();
        private readonly Dictionary<string, Image> thumbs = new Dictionary<string, Image>(), bigThumbs = new Dictionary<string, Image>();

        private TextBox gameDirBox; private Label status, dupes;
        private Panel gridHost, pages, rosterPage; private TableLayoutPanel grid;
        private Button applyBtn, resetBtn, reloadBtn, exportBtn, importBtn;
        private readonly Dictionary<string, Button> navButtons = new Dictionary<string, Button>();
        private LimitsPage limitsPage; private LimitListPage limitListPage; private UnlocksPage unlocksPage; private InstallPage installPage;
        private string currentPage = "roster";
        private bool installOk;

        public MainForm()
        {
            Native.EnableAppDarkMode();
            Text = "Final Fantasy V: Dissidia - Mod Manager";
            Width = 1400; Height = 900; MinimumSize = new Size(1040, 640);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Theme.Bg; ForeColor = Theme.Text;
            DoubleBuffered = true;
            BuildChrome();
            Shown += (s, e) => { gameDirBox.Text = Settings.FindGameDir(); LoadInstall(); };
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.DarkTitleBar(Handle); }

        private static Image LoadLogo()
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png"))
                    if (s != null) return Image.FromStream(s);
            }
            catch { }
            return null;
        }

        // ---------- chrome ----------
        private void BuildChrome()
        {
            var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 84, ColumnCount = 4, BackColor = Theme.Surface, Padding = new Padding(16, 8, 16, 8) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var logo = LoadLogo();
            var logoBox = new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Height = 64, Width = logo != null ? (int)(64.0 * logo.Width / logo.Height) : 150, Margin = new Padding(0, 2, 28, 0), BackColor = Color.Transparent };
            top.Controls.Add(logoBox, 0, 0);

            top.Controls.Add(new Label { Text = "Game folder", AutoSize = true, ForeColor = Theme.TextDim, Margin = new Padding(0, 24, 8, 0) }, 1, 0);
            var boxFrame = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Border, Padding = new Padding(1), Margin = new Padding(0, 20, 10, 20) };
            gameDirBox = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text, Font = new Font("Segoe UI", 9.5f) };
            gameDirBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; LoadInstall(); } };
            var inner = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Input, Padding = new Padding(8, 5, 8, 0) };
            inner.Controls.Add(gameDirBox); boxFrame.Controls.Add(inner);
            top.Controls.Add(boxFrame, 2, 0);
            var browse = Ui.Btn(Font, "Browse", false); browse.Margin = new Padding(0, 18, 0, 0); browse.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            browse.Click += (s, e) => Browse();
            top.Controls.Add(browse, 3, 0);
            Controls.Add(top);

            // status bar across the bottom of the window
            var statusBar = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Theme.Surface, Padding = new Padding(16, 6, 16, 0) };
            status = new Label { AutoSize = true, ForeColor = Theme.TextDim };
            statusBar.Controls.Add(status);
            Controls.Add(statusBar);

            // left navigation
            var nav = new Panel { Dock = DockStyle.Left, Width = 176, BackColor = Theme.Surface, Padding = new Padding(12, 14, 12, 12) };
            var navFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            foreach (var p in Pages)
            {
                var b = Ui.Btn(Font, p.Title, false);
                b.AutoSize = false; b.Width = 150; b.Height = 36; b.TextAlign = ContentAlignment.MiddleLeft; b.Margin = new Padding(0, 0, 0, 6);
                b.Padding = new Padding(10, 0, 0, 0);
                string key = p.Key;
                b.Click += (s, e) => ShowPage(key);
                navButtons[key] = b; navFlow.Controls.Add(b);
            }
            nav.Controls.Add(navFlow);
            Controls.Add(nav);

            // page host
            pages = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            Controls.Add(pages);
            pages.BringToFront();

            // roster page: description + action bar + duplicates line + grid
            rosterPage = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 60, ColumnCount = 1, BackColor = Theme.SurfaceAlt, Padding = new Padding(16, 10, 16, 10) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
            applyBtn = Ui.Btn(Font, "Apply to game", true); applyBtn.Enabled = false; applyBtn.Click += (s, e) => Apply();
            resetBtn = Ui.Btn(Font, "Reset to default", false); resetBtn.Enabled = false; resetBtn.Margin = new Padding(10, 0, 0, 0);
            resetBtn.Click += (s, e) => { FillFrom(files.LoadRoster(true)); SetStatus("Default roster loaded - not applied yet.", Theme.TextDim); };
            reloadBtn = Ui.Btn(Font, "Reload saved", false); reloadBtn.Enabled = false; reloadBtn.Margin = new Padding(10, 0, 0, 0);
            reloadBtn.Click += (s, e) => { FillFrom(files.LoadRoster(false)); SetStatus("Saved roster reloaded.", Theme.TextDim); };
            exportBtn = Ui.Btn(Font, "Export Roster", false); exportBtn.Enabled = false; exportBtn.Margin = new Padding(26, 0, 0, 0); exportBtn.Click += (s, e) => ExportRoster();
            importBtn = Ui.Btn(Font, "Import Roster", false); importBtn.Enabled = false; importBtn.Margin = new Padding(10, 0, 0, 0); importBtn.Click += (s, e) => ImportRoster();
            actions.Controls.AddRange(new Control[] { applyBtn, resetBtn, reloadBtn, exportBtn, importBtn });
            bottom.Controls.Add(actions, 0, 0);
            rosterPage.Controls.Add(bottom);

            dupes = new Label { Dock = DockStyle.Bottom, Height = 24, Padding = new Padding(18, 4, 0, 0), ForeColor = Theme.Warn, BackColor = Theme.Bg };
            rosterPage.Controls.Add(dupes);

            gridHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16, 8, 16, 8), BackColor = Theme.Bg };
            gridHost.HandleCreated += (s, e) => Native.DarkScrollbars(gridHost);
            rosterPage.Controls.Add(gridHost);
            gridHost.BringToFront();

            var desc = new Label { Text = RosterDescription, Dock = DockStyle.Top, AutoSize = false, Height = 70, Padding = new Padding(18, 10, 24, 0), ForeColor = Theme.Text, BackColor = Theme.Bg };
            rosterPage.Controls.Add(desc);
            pages.Controls.Add(rosterPage);
            ShowPage("roster");
        }

        public void ShowPage(string key)
        {
            if (!navButtons.ContainsKey(key)) key = "roster";
            if (!installOk && key != "install" && files != null) key = "install";
            currentPage = key;
            rosterPage.Visible = key == "roster";
            if (limitsPage != null) limitsPage.Visible = key == "limits";
            if (limitListPage != null) limitListPage.Visible = key == "limitlist";
            if (unlocksPage != null) unlocksPage.Visible = key == "unlocks";
            if (installPage != null) installPage.Visible = key == "install";
            foreach (var kv in navButtons)
            {
                bool on = kv.Key == key;
                kv.Value.BackColor = on ? Theme.Highlight : Theme.Input;
                kv.Value.ForeColor = on ? Theme.Accent : Theme.Text;
                kv.Value.FlatAppearance.BorderColor = on ? Theme.Accent : Theme.Border;
            }
            if (key == "unlocks") unlocksPage?.RefreshLocked();
            if (key == "install") installPage?.Refresh_();
        }

        // ---------- grid ----------
        private void BuildGrid()
        {
            gridHost.SuspendLayout();
            gridHost.Controls.Clear(); cells.Clear();
            grid = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = ModLayout.Columns.Length + 1, RowCount = ModLayout.Jobs.Length + 1,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None, Location = new Point(0, 0), Margin = new Padding(0), BackColor = Theme.Bg,
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            for (int i = 0; i < ModLayout.Columns.Length; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 254));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            for (int r = 0; r < ModLayout.Jobs.Length; r++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));

            grid.Controls.Add(Header("JOB", ContentAlignment.MiddleLeft), 0, 0);
            for (int c = 0; c < ModLayout.Columns.Length; c++) grid.Controls.Add(Header(ModLayout.Columns[c].Label.ToUpper(), ContentAlignment.MiddleCenter), c + 1, 0);

            for (int r = 0; r < ModLayout.Jobs.Length; r++)
            {
                var job = ModLayout.Jobs[r];
                Color rowBg = r % 2 == 0 ? Theme.Surface : Theme.SurfaceAlt;
                grid.Controls.Add(new Label { Text = job.Name, Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(14, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft, BackColor = rowBg, ForeColor = Theme.Text, Font = new Font("Segoe UI Semibold", 9.5f) }, 0, r + 1);
                for (int c = 0; c < ModLayout.Columns.Length; c++)
                {
                    var col = ModLayout.Columns[c];
                    var card = new SlotCard(rowBg) { Dock = DockStyle.Fill, Margin = new Padding(0) };
                    var key = (col.Key, job.Id);
                    string title = $"{col.Label} - {job.Name}";
                    card.Clicked += () => Pick(key, title);
                    card.Menu += (cd, at) =>
                    {
                        var menu = new DarkMenu();
                        if (cd.Hero != null) menu.Add("Limit Break: " + cd.Hero.LimitName, () => DarkTip.Show(cd, cd.Hero.LimitText(), at));
                        if (cd.Hero != null) menu.Add("Where to get " + cd.Hero.Name, () => ShowWhere(cd, cd.Hero, at));
                        menu.Add("Change hero...", () => Pick(key, title));
                        if (cd.Hero != null) menu.Add("Use vanilla sprite", () => { cd.Set(null, null); UpdateDupes(); SetStatus($"{title}: vanilla", Theme.TextDim); });
                        menu.Show(cd, at);
                    };
                    grid.Controls.Add(card, c + 1, r + 1);
                    cells[key] = card;
                }
            }
            gridHost.Controls.Add(grid);
            gridHost.ResumeLayout();
        }

        private Label Header(string text, ContentAlignment align) => new Label
        {
            Text = text, Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(align == ContentAlignment.MiddleLeft ? 14 : 0, 0, 0, 0),
            TextAlign = align, BackColor = Theme.Header, ForeColor = Theme.Accent, Font = new Font("Segoe UI Semibold", 9f),
        };

        private void Pick((string, int) key, string slotTitle)
        {
            var card = cells[key];
            using (var dlg = new HeroPickerForm(files, heroesByGame, games, card.Hero, slotTitle, k => Thumb(k, 96, bigThumbs)))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                card.Set(dlg.Selected, Thumb(dlg.Selected?.Key, 64, thumbs));
                UpdateDupes();
                SetStatus(dlg.Selected == null ? $"{slotTitle}: vanilla" : $"{slotTitle}: {dlg.Selected.Name}", Theme.TextDim);
            }
        }

        private Image Thumb(string heroKey, int size, Dictionary<string, Image> cache)
        {
            if (string.IsNullOrEmpty(heroKey)) return null;
            if (cache.TryGetValue(heroKey, out var img)) return img;
            try
            {
                string path = files.HeroPng(heroKey, "normal", "Default_00");
                using (var src = Image.FromStream(new MemoryStream(File.ReadAllBytes(path))))
                {
                    var bmp = new Bitmap(size, size);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Theme.Thumb);
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(src, new Rectangle(0, 0, size, size));
                    }
                    cache[heroKey] = bmp; return bmp;
                }
            }
            catch { cache[heroKey] = null; return null; }
        }

        // Headless screenshots: point the window at another game folder before rendering.
        public void SetGameDir(string dir) { gameDirBox.Text = dir; LoadInstall(); }

        // ---------- data ----------
        private void LoadInstall()
        {
            SetStatus("", Theme.TextDim);
            applyBtn.Enabled = resetBtn.Enabled = reloadBtn.Enabled = exportBtn.Enabled = importBtn.Enabled = false;
            files = new ModFiles(gameDirBox.Text.Trim());
            foreach (var page in new Control[] { limitsPage, limitListPage, unlocksPage, installPage }) if (page != null) { pages.Controls.Remove(page); page.Dispose(); }
            limitsPage = null; limitListPage = null; unlocksPage = null; installPage = null;

            installPage = new InstallPage(files, SetStatus, () => LoadInstall());
            pages.Controls.Add(installPage);
            string problem = files.Missing();
            installOk = problem == null;
            foreach (var kv in navButtons) kv.Value.Enabled = installOk || kv.Key == "install";
            if (!installOk)
            {
                gridHost.Controls.Clear();
                ShowPage("install");
                if (ModFiles.LooksLikeGameDir(files.GameDir) && Installer.PackageReady(files)) { SetStatus("FFV: Dissidia is not installed yet - click Install.", Theme.Warn); Settings.SaveGameDir(files.GameDir); }
                else SetStatus(problem, Theme.Error);
                return;
            }
            try
            {
                heroes = files.LoadHeroes(); thumbs.Clear(); bigThumbs.Clear();
                foreach (var h in heroes) if (string.IsNullOrWhiteSpace(h.Game)) h.Game = "Other";
                heroesByGame = heroes.GroupBy(h => h.Game).ToDictionary(g => g.Key, g => g.OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase).ToList());
                games = heroesByGame.Keys.OrderBy(g => { int i = Array.IndexOf(GameOrder, g); return i < 0 ? 999 : i; }).ThenBy(g => g).ToList();
                BuildGrid();
                FillFrom(files.LoadRoster(false));
                Settings.SaveGameDir(files.GameDir);
                limitsPage = new LimitsPage(files, SetStatus);
                limitListPage = new LimitListPage(heroesByGame, games);
                unlocksPage = new UnlocksPage(() => ModFiles.UnlockAll, SetUnlockAll, () => ModFiles.ShardSource, SetShardSource, () => files.LoadSaves(true), () => files.ShardSourceInfo,
                                              () => files.CountLocked(heroes), files.CountShopShardHeroes(heroes), heroes);
                pages.Controls.Add(limitsPage); pages.Controls.Add(limitListPage); pages.Controls.Add(unlocksPage);
                applyBtn.Enabled = resetBtn.Enabled = reloadBtn.Enabled = exportBtn.Enabled = importBtn.Enabled = true;
                ShowPage(currentPage);
                if (Installer.UpdatePending(files)) SetStatus("A new build of FFV: Dissidia is waiting in the \"" + Installer.PackageFolderName + "\" folder - open Install / Uninstall and click Update.", Theme.Warn);
                else SetStatus(HeroSummary(), Theme.TextDim);
            }
            catch (Exception ex) { SetStatus("Could not load: " + ex.Message, Theme.Error); }
        }

        private void FillFrom(Dictionary<(string, int), string> roster)
        {
            var byKey = heroes.ToDictionary(h => h.Key);
            foreach (var kv in cells)
            {
                roster.TryGetValue(kv.Key, out string heroKey);
                byKey.TryGetValue(heroKey ?? "", out Hero hero);
                kv.Value.Set(hero, Thumb(hero?.Key, 64, thumbs));
            }
            UpdateDupes();
        }

        private Dictionary<(string, int), Hero> Current()
        {
            var a = new Dictionary<(string, int), Hero>();
            foreach (var kv in cells) if (kv.Value.Hero != null) a[kv.Key] = kv.Value.Hero;
            return a;
        }

        private void UpdateDupes()
        {
            var used = Current().Values.GroupBy(h => h.Key).Where(g => g.Count() > 1).Select(g => $"{g.First().Name} x{g.Count()}").ToList();
            dupes.Text = used.Count == 0 ? "" : "Used in more than one slot: " + string.Join(", ", used);
        }

        private void Apply()
        {
            var a = Current();
            if (MessageBox.Show(this, $"Write {a.Count} hero assignments to the game?\n\nThis updates the sprite folders, DissidiaNameSwap.csv and DissidiaLimit.csv (originals are kept as .bak the first time).", "Apply roster", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                string result = files.Apply(a);
                Cursor = Cursors.Default;
                SetStatus("Applied - restart the game to see the changes", Theme.Ok);
                MessageBox.Show(this, result, "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (UnauthorizedAccessException ex)
            {
                Cursor = Cursors.Default;
                SetStatus("Apply failed: no write permission", Theme.Error);
                MessageBox.Show(this, "Windows refused to write to the game folder:\n" + ex.Message + "\n\nRight-click the Mod Manager and choose 'Run as administrator', then Apply again.", "Apply failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                SetStatus("Apply failed: " + ex.Message, Theme.Error);
                MessageBox.Show(this, ex.Message, "Apply failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Browse()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Select the FINAL FANTASY V PR game folder (contains FINAL FANTASY V.exe)", SelectedPath = gameDirBox.Text })
                if (dlg.ShowDialog(this) == DialogResult.OK) { gameDirBox.Text = dlg.SelectedPath; LoadInstall(); }
        }

        private void SetStatus(string text, Color color) { status.Text = text; status.ForeColor = color; }

        private void ShowWhere(Control at, Hero hero, Point point)
        {
            string src = string.IsNullOrEmpty(files.ShardSourceInfo) ? "this install" : files.ShardSourceInfo;
            DarkTip.Show(at, hero.WhereText(src, ModFiles.UnlockAll), point);
        }

        private string HeroSummary()
        {
            int locked = files.CountLocked(heroes), shards = files.CountShardHeroes(heroes);
            string tail = shards == 0 ? ""
                        : ModFiles.UnlockAll ? " - all heroes unlocked (Unlocks page)"
                        : $" - {locked} locked, unlocks read from {files.ShardSourceInfo}";
            return $"{heroes.Count} heroes from {games.Count} games{tail}";
        }

        // ---------- unlock settings ----------
        private void SetUnlockAll(bool on)
        {
            if (ModFiles.UnlockAll == on) return;
            ModFiles.UnlockAll = on; Settings.UnlockAll = on;
            RelockAll();
        }

        private void SetShardSource(string source)
        {
            ModFiles.ShardSource = source; Settings.Set("shard_source", source);
            RelockAll();
        }

        private void RelockAll()
        {
            files.RefreshLocks(heroes);
            foreach (var c in cells.Values) c.Invalidate();
            SetStatus(HeroSummary(), Theme.TextDim);
        }

        // ---------- sharing ----------
        private void ExportRoster()
        {
            using (var dlg = new SaveFileDialog { Title = "Export roster", Filter = "FFV Dissidia roster (*.csv)|*.csv", FileName = "FFV-Dissidia-roster.csv", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { files.ExportRoster(dlg.FileName, Current()); SetStatus("Roster exported to " + Path.GetFileName(dlg.FileName), Theme.Ok); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void ImportRoster()
        {
            using (var dlg = new OpenFileDialog { Title = "Import roster", Filter = "FFV Dissidia roster (*.csv)|*.csv|All files (*.*)|*.*", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var map = files.ImportRoster(dlg.FileName, heroes, out var unknown, out int rows);
                    if (rows == 0) { MessageBox.Show(this, "No roster rows found in this file.", "Import roster", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    FillFrom(map);
                    int locked = Current().Values.Count(h => h.Locked);
                    string msg = $"Loaded {map.Count} slots from {Path.GetFileName(dlg.FileName)} - not applied yet.";
                    if (unknown.Count > 0) msg += $"\n\n{unknown.Count} heroes are not in your hero library and were skipped: {string.Join(", ", unknown.Distinct().Take(12))}" + (unknown.Distinct().Count() > 12 ? ", ..." : "");
                    if (locked > 0) msg += $"\n\n{locked} slots use heroes you have not unlocked yet. Find their Crystal Shards, or choose \"Unlock all heroes\" on the Unlocks page, before applying.";
                    SetStatus($"Roster loaded from {Path.GetFileName(dlg.FileName)} - not applied yet.", Theme.TextDim);
                    if (unknown.Count > 0 || locked > 0) MessageBox.Show(this, msg, "Import roster", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        // Screenshot hook: a picker for the first slot, optionally opened on a given game.
        public HeroPickerForm DebugPicker(string game)
        {
            var first = cells.First();
            var hero = first.Value.Hero;
            if (game != null && heroesByGame.TryGetValue(game, out var list) && list.Count > 0) hero = list[0];
            return new HeroPickerForm(files, heroesByGame, games, hero, "Bartz - Knight", k => Thumb(k, 96, bigThumbs));
        }
    }

    // A grid slot: sprite + hero name + game + limit break; click to change.
    internal class SlotCard : Control
    {
        public Hero Hero; private Image thumb; private bool hover;
        public event Action Clicked;
        public event Action<SlotCard, Point> Menu;
        private readonly Color bg;
        private static readonly Font NameFont = new Font("Segoe UI Semibold", 9.5f), SmallFont = new Font("Segoe UI", 8.5f);

        public SlotCard(Color rowBg)
        {
            bg = rowBg; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            MouseEnter += (s, e) => { hover = true; Invalidate(); };
            MouseLeave += (s, e) => { hover = false; Invalidate(); };
            MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Clicked?.Invoke(); else if (e.Button == MouseButtons.Right) Menu?.Invoke(this, e.Location); };
        }

        public void Set(Hero hero, Image image) { Hero = hero; thumb = image; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(hover ? Theme.Highlight : bg);
            var box = new Rectangle(12, 9, 66, 66);
            using (var b = new SolidBrush(Theme.Border)) g.FillRectangle(b, box);
            using (var b = new SolidBrush(Theme.Thumb)) g.FillRectangle(b, box.X + 1, box.Y + 1, 64, 64);
            if (thumb != null) { g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half; g.DrawImage(thumb, new Rectangle(box.X + 1, box.Y + 1, 64, 64)); }
            int x = 90, w = Width - 100;
            if (Hero == null)
            {
                TextRenderer.DrawText(g, "Vanilla", NameFont, new Rectangle(x, 18, w, 20), Theme.TextDim, TextFormatFlags.Left | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, "original FFV sprite", SmallFont, new Rectangle(x, 40, w, 18), Theme.TextOff, TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            }
            else
            {
                TextRenderer.DrawText(g, Hero.Name, NameFont, new Rectangle(x, 12, w, 20), Theme.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, Hero.Game, SmallFont, new Rectangle(x, 33, w, 18), Theme.TextDim, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, Hero.Locked ? (Hero.ShardJob > 0 ? "LOCKED - needs the " + (ModLayout.Jobs.FirstOrDefault(j => j.Id == Hero.ShardJob)?.Name ?? "job") + " job" : "LOCKED - needs its Crystal Shard") : Hero.LimitName, SmallFont, new Rectangle(x, 52, w, 18), Hero.Locked ? Theme.Warn : Theme.Accent, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            if (hover) using (var p = new Pen(Theme.Accent)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    // %AppData%\FFVDissidia\settings.txt: key=value per line (gamedir, unlock_all, shard_source)
    public static class Settings
    {
        private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FFVDissidia");
        private static string File_ => Path.Combine(Dir, "settings.txt");
        private static string LegacyGameDirFile => Path.Combine(Dir, "gamedir.txt");

        private static Dictionary<string, string> Load()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(File_))
                    foreach (string line in File.ReadAllLines(File_))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                    }
                else if (File.Exists(LegacyGameDirFile)) d["gamedir"] = File.ReadAllText(LegacyGameDirFile).Trim();
            }
            catch { }
            return d;
        }

        public static string Get(string key, string fallback = "") => Load().TryGetValue(key, out string v) ? v : fallback;

        public static void Set(string key, string value)
        {
            try
            {
                var d = Load(); d[key] = value;
                Directory.CreateDirectory(Dir);
                File.WriteAllLines(File_, d.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value), new System.Text.UTF8Encoding(false));
            }
            catch { }
        }

        public static bool UnlockAll { get => Get("unlock_all") == "1"; set => Set("unlock_all", value ? "1" : "0"); }

        public static string FindGameDir()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            if (ModFiles.LooksLikeGameDir(exeDir)) return exeDir;
            try { string s = Get("gamedir"); if (ModFiles.LooksLikeGameDir(s)) return s; } catch { }
            foreach (string guess in new[] {
                @"C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY V PR",
                @"C:\Program Files\Steam\steamapps\common\FINAL FANTASY V PR",
                @"D:\Steam\steamapps\common\FINAL FANTASY V PR", @"D:\SteamLibrary\steamapps\common\FINAL FANTASY V PR" })
                if (ModFiles.LooksLikeGameDir(guess)) return guess;
            return exeDir;
        }

        public static void SaveGameDir(string dir) => Set("gamedir", dir);
    }
}
