using System.Drawing.Drawing2D;

namespace MouseWithOutBorders;

internal static class DarkTheme
{
    internal static readonly Color Background = Color.FromArgb(17, 23, 33);
    internal static readonly Color Surface = Color.FromArgb(27, 36, 49);
    internal static readonly Color Text = Color.FromArgb(235, 241, 249);
    internal static readonly Color Muted = Color.FromArgb(177, 191, 208);
    internal static readonly Color Accent = Color.FromArgb(113, 180, 255);
    internal static readonly Color Hover = Color.FromArgb(44, 60, 79);
    internal static readonly Color Selected = Color.FromArgb(39, 76, 115);

    internal static void PaintButton(Control control, PaintEventArgs e, bool selected, bool hover, bool focus)
    {
        bool primary = control.BackColor == Accent;
        Color background = !control.Enabled ? (selected ? Selected : Surface) : selected ? Selected :
            hover ? (primary ? Color.FromArgb(145, 199, 255) : Hover) : control.BackColor;
        Color foreground = !control.Enabled ? Muted : control.ForeColor;
        e.Graphics.Clear(background);
        using Pen border = new(selected || focus ? Accent : Color.FromArgb(93, 115, 141));
        e.Graphics.DrawRectangle(border, 0, 0, control.Width - 1, control.Height - 1);
        TextRenderer.DrawText(e.Graphics, control.Text, control.Font,
            Rectangle.Inflate(control.ClientRectangle, -8, -4), foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        if (focus) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(control.ClientRectangle, -4, -4), foreground, background);
    }
}

internal sealed class DarkButton : Button
{
    private bool _hover;
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e) => DarkTheme.PaintButton(this, e, false, _hover, Focused && ShowFocusCues);
}

internal sealed class DarkRoleButton : RadioButton
{
    private bool _hover;
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e) => DarkTheme.PaintButton(this, e, Checked, _hover, Focused && ShowFocusCues);
}

internal sealed class DarkCheckBox : CheckBox
{
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        int side = Math.Min(18 * DeviceDpi / 96, Height - 4);
        Rectangle box = new(1, (Height - side) / 2, side, side);
        using SolidBrush fill = new(Checked ? DarkTheme.Accent : DarkTheme.Surface);
        using Pen border = new(Focused ? DarkTheme.Accent : DarkTheme.Muted);
        e.Graphics.FillRectangle(fill, box);
        e.Graphics.DrawRectangle(border, box);
        if (Checked)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using Pen tick = new(DarkTheme.Background, Math.Max(2, DeviceDpi / 48f));
            e.Graphics.DrawLines(tick, new PointF[] { new(box.Left + side * .2f, box.Top + side * .5f),
                new(box.Left + side * .43f, box.Top + side * .73f), new(box.Left + side * .82f, box.Top + side * .25f) });
        }
        Rectangle label = new(box.Right + 10, 0, Width - box.Right - 10, Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, label, Enabled ? ForeColor : DarkTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, label, DarkTheme.Text, BackColor);
    }
}

internal sealed class DarkComboBox : ComboBox
{
    internal DarkComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 26;
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using SolidBrush background = new(selected ? DarkTheme.Selected : DarkTheme.Surface);
        e.Graphics.FillRectangle(background, e.Bounds);
        string text = (e.Index >= 0 && e.Index < Items.Count ? GetItemText(Items[e.Index]) : Text) ?? "";
        TextRenderer.DrawText(e.Graphics, text, Font, Rectangle.Inflate(e.Bounds, -6, 0),
            Enabled ? DarkTheme.Text : DarkTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg is not (0x000F or 0x0317 or 0x0318)) return; // PAINT / PRINT / PRINTCLIENT
        if (message.Msg != 0x000F && message.WParam == IntPtr.Zero) return;
        using Graphics graphics = message.Msg == 0x000F ? Graphics.FromHwnd(Handle) : Graphics.FromHdc(message.WParam);
        int arrowWidth = SystemInformation.VerticalScrollBarWidth + 6;
        using SolidBrush surface = new(DarkTheme.Surface);
        graphics.FillRectangle(surface, Width - arrowWidth, 1, arrowWidth - 1, Height - 2);
        using Pen stroke = new(Enabled ? DarkTheme.Text : DarkTheme.Muted, Math.Max(1, DeviceDpi / 96f));
        int middle = Width - arrowWidth / 2;
        graphics.DrawLines(stroke, new Point[] { new(middle - 4, Height / 2 - 2), new(middle, Height / 2 + 2), new(middle + 4, Height / 2 - 2) });
        using Pen border = new(Focused ? DarkTheme.Accent : DarkTheme.Muted);
        graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
}

internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    internal DarkMenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? DarkTheme.Text : DarkTheme.Muted;
        base.OnRenderItemText(e);
    }
    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => DarkTheme.Surface;
        public override Color ImageMarginGradientBegin => DarkTheme.Surface;
        public override Color ImageMarginGradientMiddle => DarkTheme.Surface;
        public override Color ImageMarginGradientEnd => DarkTheme.Surface;
        public override Color MenuItemSelected => DarkTheme.Selected;
        public override Color MenuItemBorder => DarkTheme.Accent;
        public override Color MenuBorder => DarkTheme.Hover;
        public override Color SeparatorDark => DarkTheme.Hover;
        public override Color SeparatorLight => DarkTheme.Hover;
    }
}
