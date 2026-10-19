namespace Wsg.Switcher;

public sealed partial class MainForm
{
    async Task CleanStoredFiles() {
        if (preview) throw new InvalidOperationException("UI preview: cleanup is disabled.");
        using var dialog = new Form { Text = "Clean stored files", Font = Font, ClientSize = new Size(660, 230),
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(680, 0),
            StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, FormBorderStyle = FormBorderStyle.FixedDialog };
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(660, 0), Padding = new Padding(20), ColumnCount = 1, RowCount = 3 };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize)); content.RowStyles.Add(new RowStyle(SizeType.AutoSize)); content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        dialog.Controls.Add(content);
        content.Controls.Add(TextLabel("Backups, downloads and unused presets", 13, true));
        content.Controls.Add(TextLabel("Deletes completed recovery copies, cached installers, and saved version presets whose mod version is no longer installed.\n\nKeeps installed mods, the applied preset, current settings and test notes. Unfinished operations keep their recovery files.", 10));
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = Button("Cancel"); cancel.DialogResult = DialogResult.Cancel;
        var clean = Button("Clean now"); clean.DialogResult = DialogResult.OK;
        actions.Controls.Add(cancel); actions.Controls.Add(clean); content.Controls.Add(actions); dialog.CancelButton = cancel; dialog.AcceptButton = clean;
        WindowTheme.Bind(dialog, state.LightTheme);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SaveControls(); SetStatus("Cleaning stored files…");
        var result = await Task.Run(storage.CleanupCompletedOperations);
        SetStatus(result.RecoveryPending ? "Nothing removed: finish or recover the pending operation first. Its recovery files are still needed."
            : $"Cleaned {result.FilesDeleted} files ({result.BytesDeleted / 1048576d:0.0} MB)."
                + (result.DeferredItems > 0 ? " Some files were kept because they are in use, modified or unrecognized. Details: switcher.log in Data folder." : " Installed mods, current presets and notes kept."));
    }
}
