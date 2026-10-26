using Wsg.OpenXR;

namespace Wsg.Switcher;

public sealed partial class MainForm
{
    async Task ApplyStartupPreset() {
        var preset = new LaunchOptions(ApplyDefault: true).Resolve(state);
        state.SelectedGameId = preset.Id;
        state.Games.Single(g => g.Id == preset.Id).Provider = preset.Provider;
        loading = true; ReloadGames(); loading = false; LoadGame();
        SetStatus("Applying startup preset…");
        await StartupLaunch.Apply(storage, state, host, providers.Catalog, preset);
        observed = coordinator.Fingerprint();
        SetStatus("Startup preset applied. Ready for the next game launch.");
    }
    void ChooseStartup() {
        SaveControls();
        using var dialog = CreateStartupDialog();
        dialog.ShowDialog(this);
    }
    Form CreateStartupDialog() {
        var dialog = new Form { Text = "Startup", Font = Font, ClientSize = new Size(680, 380),
            MinimumSize = new Size(700, 0), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, FormBorderStyle = FormBorderStyle.FixedDialog };
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, MinimumSize = new Size(680, 0), Padding = new Padding(20), ColumnCount = 1 };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); dialog.Controls.Add(content);
        void Row(Control control) { content.RowStyles.Add(new RowStyle(SizeType.AutoSize)); content.Controls.Add(control, 0, content.RowCount++); }
        Row(TextLabel("Default startup preset", 13, true));
        var game = Combo(); game.Width = 620; game.AccessibleName = "Startup game preset";
        game.Tag = GameDiscovery.Labels(state.Games); foreach (var preset in state.Games) game.Items.Add(preset);
        game.SelectedItem = state.Games.FirstOrDefault(g => g.Id == state.Startup.GameId) ?? loadedGame;
        Row(game);
        var provider = Combo(); provider.Width = 250; provider.AccessibleName = "Startup provider";
        var ids = new[] { ProviderId.Off }.Concat(ProviderInfo.DisplayOrder.Where(id => id != ProviderId.Off)).ToArray();
        provider.Items.AddRange(ids.Select(ProviderInfo.NameOf).Cast<object>().ToArray());
        provider.SelectedIndex = Array.IndexOf(ids, state.Startup.GameId == null ? loadedGame?.Provider ?? ProviderId.Off : state.Startup.Provider);
        Row(provider);
        Row(TextLabel("Uses this game's saved version and settings for the chosen mod.", 9));
        var auto = new ThemeCheckBox { Text = "Apply this preset when WSG starts", Checked = state.Startup.ApplyOnLaunch, AutoSize = true };
        var minimized = new ThemeCheckBox { Text = "Start in tray", Checked = state.Startup.StartMinimized, AutoSize = true };
        Row(auto); Row(minimized);
        Row(TextLabel("Launch script: --apply-default applies once and exits, leaving the setup active. Wait for exit code 0 before starting the game.\n--tray opens WSG in the tray. Startup errors are shown.", 9));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var cancel = Button("Cancel"); cancel.DialogResult = DialogResult.Cancel;
        var save = Button("Save");
        save.Click += (_, _) => {
            if (provider.SelectedIndex < 0 || (auto.Checked && game.SelectedItem is not GamePreset)) return;
            state.Startup = new() { GameId = (game.SelectedItem as GamePreset)?.Id, Provider = ids[provider.SelectedIndex],
                ApplyOnLaunch = auto.Checked, StartMinimized = minimized.Checked };
            if (!preview) storage.Save(state);
            dialog.DialogResult = DialogResult.OK;
        };
        actions.Controls.Add(cancel); actions.Controls.Add(save); Row(actions);
        dialog.CancelButton = cancel; dialog.AcceptButton = save; WindowTheme.Bind(dialog, state.LightTheme);
        return dialog;
    }
}
