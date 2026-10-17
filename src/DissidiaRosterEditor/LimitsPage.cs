using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Dissidia.Common;

namespace DissidiaRosterEditor
{
    // Limit Break settings: charge mode, speed, levels, MP cost, gauge, testing switch, and the
    // per-job adjustment table. Writes BepInEx\config\dissidia.limit.cfg and DissidiaLimitJobs.csv.
    public class LimitsPage : UserControl
    {
        public const string Description =
            "In Final Fantasy V: Dissidia, every Crystal Shard character has their own unique Limit Break. Limits will not unlock until you have " +
            "mastered the respective job. These are activated in one of two ways which you can select below. You can either have them charge when " +
            "you take damage, or have them activate after a certain number of turns. Limits charge at different rates based on the Job slot they are " +
            "in and the strength or level of the limit. The strength of the Limit will also depend on the stats associated with the Job. When in " +
            "doubt, ask yourself, \"Would this character fit in this role?\"";

        private readonly ModFiles files;
        private readonly Action<string, Color> status;
        private RadioButton modeDamage, modeTurns; private DarkCombo speedBox; private TextBox turnsBase;
        private readonly TextBox[] pct = new TextBox[4], turns = new TextBox[4];
        private CheckBox mpCost, showGauge, alwaysReady; private TextBox gaugeColor, trackColor; private Panel gaugeSwatch, trackSwatch;
        private DataGridView jobGrid; private List<JobAdjust> jobs = new List<JobAdjust>();

        public LimitsPage(ModFiles files, Action<string, Color> status)
        {
            this.files = files; this.status = status;
            Dock = DockStyle.Fill; BackColor = Theme.Bg; AutoScroll = true; Padding = new Padding(0);
            HandleCreated += (s, e) => Native.DarkScrollbars(this);
            Build();
            Load_();
        }

        private void Build()
        {
            var body = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(22, 14, 22, 20), BackColor = Theme.Bg };

            body.Controls.Add(new Label { Text = Description, AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 14) });

            body.Controls.Add(Section("CHARGING"));
            var modeRow = Row();
            modeDamage = Radio("Charge with damage taken (% of max HP)"); modeTurns = Radio("Activate after a number of actions");
            modeRow.Controls.Add(modeDamage); modeRow.Controls.Add(modeTurns);
            body.Controls.Add(modeRow);

            var speedRow = Row(); speedRow.Controls.Add(Ui.Lbl("Speed"));
            speedBox = new DarkCombo { Width = 160, Margin = new Padding(0, 1, 16, 0) };
            speedBox.Items.AddRange(new object[] { "Slow", "Normal", "Fast" });
            speedRow.Controls.Add(speedBox);
            body.Controls.Add(speedRow);

            body.Controls.Add(Sub("Requirement by limit level (stronger limits are higher levels; see the Limit List page):"));
            var lvl = new TableLayoutPanel { AutoSize = true, ColumnCount = 6, Margin = new Padding(24, 2, 0, 6), BackColor = Theme.Bg };
            lvl.Controls.Add(new Label { Text = "", AutoSize = true }, 0, 0);
            lvl.Controls.Add(new Label { Text = "Base", AutoSize = true, ForeColor = Theme.Accent, Margin = new Padding(6, 4, 6, 0), Font = new Font("Segoe UI Semibold", 9f) }, 1, 0);
            for (int i = 0; i < 4; i++) lvl.Controls.Add(new Label { Text = "Lv" + (i + 1), AutoSize = true, ForeColor = Theme.Accent, Margin = new Padding(6, 4, 6, 0), Font = new Font("Segoe UI Semibold", 9f) }, i + 2, 0);
            lvl.Controls.Add(Ui.Lbl("Damage: % of max HP"), 0, 1);
            lvl.Controls.Add(Ui.Lbl("Actions: base + added per level"), 0, 2);
            lvl.Controls.Add(new Label { Text = "-", AutoSize = true, ForeColor = Theme.TextOff, Margin = new Padding(30, 6, 6, 2) }, 1, 1);
            turnsBase = Ui.Field(out Control wb, 64, ""); wb.Margin = new Padding(6, 2, 6, 2); lvl.Controls.Add(wb, 1, 2);
            for (int i = 0; i < 4; i++)
            {
                pct[i] = Ui.Field(out Control w1, 64, ""); w1.Margin = new Padding(6, 2, 6, 2); lvl.Controls.Add(w1, i + 2, 1);
                turns[i] = Ui.Field(out Control w2, 64, ""); w2.Margin = new Padding(6, 2, 6, 2); lvl.Controls.Add(w2, i + 2, 2);
            }
            body.Controls.Add(lvl);
            body.Controls.Add(Sub("Plus the job adjustment below for the job the character is wearing."));

            body.Controls.Add(Section("COST AND GAUGE", 14));
            mpCost = Ui.Check("Limits also cost MP", false);
            showGauge = Ui.Check("Draw the limit bar under the ATB gauge", true);
            body.Controls.Add(mpCost); body.Controls.Add(showGauge);
            var colRow = Row();
            colRow.Controls.Add(Ui.Lbl("Bar colour"));
            gaugeColor = Ui.Field(out Control gc, 80, ""); colRow.Controls.Add(gc);
            gaugeSwatch = Swatch(); colRow.Controls.Add(gaugeSwatch);
            colRow.Controls.Add(Ui.Lbl("Empty part"));
            trackColor = Ui.Field(out Control tc, 80, ""); colRow.Controls.Add(tc);
            trackSwatch = Swatch(); colRow.Controls.Add(trackSwatch);
            colRow.Controls.Add(Ui.Lbl("RRGGBB hex"));
            gaugeColor.TextChanged += (s, e) => Paint(gaugeSwatch, gaugeColor.Text);
            trackColor.TextChanged += (s, e) => Paint(trackSwatch, trackColor.Text);
            body.Controls.Add(colRow);

            body.Controls.Add(Section("OPTIONS", 14));
            alwaysReady = Ui.Check("Limits always active", false);
            body.Controls.Add(alwaysReady);

            body.Controls.Add(Section("JOB ADJUSTMENTS", 14));
            body.Controls.Add(Note("Added to the requirement for whoever wears the job: negative charges sooner."));
            jobGrid = new DataGridView
            {
                Width = 560, Height = 30 + 22 * 22 + 2, Margin = new Padding(0, 2, 0, 8), AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                RowHeadersVisible = false, BorderStyle = BorderStyle.None, BackgroundColor = Theme.Surface, GridColor = Theme.Border, EnableHeadersVisualStyles = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 30,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None, ScrollBars = ScrollBars.None, RowTemplate = { Height = 22 },
            };
            jobGrid.DefaultCellStyle.BackColor = Theme.Surface; jobGrid.DefaultCellStyle.ForeColor = Theme.Text;
            jobGrid.DefaultCellStyle.SelectionBackColor = Theme.Highlight; jobGrid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            jobGrid.AlternatingRowsDefaultCellStyle.BackColor = Theme.SurfaceAlt;
            jobGrid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Header; jobGrid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Accent;
            jobGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Header; jobGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9f);
            jobGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Job", Width = 150, ReadOnly = true });
            jobGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Damage: % added", Width = 170 });
            jobGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Actions added", Width = 190 });
            jobGrid.Columns[1].DefaultCellStyle.Alignment = jobGrid.Columns[2].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            body.Controls.Add(jobGrid);

            var btnRow = Row(); btnRow.Margin = new Padding(0, 10, 0, 0);
            var save = Ui.Btn(Font, "Save settings", true); save.Click += (s, e) => Save();
            var reset = Ui.Btn(Font, "Reset to defaults", false); reset.Margin = new Padding(10, 0, 0, 0); reset.Click += (s, e) => { Show(new LimitSettings(), DefaultJobs()); status("Defaults loaded - click Save settings to write them.", Theme.TextDim); };
            btnRow.Controls.Add(save); btnRow.Controls.Add(reset);
            body.Controls.Add(btnRow);
            body.Controls.Add(Note("Changes apply the next time the game starts.", 8));

            Controls.Add(body);
        }

        private void Load_()
        {
            var cfg = new BepCfg(Installer.LimitCfg(files));
            Show(LimitSettings.Load(cfg), JobTable.Load(Installer.JobsCsv(files)));
        }

        private List<JobAdjust> DefaultJobs()
        {
            string def = Path.Combine(Installer.ConfigDir(files), "DissidiaLimitJobs.default.csv");
            return JobTable.Load(File.Exists(def) ? def : Installer.JobsCsv(files));
        }

        private void Show(LimitSettings s, List<JobAdjust> jobRows)
        {
            modeDamage.Checked = !s.ChargeMode.Equals("Turns", StringComparison.OrdinalIgnoreCase); modeTurns.Checked = !modeDamage.Checked;
            speedBox.SelectedItem = new[] { "Slow", "Normal", "Fast" }.FirstOrDefault(x => x.Equals(s.Speed, StringComparison.OrdinalIgnoreCase)) ?? "Normal";
            turnsBase.Text = s.TurnsBase.ToString();
            for (int i = 0; i < 4; i++) { pct[i].Text = s.LevelPercents[i].ToString(); turns[i].Text = s.LevelTurns[i].ToString(); }
            mpCost.Checked = s.MpCost; showGauge.Checked = s.ShowGauge; alwaysReady.Checked = s.AlwaysReady;
            gaugeColor.Text = s.GaugeColor; trackColor.Text = s.GaugeTrackColor;
            jobs = jobRows;
            jobGrid.Rows.Clear();
            foreach (var j in jobs) jobGrid.Rows.Add(j.JobName, j.PercentAdd, j.TurnsAdd);
        }

        private void Save()
        {
            try
            {
                var s = new LimitSettings
                {
                    ChargeMode = modeTurns.Checked ? "Turns" : "Damage", Speed = speedBox.SelectedItem as string ?? "Normal",
                    MpCost = mpCost.Checked, ShowGauge = showGauge.Checked, AlwaysReady = alwaysReady.Checked,
                    GaugeColor = Hex(gaugeColor.Text, "4CD964"), GaugeTrackColor = Hex(trackColor.Text, "55555F"),
                };
                if (!int.TryParse(turnsBase.Text.Trim(), out int tb) || tb < 1 || tb > 99) throw new Exception("The base number of actions must be a whole number between 1 and 99.");
                s.TurnsBase = tb;
                for (int i = 0; i < 4; i++)
                {
                    if (!int.TryParse(pct[i].Text.Trim(), out int p) || p < 1 || p > 999) throw new Exception($"Lv{i + 1} % of max HP must be a whole number between 1 and 999.");
                    if (!int.TryParse(turns[i].Text.Trim(), out int t) || t < 0 || t > 99) throw new Exception($"Lv{i + 1} actions added must be a whole number between 0 and 99.");
                    s.LevelPercents[i] = p; s.LevelTurns[i] = t;
                }
                jobGrid.EndEdit();
                for (int i = 0; i < jobs.Count && i < jobGrid.Rows.Count; i++)
                {
                    if (!int.TryParse(Convert.ToString(jobGrid.Rows[i].Cells[1].Value), out int p)) throw new Exception(jobs[i].JobName + ": the % added must be a whole number.");
                    if (!int.TryParse(Convert.ToString(jobGrid.Rows[i].Cells[2].Value), out int t)) throw new Exception(jobs[i].JobName + ": the actions added must be a whole number.");
                    jobs[i].PercentAdd = p; jobs[i].TurnsAdd = t;
                }
                var cfg = new BepCfg(Installer.LimitCfg(files));
                s.SaveTo(cfg);
                if (jobs.Count > 0) JobTable.Save(Installer.JobsCsv(files), jobs);
                status("Limit settings saved - restart the game to use them.", Theme.Ok);
            }
            catch (UnauthorizedAccessException ex) { MessageBox.Show(this, "Windows refused to write to the game folder:\n" + ex.Message + "\n\nRun the Mod Manager as administrator and try again.", "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Check the settings", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private static string Hex(string text, string fallback)
        {
            string t = (text ?? "").Trim().TrimStart('#').ToUpperInvariant();
            return t.Length == 6 && t.All(c => Uri.IsHexDigit(c)) ? t : fallback;
        }
        private static void Paint(Panel swatch, string hex)
        {
            string t = Hex(hex, "");
            swatch.BackColor = t.Length == 6 ? Color.FromArgb(Convert.ToInt32(t.Substring(0, 2), 16), Convert.ToInt32(t.Substring(2, 2), 16), Convert.ToInt32(t.Substring(4, 2), 16)) : Theme.Border;
        }
        private static Panel Swatch() => new Panel { Width = 26, Height = 22, Margin = new Padding(0, 2, 16, 0), BackColor = Theme.Border, BorderStyle = BorderStyle.FixedSingle };
        private static FlowLayoutPanel Row() => new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 2, 0, 4), WrapContents = false };
        private static Label Section(string text, int top = 0) => new Label { Text = text, AutoSize = true, ForeColor = Theme.Accent, Font = new Font("Segoe UI Semibold", 9f), Margin = new Padding(0, top, 0, 6) };
        private static Label Note(string text, int top = 0) => new Label { Text = text, AutoSize = true, MaximumSize = new Size(820, 0), ForeColor = Theme.TextDim, Margin = new Padding(0, top, 0, 8) };
        private static Label Sub(string text) => new Label { Text = text, AutoSize = true, MaximumSize = new Size(800, 0), ForeColor = Theme.TextDim, Margin = new Padding(24, 0, 0, 4) };
        private static RadioButton Radio(string text) => new RadioButton { Text = text, AutoSize = true, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(0, 2, 18, 2) };
    }
}
