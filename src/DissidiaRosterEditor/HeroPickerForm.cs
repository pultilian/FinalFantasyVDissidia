using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Dissidia.Common;

namespace DissidiaRosterEditor
{
    // Pop-up picker: choose a game, then click a hero card (sprite, name, limit break).
    public class HeroPickerForm : Form
    {
        public Hero Selected;                       // null = vanilla (when DialogResult.OK)
        public const string VanillaGame = "Vanilla";

        private readonly ModFiles files;
        private readonly Dictionary<string, List<Hero>> byGame;
        private readonly List<string> games;
        private readonly Hero current;
        private readonly Func<string, Image> bigThumb;
        private readonly string sourceInfo;
        private DarkCombo gameBox; private FlowLayoutPanel gallery; private Label count;

        public HeroPickerForm(ModFiles files, Dictionary<string, List<Hero>> byGame, List<string> games, Hero current, string slotTitle, Func<string, Image> bigThumb)
        {
            this.files = files; this.byGame = byGame; this.games = games; this.current = current; this.bigThumb = bigThumb;
            sourceInfo = string.IsNullOrEmpty(files.ShardSourceInfo) ? "this install" : files.ShardSourceInfo;
            Text = "Choose a hero - " + slotTitle;
            Width = 1000; Height = 720; MinimumSize = new Size(700, 480);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9.5f); BackColor = Theme.Bg; ForeColor = Theme.Text; DoubleBuffered = true;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
            Build();
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.DarkTitleBar(Handle); }

        private void Build()
        {
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, BackColor = Theme.Surface, Padding = new Padding(16, 12, 16, 0), WrapContents = false };
            top.Controls.Add(Ui.Lbl("Game"));
            gameBox = new DarkCombo { Width = 220, Margin = new Padding(0, 1, 16, 0) };
            gameBox.Items.Add(VanillaGame);
            foreach (var g in games) gameBox.Items.Add(g);
            gameBox.SelectedIndexChanged += (s, e) => Populate();
            top.Controls.Add(gameBox);
            count = new Label { AutoSize = true, ForeColor = Theme.TextDim, Margin = new Padding(0, 5, 0, 0) };
            top.Controls.Add(count);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.Surface, Padding = new Padding(16, 10, 16, 0), WrapContents = false, FlowDirection = FlowDirection.RightToLeft };
            var cancel = Ui.Btn(Font, "Cancel", false); cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            var vanilla = Ui.Btn(Font, "Use vanilla sprite", false); vanilla.Click += (s, e) => { Selected = null; DialogResult = DialogResult.OK; Close(); };
            bottom.Controls.Add(cancel); bottom.Controls.Add(vanilla);
            var hint = new Label { AutoSize = true, ForeColor = Theme.TextDim, Margin = new Padding(0, 6, 16, 0), Text = "Click a hero to choose them. Right-click shows where to get them. Esc cancels." };
            bottom.Controls.Add(hint);

            gallery = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg, Padding = new Padding(12, 10, 12, 10) };
            gallery.HandleCreated += (s, e) => Native.DarkScrollbars(gallery);

            Controls.Add(gallery); Controls.Add(bottom); Controls.Add(top);

            string startGame = current != null && games.Contains(current.Game) ? current.Game : (games.Count > 0 ? games[0] : VanillaGame);
            gameBox.SelectedItem = startGame;
            if (gameBox.SelectedIndex < 0) gameBox.SelectedIndex = 0;
        }

        private void Populate()
        {
            gallery.SuspendLayout();
            foreach (Control c in gallery.Controls.Cast<Control>().ToList()) { gallery.Controls.Remove(c); c.Dispose(); }
            string game = gameBox.SelectedItem as string;
            var list = game != null && byGame.TryGetValue(game, out var l) ? l : new List<Hero>();
            foreach (var h in list)
            {
                var card = new HeroCard(h, bigThumb(h.Key), current != null && current.Key == h.Key);
                card.Chosen += hero =>
                {
                    if (hero.Locked)
                    {
                        MessageBox.Show(this, $"{hero.Name} is locked.\n\n{hero.UnlockHint}\n\n(The editor reads the save chosen in Settings; save the game and click Rescan there after you progress.)", "Locked hero", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    Selected = hero; DialogResult = DialogResult.OK; Close();
                };
                card.WhereRequested += (hero, at) => DarkTip.Show(card, hero.WhereText(sourceInfo, ModFiles.UnlockAll), at);
                gallery.Controls.Add(card);
            }
            int lockedCount = list.Count(h => h.Locked);
            count.Text = game == VanillaGame ? "Original FFV sprite - use the button below" : $"{list.Count} heroes" + (lockedCount > 0 ? $", {lockedCount} locked (find their Crystal Shards in game)" : "");
            gallery.ResumeLayout();
            gallery.AutoScrollPosition = new Point(0, 0);
        }
    }

    // A hero card: 96px sprite, name, limit break.
    internal class HeroCard : Control
    {
        public readonly Hero Hero;
        public event Action<Hero> Chosen;
        public event Action<Hero, Point> WhereRequested;
        private readonly Image thumb; private readonly bool isCurrent; private bool hover;
        private static readonly Font NameFont = new Font("Segoe UI Semibold", 9.5f), LimitFont = new Font("Segoe UI", 8.5f);

        public HeroCard(Hero hero, Image thumb, bool isCurrent)
        {
            Hero = hero; this.thumb = thumb; this.isCurrent = isCurrent;
            Size = new Size(150, 172); Margin = new Padding(6); Cursor = hero.Locked ? Cursors.No : Cursors.Hand;
            if (hero.Locked) new ToolTip().SetToolTip(this, "Locked - " + hero.UnlockHint);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            MouseEnter += (s, e) => { hover = true; Invalidate(); };
            MouseLeave += (s, e) => { hover = false; Invalidate(); };
            MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) Chosen?.Invoke(Hero);
                else if (e.Button == MouseButtons.Right)
                {
                    var menu = new DarkMenu();
                    var at = e.Location;
                    menu.Add("Limit Break: " + Hero.LimitName, () => DarkTip.Show(this, Hero.LimitText(), at));
                    menu.Add("Where to get " + Hero.Name, () => WhereRequested?.Invoke(Hero, at));
                    if (!Hero.Locked) menu.Add("Choose " + Hero.Name, () => Chosen?.Invoke(Hero));
                    menu.Show(this, e.Location);
                }
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(hover ? Theme.SurfaceAlt : Theme.Surface);
            var box = new Rectangle((Width - 96) / 2, 10, 96, 96);
            using (var b = new SolidBrush(Theme.Thumb)) g.FillRectangle(b, box);
            if (thumb != null)
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
                if (Hero.Locked)
                {
                    // silhouette: the sprite drawn dark and desaturated
                    var cm = new System.Drawing.Imaging.ColorMatrix(new float[][] {
                        new[] { 0.11f, 0.11f, 0.11f, 0, 0 }, new[] { 0.11f, 0.11f, 0.11f, 0, 0 }, new[] { 0.12f, 0.12f, 0.12f, 0, 0 },
                        new[] { 0f, 0f, 0f, 0.55f, 0 }, new[] { 0.05f, 0.05f, 0.06f, 0, 1 } });
                    using (var ia = new System.Drawing.Imaging.ImageAttributes()) { ia.SetColorMatrix(cm); g.DrawImage(thumb, box, 0, 0, thumb.Width, thumb.Height, GraphicsUnit.Pixel, ia); }
                }
                else g.DrawImage(thumb, box);
            }
            using (var p = new Pen(isCurrent ? Theme.Accent : Theme.Border, isCurrent ? 2f : 1f)) g.DrawRectangle(p, box);
            if (Hero.Locked) DrawPadlock(g, new Rectangle(box.Right - 26, box.Bottom - 26, 20, 20));
            TextRenderer.DrawText(g, Hero.Name, NameFont, new Rectangle(4, 112, Width - 8, 20), Hero.Locked ? Theme.TextDim : Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Hero.Locked)
            {
                TextRenderer.DrawText(g, "LOCKED", LimitFont, new Rectangle(4, 134, Width - 8, 18), Theme.Warn, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, Hero.ShardJob > 0 ? "needs the " + (ModLayout.Jobs.FirstOrDefault(j => j.Id == Hero.ShardJob)?.Name ?? "job") + " job" : "find the Crystal Shard", LimitFont, new Rectangle(4, 152, Width - 8, 16), Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            else
            {
                TextRenderer.DrawText(g, Hero.LimitName, LimitFont, new Rectangle(4, 134, Width - 8, 18), Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (!string.IsNullOrEmpty(Hero.BattleName) && Hero.BattleName != Hero.Name)
                    TextRenderer.DrawText(g, "in battle: " + Hero.BattleName, LimitFont, new Rectangle(4, 152, Width - 8, 16), Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            using (var p = new Pen(hover ? Theme.Accent : Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        private static void DrawPadlock(Graphics g, Rectangle r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Color.FromArgb(200, 20, 20, 24))) g.FillEllipse(b, r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6);
            var body = new Rectangle(r.X + 3, r.Y + 9, r.Width - 6, r.Height - 10);
            using (var b = new SolidBrush(Theme.Warn)) g.FillRectangle(b, body);
            using (var p = new Pen(Theme.Warn, 2.2f)) g.DrawArc(p, r.X + 5, r.Y + 1, r.Width - 10, 14, 180, 180);
            using (var b = new SolidBrush(Color.FromArgb(40, 40, 48))) g.FillEllipse(b, body.X + body.Width / 2 - 2, body.Y + 3, 4, 4);
            g.SmoothingMode = SmoothingMode.Default;
        }
    }
}
