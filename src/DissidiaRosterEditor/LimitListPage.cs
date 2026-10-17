using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Dissidia.Common;

namespace DissidiaRosterEditor
{
    // Lookup table: every hero's Limit Break with its level and effect, by game then character.
    public class LimitListPage : UserControl
    {
        private readonly Dictionary<string, List<Hero>> byGame; private readonly List<string> games;
        private DarkCombo gameBox; private DataGridView grid; private Label count;
        private const string AllGames = "All games";

        public LimitListPage(Dictionary<string, List<Hero>> byGame, List<string> games)
        {
            this.byGame = byGame; this.games = games;
            Dock = DockStyle.Fill; BackColor = Theme.Bg;
            Build();
        }

        private void Build()
        {
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(22, 14, 22, 4), WrapContents = false, BackColor = Theme.Bg };
            top.Controls.Add(new Label { Text = "LIMIT BREAKS BY GAME", AutoSize = true, ForeColor = Theme.Accent, Font = new Font("Segoe UI Semibold", 9f), Margin = new Padding(0, 6, 24, 0) });
            top.Controls.Add(Ui.Lbl("Game"));
            gameBox = new DarkCombo { Width = 220, Margin = new Padding(0, 1, 16, 0) };
            gameBox.Items.Add(AllGames);
            foreach (var g in games) gameBox.Items.Add(g);
            gameBox.SelectedIndexChanged += (s, e) => Populate();
            top.Controls.Add(gameBox);
            count = new Label { AutoSize = true, ForeColor = Theme.TextDim, Margin = new Padding(0, 5, 0, 0) };
            top.Controls.Add(count);

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
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Game", Width = 130 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Character", Width = 210 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Limit Break", Width = 200 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Level", Width = 70 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Effect", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 200 });
            grid.Columns[3].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.HandleCreated += (s, e) => Native.DarkScrollbars(grid);

            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22, 4, 22, 16), BackColor = Theme.Bg };
            host.Controls.Add(grid);
            Controls.Add(host); Controls.Add(top);
            gameBox.SelectedIndex = 0;
        }

        private void Populate()
        {
            string game = gameBox.SelectedItem as string ?? AllGames;
            var rows = game == AllGames ? games.SelectMany(g => byGame[g]) : (byGame.TryGetValue(game, out var l) ? l : new List<Hero>());
            grid.SuspendLayout(); grid.Rows.Clear();
            foreach (var h in rows) grid.Rows.Add(h.Game, h.Name, h.LimitName, h.LimitLevel > 0 ? "Lv" + h.LimitLevel : "", h.LimitDesc);
            grid.ResumeLayout();
            grid.ClearSelection();
            count.Text = grid.Rows.Count + " heroes";
        }
    }
}
