namespace Wsg.Switcher;

// AutoSize calculates height only after the label is constrained to its parent's width.
internal sealed class WrappingLabel : Label
{
    Control? widthOwner;
    public WrappingLabel() { AutoSize = true; AutoEllipsis = false; }
    protected override void OnParentChanged(EventArgs e) {
        if (widthOwner != null) widthOwner.ClientSizeChanged -= UpdateWidth;
        base.OnParentChanged(e);
        widthOwner = Parent;
        if (widthOwner != null) widthOwner.ClientSizeChanged += UpdateWidth;
        UpdateWidth(this, EventArgs.Empty);
    }
    void UpdateWidth(object? sender, EventArgs e) {
        if (widthOwner == null) return;
        var width = Math.Max(1, widthOwner.ClientSize.Width - widthOwner.Padding.Horizontal - Margin.Horizontal);
        if (MaximumSize.Width != width) MaximumSize = new Size(width, 0);
    }
    protected override void Dispose(bool disposing) {
        if (disposing && widthOwner != null) widthOwner.ClientSizeChanged -= UpdateWidth;
        base.Dispose(disposing);
    }
}
