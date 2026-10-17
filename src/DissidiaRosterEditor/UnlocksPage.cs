using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Dissidia.Common;

namespace DissidiaRosterEditor
{
    // How heroes unlock: Crystal Shards and jobs read from a chosen save, or everything at once;
    // below that, where every Crystal Shard is sold, town by town.
    public class UnlocksPage : UserControl
    {
        private class SourceItem { public string Value, Text; public override string ToString() => Text; }
        private class ShopRow { public string Town, Shop, Hero, Game, Note; public int Price, TownOrder; }

        // Story order of the towns that sell shards (anything unknown sorts by price after them).
        private static readonly string[] TownOrder = { "Tule", "Carwen", "Walse", "Karnak", "Jachol", "Crescent", "Lix", "Istory", "Regole", "Quelb", "Surgate Castle", "Moore", "Phantom Village" };
        private const string AllTowns = "All towns";

        private readonly Func<bool> getUnlockAll; private readonly Action<bool> setUnlockAll;
        private readonly Func<string> getSource; private readonly Action<string> setSource;
        private readonly Func<List<SaveSlot>> loadSaves; private readonly Func<string> sourceInfo;
        private readonly Func<int> lockedCount; private readonly int shardCount;
        private readonly List<ShopRow> shops = new List<ShopRow>();
        private RadioButton useShards, unlockAll; private DarkCombo sourceBox, townBox; private Label lockedLabel, shopCount; private Button rescan; private DataGridView grid;
        private bool building;

        public UnlocksPage(Func<bool> getUnlockAll, Action<bool> setUnlockAll, Func<string> getSource, Action<string> setSource, Func<List<SaveSlot>> loadSaves,
                           Func<string> sourceInfo, Func<int> lockedCount, int shardCount, IEnumerable<Hero> heroes)
        {
            this.getUnlockAll = getUnlockAll; this.setUnlockAll = setUnlockAll; this.getSource = getSource; this.setSource = setSource; this.loadSaves = loadSaves;
            this.sourceInfo = sourceInfo; this.lockedCount = lockedCount; this.shardCount = shardCount;
            BuildShopRows(heroes);
            Dock = DockStyle.Fill; BackColor = Theme.Bg;
            Build();
        }

        private void BuildShopRows(IEnumerable<Hero> heroes)
        {
            foreach (var h in heroes)
            {
                if (h.UnlockHint == null || h.ShardJob != 0 || string.IsNullOrEmpty(h.ShardTown)) continue;
                int.TryParse(h.ShardPrice, out int price);
                shops.Add(new ShopRow { Town = h.ShardTown, Shop = h.ShardShop, Price = price, Hero = h.Name, Game = h.Game, Note = string.IsNullOrEmpty(h.ShardAlso) ? "" : "also sold in " + h.ShardAlso });
                if (!string.IsNullOrEmpty(h.ShardAlso))
                {
                    // "Phantom Village, Item Shop" -> a second row under that town, marked as the copy
                    int comma = h.ShardAlso.IndexOf(", ", StringComparison.Ordinal);
                    string town = comma > 0 ? h.ShardAlso.Substring(0, comma) : h.ShardAlso, shop = comma > 0 ? h.ShardAlso.Substring(comma + 2) : "";
                    shops.Add(new ShopRow { Town = town, Shop = shop, Price = price, Hero = h.Name, Game = h.Game, Note = "copy - first sold in " + h.ShardTown + " (" + h.ShardShop + ")" });
                }
            }
            foreach (var r in shops) { int i = Array.IndexOf(TownOrder, r.Town); r.TownOrder = i >= 0 ? i : TownOrder.Length + r.Price; }
            shops.Sort((a, b) => { int c = a.TownOrder.CompareTo(b.TownOrder); if (c != 0) return c; c = string.CompareOrdinal(a.Shop, b.Shop); return c != 0 ? c : string.Compare(a.Hero, b.Hero, StringComparison.OrdinalIgnoreCase); });
        }

        private void Build()
        {
            building = true;
            var body = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(22, 14, 22, 0), BackColor = Theme.Bg };

            body.Controls.Add(new Label
            {
                Text = "By default, Crystal Shards/Characters unlock in one of two ways. The default cast will unlock when the job is unlocked. An additional " + shardCount +
                       " characters can be bought in every major town in the game. The Phantom Village contains all missable Crystal Shards and extra strong Final Fantasy V " +
                       "bonus characters. All you need to do is buy the shard, save, and then exit the game. This Mod Manager reads your save for these key items and unlocks " +
                       "them. You can load the save below. The other option is to unlock all characters from the beginning to create a custom roster for yourself.",
                AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 14),
            });
            useShards = Radio("Use Crystal Shards and jobs from a save", !getUnlockAll());
            body.Controls.Add(useShards);

            var srcRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(24, 0, 0, 2), WrapContents = false };
            srcRow.Controls.Add(Ui.Lbl("Read them from"));
            sourceBox = new DarkCombo { Width = 420, Margin = new Padding(0, 1, 8, 0) };
            sourceBox.SelectedIndexChanged += (s, e) => { if (!building && sourceBox.SelectedItem is SourceItem it) { setSource(it.Value); RefreshLocked(); } };
            srcRow.Controls.Add(sourceBox);
            rescan = Ui.Btn(Font, "Rescan saves", false); rescan.Margin = new Padding(0);
            rescan.Click += (s, e) => { FillSources(); setSource(getSource()); RefreshLocked(); };
            srcRow.Controls.Add(rescan);
            body.Controls.Add(srcRow);
            body.Controls.Add(Sub("Jobs and shards are read straight from the save file, so each save has its own unlocks. \"Every save combined\" uses the record the game keeps while you play."));

            unlockAll = Radio("Unlock all heroes", getUnlockAll());
            body.Controls.Add(unlockAll);
            body.Controls.Add(Sub("Everyone is available right away."));
            lockedLabel = Note("", 6);
            body.Controls.Add(lockedLabel);
            useShards.CheckedChanged += (s, e) => { if (useShards.Checked) { setUnlockAll(false); SyncEnabled(); RefreshLocked(); } };
            unlockAll.CheckedChanged += (s, e) => { if (unlockAll.Checked) { setUnlockAll(true); SyncEnabled(); RefreshLocked(); } };

            // Shard shops: header row with a town filter, then the table filling the rest of the page
            var shopRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 4), WrapContents = false };
            shopRow.Controls.Add(Section("CRYSTAL SHARD SHOPS", 6)); shopRow.Controls[0].Margin = new Padding(0, 6, 24, 0);
            shopRow.Controls.Add(Ui.Lbl("Town"));
            townBox = new DarkCombo { Width = 200, Margin = new Padding(0, 1, 16, 0) };
            townBox.Items.Add(AllTowns);
            foreach (var t in shops.Select(r => r.Town).Distinct().OrderBy(t => shops.First(r => r.Town == t).TownOrder)) townBox.Items.Add(t);
            townBox.SelectedIndexChanged += (s, e) => PopulateShops();
            shopRow.Controls.Add(townBox);
            shopCount = new Label { AutoSize = true, ForeColor = Theme.TextDim, Margin = new Padding(0, 5, 0, 0) };
            shopRow.Controls.Add(shopCount);
            body.Controls.Add(shopRow);
            body.Controls.Add(Note("Towns in story order. Walse, Lix, Istory and Quelb cannot be revisited later in the game, so their shards are also sold in Phantom Village.", 0));

            grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                RowHeadersVisible = false, BorderStyle = BorderStyle.None, BackgroundColor = Theme.Bg, GridColor = Theme.Border, EnableHeadersVisualStyles = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 32,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None, RowTemplate = { Height = 26 }, Margin = new Padding(0),
            };
            grid.DefaultCellStyle.BackColor = Theme.Surface; grid.DefaultCellStyle.ForeColor = Theme.Text; grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            grid.DefaultCellStyle.SelectionBackColor = Theme.Highlight; grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Theme.SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Header; grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Accent;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Header; grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9f);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 0, 0);
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Town", Width = 130 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Shop", Width = 120 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Gil", Width = 70 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Unlocks", Width = 210 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Game", Width = 150 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Note", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 160 });
            grid.Columns[2].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.HandleCreated += (s, e) => Native.DarkScrollbars(grid);

            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22, 4, 22, 16), BackColor = Theme.Bg };
            host.Controls.Add(grid);
            Controls.Add(host); Controls.Add(body);
            FillSources();
            SyncEnabled();
            RefreshLocked();
            townBox.SelectedIndex = 0;
            building = false;
        }

        private void PopulateShops()
        {
            string town = townBox.SelectedItem as string ?? AllTowns;
            var rows = town == AllTowns ? shops : shops.Where(r => r.Town == town).ToList();
            grid.SuspendLayout(); grid.Rows.Clear();
            foreach (var r in rows) grid.Rows.Add(r.Town, r.Shop, r.Price > 0 ? r.Price.ToString("N0") : "", r.Hero, r.Game, r.Note);
            grid.ResumeLayout();
            grid.ClearSelection();
            int towns = rows.Select(r => r.Town).Distinct().Count();
            shopCount.Text = rows.Count + " shards" + (town == AllTowns ? " in " + towns + " towns" : "");
        }

        private void FillSources()
        {
            building = true;
            sourceBox.Items.Clear();
            sourceBox.Items.Add(new SourceItem { Value = "auto", Text = "Most recent save (automatic)" });
            var saves = loadSaves();
            foreach (var s in saves.OrderBy(x => x.Id)) sourceBox.Items.Add(new SourceItem { Value = "slot:" + s.Id, Text = s.Label });
            sourceBox.Items.Add(new SourceItem { Value = "combined", Text = "Every save combined (what the game recorded while playing)" });
            string cur = getSource(); int idx = 0;
            for (int i = 0; i < sourceBox.Items.Count; i++) if (((SourceItem)sourceBox.Items[i]).Value == cur) idx = i;
            sourceBox.SelectedIndex = idx;
            if (saves.Count == 0) sourceBox.Items[0] = new SourceItem { Value = "auto", Text = "Most recent save (no FFV save files found)" };
            building = false;
        }

        private void SyncEnabled() { sourceBox.Enabled = rescan.Enabled = useShards.Checked; }

        public void RefreshLocked()
        {
            int locked = lockedCount();
            string from = useShards.Checked ? $"  (read from {sourceInfo()})" : "";
            lockedLabel.Text = unlockAll.Checked ? "Every hero is unlocked." : locked > 0 ? $"Still locked right now: {locked}.{from}" : $"Every hero is unlocked.{from}";
        }

        private static Label Section(string text, int top = 0) => new Label { Text = text, AutoSize = true, ForeColor = Theme.Accent, Font = new Font("Segoe UI Semibold", 9f), Margin = new Padding(0, top, 0, 6) };
        private static Label Note(string text, int top = 0) => new Label { Text = text, AutoSize = true, MaximumSize = new Size(820, 0), ForeColor = Theme.TextDim, Margin = new Padding(0, top, 0, 8) };
        private static Label Sub(string text) => new Label { Text = text, AutoSize = true, MaximumSize = new Size(790, 0), ForeColor = Theme.TextDim, Margin = new Padding(24, 0, 0, 8) };
        private static RadioButton Radio(string text, bool on) => new RadioButton { Text = text, AutoSize = true, Checked = on, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(0, 2, 0, 2) };
    }
}
