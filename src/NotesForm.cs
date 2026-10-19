using Wsg.OpenXR;

namespace Wsg.Switcher;

public sealed class NotesForm : Form
{
    public NotesForm(Storage storage, GamePreset game, string version, string runtime, bool light, bool applied) {
        Text = "Test notes • " + game.Name; ClientSize = new Size(830, 790); MinimumSize = new Size(780, 760);
        StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 10); ShowInTaskbar = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 0 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); Controls.Add(layout);
        void Row(Control control, float height) {
            var row = layout.RowCount++; layout.RowStyles.Add(new RowStyle(height == 0 ? SizeType.Percent : SizeType.Absolute, height == 0 ? 100 : height));
            control.Dock = DockStyle.Fill; layout.Controls.Add(control, 0, row);
        }
        Label Label(string text, float size = 10, bool bold = false) => new() { Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), AutoSize = false };
        Row(Label("Record a test run", 20, true), 42);
        Row(Label("1. Apply a setup and play the same scene.   2. Write what you noticed.   3. Save and compare below.\nFPS values are optional and entered by you. WSG does not measure performance."), 54);
        var setupSource = applied ? "Applied setup snapshot" : "Selected preset — not verified as applied";
        var summary = Label(game.Name + "  •  " + ProviderInfo.NameOf(game.Provider) + (game.Provider == ProviderId.Off ? "" : "  •  " + version)
            + "\n" + (game.Provider == ProviderId.Off ? "Frame generation off" : SettingsSummary(game.Provider, game.Settings.GetValueOrDefault(game.Provider) ?? EngineSettings.Defaults(game.Provider)))
            + (game.KeepVectorFeatures && game.Provider != ProviderId.VectorXR ? " • VectorXR enhancements retained" : ""), 10, true);
        Row(summary, 62);
        Row(Label(applied ? "Uses the last verified applied settings, even if you have since edited your selection."
            : "Unapplied selection: confirm you actually tested these settings before saving."), 32);
        var fps = new FlowLayoutPanel { WrapContents = false };
        var render = new TextBox { Width = 100, PlaceholderText = "Optional", AccessibleName = "Game FPS" };
        var display = new TextBox { Width = 100, PlaceholderText = "Optional", AccessibleName = "Headset FPS" };
        fps.Controls.Add(new Label { Text = "Game FPS", AutoSize = true, Margin = new Padding(0, 6, 12, 0) }); fps.Controls.Add(render);
        fps.Controls.Add(new Label { Text = "Headset / display FPS", AutoSize = true, Margin = new Padding(24, 6, 12, 0) }); fps.Controls.Add(display); Row(fps, 42);
        Row(Label("What did you notice?", 10, true), 26);
        var notes = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, PlaceholderText = "Scene / route, headset refresh rate, resolution, smoothness, artifacts, latency…\r\nIf entering FPS, include the counter or overlay you used.", AccessibleName = "Test observations" };
        Row(notes, 105);
        var confirm = new CheckBox { Text = "I tested the selected preset shown above", AutoSize = true, Visible = !applied };
        Row(confirm, applied ? 1 : 30);
        var actions = new FlowLayoutPanel { WrapContents = false, Padding = new Padding(0, 5, 0, 0) };
        var save = new ThemeButton { Text = "Save test note", AutoSize = true, Height = 34, Enabled = applied };
        var export = new ThemeButton { Text = "Export all notes to CSV…", AutoSize = true, Height = 34 };
        var result = new Label { AutoSize = true, Margin = new Padding(12, 8, 0, 0) };
        actions.Controls.Add(save); actions.Controls.Add(export); actions.Controls.Add(result); Row(actions, 48);
        void UpdateSave() {
            save.Enabled = applied || confirm.Checked;
            save.BackColor = save.Enabled ? WindowTheme.Accent(light) : WindowTheme.Surface(light);
            save.ForeColor = save.Enabled ? (light ? Color.White : WindowTheme.Background(false)) : WindowTheme.Foreground(light);
        }
        confirm.CheckedChanged += (_, _) => UpdateSave();
        var historyHeading = Label("Saved runs for this game", 12, true); Row(historyHeading, 30);
        var history = new ListBox { IntegralHeight = false, AccessibleName = "Saved test runs" }; Row(history, 92);
        var detail = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "Saved run details" }; Row(detail, 0);
        var entries = new List<ComparisonNote>();
        void ReloadHistory() {
            entries = storage.ReadNotes().Where(n => n.Executable.Equals(game.Executable, StringComparison.OrdinalIgnoreCase)).OrderByDescending(n => n.Time).ToList();
            history.Items.Clear();
            foreach (var n in entries) history.Items.Add($"{n.Time.LocalDateTime:g}  •  {ProviderInfo.NameOf(n.Provider)} {n.Version}  •  Game FPS: {(n.RenderFps.Length == 0 ? "—" : n.RenderFps)}  •  Display: {(n.DisplayFps.Length == 0 ? "—" : n.DisplayFps)}");
            historyHeading.Text = $"Saved runs for this game ({entries.Count}) — select one to review";
            if (entries.Count > 0) history.SelectedIndex = 0;
            else detail.Text = "No notes yet. Save your first test above, then try another mod or Off using the same scene.";
        }
        history.SelectedIndexChanged += (_, _) => {
            if (history.SelectedIndex < 0) return; var n = entries[history.SelectedIndex];
            detail.Text = n.SetupSource + "\r\n" + (n.Provider == ProviderId.Off ? "Frame generation off" : SettingsSummary(n.Provider, n.Settings))
                + (n.KeepVectorFeatures && n.Provider != ProviderId.VectorXR ? " • VectorXR enhancements retained" : "") + "\r\nRuntime: " + n.Runtime + "\r\n\r\n" + n.Notes;
        };
        save.Click += (_, _) => {
            try {
                foreach (var field in new[] { render, display }) if (field.Text.Trim().Length > 0 && (!decimal.TryParse(field.Text, out var number) || number < 0)) { result.Text = "Enter a valid FPS value."; field.Focus(); return; }
                if (string.IsNullOrWhiteSpace(notes.Text) && string.IsNullOrWhiteSpace(render.Text) && string.IsNullOrWhiteSpace(display.Text)) { result.Text = "Add an observation or FPS."; notes.Focus(); return; }
                storage.AddNote(new(DateTimeOffset.Now, game.Name, game.Executable, game.Provider, version, Json.Read<EngineSettings>(Json.Write(game.For(game.Provider))), game.KeepVectorFeatures, runtime, render.Text.Trim(), display.Text.Trim(), notes.Text.Trim()) { SetupSource = setupSource });
                ReloadHistory(); result.Text = "Saved locally."; notes.Clear(); render.Clear(); display.Clear();
            } catch (Exception ex) { CenteredMessageBox.Show(this, ex.Message, "Could not save note"); }
        };
        export.Click += (_, _) => {
            using var file = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "WSG-OpenXR-comparisons.csv" };
            if (file.ShowDialog(this) != DialogResult.OK) return;
            try { storage.ExportNotes(file.FileName); result.Text = "All games exported."; }
            catch (Exception ex) { CenteredMessageBox.Show(this, ex.Message, "Could not export notes"); }
        };
        ReloadHistory(); WindowTheme.Bind(this, light);
        UpdateSave();
    }

    internal static string SettingsSummary(ProviderId provider, EngineSettings s) {
        var method = s.Method switch { "nvidia" => "NVIDIA optical flow", "fidelityfx" => "FidelityFX optical flow", "vectors" => "DLSS motion vectors", "dlss" => "Native DLSS FG", "hybrid" => "DLSS + FidelityFX", "extrapolate" => "Extrapolate", "interpolate" => "Interpolate", _ => s.Method };
        var text = $"{method} • {s.Multiplier}";
        if (provider == ProviderId.VectorXR || s.Method == "nvidia") text += $" • {s.Quality}";
        text += $" • overlay {(s.Overlay ? "on" : "off")}";
        if (provider is ProviderId.Djules or ProviderId.Elliott) {
            if (s.Method != "dlss") text += $" • flow {s.FlowResolution}%";
            text += $" • prefer FPS {(s.PreferFps ? "on" : "off")} • reduced VRAM {(s.LowerVram ? "on" : "off")}";
        }
        return text;
    }
}
