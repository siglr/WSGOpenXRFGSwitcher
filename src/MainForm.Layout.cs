using System.Diagnostics;
using System.Reflection;
using Wsg.OpenXR;

namespace Wsg.Switcher;

public sealed partial class MainForm
{
    readonly Label currentHeading = TextLabel("CURRENT SETUP", 9, true);
    readonly Label currentName = TextLabel("Checking setup…", 21, true);
    readonly Label currentDetail = TextLabel("", 10);
    readonly Label settingsHeading = TextLabel("Mod settings", 15, true);
    readonly Label pendingHint = TextLabel("", 10);
    readonly Label offHint = TextLabel("Turn off frame generation from all managed mods for a baseline test.", 10);
    readonly Label installLocation = TextLabel("", 9);
    readonly Label providerUpdateHint = TextLabel("", 9);
    readonly Label vectorProfile = TextLabel("", 9);
    readonly Label compatibility = TextLabel("", 9);
    readonly Label overlayOptionsHeading = TextLabel("FPS overlay options", 9, true);
    readonly DisclosureSection moreOptions = new(), installationDetails = new(), compatibilityDetails = new();
    readonly ToolTip hints = new() { AutoPopDelay = 12000, InitialDelay = 500, ReshowDelay = 150, ShowAlways = true };
    string compatibilityAttention = "";
    readonly Dictionary<ProviderId, Control> versionRows = new();
    readonly Dictionary<ProviderId, Button> installButtons = new();
    readonly Label coexistHint = TextLabel("Experimental • VectorXR FG stays off.", 9);
    readonly System.Windows.Forms.Timer statusTimer = new() { Interval = 5000 };
    bool appliedVerified;
    Button? recoveryButton;

    void BuildLayout() {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); Controls.Add(root);
        var sidebar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(18, 24, 12, 12) }; surfaces.Add(sidebar); root.Controls.Add(sidebar, 0, 0);
        var logo = new PictureBox { Size = new Size(160, 75), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 0, 20) };
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("wsg-logo.png")!; logo.Image = new Bitmap(Image.FromStream(stream)); sidebar.Controls.Add(logo);
        sidebar.Controls.Add(TextLabel("OPENXR FG", 14, true)); sidebar.Controls.Add(TextLabel("One place to switch.", 10));
        AddNav(sidebar, "Test notes", () => { ShowNotes(); return Task.CompletedTask; });
        var rescan = AddNav(sidebar, "Rescan setup", RefreshState);
        var updates = AddNav(sidebar, "Check for updates", () => CheckUpdates(true));
        recoveryButton = AddNav(sidebar, "Recover operation…", async () => { await providers.RecoverRemoval(state); await providers.RecoverInstallation(state, SetStatus); await coordinator.Recover(state); observed = coordinator.Fingerprint(); PopulateVersions(); SetStatus("Recovery complete. Review the current setup before applying your next configuration."); });
        hints.SetToolTip(rescan, "Reread installed mods, saved settings and OpenXR registrations after changes made outside WSG. Does not switch providers.");
        hints.SetToolTip(updates, "Check official mod releases and WSG updates. Provider updates are never installed automatically.");
        rescan.AccessibleDescription = hints.GetToolTip(rescan); updates.AccessibleDescription = hints.GetToolTip(updates);
        AddNav(sidebar, "Diagnostics", () => { ShowDiagnostics(); return Task.CompletedTask; });
        AddNav(sidebar, "Data folder", () => { Process.Start(new ProcessStartInfo(storage.Root) { UseShellExecute = true }); return Task.CompletedTask; });
        AddNav(sidebar, "Mod folder…", () => { ChooseModFolder(); return Task.CompletedTask; });
        AddNav(sidebar, "Startup…", () => { ChooseStartup(); return Task.CompletedTask; });
        AddNav(sidebar, "Help", () => { using var help = new HelpForm(state.LightTheme); help.ShowDialog(this); return Task.CompletedTask; });
        AddNav(sidebar, state.LightTheme ? "Use dark appearance" : "Use light appearance", () => { SetLight(!state.LightTheme); return Task.CompletedTask; }, true);
        exitButton = AddNav(sidebar, "Exit", RequestExit, exitAction: true);
        sidebar.Controls.Add(new Label { Text = "v" + global::ProductVersion.Value + (AppUpdate.Enabled ? "\nPreview release" : "\nDevelopment build") + "\nHeadset testing pending", AutoSize = false, Size = new Size(168, 80), Margin = new Padding(0, 24, 0, 0), Font = new Font("Segoe UI", 9) });
        root.Controls.Add(workspace, 1, 0);
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 0, Padding = new Padding(26, 22, 26, 22) };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); workspace.Controls.Add(content);
        void Row(Control control) { var index = content.RowCount++; content.RowStyles.Add(new RowStyle(SizeType.AutoSize)); content.Controls.Add(control, 0, index); control.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top; }
        var current = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(16), Margin = new Padding(0, 0, 0, 18) };
        current.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        current.Controls.Add(currentHeading); current.Controls.Add(currentName); current.Controls.Add(currentDetail);
        surfaces.Add(current); Row(current);
        var gameLine = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 0, 0, 12) };
        gameLine.Controls.Add(new Label { Text = "Game preset", AutoSize = true, Margin = new Padding(0, 9, 12, 0) });
        games.Width = 360; games.AccessibleName = "Game preset"; gameLine.Controls.Add(games);
        var add = Button("Add game…"); AlignInlineButton(add, games); add.Click += (_, _) => AddGame(); gameLine.Controls.Add(add); Row(gameLine);
        Row(setupHint);
        var ids = ProviderInfo.DisplayOrder;
        var cards = new TableLayoutPanel { ColumnCount = ids.Length, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 4, 0, 12) };
        cards.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (int i = 0; i < ids.Length; i++) {
            var id = ids[i]; cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / ids.Length));
            var card = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 2, Padding = new Padding(0), Margin = new Padding(i == 0 ? 0 : 5, 0, i == ids.Length - 1 ? 0 : 5, 0) };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var radio = new ThemeRadioButton { Text = ProviderInfo.NameOf(id), Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11, FontStyle.Bold), Margin = Padding.Empty, Cursor = Cursors.Hand, AccessibleName = "Select " + ProviderInfo.NameOf(id) };
            if (id == ProviderId.Tig3r) radio.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            choices[id] = radio; card.Controls.Add(radio, 0, 0);
            var info = new WrappingLabel { Text = id == ProviderId.Off ? "Baseline test" : "Not installed", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9, FontStyle.Bold), Padding = new Padding(0, 6, 0, 6), Margin = Padding.Empty };
            providerStatus[id] = info; card.Controls.Add(info, 0, 1); cards.Controls.Add(card, i, 0);
        }
        Row(cards); Row(settingsHeading); Row(offHint);
        foreach (var provider in ProviderInfo.All) {
            var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 0, 0, 12) };
            row.Controls.Add(new Label { Text = "Version", AutoSize = true, Margin = new Padding(0, 9, 12, 0) });
            var combo = Combo(); combo.Width = 265; combo.AccessibleName = provider.Name + " version"; versions[provider.Id] = combo; row.Controls.Add(combo);
            var install = Button("Download & install"); AlignInlineButton(install, combo); installButtons[provider.Id] = install;
            install.Click += async (_, _) => await Run(async () => {
                if (preview) throw new InvalidOperationException("UI preview: provider installation is disabled.");
                SaveControls(); var release = providers.Catalog.Document.Releases.FirstOrDefault(r => r.Provider == provider.Id && r.Version == versions[provider.Id].SelectedItem as string) ?? providers.Catalog.Latest(provider.Id);
                if (!providers.InstallationRecoveryPending) RequireObservedSetup();
                var vectorInstalled = state.Installations.FirstOrDefault(i => i.Provider == ProviderId.VectorXR);
                if (provider.Id == ProviderId.VectorXR && !providers.InstallationRecoveryPending && CenteredMessageBox.Show(this, "WSG will download the official signed VectorXR installer and keep temporary recovery data until setup finishes. Windows may ask for administrator permission.\n\nInstall folder: " + ProviderPaths.Destination(storage, state, release, vectorInstalled) + "\n\nComplete the installer and leave its app closed afterwards.", "Set up VectorXR", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
                if (!providers.InstallationRecoveryPending) RequireObservedSetup();
                var installed = await providers.Install(release, state, SetStatus); if (loadedGame != null) loadedGame.Versions[provider.Id] = installed.Version;
                PopulateVersions(); observed = coordinator.Fingerprint(); storage.Save(state);
            });
            row.Controls.Add(install); versionRows[provider.Id] = row; Row(row);
        }
        Row(providerUpdateHint);
        moreOptions.SetTitle("More options"); installationDetails.SetTitle("Manage installation");
        var settings = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
        foreach (var pair in new[] { ("Generation method", method), ("Frame rate", multiplier), ("Quality", quality), ("FPS overlay", overlayPosition), ("Flow resolution", scale) }) {
            var field = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 0, 12, 0) };
            var label = TextLabel(pair.Item1, 9); field.Controls.Add(label);
            pair.Item2.Width = pair.Item2 == method ? 185 : 140; pair.Item2.AccessibleName = pair.Item1; field.Controls.Add(pair.Item2);
            if (pair.Item2 == scale) {
                field.FlowDirection = FlowDirection.LeftToRight; field.Margin = new Padding(0, 0, 0, 8);
                label.Margin = new Padding(0, 8, 12, 0); moreOptions.AddRow(field);
            } else settings.Controls.Add(field);
        }
        Row(settings);
        var switches = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
        overlay.Text = "Show in-game overlay"; lowerVram.Text = "Reduce VRAM use";
        Row(overlay); switches.Controls.Add(preferFps); switches.Controls.Add(lowerVram); moreOptions.AddRow(switches);
        djulesOptions.Controls.Add(hybrid); djulesOptions.Controls.Add(hybridDepth);
        overlaySize.Width = 140; overlaySize.AccessibleName = "OFXR overlay size";
        var overlaySizeLabel = TextLabel("Size", 9); overlaySizeLabel.Margin = new Padding(0, 8, 8, 0);
        overlayDetails.Controls.Add(overlaySizeLabel); overlayDetails.Controls.Add(overlaySize);
        overlayDetails.Controls.Add(overlayLatency); overlayDetails.Controls.Add(overlayMultiplier); overlayDetails.Controls.Add(overlayHybrid);
        overlayOptionsHeading.Margin = new Padding(0, 8, 0, 2); overlayDetails.Margin = new Padding(0, 0, 0, 12);
        moreOptions.AddRow(overlayOptionsHeading); moreOptions.AddRow(overlayDetails); moreOptions.AddRow(djulesOptions);
        hints.SetToolTip(overlayOptionsHeading, "Appearance and information shown by the FPS overlay. Choose a corner in the FPS overlay dropdown above to enable it.");
        hints.SetToolTip(hybrid, "djules 0.2.14.1+: combines a DLSS game's motion vectors with optical flow. Adds GPU work; requires game support. Off by default.");
        hints.SetToolTip(hybridDepth, "Uses game depth to improve thin nearby objects. Requires Hybrid and adds GPU work, especially at high VR resolutions.");
        hints.SetToolTip(scale, "Optical-flow input resolution. Lower values reduce GPU work and motion detail. 25% requires djules 0.2.14.1+. Applies to both optical-flow backends in that release.");
        hints.SetToolTip(overlayHybrid, "The provider's overlay reports HYBRID OK or HYBRID KO in game; selecting Hybrid alone does not prove it is working.");
        moreOptions.AddRow(keepVector); coexistHint.Margin = new Padding(0, 3, 0, 8); moreOptions.AddRow(coexistHint); Row(moreOptions);
        var tools = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 12) };
        advanced.Text = "Open mod’s settings…";
        var import = Button("Find existing install…"); import.Click += async (_, _) => await Run(() => { ImportInstallation(); return Task.CompletedTask; }); tools.Controls.Add(import); tools.Controls.Add(remove);
        var clean = Button("Clean stored files…"); clean.AccessibleName = "Clean backups, downloads and unused presets";
        clean.Click += async (_, _) => await Run(CleanStoredFiles); tools.Controls.Add(clean);
        hints.SetToolTip(clean, "Remove completed backups, cached installers and presets for versions no longer installed.");
        installationDetails.AddRow(vectorProfile); installationDetails.AddRow(installLocation); installationDetails.AddRow(tools);
        runtime.Font = new Font("Segoe UI", 9); runtime.Text = "OpenXR runtime: detected on refresh"; installationDetails.AddRow(runtime);
        installationDetails.AddRow(updateStatus); Row(installationDetails);
        compatibilityDetails.AddRow(compatibility); Row(compatibilityDetails);
        var providerActions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 5, 0, 12) };
        providerActions.Controls.Add(advanced); Row(providerActions);
        apply.MinimumSize = new Size(210, 44); apply.Margin = new Padding(0, 3, 0, 3); Row(apply);
        pendingHint.Margin = new Padding(0, 8, 0, 12); Row(pendingHint);
        status.Font = new Font("Segoe UI", 9); status.Margin = new Padding(0, 0, 0, 8); surfaces.Add(status); Row(status);
        hints.SetToolTip(setupHint, "Green and Applied mark the verified setup. The blue outline marks the tab you are editing. Selecting a tab does not activate it.");
        hints.SetToolTip(games, "Save a separate provider and settings preset for each game. Only one setup is active at a time.");
        hints.SetToolTip(keepVector, "Retain enhancements such as Pivot with OFXR or Off. VectorXR frame generation stays disabled. This combination is experimental.");
        hints.SetToolTip(preferFps, "Use the provider's deeper pipeline to favor FPS over latency. Required for 3x and elliott Auto.");
        hints.SetToolTip(overlayPosition, "Position of the OFXR FPS overlay, or Off to hide it.");
        hints.SetToolTip(apply, "Close all OpenXR games before applying. Start the game again afterwards; toggling VR mode is not enough.");
        FormClosed += (_, _) => hints.Dispose();
    }

    void UpdateCurrentSetup() {
        try {
            if (!busy && !preview && providers.TryCompleteVectorRemoval(state, SetStatus)) {
                observed = coordinator.Fingerprint(); PopulateVersions();
            }
            var fingerprint = coordinator.Fingerprint();
            if (observed.Length > 0 && observed != fingerprint) observed = "";
            var recovery = coordinator.RecoveryPending || providers.InstallationRecoveryPending || providers.RemovalRecoveryPending;
            appliedVerified = !recovery && state.LastAppliedFingerprint != null && state.LastAppliedFingerprint == fingerprint;
            if (providers.RemovalRecoveryPending || providers.VectorRemovalPending) {
                currentHeading.Text = "CURRENT SETUP • REMOVAL PENDING"; currentName.Text = "Checking provider removal";
                currentDetail.Text = providers.VectorRemovalPending ? "Complete the VectorXR uninstaller. WSG checks automatically while games are closed. If cancelled, use Recover operation." : "Use Recover operation to restore the interrupted OFXR removal.";
            } else if (providers.InstallationRecoveryPending) {
                currentHeading.Text = "CURRENT SETUP • INSTALLATION PENDING"; currentName.Text = "Finish VectorXR setup";
                currentDetail.Text = "Close VectorXR, select its card, then choose Finish setup. If installation was cancelled, use Recover operation.";
            } else if (recovery) {
                currentHeading.Text = "CURRENT SETUP • NEEDS ATTENTION"; currentName.Text = "Recovery required";
                currentDetail.Text = "Use Recover operation before making another change.";
            } else if (appliedVerified) {
                currentHeading.Text = "CURRENT SETUP • READY FOR NEXT LAUNCH";
                currentName.Text = state.ActiveProvider == ProviderId.Off ? "Off — frame generation disabled" : ProviderInfo.NameOf(state.ActiveProvider);
                var applied = state.AppliedSetup;
                var gameName = applied?.Name ?? state.Games.FirstOrDefault(g => g.Id == state.ActiveGameId)?.Name ?? "Previous game";
                var version = applied?.Versions.GetValueOrDefault(state.ActiveProvider);
                currentDetail.Text = gameName + (state.ActiveProvider != ProviderId.Off && version != null ? " • " + version : "")
                    + (applied?.KeepVectorFeatures == true && applied.Provider != ProviderId.VectorXR ? " • VectorXR enhancements retained" : "");
                hints.SetToolTip(currentHeading, "Settings and registrations verified. Check the provider's in-game overlay to confirm generation is running.");
            } else {
                currentHeading.Text = "CURRENT SETUP • NOT VERIFIED";
                currentName.Text = state.LastAppliedFingerprint == null ? "No setup applied by WSG yet" : "Setup changed outside WSG";
                currentDetail.Text = "Choose a mod, or Rescan setup to review external changes.";
            }
        } catch (Exception ex) {
            appliedVerified = false; currentHeading.Text = "CURRENT SETUP • NEEDS ATTENTION"; currentName.Text = "Unable to verify setup";
            currentDetail.Text = ex.Message;
        }
        UpdateSelection();
    }

    void UpdateSelection() {
        var recoveryPending = coordinator.RecoveryPending || providers.InstallationRecoveryPending || providers.RemovalRecoveryPending;
        if (recoveryButton != null) {
            recoveryButton.Enabled = !busy && recoveryPending;
            var description = recoveryPending ? "Restore the previous setup after the interrupted installation, removal or switch. Close games and provider editors first."
                : "No interrupted operation to recover. This restores an unfinished installation, removal or switch; it is not a general refresh.";
            hints.SetToolTip(recoveryButton, description); recoveryButton.AccessibleDescription = description;
        }
        var selected = loadedGame?.Provider ?? ProviderId.Off;
        var newest = selected == ProviderId.Off ? null : providers.Catalog.Latest(selected);
        providerUpdateHint.Visible = newest != null && state.Installations.Any(i => i.Provider == selected)
            && !state.Installations.Any(i => i.Provider == selected && i.Version == newest.Version);
        providerUpdateHint.Text = newest == null ? "" : "New version: " + newest.Version + " — choose it in Version to download and install.";
        var compatibilityIssues = ReadCompatibility();
        var conflict = compatibilityIssues.FirstOrDefault(i => i.BlocksApply);
        PresentCompatibility(compatibilityIssues, !preview && loadedGame != null && (selected == ProviderId.VectorXR || loadedGame.KeepVectorFeatures));
        var vectorRemovalPending = providers.VectorRemovalPending;
        var same = SetupSelection.MatchesApplied(state, loadedGame, appliedVerified);
        foreach (var pair in versionRows) pair.Value.Visible = pair.Key == selected;
        settingsHeading.Text = selected == ProviderId.Off ? "Off • baseline setup" : ProviderInfo.NameOf(selected) + " settings";
        if (SelectedRelease()?.Prerelease == true) settingsHeading.Text += " • Beta";
        vectorProfile.Visible = selected is ProviderId.VectorXR or ProviderId.Tig3r;
        vectorProfile.Text = loadedGame == null ? "Add a game preset to create its VectorXR profile."
            : "Game profile: " + loadedGame.Name + "\nExecutable: " + Path.GetFileName(loadedGame.Executable)
                + "\nLocation: " + Path.GetDirectoryName(loadedGame.Executable);
        hints.SetToolTip(vectorProfile, "Apply creates or updates this game's FG profile. VectorXR matches the executable name, not the folder.");
        if (selected == ProviderId.Tig3r) {
            vectorProfile.Text = loadedGame == null ? "Add a game preset to create its tig3rmast3r profile."
                : "Game profile: " + Path.GetFileName(loadedGame.Executable) + " • Saved separately for this game";
            hints.SetToolTip(vectorProfile, "Apply updates the native executable-based profile. Advanced settings are preserved. Desktop provider settings edit defaults; F10 edits the running game's profile.");
        }
        installationDetails.SetTitle(selected == ProviderId.Off ? "Setup details" : "Manage installation");
        moreOptions.Visible = selected != ProviderId.VectorXR;
        moreOptions.SetTitle("More options" + (loadedGame?.KeepVectorFeatures == true ? " • VectorXR features on" : ""));
        advanced.Text = selected == ProviderId.Tig3r ? "Provider settings…" : "Start VectorXR app";
        advanced.Parent!.Visible = selected is ProviderId.VectorXR or ProviderId.Tig3r;
        hints.SetToolTip(advanced, selected == ProviderId.Tig3r
            ? "Open the provider's actual desktop settings without arming it. Desktop edits change defaults when no game is active; F10 edits the running game's profile. Close the editor and Rescan before applying in WSG."
            : "Open full settings without applying FG. Close the editor and Rescan setup before applying here.");
        offHint.Visible = selected == ProviderId.Off;
        coexistHint.Visible = selected != ProviderId.VectorXR;
        foreach (var pair in choices) {
            var isSelected = pair.Key == selected;
            var isApplied = appliedVerified && state.ActiveProvider == pair.Key;
            pair.Value.Text = ProviderInfo.NameOf(pair.Key);
            pair.Value.AccessibleName = ProviderInfo.NameOf(pair.Key) + (isApplied ? ", applied" : ", not applied") + (isSelected ? ", editing this tab" : "");
            pair.Value.BackColor = isApplied ? WindowTheme.Accent(state.LightTheme) : WindowTheme.Surface(state.LightTheme);
            pair.Value.ForeColor = isApplied ? (state.LightTheme ? Color.White : WindowTheme.Background(false)) : WindowTheme.Foreground(state.LightTheme);
            pair.Value.FlatAppearance.BorderSize = isSelected ? 3 : 1;
            pair.Value.FlatAppearance.CheckedBackColor = pair.Value.BackColor;
            pair.Value.FlatAppearance.MouseOverBackColor = pair.Value.BackColor;
            pair.Value.FlatAppearance.BorderColor = isSelected ? (state.LightTheme ? Color.FromArgb(40, 95, 190) : Color.FromArgb(125, 182, 255)) : isApplied ? WindowTheme.Accent(state.LightTheme) : Color.FromArgb(90, 115, 125);
            providerStatus[pair.Key].Text = pair.Key == ProviderId.VectorXR && vectorRemovalPending ? "Removal pending" : isApplied ? "✓ APPLIED" : pair.Key == ProviderId.Off ? "Not applied" : state.Installations.Any(i => i.Provider == pair.Key) ? "Installed • Not applied" : "Not installed";
            providerStatus[pair.Key].ForeColor = isApplied ? WindowTheme.Accent(state.LightTheme) : WindowTheme.Foreground(state.LightTheme);
        }
        foreach (var pair in installButtons) {
            var pendingRemoval = pair.Key == ProviderId.VectorXR && vectorRemovalPending;
            var installed = state.Installations.Any(i => i.Provider == pair.Key && i.Version == versions[pair.Key].SelectedItem as string);
            versions[pair.Key].Tag = state.Installations.Where(i => i.Provider == pair.Key && !pendingRemoval).Select(i => i.Version).ToHashSet(StringComparer.Ordinal);
            versions[pair.Key].Invalidate();
            pair.Value.Text = pendingRemoval ? "Removal pending" : pair.Key == ProviderId.VectorXR && providers.InstallationRecoveryPending ? "Finish setup" : installed ? "Repair / reinstall" : "Download & install";
            pair.Value.Enabled = !busy && !vectorRemovalPending && !providers.RemovalRecoveryPending && !coordinator.RecoveryPending;
        }
        var missing = loadedGame != null && selected != ProviderId.Off && SelectedInstallation() == null;
        apply.Text = same ? "Already applied" : selected == ProviderId.Off ? "Apply Off setup" : "Apply " + ProviderInfo.NameOf(selected);
        var removalPending = providers.RemovalRecoveryPending || providers.VectorRemovalPending;
        apply.Enabled = !busy && !same && conflict == null && loadedGame != null && !missing && !providers.InstallationRecoveryPending && !removalPending && !coordinator.RecoveryPending;
        apply.BackColor = apply.Enabled ? WindowTheme.Accent(state.LightTheme) : WindowTheme.Surface(state.LightTheme);
        apply.ForeColor = apply.Enabled ? (state.LightTheme ? Color.White : WindowTheme.Background(false)) : WindowTheme.Foreground(state.LightTheme);
        advanced.Enabled = !busy && !missing && selected is ProviderId.VectorXR or ProviderId.Tig3r && !providers.InstallationRecoveryPending && !providers.RemovalRecoveryPending;
        remove.Visible = selected != ProviderId.Off && !vectorRemovalPending;
        remove.Text = selected == ProviderId.VectorXR ? "Uninstall VectorXR…" : "Remove this version…";
        remove.Enabled = !busy && !missing && selected != ProviderId.Off && !removalPending && !providers.InstallationRecoveryPending && !coordinator.RecoveryPending;
        installLocation.Visible = selected != ProviderId.Off;
        if (selected != ProviderId.Off) {
            var installed = SelectedInstallation();
            var release = providers.Catalog.Document.Releases.FirstOrDefault(r => r.Provider == selected && r.Version == versions[selected].SelectedItem as string) ?? providers.Catalog.Latest(selected);
            installLocation.Text = installed != null ? "Installed at: " + installed.Directory : "Next install: " + ProviderPaths.Destination(storage, state, release, state.Installations.FirstOrDefault(i => i.Provider == ProviderId.VectorXR));
        }
        pendingHint.Text = removalPending ? (providers.VectorRemovalPending ? "Checking removal automatically. You can exit safely." : "Use Recover operation to restore the interrupted removal.") : providers.InstallationRecoveryPending ? "Close VectorXR, then choose Finish setup." : loadedGame == null ? "Add a game to get started." : conflict != null ? "Resolve the compatibility conflict above to apply." : missing ? "Install this version to continue."
            : same ? "Start the game normally."
            : appliedVerified && state.ActiveProvider == selected ? "Settings changed. Close the game, then apply."
            : "Close all OpenXR games before applying.";
        currentName.ForeColor = appliedVerified ? WindowTheme.Accent(state.LightTheme) : WindowTheme.Foreground(state.LightTheme);
    }

    void PresentCompatibility(IReadOnlyList<CompatibilityIssue> issues, bool relevant) {
        compatibilityDetails.Visible = relevant;
        var conflicts = issues.Count(i => i.BlocksApply);
        var warnings = issues.Count(i => i.Warning && !i.BlocksApply);
        compatibilityDetails.SetTitle(conflicts > 0 ? "Compatibility • Conflict blocks Apply"
            : warnings > 0 ? $"Compatibility • {warnings} warning{(warnings == 1 ? "" : "s")}" : "Compatibility • Details");
        compatibility.Text = string.Join("\n\n", issues.Select(i => (i.BlocksApply ? "Conflict: " : i.Warning ? "Warning: " : "") + i.Message));
        var attention = relevant ? string.Join("\n", issues.Where(i => i.BlocksApply || i.Warning).Select(i => i.Message)) : "";
        if (attention.Length > 0 && attention != compatibilityAttention) compatibilityDetails.SetExpanded(true);
        compatibilityAttention = attention;
        var color = conflicts > 0 ? (state.LightTheme ? Color.DarkRed : Color.Salmon)
            : warnings > 0 ? (state.LightTheme ? Color.FromArgb(135, 77, 0) : Color.FromArgb(255, 205, 115)) : WindowTheme.Foreground(state.LightTheme);
        compatibilityDetails.SetHeaderColor(color); compatibility.ForeColor = color;
    }
}
