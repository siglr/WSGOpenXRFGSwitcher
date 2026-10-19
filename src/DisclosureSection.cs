namespace Wsg.Switcher;

// Ordinary buttons keep expand/collapse available to keyboard and screen-reader users.
internal sealed class DisclosureSection : TableLayoutPanel
{
    readonly DisclosureToggle toggle = new() { AutoSize = false, Height = 36, Dock = DockStyle.Top,
        TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat, UseMnemonic = false,
        Padding = new Padding(6, 2, 6, 2), Cursor = Cursors.Hand, Margin = Padding.Empty };
    readonly DisclosureBody body = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = new Padding(10, 8, 0, 0), Dock = DockStyle.Top, Visible = false };
    string title = "Details";
    bool expanded;
    public DisclosureSection() {
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; ColumnCount = 1; RowCount = 2; Margin = new Padding(0, 0, 0, 6);
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        RowStyles.Add(new RowStyle(SizeType.AutoSize)); RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(toggle, 0, 0); Controls.Add(body, 0, 1);
        toggle.Click += (_, _) => SetExpanded(!expanded); UpdateHeader();
    }
    internal bool Expanded => expanded;
    internal Rectangle HeaderBounds => toggle.RectangleToScreen(toggle.ClientRectangle);
    internal void SetTitle(string value) { title = value; UpdateHeader(); }
    internal void SetExpanded(bool value) { expanded = value; body.Visible = value; UpdateHeader(); }
    internal void SetHeaderColor(Color value) { toggle.ForeColor = value; }
    void UpdateHeader() {
        toggle.Text = title; toggle.Expanded = expanded; toggle.Invalidate();
        toggle.AccessibleName = title + (expanded ? ", expanded" : ", collapsed");
        toggle.AccessibleDescription = "Press Space or Enter to " + (expanded ? "collapse" : "expand") + ".";
    }
    internal void AddRow(Control control) {
        control.Dock = DockStyle.None;
        control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        body.Controls.Add(control);
    }
}

// Measure every row at the width it will actually receive. Nested percent-width
// tables can measure a wrapping button row at its minimum width, then retain the
// height of three rows even though the buttons fit on one row when displayed.
internal sealed class DisclosureBody : Panel
{
    public override Size GetPreferredSize(Size proposedSize) {
        var width = Math.Max(1, (Parent?.ClientSize.Width ?? Width) - Margin.Horizontal);
        var height = Padding.Vertical;
        foreach (Control child in Controls) {
            if (!child.Visible) continue;
            var available = Math.Max(1, width - Padding.Horizontal - child.Margin.Horizontal);
            height += child.GetPreferredSize(new Size(available, 0)).Height + child.Margin.Vertical;
        }
        return new Size(width, height);
    }
    protected override void OnLayout(LayoutEventArgs e) {
        var y = Padding.Top;
        foreach (Control child in Controls) {
            if (!child.Visible) continue;
            var width = Math.Max(1, ClientSize.Width - Padding.Horizontal - child.Margin.Horizontal);
            var height = child.GetPreferredSize(new Size(width, 0)).Height;
            child.SetBounds(Padding.Left + child.Margin.Left, y + child.Margin.Top, width, height);
            y += height + child.Margin.Vertical;
        }
        base.OnLayout(e);
    }
}

internal sealed class DisclosureToggle : Button
{
    internal bool Expanded;
    protected override void OnPaint(PaintEventArgs e) {
        using var background = new SolidBrush(BackColor); e.Graphics.FillRectangle(background, ClientRectangle);
        using var border = new Pen(FlatAppearance.BorderColor); e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        var foreground = Enabled ? ForeColor : BackColor.GetBrightness() < .5f ? Color.FromArgb(163, 182, 191) : Color.FromArgb(95, 112, 120);
        // Fixed DPI-scaled gutters prevent text length, focus and expansion from moving the arrow.
        var gutter = (int)Math.Round(30 * DeviceDpi / 96f);
        TextRenderer.DrawText(e.Graphics, Expanded ? "▾" : "▸", Font, new Rectangle(0, 0, gutter, Height), foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(gutter, 0, Math.Max(0, Width - gutter - 8), Height), foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4), foreground, BackColor);
    }
}
