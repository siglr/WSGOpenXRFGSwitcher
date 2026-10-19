using Wsg.OpenXR;

namespace Wsg.Switcher;

public sealed partial class MainForm
{
    void ChooseModFolder() {
        using var dialog = new Form { Text = "Mod installation folder", Font = Font, ClientSize = new Size(720, 360), MinimumSize = new Size(650, 390), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        string? chosen = state.ProviderRoot;
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 1, RowCount = 5, Padding = new Padding(18) };
        for (var row = 0; row < 5; row++) content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); dialog.Controls.Add(content);
        content.Controls.Add(TextLabel("Choose the root folder for new mod installations", 12, true));
        var path = new TextBox { ReadOnly = true, Dock = DockStyle.Top, Text = ProviderPaths.Root(storage, state) }; content.Controls.Add(path);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        var browse = Button("Browse…"); var defaults = Button("Use defaults"); buttons.Controls.Add(browse); buttons.Controls.Add(defaults); content.Controls.Add(buttons);
        var details = new Label { AutoSize = true, MaximumSize = new Size(665, 0), Text = "New OFXR installs use separate fork and version subfolders. New VectorXR installs use a VectorXR subfolder through the official installer.\n\nExisting installations are not moved. To relocate VectorXR, uninstall it first, then reinstall. Defaults: OFXR in WSG’s data folder; VectorXR in Program Files. Settings and notes stay in WSG’s data folder." }; content.Controls.Add(details);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft };
        var save = Button("Save folder"); var cancel = Button("Cancel"); cancel.DialogResult = DialogResult.Cancel;
        actions.Controls.Add(cancel); actions.Controls.Add(save); content.Controls.Add(actions); dialog.CancelButton = cancel; dialog.AcceptButton = save;
        browse.Click += (_, _) => {
            using var picker = new FolderBrowserDialog { Description = "Choose a root folder for future OpenXR mod installations", UseDescriptionForTitle = true, SelectedPath = path.Text };
            if (picker.ShowDialog(dialog) == DialogResult.OK) { chosen = picker.SelectedPath; path.Text = chosen; }
        };
        defaults.Click += (_, _) => { chosen = null; path.Text = Path.Combine(storage.Root, "providers"); };
        save.Click += (_, _) => {
            try {
                if (preview) throw new InvalidOperationException("UI preview: folder changes are disabled.");
                if (chosen != null) {
                    chosen = ProviderPaths.ValidateRoot(chosen);
                    var appFolder = AppContext.BaseDirectory.TrimEnd('\\');
                    if (chosen.Equals(appFolder, StringComparison.OrdinalIgnoreCase) || chosen.StartsWith(appFolder + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose a folder outside the WSG application package so updates cannot remove your mods.");
                    if (state.Installations.Any(i => chosen.Equals(i.Directory, StringComparison.OrdinalIgnoreCase) || chosen.StartsWith(i.Directory.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))) throw new IOException("Choose a root outside existing provider installations.");
                    ProviderPaths.CheckWritable(chosen);
                }
                state.ProviderRoot = chosen; storage.Save(state); dialog.DialogResult = DialogResult.OK; dialog.Close();
            } catch (Exception ex) { CenteredMessageBox.Show(dialog, ex.Message, "Mod folder", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        };
        WindowTheme.Bind(dialog, state.LightTheme);
        if (dialog.ShowDialog(this) == DialogResult.OK) { UpdateSelection(); SetStatus("Mod folder saved for future installations. Existing mods, settings and runtime were not changed."); }
    }
}
