using System.Runtime.InteropServices;

namespace Wsg.Switcher;

internal sealed class ThemeRadioButton : RadioButton
{
    protected override void OnPaint(PaintEventArgs e) {
        using var background = new SolidBrush(BackColor); e.Graphics.FillRectangle(background, ClientRectangle);
        using var border = new Pen(FlatAppearance.BorderColor, FlatAppearance.BorderSize);
        e.Graphics.DrawRectangle(border, 1, 1, Width - 2, Height - 2);
        var text = Enabled ? ForeColor : BackColor.GetBrightness() < .5f ? Color.FromArgb(163, 182, 191) : Color.FromArgb(65, 85, 93);
        var bounds = Rectangle.Inflate(ClientRectangle, -5, -3);
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        if (TextRenderer.MeasureText(Text, Font).Width > bounds.Width) {
            flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            var size = TextRenderer.MeasureText(e.Graphics, Text, Font, new Size(bounds.Width, 0), flags);
            bounds.Y += Math.Max(0, (bounds.Height - size.Height) / 2);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, text, flags);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), text, BackColor);
    }
}

internal sealed class ThemeCheckBox : CheckBox
{
    protected override void OnPaint(PaintEventArgs e) {
        using var background = new SolidBrush(BackColor); e.Graphics.FillRectangle(background, ClientRectangle);
        var state = Enabled ? (Checked ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal)
            : (Checked ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedDisabled : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedDisabled);
        var size = CheckBoxRenderer.GetGlyphSize(e.Graphics, state);
        CheckBoxRenderer.DrawCheckBox(e.Graphics, new Point(0, (Height - size.Height) / 2), state);
        var text = Enabled ? ForeColor : BackColor.GetBrightness() < .5f ? Color.FromArgb(163, 182, 191) : Color.FromArgb(95, 112, 120);
        var bounds = new Rectangle(size.Width + 4, 0, Width - size.Width - 4, Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, text, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, bounds, text, BackColor);
    }
}

internal sealed class ThemeButton : Button
{
    protected override void OnPaint(PaintEventArgs e) {
        var dark = BackColor.GetBrightness() < .5f;
        using var background = new SolidBrush(BackColor); e.Graphics.FillRectangle(background, ClientRectangle);
        using var border = new Pen(FlatAppearance.BorderColor);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        var foreground = Enabled ? ForeColor : (dark ? Color.FromArgb(163, 182, 191) : Color.FromArgb(95, 112, 120));
        TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Inflate(ClientRectangle, -6, -3), foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4), foreground, BackColor);
    }
}

internal static class WindowTheme
{
    internal static Color Background(bool light) => ColorTranslator.FromHtml(light ? "#edf3f7" : "#0d1418");
    internal static Color Surface(bool light) => ColorTranslator.FromHtml(light ? "#ffffff" : "#17242a");
    internal static Color Foreground(bool light) => ColorTranslator.FromHtml(light ? "#183142" : "#edf7f6");
    internal static Color Accent(bool light) => ColorTranslator.FromHtml(light ? "#168060" : "#78d8b7");

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    internal static void Caption(Form form, bool light) {
        if (!form.IsHandleCreated) return;
        var dark = light ? 0 : 1;
        DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
        var background = ColorTranslator.ToWin32(Background(light));
        var foreground = ColorTranslator.ToWin32(Foreground(light));
        var border = ColorTranslator.ToWin32(light ? Color.FromArgb(169, 189, 200) : Color.FromArgb(48, 74, 83));
        DwmSetWindowAttribute(form.Handle, 34, ref border, sizeof(int));
        DwmSetWindowAttribute(form.Handle, 35, ref background, sizeof(int));
        DwmSetWindowAttribute(form.Handle, 36, ref foreground, sizeof(int));
    }

    internal static void Apply(Form form, bool light) {
        void Paint(Control control) {
            control.ForeColor = Foreground(light);
            control.BackColor = control is TextBoxBase or ListBox or ComboBox ? Surface(light) : Background(light);
            if (control is Button button) {
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = Surface(light);
                button.FlatAppearance.BorderColor = light ? Color.FromArgb(169, 189, 200) : Color.FromArgb(48, 74, 83);
            }
            foreach (Control child in control.Controls) Paint(child);
        }
        Paint(form); Caption(form, light);
    }

    internal static void Bind(Form form, bool light) {
        form.HandleCreated += (_, _) => Caption(form, light);
        Apply(form, light);
    }
}
