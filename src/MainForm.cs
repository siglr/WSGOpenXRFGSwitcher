using System.Diagnostics;
using System.Reflection;
using Wsg.OpenXR;

namespace Wsg.Switcher;

public sealed partial class MainForm : Form
{
    readonly Storage storage;
    readonly AppState state;
    readonly WindowsHost host;
    readonly ProviderManager providers;
    readonly SwitchCoordinator coordinator;
    readonly bool preview;
    readonly ComboBox games = Combo(), method = Combo(), multiplier = Combo(), quality = Combo(), scale = Combo(), overlayPosition = Combo(), overlaySize = Combo();
    readonly CheckBox hybrid = new ThemeCheckBox() { Text = "Hybrid (DLSS game motion + optical flow)", AutoSize = true };
    readonly CheckBox hybridDepth = new ThemeCheckBox() { Text = "Hybrid depth search", AutoSize = true };
    readonly CheckBox overlayLatency = new ThemeCheckBox() { Text = "Show latency", AutoSize = true };
    readonly CheckBox overlayMultiplier = new ThemeCheckBox() { Text = "Show multiplier", AutoSize = true };
    readonly CheckBox overlayHybrid = new ThemeCheckBox() { Text = "Show Hybrid status", AutoSize = true };
    readonly FlowLayoutPanel djulesOptions = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    readonly FlowLayoutPanel overlayDetails = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
    readonly Dictionary<ProviderId, RadioButton> choices = new();
    readonly Dictionary<ProviderId, ComboBox> versions = new();
    readonly Dictionary<ProviderId, Label> providerStatus = new();
    readonly CheckBox keepVector = new ThemeCheckBox() { Text = "Keep VectorXR's other features", AutoSize = true };
    readonly CheckBox overlay = new ThemeCheckBox() { Text = "Show provider overlay", AutoSize = true };
    readonly CheckBox preferFps = new ThemeCheckBox() { Text = "Prefer FPS over latency", AutoSize = true };
    readonly CheckBox lowerVram = new ThemeCheckBox() { Text = "Lower VRAM", AutoSize = true };
    readonly Label status = new WrappingLabel { Dock = DockStyle.Top, Padding = new Padding(12), TextAlign = ContentAlignment.MiddleLeft };
    readonly Label runtime = new WrappingLabel { Dock = DockStyle.Top };
    readonly Label setupHint = TextLabel("Choose a mod to edit its settings.", 10);
    readonly Label updateStatus = TextLabel("Provider updates are checked automatically. You choose when to install.", 9);
    readonly Button apply = Button("Apply setup"), advanced = Button("Advanced settings"), remove = Button("Remove this version…");
    readonly Panel workspace = new WorkspacePanel { Dock = DockStyle.Fill };
    readonly NotifyIcon tray;
    readonly System.Windows.Forms.Timer exitTimer = new() { Interval = 3000 };
    Button? exitButton;
    readonly ToolStripMenuItem exitMenu = new("Exit");
    readonly List<Button> navigation = [];
    readonly List<Control> surfaces = [];
    string observed = "";
    bool loading, busy, exitRequested, closing;
    GamePreset? loadedGame;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AllowClose { get; set; }
    public MainForm(Storage storage, AppState state, WindowsHost host, Catalog catalog, bool preview = false, bool startMinimized = false) {
        this.storage = storage; this.state = state; this.host = host; this.preview = preview;
        providers = new(storage, catalog, host); coordinator = new(storage, host, catalog.Verify, catalog.ValidateCoexistence, catalog.Get);
        var startHidden = !preview && (startMinimized || state.Startup.StartMinimized);
        if (startHidden) { Opacity = 0; ShowInTaskbar = false; }
        Text = "WSG OpenXR FG Switcher"; Font = new Font("Segoe UI", 10); ClientSize = new Size(1160, 850);
        MinimumSize = new Size(1050, 800); StartPosition = FormStartPosition.CenterScreen;
        using var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("wsg.ico")!;
        Icon = new Icon(iconStream);
        tray = new NotifyIcon { Icon = Icon, Text = "WSG OpenXR FG Switcher", Visible = !preview };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open WSG Switcher", null, (_, _) => Restore());
        exitMenu.Click += async (_, _) => await RequestExit(); menu.Items.Add(exitMenu);
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Restore();
        BuildLayout();
        games.DropDown += (_, _) => SizeGameDropDown();
        games.SelectedIndexChanged += (_, _) => { if (loading) return; SaveControls(); LoadGame(); };
        foreach (var c in choices.Values) c.CheckedChanged += (_, _) => { if (!loading && c.Checked) ChangeProvider(); };
        method.SelectedIndexChanged += (_, _) => { if (!loading) ChangeGenerationMethod(); };
        multiplier.SelectedIndexChanged += (_, _) => { if (!loading) UpdateCapabilityControls(); };
        hybrid.CheckedChanged += (_, _) => { if (!loading) UpdateCapabilityControls(); };
        overlayPosition.SelectedIndexChanged += (_, _) => { if (!loading) UpdateCapabilityControls(); };
        foreach (var version in versions.Values) version.SelectedIndexChanged += (_, _) => { if (!loading) UpdateVersionControls(); };
        keepVector.CheckedChanged += (_, _) => {
            if (loading) return;
            if (keepVector.Checked && loadedGame != null && !loadedGame.ExperimentalAcknowledged) {
                if (CenteredMessageBox.Show(this, "This keeps VectorXR enhancements such as Pivot alongside the selected provider. Frame generation will run through one provider only.\n\nThis combination has not been headset-tested by WSG. Close all games before applying it. Continue with experimental coexistence?", "VectorXR coexistence", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) { keepVector.Checked = false; return; }
                loadedGame.ExperimentalAcknowledged = true;
            }
            SaveControls();
        };
        apply.Click += async (_, _) => await Run(ApplySelected);
        remove.Click += async (_, _) => await Run(RemoveSelected);
        advanced.Click += async (_, _) => await Run(() => {
            if (preview) throw new InvalidOperationException("UI preview: provider launch is disabled.");
            if (loadedGame?.Provider is not (ProviderId.VectorXR or ProviderId.Tig3r)) return Task.CompletedTask;
            SaveControls(); providers.OpenAdvanced(SelectedInstallation() ?? throw new InvalidOperationException("Install this provider first."));
            SetStatus("Close the provider editor and Rescan setup before applying here.");
            return Task.CompletedTask;
        });
        FormClosing += (_, e) => {
            SaveBeforeExit();
            if (AllowClose || e.CloseReason == CloseReason.WindowsShutDown) return;
            e.Cancel = true; Hide(); if (!preview) tray.ShowBalloonTip(2500, Text, "Still available in the notification area. Use Exit for cleanup after games close.", ToolTipIcon.Info);
        };
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized && !preview) { SaveWindowPlacement(); Hide(); } };
        FormClosed += (_, _) => { exitTimer.Stop(); exitTimer.Dispose(); tray.Dispose(); };
        exitTimer.Tick += async (_, _) => { if (!busy && exitRequested) await RequestExit(); };
        Shown += async (_, _) => {
            if (startHidden) { Hide(); Opacity = 1; ShowInTaskbar = true; }
            loading = true; ReloadGames(); loading = false; LoadGame();
            if (preview) { UpdateCurrentSetup(); SetStatus("UI preview • Installation and switching are disabled."); return; }
            var initialized = false;
            await Run(async () => {
                if (!providers.RemovalRecoveryPending && !providers.InstallationRecoveryPending)
                    state.Installations = providers.Discover(state).DistinctBy(i => i.Executable, StringComparer.OrdinalIgnoreCase).ToList();
                PopulateVersions();
                var current = coordinator.Fingerprint();
                observed = state.LastAppliedFingerprint == null || state.LastAppliedFingerprint == current ? current : "";
                SetStatus(coordinator.RecoveryPending ? "An interrupted switch needs recovery. Use Recover before applying a setup." : observed.Length == 0 ? "External provider changes detected. Use Rescan setup before applying." : "");
                runtime.Text = "OpenXR runtime: " + Path.GetFileNameWithoutExtension(OpenXrRegistry.Runtime());
                storage.Save(state);
                if (state.Startup.ApplyOnLaunch) await ApplyStartupPreset();
                initialized = true;
            });
            sessionReady = true;
            if (!initialized) Restore();
            RefreshPollingMode();
            // Flight startup stays local. Deferred release checks run only when the window is opened.
            if (Visible && !state.Startup.ApplyOnLaunch) await CheckVisibleStartupUpdates();
        };
        SetLight(state.LightTheme);
        foreach (var combo in new[] { method, multiplier, quality, scale, overlayPosition, overlaySize }.Concat(versions.Values))
            combo.SelectedIndexChanged += (_, _) => { if (!loading) { SaveControls(); UpdateSelection(); } };
        foreach (var check in new[] { overlay, preferFps, lowerVram, keepVector, hybrid, hybridDepth, overlayLatency, overlayMultiplier, overlayHybrid })
            check.CheckedChanged += (_, _) => { if (!loading) { SaveControls(); UpdateSelection(); } };
        statusTimer.Tick += (_, _) => PollStatus();
        VisibleChanged += (_, _) => {
            RefreshPollingMode();
            if (sessionReady && Visible && !busy) RefreshOnReturn();
        };
        Activated += (_, _) => { if (sessionReady && Visible && !busy) RefreshOnReturn(); };
        FormClosed += (_, _) => statusTimer.Dispose();
        InitializeWindowPlacement();
    }
    static ComboBox Combo() {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, IntegralHeight = false, DropDownHeight = 220, FlatStyle = FlatStyle.Flat, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 23 };
        combo.DrawItem += (_, e) => {
            using var brush = new SolidBrush(combo.BackColor); e.Graphics.FillRectangle(brush, e.Bounds);
            var label = e.Index < 0 ? "" : combo.GetItemText(combo.Items[e.Index]) ?? "";
            if (e.Index >= 0 && combo.Items[e.Index] is GamePreset game && combo.Tag is Dictionary<string, string> gameLabels) label = gameLabels.GetValueOrDefault(game.Id, game.Name);
            if (combo.Tag is HashSet<string> installedVersions && installedVersions.Contains(label)) label += " • installed";
            label = label switch { "upper_left" => "Upper left", "upper_right" => "Upper right", "lower_left" => "Lower left", "lower_right" => "Lower right", "elliott_controller" => "Controller", "elliott_bottom" => "Bottom", "off" => "Off", "ofxr_reference" => "NVIDIA OFXR 0.2.1", "ofxr_djules" => "NVIDIA OFXR 0.2.13", "ofxr_new" => "NVIDIA OFXR 0.3.0", "fsr3" => "FSR 3.1", _ => label };
            label = label switch { "nvidia" => "NVIDIA optical flow", "fidelityfx" => "FidelityFX optical flow", "vectors" => "DLSS motion vectors", "dlss" => "Native DLSS FG", "hybrid" => combo.Tag is ProviderId.Tig3r ? "Hybrid (needs DLSS)" : "DLSS + FidelityFX", "extrapolate" => "Extrapolate", "interpolate" => "Interpolate", "dynamic" => "Dynamic", "auto" => "Auto", "medium" => "Medium", "slow" => "Slow / quality", "fast" => "Fast", "performance" => "Performance", "balanced" => "Balanced", "quality" => "Quality", "25" => "25%", "50" => "50%", "75" => "75%", "100" => "100%", "big" => "Big", "small" => "Small", _ => label };
            TextRenderer.DrawText(e.Graphics, label, combo.Font, new Rectangle(e.Bounds.X + 5, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height), combo.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        };
        return combo;
    }
    static Button Button(string text) => new ThemeButton { Text = text, UseMnemonic = false, AutoSize = true, MinimumSize = new Size(110, 36), FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 3, 8, 3), Cursor = Cursors.Hand };
    static void AlignInlineButton(Button button, ComboBox combo) {
        button.AutoSize = false; button.MinimumSize = new Size(110, 0); button.Padding = new Padding(8, 0, 8, 0);
        void Align() {
            button.Height = combo.Height;
            // Match ThemeButton's literal text rendering: '&' is visible, not a mnemonic.
            var textWidth = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
            button.Width = Math.Max(110, textWidth + (int)Math.Ceiling(32 * button.DeviceDpi / 96.0));
            button.Margin = combo.Margin;
        }
        combo.SizeChanged += (_, _) => Align(); combo.MarginChanged += (_, _) => Align();
        button.FontChanged += (_, _) => Align(); button.TextChanged += (_, _) => Align(); button.DpiChangedAfterParent += (_, _) => Align();
        button.ParentChanged += (_, _) => Align();
        Align();
    }
    static Label TextLabel(string text, float size = 10, bool bold = false) => new WrappingLabel { Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(0, 0, 0, 8) };
    Button AddNav(FlowLayoutPanel panel, string text, Func<Task> action, bool theme = false, bool exitAction = false) {
        var button = Button(text); button.Width = 168; button.AutoSize = false; button.Height = 42; button.Font = new Font("Segoe UI", 9); button.Padding = new Padding(4, 2, 4, 2); button.Margin = new Padding(0, 3, 0, 3); button.Tag = theme ? "theme" : null;
        button.Click += async (_, _) => { if (exitAction) await action(); else await Run(action); };
        navigation.Add(button); panel.Controls.Add(button); return button;
    }
    void ReloadGames() {
        games.Tag = GameDiscovery.Labels(state.Games);
        games.Items.Clear(); foreach (var game in state.Games) games.Items.Add(game);
        games.SelectedItem = state.Games.FirstOrDefault(g => g.Id == state.SelectedGameId) ?? state.Games.FirstOrDefault();
    }
    void SizeGameDropDown() {
        var labels = games.Tag as Dictionary<string, string>;
        var textWidth = labels?.Values.Select(label => TextRenderer.MeasureText(label, games.Font).Width).DefaultIfEmpty(0).Max() ?? 0;
        var origin = games.PointToScreen(Point.Empty);
        var right = Math.Min(RectangleToScreen(ClientRectangle).Right, Screen.FromControl(games).WorkingArea.Right);
        var available = Math.Max(games.Width, right - origin.X - 12);
        games.DropDownWidth = Math.Max(games.Width, Math.Min(available, textWidth + SystemInformation.VerticalScrollBarWidth + 16));
    }
    void LoadGame() {
        loadedGame = games.SelectedItem as GamePreset; state.SelectedGameId = loadedGame?.Id;
        loading = true;
        foreach (var pair in choices) pair.Value.Checked = pair.Key == (loadedGame?.Provider ?? ProviderId.Off);
        keepVector.Checked = loadedGame?.KeepVectorFeatures ?? false;
        PopulateVersions(); PopulateSettings(); loading = false; UpdateSelection();
        if (!preview) storage.Save(state);
    }
    void ChangeProvider() {
        if (loadedGame == null) return;
        // RadioButtons live in separate cards; explicitly enforce one selection.
        var chosen = choices.First(p => p.Value.Checked && p.Key != loadedGame.Provider).Key;
        SaveControls(); loadedGame.Provider = chosen; loading = true;
        foreach (var pair in choices) pair.Value.Checked = pair.Key == chosen;
        PopulateSettings(); loading = false; if (!preview) storage.Save(state); UpdateSelection();
    }
    void PopulateSettings() {
        var id = loadedGame?.Provider ?? ProviderId.Off; var settings = loadedGame?.For(id) ?? new();
        method.Items.Clear(); multiplier.Items.Clear(); quality.Items.Clear(); scale.Items.Clear(); method.Tag = id;
        if (id == ProviderId.VectorXR) { method.Items.AddRange(["interpolate", "extrapolate"]); multiplier.Items.AddRange(["dynamic", "auto", "2x", "3x", "4x"]); quality.Items.AddRange(["performance", "balanced", "quality"]); }
        else if (id == ProviderId.Tig3r) {
            method.Items.AddRange(Tig3rConfiguration.Routes.Cast<object>().ToArray());
            multiplier.Items.AddRange(Tig3rConfiguration.SupportsThreeX(settings.Method) ? ["2x", "3x"] : ["2x"]);
            quality.Items.AddRange(["fast", "medium", "slow"]);
        } else { method.Items.AddRange(id == ProviderId.Elliott ? ["nvidia", "fidelityfx", "vectors", "dlss", "hybrid", "extrapolate"] : ["nvidia", "fidelityfx"]); multiplier.Items.AddRange(id == ProviderId.Elliott ? ["2x", "3x", "auto"] : ["2x", "3x"]); quality.Items.AddRange(["fast", "medium", "slow"]); }
        method.Width = id == ProviderId.Tig3r ? 240 : 185;
        scale.Items.AddRange(ModernDjulesSelected() || id == ProviderId.Tig3r ? ["25", "50", "75", "100"] : ["50", "75", "100"]);
        method.SelectedItem = settings.Method; multiplier.SelectedItem = settings.Multiplier; quality.SelectedItem = settings.Quality; scale.SelectedItem = settings.FlowResolution.ToString();
        overlay.Checked = settings.Overlay; preferFps.Checked = settings.PreferFps; lowerVram.Checked = settings.LowerVram;
        overlayPosition.Items.Clear(); overlayPosition.Items.AddRange(id == ProviderId.Tig3r
            ? Tig3rConfiguration.OverlayPositions.Cast<object>().ToArray() : ["upper_left", "upper_right", "lower_left", "lower_right", "off"]);
        overlayPosition.SelectedItem = settings.Overlay ? settings.OverlayPosition : "off";
        hybrid.Checked = settings.Hybrid; hybridDepth.Checked = settings.HybridDepthSearch;
        overlaySize.Items.Clear(); overlaySize.Items.AddRange(["big", "medium", "small"]); overlaySize.SelectedItem = settings.OverlaySize;
        overlayLatency.Checked = settings.OverlayLatency; overlayMultiplier.Checked = settings.OverlayMultiplier; overlayHybrid.Checked = settings.OverlayHybridStatus;
        foreach (Control control in new Control[] { method, multiplier, quality }) control.Parent!.Visible = id != ProviderId.Off;
        overlay.Visible = id == ProviderId.VectorXR;
        overlayPosition.Parent!.Visible = ProviderInfo.IsOfxr(id);
        scale.Parent!.Visible = ProviderInfo.IsOfxr(id);
        preferFps.Visible = id is ProviderId.Djules or ProviderId.Elliott;
        lowerVram.Visible = ProviderInfo.IsOfxr(id);
        lowerVram.Text = id == ProviderId.Tig3r ? "Fast mode" : "Reduce VRAM use";
        hints.SetToolTip(lowerVram, id == ProviderId.Tig3r ? "Uses private synthesis textures and fewer swapchains. Can reduce VRAM or latency; performance varies by mode and game. Has no effect on Luke Ross AFW." : "Use the provider's reduced-VRAM mode.");
        keepVector.Visible = id != ProviderId.VectorXR; advanced.Visible = id is ProviderId.VectorXR or ProviderId.Tig3r; apply.Enabled = loadedGame != null;
        UpdateCapabilityControls();
    }
    void ChangeGenerationMethod() {
        if (loadedGame?.Provider == ProviderId.Tig3r && method.SelectedItem is string route) {
            var previous = loadedGame.For(ProviderId.Tig3r).Method;
            if (route != previous) {
                SaveControls(previous); loadedGame.SelectTig3rRoute(route);
                var wasLoading = loading; loading = true; PopulateSettings(); loading = wasLoading;
            }
        }
        UpdateCapabilityControls();
    }
    bool ModernDjulesSelected() => loadedGame?.Provider == ProviderId.Djules
        && providers.Catalog.Document.Releases.Any(r => r.Provider == ProviderId.Djules && r.Version == versions[ProviderId.Djules].SelectedItem as string && r.DjulesHybrid);
    void UpdateVersionControls() {
        if (loadedGame?.Provider == ProviderId.Tig3r) { UpdateCapabilityControls(); return; }
        if (loadedGame?.Provider != ProviderId.Djules) return;
        var wasLoading = loading; loading = true;
        var resolution = scale.SelectedItem as string ?? "50";
        scale.Items.Clear(); scale.Items.AddRange(ModernDjulesSelected() ? ["25", "50", "75", "100"] : ["50", "75", "100"]);
        scale.SelectedItem = scale.Items.Contains(resolution) ? resolution : "50";
        if (!ModernDjulesSelected()) { hybrid.Checked = false; hybridDepth.Checked = false; }
        loading = wasLoading; UpdateCapabilityControls();
    }
    void UpdateCapabilityControls() {
        var id = loadedGame?.Provider ?? ProviderId.Off;
        var native = id is ProviderId.Elliott or ProviderId.Tig3r && method.SelectedItem as string == "dlss";
        var forcedDepth = id is ProviderId.Djules or ProviderId.Elliott && multiplier.SelectedItem as string is "3x" or "auto";
        if (forcedDepth) preferFps.Checked = true;
        preferFps.Enabled = !busy && !forcedDepth;
        preferFps.Text = forcedDepth ? "Prefer FPS over latency (required)" : "Prefer FPS over latency";
        quality.Parent!.Visible = id == ProviderId.VectorXR || id == ProviderId.Tig3r && Tig3rConfiguration.UsesOpticalFlow(method.SelectedItem as string ?? "") || (id is ProviderId.Djules or ProviderId.Elliott && method.SelectedItem as string == "nvidia");
        scale.Parent!.Visible = id == ProviderId.Tig3r ? Tig3rConfiguration.UsesOpticalFlow(method.SelectedItem as string ?? "") : id is ProviderId.Djules or ProviderId.Elliott && !native;
        multiplier.Enabled = !busy && (id != ProviderId.Tig3r || Tig3rConfiguration.SupportsThreeX(method.SelectedItem as string ?? ""));
        djulesOptions.Visible = ModernDjulesSelected();
        overlayOptionsHeading.Visible = overlayDetails.Visible = ModernDjulesSelected();
        hybridDepth.Enabled = !busy && hybrid.Checked;
        // Keep the labels readable in both themes while the FPS overlay is off.
        overlaySize.Enabled = overlayLatency.Enabled = overlayMultiplier.Enabled = overlayHybrid.Enabled =
            !busy && overlayPosition.SelectedItem as string != "off";
        var beta = SelectedRelease()?.Prerelease == true ? "Beta • " : "";
        setupHint.Text = id == ProviderId.Tig3r ? beta + (native ? "DLSS-G needs compatible hardware and DLSS inputs; 3X needs supported multi-frame hardware."
            : method.SelectedItem as string is "hybrid" or "fsr3" ? "This mode needs compatible DLSS game inputs. F10 opens advanced settings."
            : "F10 opens advanced settings while in game.") : native ? "Native DLSS requires compatible NVIDIA hardware and game support." : "Choose a mod to edit its settings.";
    }
    void PopulateVersions() {
        var wasLoading = loading; loading = true;
        foreach (var provider in ProviderInfo.All) {
            var list = state.Installations.Where(i => i.Provider == provider.Id).ToArray();
            var combo = versions[provider.Id]; combo.Items.Clear();
            combo.Tag = list.Select(i => i.Version).ToHashSet(StringComparer.Ordinal);
            combo.DropDownWidth = 340;
            foreach (var release in providers.Catalog.Document.Releases.Where(r => r.Provider == provider.Id && providers.Catalog.Supports(r))) combo.Items.Add(release.Version);
            var desired = loadedGame?.Versions.GetValueOrDefault(provider.Id);
            combo.SelectedItem = desired; if (combo.SelectedIndex < 0) combo.SelectedIndex = combo.Items.Count - 1;
            providerStatus[provider.Id].Text = list.Length == 0 ? "Download available" : "Installed";
        }
        loading = wasLoading;
        UpdateSelection();
    }
    void SaveControls(string? methodOverride = null) {
        if (loading || loadedGame == null) return;
        var s = loadedGame.For(loadedGame.Provider);
        if (methodOverride != null) s.Method = methodOverride;
        else if (method.SelectedItem is string m) s.Method = m;
        if (multiplier.SelectedItem is string rate) s.Multiplier = rate;
        if (quality.SelectedItem is string q) s.Quality = q;
        if (int.TryParse(scale.SelectedItem as string, out var value)) s.FlowResolution = value;
        if (ProviderInfo.IsOfxr(loadedGame.Provider) && overlayPosition.SelectedItem is string position) {
            s.Overlay = position != "off"; if (s.Overlay) s.OverlayPosition = position;
        } else if (loadedGame.Provider == ProviderId.VectorXR) s.Overlay = overlay.Checked;
        s.PreferFps = preferFps.Checked; s.LowerVram = lowerVram.Checked;
        if (loadedGame.Provider == ProviderId.Djules) {
            s.Hybrid = ModernDjulesSelected() && hybrid.Checked;
            s.HybridDepthSearch = ModernDjulesSelected() && hybridDepth.Checked;
            if (overlaySize.SelectedItem is string size) s.OverlaySize = size;
            s.OverlayLatency = overlayLatency.Checked; s.OverlayMultiplier = overlayMultiplier.Checked; s.OverlayHybridStatus = overlayHybrid.Checked;
        }
        loadedGame.KeepVectorFeatures = keepVector.Checked;
        foreach (var pair in versions) if (pair.Value.SelectedItem is string version && version != "Not installed") loadedGame.Versions[pair.Key] = version;
        if (!preview) storage.Save(state);
    }
    Installation? SelectedInstallation() {
        if (loadedGame == null || loadedGame.Provider == ProviderId.Off) return null;
        return state.Installations.FirstOrDefault(i => i.Provider == loadedGame.Provider && i.Version == versions[loadedGame.Provider].SelectedItem as string);
    }
    CatalogRelease? SelectedRelease() {
        var id = loadedGame?.Provider ?? ProviderId.Off;
        return id == ProviderId.Off ? null : providers.Catalog.Document.Releases.FirstOrDefault(r => r.Provider == id && r.Version == versions[id].SelectedItem as string);
    }
    async Task ApplySelected() {
        if (preview) throw new InvalidOperationException("UI preview: switching is disabled.");
        SaveControls(); if (loadedGame == null) throw new InvalidOperationException("Add a game first.");
        RequireObservedSetup();
        var conflict = ReadCompatibility().FirstOrDefault(i => i.BlocksApply);
        if (conflict != null) throw new InvalidOperationException(conflict.Message);
        if (providers.InstallationRecoveryPending || providers.RemovalRecoveryPending) throw new InvalidOperationException("Use Recover operation to complete the interrupted operation before applying a setup.");
        if (observed.Length == 0) throw new InvalidOperationException("Rescan setup the current setup before applying.");
        if (loadedGame.Provider != ProviderId.Off && !File.Exists(loadedGame.Executable)) throw new IOException("The selected game executable is missing. Add its current executable.");
        if (SelectedInstallation() is Installation selected) ProviderAdapters.For(selected.Provider).ValidateCapabilities(providers.Catalog.Get(selected.CatalogKey), loadedGame.For(selected.Provider));
        SetStatus("Preparing " + ProviderInfo.NameOf(loadedGame.Provider) + "…");
        await coordinator.Apply(state, loadedGame, SelectedInstallation(), observed);
        observed = coordinator.Fingerprint();
        SetStatus("Ready for next launch: " + ProviderInfo.NameOf(loadedGame.Provider) + (loadedGame.KeepVectorFeatures && loadedGame.Provider != ProviderId.VectorXR ? " + VectorXR enhancements" : "") + ". Start " + loadedGame.Name + " normally. Use the provider overlay to verify generation.");
    }
    List<CompatibilityIssue> ReadCompatibility() {
        if (preview || loadedGame == null) return [];
        try { return CompatibilityChecks.Read(loadedGame, host.VectorSettingsPath, host.Layers()); }
        catch (Exception ex) { return [new(false, "Compatibility check unavailable: " + ex.Message, true)]; }
    }
    Task RefreshState() {
        SaveControls();
        if (providers.TryCompleteVectorRemoval(state, SetStatus)) PopulateVersions();
        if (providers.VectorRemovalPending) {
            SetStatus("Waiting for VectorXR removal to finish. WSG checks automatically while games are closed. If cancelled, use Recover operation; Exit remains available.");
            return Task.CompletedTask;
        }
        if (providers.RemovalRecoveryPending || providers.InstallationRecoveryPending) throw new InvalidOperationException("Use Recover operation before refreshing an interrupted installation or removal.");
        if (!coordinator.RecoveryPending) VectorSnapshots.Reconcile(storage, host.VectorSettingsPath);
        state.Installations = providers.Discover(state).DistinctBy(i => i.Executable, StringComparer.OrdinalIgnoreCase).ToList();
        observed = coordinator.Fingerprint(); PopulateVersions(); runtime.Text = "OpenXR runtime: " + Path.GetFileNameWithoutExtension(OpenXrRegistry.Runtime());
        if (!coordinator.RecoveryPending) state.LastReviewedFingerprint = observed;
        SetStatus(coordinator.RecoveryPending ? "Recovery required. Originals and the recovery journal are preserved." : "Setup refreshed.");
        storage.Save(state); return Task.CompletedTask;
    }
    async Task RemoveSelected() {
        if (preview) throw new InvalidOperationException("UI preview: removal is disabled.");
        SaveControls();
        RequireObservedSetup();
        var installation = SelectedInstallation() ?? throw new InvalidOperationException("Select an installed version first.");
        if (observed.Length == 0) throw new InvalidOperationException("Use Rescan setup before removing a provider.");
        host.RequireIdle(state.Games);
        var description = providers.RemovalDescription(installation)
            + "\n\nIf this provider is active, WSG will deactivate it first. All games must be closed. Continue?";
        if (CenteredMessageBox.Show(this, description, installation.Provider == ProviderId.VectorXR ? "Uninstall VectorXR" : "Remove OFXR version", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        await providers.Remove(installation, state, observed, SetStatus);
        observed = coordinator.Fingerprint(); PopulateVersions(); storage.Save(state);
    }
    void AddGame() {
        if (busy) return; using var dialog = new OpenFileDialog { Filter = "Game executable (*.exe)|*.exe", Title = "Choose the game's executable, not its launcher" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SaveControls();
        var identity = GameDiscovery.Identity(dialog.FileName);
        var existing = state.Games.FirstOrDefault(g => GameDiscovery.Identity(g.Executable).Equals(identity, StringComparison.OrdinalIgnoreCase));
        if (existing == null) { existing = new GamePreset { Executable = dialog.FileName, Name = Path.GetFileNameWithoutExtension(dialog.FileName) }; state.Games.Add(existing); }
        state.SelectedGameId = existing.Id; loading = true; ReloadGames(); loading = false; LoadGame();
    }
    void ImportInstallation() {
        using var dialog = new FolderBrowserDialog { Description = "Choose a provider folder. WSG verifies it against its signed catalog." };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var installation = providers.Identify(dialog.SelectedPath); state.Installations.RemoveAll(i => i.Executable.Equals(installation.Executable, StringComparison.OrdinalIgnoreCase)); state.Installations.Add(installation);
        storage.Save(state); PopulateVersions(); SetStatus("Verified " + ProviderInfo.NameOf(installation.Provider) + " " + installation.Version + ".");
    }
    async Task CheckUpdates(bool manual, CancellationToken cancellation = default) {
        if (manual) installationDetails.SetExpanded(true);
        updateStatus.Text = "Checking official provider releases…";
        var catalogNotice = "";
        try {
            var changed = await ProviderCatalogUpdates.Refresh(storage, providers.Catalog,
                () => !IsDisposed && Visible && (!busy || manual) && !coordinator.Busy && !coordinator.RecoveryPending
                    && !providers.InstallationRecoveryPending && !providers.RemovalRecoveryPending, cancellation);
            if (changed) {
                PopulateVersions(); UpdateVersionControls();
                catalogNotice = "New mod versions are available in the Version lists. Choose one to download and install.\n";
            }
        } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or FormatException or System.Text.Json.JsonException or InvalidOperationException or OperationCanceledException) {
            storage.Log("Provider catalog refresh: " + ex.Message);
            catalogNotice = "Mod list could not refresh; saved versions remain available.\n";
        }
        updateStatus.Text = catalogNotice + string.Join(Environment.NewLine, await providers.CheckReleases(cancellation, state));
        if (!manual && (!Visible || cancellation.IsCancellationRequested)) return;
        if (!AppUpdate.Enabled) { if (manual) SetStatus("Development build: WSG app updates are disabled. Signed mod-list updates work independently."); return; }
        var restart = await AppUpdate.CheckAsync(AppContext.BaseDirectory, SetStatus, () => {
            try { host.RequireIdle(state.Games); return (!busy || manual) && !coordinator.Busy && !coordinator.RecoveryPending && !providers.InstallationRecoveryPending && !providers.RemovalRecoveryPending; } catch { return false; }
        }, null!, () => WindowState == FormWindowState.Minimized);
        if (restart) { AllowClose = true; Close(); }
    }
    async Task Run(Func<Task> action) {
        if (busy) return; busy = true; RefreshPollingMode();
        var disabled = new List<Control>();
        void DisableInputs(Control parent) {
            foreach (Control control in parent.Controls) {
                if (control is ButtonBase or ComboBox or TextBoxBase) {
                    if (ReferenceEquals(control, exitButton)) continue;
                    if (control.Enabled) { disabled.Add(control); control.Enabled = false; }
                } else DisableInputs(control);
            }
        }
        DisableInputs(this);
        try { await action(); }
        catch (Exception ex) { storage.Log(ex.ToString()); SetStatus(ex.Message); if (!preview) { if (!Visible) Restore(); CenteredMessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); } }
        finally { busy = false; foreach (var control in disabled) if (!control.IsDisposed) control.Enabled = true; if (!IsDisposed && !Disposing) { if (Visible || preview) UpdateCurrentSetup(); RefreshPollingMode(); } }
    }
    void SetStatus(string text) { status.Text = text; status.AccessibleName = text; status.Visible = !string.IsNullOrWhiteSpace(text); }
    public void SetLight(bool light) {
        state.LightTheme = light; var background = ColorTranslator.FromHtml(light ? "#edf3f7" : "#0d1418");
        var panel = ColorTranslator.FromHtml(light ? "#ffffff" : "#17242a"); var text = ColorTranslator.FromHtml(light ? "#183142" : "#edf7f6");
        var accent = ColorTranslator.FromHtml(light ? "#168060" : "#78d8b7"); var button = ColorTranslator.FromHtml(light ? "#e5eef3" : "#20323a");
        void Paint(Control control) {
            control.BackColor = control is System.Windows.Forms.Button or ComboBox ? button : background; control.ForeColor = text;
            if (control is Button b) b.FlatAppearance.BorderColor = ColorTranslator.FromHtml(light ? "#a9bdc8" : "#304a53");
            foreach (Control child in control.Controls) Paint(child);
        }
        Paint(this);
        foreach (var surface in surfaces) { surface.BackColor = panel; foreach (Control child in surface.Controls) if (child is not System.Windows.Forms.Button and not ComboBox) child.BackColor = panel; }
        apply.BackColor = accent; apply.ForeColor = light ? Color.White : background; apply.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        foreach (var b in navigation.Where(b => b.Tag as string == "theme")) b.Text = light ? "Use dark appearance" : "Use light appearance";
        WindowTheme.Caption(this, light); UpdateSelection();
        if (!preview) storage.Save(state); Invalidate(true);
    }
    async Task RequestExit() {
        if (closing || AllowClose || IsDisposed) return;
        exitRequested = true;
        ShowExitStatus(busy ? "Exit queued…" : "Exiting…",
            busy ? "Exit requested. WSG will close after the current operation finishes safely." : "Exit requested. Checking that provider cleanup is safe…");
        if (busy) { exitTimer.Start(); return; }
        await Run(AttemptExit);
    }
    void ShowExitStatus(string label, string message) {
        if (exitButton != null) { exitButton.Text = label; exitButton.Refresh(); }
        exitMenu.Text = label; SetStatus(message);
    }
    async Task AttemptExit() {
        if (preview) { AllowClose = true; Close(); return; }
        if (ExitPendingOperation()) return;
        SaveBeforeExit();
        var gamesToCheck = state.Games.ToArray();
        try { await Task.Run(() => host.RequireIdle(gamesToCheck)); }
        catch (Exception ex) {
            exitTimer.Start();
            ShowExitStatus("Exit queued…", "Waiting to exit: " + ex.Message + " WSG will close automatically once games and provider editors close.");
            return;
        }
        await FinishExit();
    }
    void SaveBeforeExit() {
        try { SaveControls(); SaveWindowPlacement(); }
        catch (Exception ex) { storage.Log("Unable to save preferences during Exit; closing remains available. " + ex); }
    }
    bool ExitPendingOperation() {
        if (!coordinator.RecoveryPending && !providers.InstallationRecoveryPending && !providers.RemovalRecoveryPending) return false;
        // Pending recovery must never trap the user or require reinstalling software to quit.
        exitTimer.Stop(); exitRequested = false;
        SaveBeforeExit();
        storage.Log("Exited with an operation pending. Recovery records preserved; no provider cleanup performed.");
        AllowClose = true; Close();
        return true;
    }
    async Task FinishExit() {
        closing = true;
        ShowExitStatus("Exiting…", "Closing providers safely. Complete any Windows elevation prompt to finish cleanup.");
        exitTimer.Stop(); exitRequested = false;
        if (ExitPendingOperation()) return;
        var game = loadedGame ?? new GamePreset { Name = "Cleanup" }; var off = Json.Read<GamePreset>(Json.Write(game)); off.Provider = ProviderId.Off; off.KeepVectorFeatures = false;
        try { await coordinator.Apply(state, off, null, coordinator.Fingerprint()); }
        catch (Exception ex) {
            storage.Log("Exit cleanup failed; closing without further changes. " + ex);
            CenteredMessageBox.Show(this, "WSG could not complete provider cleanup. Some providers may remain enabled. Any recovery records have been kept.\n\nWSG will now close without making further changes.\n\n" + ex.Message, "Closing WSG", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        AllowClose = true; Close();
    }
    void ShowDiagnostics() {
        var lines = new List<string> { "WSG OpenXR FG Switcher 0.1.0", "No in-game generation measurement is performed by WSG.", "", "Runtime: " + OpenXrRegistry.Runtime(), "OFXR tray: " + (host.RunningTray() ?? "Not running"), "Recovery pending: " + coordinator.RecoveryPending, "" };
        lines.AddRange(host.Layers().Select(r => $"{r.Hive} {r.View}-bit • {(r.Enabled ? "Enabled" : "Disabled")} • {r.LayerName}\r\n  {r.Manifest}"));
        using var dialog = new Form { Text = "OpenXR diagnostics", Size = new Size(850, 570), StartPosition = FormStartPosition.CenterParent };
        dialog.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Text = string.Join(Environment.NewLine, lines), Font = new Font("Consolas", 10) }); WindowTheme.Bind(dialog, state.LightTheme); dialog.ShowDialog(this);
    }
    void ShowNotes() {
        SaveControls(); UpdateCurrentSetup();
        if (loadedGame == null) { SetStatus("Add a game before recording a test."); return; }
        var applied = appliedVerified && state.AppliedSetup?.Id == loadedGame.Id;
        var noteGame = Json.Read<GamePreset>(Json.Write(applied ? state.AppliedSetup! : loadedGame));
        using var dialog = new NotesForm(storage, noteGame,
            noteGame.Provider == ProviderId.Off ? "Off" : noteGame.Versions.GetValueOrDefault(noteGame.Provider, "Unknown"),
            OpenXrRegistry.Runtime(), state.LightTheme, applied);
        dialog.ShowDialog(this);
    }
    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e); WindowTheme.Caption(this, state.LightTheme);
    }
    public void SavePreview(string path) { using var image = new Bitmap(Width, Height); DrawToBitmap(image, new Rectangle(Point.Empty, Size)); image.Save(path); }
    public Task SaveBusyPreview(string path) {
        if (!preview) throw new InvalidOperationException("Busy preview is only available in UI smoke mode.");
        return Run(() => { SavePreview(path); return Task.CompletedTask; });
    }
}
