using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Dark theme, native dark-mode hooks and the custom-drawn widgets shared by the FFV: Dissidia tools.
namespace Dissidia.Common
{
    // Dark palette. One accent (gold), everything else neutral.
    internal static class Theme
    {
        public static readonly Color Bg          = Color.FromArgb(24, 24, 30);
        public static readonly Color Surface     = Color.FromArgb(32, 32, 40);
        public static readonly Color SurfaceAlt  = Color.FromArgb(37, 37, 46);
        public static readonly Color Header      = Color.FromArgb(44, 44, 56);
        public static readonly Color Border      = Color.FromArgb(58, 58, 72);
        public static readonly Color Input       = Color.FromArgb(44, 44, 54);
        public static readonly Color InputHover  = Color.FromArgb(52, 52, 64);
        public static readonly Color InputOff    = Color.FromArgb(34, 34, 42);
        public static readonly Color Highlight   = Color.FromArgb(66, 66, 86);
        public static readonly Color Thumb       = Color.FromArgb(16, 16, 24);
        public static readonly Color Text        = Color.FromArgb(232, 232, 238);
        public static readonly Color TextDim     = Color.FromArgb(150, 150, 170);
        public static readonly Color TextOff     = Color.FromArgb(96, 96, 112);
        public static readonly Color Accent      = Color.FromArgb(255, 217, 122);
        public static readonly Color AccentHover = Color.FromArgb(255, 228, 160);
        public static readonly Color Warn        = Color.FromArgb(240, 179, 90);
        public static readonly Color Error       = Color.FromArgb(240, 110, 110);
        public static readonly Color Ok          = Color.FromArgb(140, 210, 150);
    }

    internal static class Native
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hWnd, string app, string idList);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", EntryPoint = "#135")] private static extern int SetPreferredAppMode(int mode);          // Win10 1903+: 1 = AllowDark
        [DllImport("uxtheme.dll", EntryPoint = "#133")] private static extern bool AllowDarkModeForWindow(IntPtr hWnd, bool allow);

        public static void EnableAppDarkMode() { try { SetPreferredAppMode(1); } catch { } }
        public static void AllowDark(Control c) { try { AllowDarkModeForWindow(c.Handle, true); } catch { } }
        public static void DarkScrollbars(Control c) { AllowDark(c); try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } }
        public static void DarkComboTheme(Control c) { AllowDark(c); try { SetWindowTheme(c.Handle, "DarkMode_CFD", null); } catch { } }
        public static void DarkTitleBar(IntPtr h)
        {
            try { int on = 1; if (DwmSetWindowAttribute(h, 20, ref on, 4) != 0) DwmSetWindowAttribute(h, 19, ref on, 4); } catch { }
        }
    }

    // A ComboBox drawn entirely by us: dark closed state, dark list, gold border when focused.
    internal class DarkCombo : ComboBox
    {
        private bool hover;
        public Func<object, string> Display;                               // how an item is shown (default ToString)

        public DarkCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 22;
            BackColor = Theme.Input; ForeColor = Theme.Text;
            DropDownHeight = 420; MaxDropDownItems = 18; IntegralHeight = false;
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.DarkComboTheme(this); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { BackColor = Enabled ? Theme.Input : Theme.InputOff; Invalidate(); base.OnEnabledChanged(e); }

        private string TextOf(int i) => Display != null ? Display(Items[i]) : Items[i].ToString();

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool inList = (e.State & DrawItemState.ComboBoxEdit) == 0;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color bg = !Enabled ? Theme.InputOff : inList ? (selected ? Theme.Highlight : Theme.Input) : (hover ? Theme.InputHover : Theme.Input);
            using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
            var r = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - (inList ? 8 : 30), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, TextOf(e.Index), Font, r, Enabled ? Theme.Text : Theme.TextOff, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != 0x000F) return;                                   // WM_PAINT: overdraw border + chevron
            using (var g = Graphics.FromHwnd(Handle))
            {
                var r = ClientRectangle;
                var btn = new Rectangle(r.Width - 24, 1, 23, r.Height - 2);
                using (var b = new SolidBrush(!Enabled ? Theme.InputOff : hover ? Theme.InputHover : Theme.Input)) g.FillRectangle(b, btn);
                if (Enabled)
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    int cx = btn.X + btn.Width / 2, cy = btn.Y + btn.Height / 2;
                    using (var p = new Pen(Theme.TextDim, 1.6f))
                        g.DrawLines(p, new[] { new Point(cx - 4, cy - 2), new Point(cx, cy + 2), new Point(cx + 4, cy - 2) });
                    g.SmoothingMode = SmoothingMode.None;
                }
                using (var p = new Pen(Focused ? Theme.Accent : Theme.Border)) g.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
            }
        }
    }

    // A dark right-click menu.
    internal class DarkMenu : ContextMenuStrip
    {
        public DarkMenu()
        {
            Renderer = new DarkMenuRenderer();
            BackColor = Theme.Surface; ForeColor = Theme.Text; ShowImageMargin = false;
            Font = new Font("Segoe UI", 9.5f);
        }

        public ToolStripMenuItem Add(string text, Action onClick, bool enabled = true)
        {
            var item = new ToolStripMenuItem(text) { ForeColor = Theme.Text, BackColor = Theme.Surface, Enabled = enabled, Padding = new Padding(6, 4, 6, 4) };
            item.Click += (s, e) => onClick();
            Items.Add(item);
            return item;
        }

        private class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            public DarkMenuRenderer() : base(new DarkMenuColors()) { }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextOff; base.OnRenderItemText(e); }
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                var r = new Rectangle(Point.Empty, e.Item.Size);
                using (var b = new SolidBrush(e.Item.Selected && e.Item.Enabled ? Theme.Highlight : Theme.Surface)) e.Graphics.FillRectangle(b, r);
            }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
            }
        }

        private class DarkMenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Theme.Surface;
            public override Color MenuBorder => Theme.Border;
            public override Color MenuItemBorder => Theme.Highlight;
            public override Color MenuItemSelected => Theme.Highlight;
            public override Color ImageMarginGradientBegin => Theme.Surface;
            public override Color ImageMarginGradientMiddle => Theme.Surface;
            public override Color ImageMarginGradientEnd => Theme.Surface;
            public override Color SeparatorDark => Theme.Border;
            public override Color SeparatorLight => Theme.Border;
        }
    }

    // A dark, multi-line tooltip shown on demand near the cursor (owner-drawn ToolTip).
    internal static class DarkTip
    {
        private static ToolTip tip;
        private static readonly Font TipFont = new Font("Segoe UI", 9.5f);
        private const int MaxWidth = 440, Pad = 10;

        private static ToolTip Tip()
        {
            if (tip != null) return tip;
            tip = new ToolTip { OwnerDraw = true, ShowAlways = true, UseAnimation = false, UseFading = false };
            tip.Popup += (s, e) =>
            {
                string text = ((ToolTip)s).GetToolTip(e.AssociatedControl) ?? "";
                var size = TextRenderer.MeasureText(text, TipFont, new Size(MaxWidth, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                e.ToolTipSize = new Size(size.Width + Pad * 2, size.Height + Pad * 2);
            };
            tip.Draw += (s, e) =>
            {
                e.Graphics.Clear(Theme.Surface);
                using (var p = new Pen(Theme.Accent)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                var r = new Rectangle(Pad, Pad, e.Bounds.Width - Pad * 2, e.Bounds.Height - Pad * 2);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, TipFont, r, Theme.Text, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            };
            return tip;
        }

        // Shows the text just below-right of a point inside the control; it closes on its own.
        public static void Show(Control owner, string text, Point at, int milliseconds = 12000)
        {
            Tip().Hide(owner);
            Tip().Show(text, owner, at.X + 12, at.Y + 12, milliseconds);
        }

        public static void Hide(Control owner) { if (tip != null) tip.Hide(owner); }
    }

    // Shared dark-themed widgets.
    internal static class Ui
    {
        public static Button Btn(Font font, string text, bool primary)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, UseMnemonic = false,
                Padding = new Padding(12, 4, 12, 4), Margin = new Padding(0, 0, 8, 0),
                BackColor = primary ? Theme.Accent : Theme.Input, ForeColor = primary ? Theme.Bg : Theme.Text,
                Font = primary ? new Font("Segoe UI Semibold", 9.5f) : font,
            };
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = primary ? Theme.Accent : Theme.Border;
            b.FlatAppearance.MouseOverBackColor = primary ? Theme.AccentHover : Theme.InputHover;
            b.FlatAppearance.MouseDownBackColor = primary ? Theme.Accent : Theme.Highlight;
            b.EnabledChanged += (s, e) => { b.ForeColor = b.Enabled ? (primary ? Theme.Bg : Theme.Text) : Theme.TextDim; if (primary) b.BackColor = b.Enabled ? Theme.Accent : Theme.Input; };
            return b;
        }

        // A bordered dark text field. Returns the TextBox; the wrapper is what you add to a layout.
        public static TextBox Field(out Control wrapper, int width, string text)
        {
            var frame = new Panel { Size = new Size(width, 26), BackColor = Theme.Border, Padding = new Padding(1), Margin = new Padding(0, 0, 10, 0) };
            var inner = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Input, Padding = new Padding(6, 4, 6, 0) };
            var box = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text, Text = text };
            inner.Controls.Add(box); frame.Controls.Add(inner);
            wrapper = frame;
            return box;
        }

        public static Label Lbl(string text, Color? color = null) => new Label { Text = text, AutoSize = true, ForeColor = color ?? Theme.TextDim, Margin = new Padding(0, 5, 6, 0) };

        public static CheckBox Check(string text, bool on) =>
            new CheckBox { Text = text, AutoSize = true, Checked = on, ForeColor = Theme.Text, Margin = new Padding(0, 4, 14, 0), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
    }
}
