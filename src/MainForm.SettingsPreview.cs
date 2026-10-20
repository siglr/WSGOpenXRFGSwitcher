using Wsg.OpenXR;

namespace Wsg.Switcher;

public sealed partial class MainForm
{
    internal void VerifySettingsPreview(string output) {
        if (!preview) throw new InvalidOperationException("Settings UI tests require preview mode.");
        var checks = new List<string>();
        void Check(bool success, string message) {
            if (!success) {
                File.WriteAllText(Path.Combine(output, "settings-ui.json"), Json.Write(new { passed = false, checks, failure = message }));
                throw new InvalidOperationException(message);
            }
            checks.Add(message);
        }
        void CheckSectionRows(DisclosureSection section) {
            var body = section.Controls.OfType<DisclosureBody>().Single();
            var bottom = body.Padding.Top;
            foreach (Control row in body.Controls.Cast<Control>().OrderBy(control => control.Top)) {
                if (!row.Visible) continue;
                Check(row.Top >= bottom && row.Bottom <= body.ClientSize.Height,
                    "Expanded " + section.Controls[0].Text + " rows do not overlap or clip: " + row.GetType().Name);
                bottom = row.Bottom + row.Margin.Bottom;
            }
        }
        ClientSize = new Size(1040, 900);
        state.ProviderRoot = @"C:\OpenXR FG"; // Display-only fixture; preview cannot install providers.
        SetStatus("");
        state.ActiveProvider = ProviderId.Off;
        state.ActiveGameId = loadedGame!.Id;
        state.LastAppliedFingerprint = coordinator.Fingerprint();
        UpdateCurrentSetup();
        Check(!moreOptions.Expanded && !installationDetails.Expanded, "Secondary sections start collapsed");
        choices[ProviderId.Djules].Checked = true;
        UpdateSelection();
        runtime.Text = "OpenXR runtime: PiOpenXR_64";
        updateStatus.Text = string.Join(Environment.NewLine, new[] {
            "djules OFXR: 0.2.13.4 is the reviewed release.",
            "elliott OFXR: 0.2.13.38-dlssg-V439 is the reviewed release.",
            "VectorXR: v0.19.2 is the reviewed release." });
        installationDetails.SetExpanded(true); PerformLayout();
        CheckSectionRows(installationDetails);
        Check(apply.RectangleToScreen(apply.ClientRectangle).Top
            - updateStatus.RectangleToScreen(updateStatus.ClientRectangle).Bottom <= 28 * DeviceDpi / 96,
            "Short mod path and full release names do not reserve empty button rows");
        SavePreview(Path.Combine(output, "initial-installation-gap.png"));
        var metrics = new List<object>();
        void Measure(Control control) {
            metrics.Add(new { type = control.GetType().Name, text = control.Text, control.Visible,
                control.Bounds, control.MaximumSize, preferred = control.GetPreferredSize(new Size(control.Width, 0)) });
            foreach (Control child in control.Controls) Measure(child);
        }
        Measure(installationDetails);
        File.WriteAllText(Path.Combine(output, "initial-installation-metrics.json"), Json.Write(metrics));
        installationDetails.SetExpanded(false);
        Check(!advanced.Visible && !advanced.Parent!.Visible, "OFXR has no instruction-only settings button or empty action row");
        overlayPosition.SelectedItem = "lower_left"; SaveControls();
        Check(loadedGame!.For(ProviderId.Djules).OverlayPosition == "lower_left", "OFXR corner selection is saved in the game preset");
        overlayPosition.SelectedItem = "off"; SaveControls();
        Check(!loadedGame.For(ProviderId.Djules).Overlay && loadedGame.For(ProviderId.Djules).OverlayPosition == "lower_left", "Overlay Off preserves the remembered corner");
        SetLight(false); SavePreview(Path.Combine(output, "djules-settings-dark.png"));
        Check(apply.RectangleToScreen(apply.ClientRectangle).Bottom <= workspace.RectangleToScreen(workspace.ClientRectangle).Bottom,
            "Apply fits in the compact OFXR view");
        choices[ProviderId.VectorXR].Checked = true;
        Check(!vectorProfile.Visible, "VectorXR profile details stay collapsed until requested");
        installationDetails.SetExpanded(true);
        Check(vectorProfile.Visible && vectorProfile.Text.Contains("FlightSimulator2024.exe") && !overlayPosition.Parent!.Visible,
            "VectorXR tab shows executable/profile details and its own overlay control");
        CheckSectionRows(installationDetails);
        Check(advanced.Text == "Start VectorXR app", "VectorXR has an explicit app launch label");
        SavePreview(Path.Combine(output, "vector-details-dark.png"));
        installationDetails.SetExpanded(false);
        PresentCompatibility([new(false, "VectorXR compatibility checks will be available after installation and setup.")], true);
        SavePreview(Path.Combine(output, "vector-settings-dark.png"));
        SetLight(true);
        PresentCompatibility([new(false, "VectorXR compatibility checks will be available after installation and setup.")], true);
        SavePreview(Path.Combine(output, "vector-settings-light.png"));
        var warning = new CompatibilityIssue(false, "Toolkit Turbo is enabled. Review its pacing override before testing VectorXR FG.", true);
        PresentCompatibility([warning], true);
        Check(compatibilityDetails.Expanded && compatibility.Visible, "A new compatibility warning opens its explanation");
        SavePreview(Path.Combine(output, "compatibility-warning-light.png"));
        compatibilityDetails.SetExpanded(false); PresentCompatibility([warning], true);
        Check(!compatibilityDetails.Expanded, "Repeated checks respect a manually collapsed warning");
        var conflict = new CompatibilityIssue(true, "Toolkit Turbo and retained VectorXR Turbo are both enabled. Turn one off, save, then Rescan setup.");
        PresentCompatibility([conflict], true);
        Check(compatibilityDetails.Expanded, "A new blocking conflict reopens compatibility details");
        SetLight(false); PresentCompatibility([conflict], true);
        SavePreview(Path.Combine(output, "compatibility-conflict-dark.png"));
        foreach (var light in new[] { false, true }) {
            using var help = new HelpForm(light); help.Show(this); help.Refresh();
            using var bitmap = new Bitmap(help.Width, help.Height); help.DrawToBitmap(bitmap, new Rectangle(Point.Empty, help.Size));
            bitmap.Save(Path.Combine(output, light ? "help-light.png" : "help-dark.png")); help.Close();
        }
        choices[ProviderId.Elliott].Checked = true;
        moreOptions.SetExpanded(true);
        CheckSectionRows(moreOptions);
        multiplier.SelectedItem = "auto"; SaveControls();
        Check(loadedGame.For(ProviderId.Elliott).Multiplier == "auto" && preferFps.Checked && !preferFps.Enabled, "Elliott Auto exposes its required pipeline setting");
        Check(!vectorProfile.Visible && overlayPosition.Parent!.Visible, "OFXR tabs hide VectorXR-only profile details");
        choices[ProviderId.Djules].Checked = true;
        Check(overlayPosition.SelectedItem as string == "off", "OFXR overlay selection remains isolated across provider tabs");
        versions[ProviderId.Djules].SelectedItem = "0.2.14.1";
        Check(djulesOptions.Visible && scale.Items.Contains("25"), "Reviewed djules 0.2.14.1 exposes Hybrid and 25% flow");
        hybrid.Checked = true; hybridDepth.Checked = true; scale.SelectedItem = "25";
        overlayPosition.SelectedItem = "upper_right"; overlaySize.SelectedItem = "small"; overlayLatency.Checked = false;
        SaveControls();
        Check(loadedGame.For(ProviderId.Djules).Hybrid && loadedGame.For(ProviderId.Djules).HybridDepthSearch
            && loadedGame.For(ProviderId.Djules).FlowResolution == 25 && loadedGame.For(ProviderId.Djules).OverlaySize == "small"
            && !loadedGame.For(ProviderId.Djules).OverlayLatency, "New djules options save in this game's preset");
        CheckSectionRows(moreOptions); PerformLayout();
        SavePreview(Path.Combine(output, "djules-0.2.14.1-options-dark.png"));
        hybrid.Checked = false;
        foreach (var light in new[] { false, true }) {
            SetLight(light); PerformLayout();
            Check(overlayOptionsHeading.Visible && overlayOptionsHeading.Text == "FPS overlay options"
                && overlayOptionsHeading.Bottom <= overlayDetails.Top && overlayDetails.Bottom <= djulesOptions.Top,
                "FPS overlay options are labelled and separated from Hybrid in " + (light ? "light" : "dark") + " appearance");
            Check(overlayPosition.Visible && !moreOptions.Contains(overlayPosition) && overlaySize.Enabled
                && overlayLatency.Enabled && overlayMultiplier.Enabled && overlayHybrid.Enabled && !hybridDepth.Enabled,
                "Main overlay selector stays accessible and overlay options work independently of Hybrid");
            Check(overlayOptionsHeading.ForeColor == WindowTheme.Foreground(light), "Overlay heading uses the active theme foreground");
            var flowLabel = scale.Parent!.Controls.OfType<Label>().Single();
            Check(flowLabel.Right <= scale.Left && Math.Abs((flowLabel.Top + flowLabel.Height / 2) - (scale.Top + scale.Height / 2)) <= 3 * DeviceDpi / 96,
                "Flow resolution label and dropdown share a row and align vertically");
            overlayPosition.SelectedItem = "off"; PerformLayout();
            Check(!overlaySize.Enabled && !overlayLatency.Enabled && !overlayMultiplier.Enabled && !overlayHybrid.Enabled
                && overlayDetails.Enabled && overlayDetails.Controls.OfType<Label>().All(label => label.Enabled),
                "Overlay Off disables its extra controls without dimming their labels");
            SavePreview(Path.Combine(output, "overlay-options-off-" + (light ? "light" : "dark") + ".png"));
            overlayPosition.SelectedItem = "upper_right"; PerformLayout();
            Check(overlaySize.SelectedItem as string == "small" && !overlayLatency.Checked,
                "Toggling overlay Off preserves its appearance and information choices");
            CheckSectionRows(moreOptions);
            foreach (Control control in overlayDetails.Controls)
                Check(control.Right <= overlayDetails.ClientSize.Width && control.Bottom <= overlayDetails.ClientSize.Height,
                    "FPS overlay controls fit the available width: " + control.Text);
            SavePreview(Path.Combine(output, "overlay-options-on-" + (light ? "light" : "dark") + ".png"));
        }
        SetLight(false); hybrid.Checked = true;
        choices[ProviderId.Elliott].Checked = true;
        Check(!djulesOptions.Visible && !overlayOptionsHeading.Visible && !overlayDetails.Visible
            && !scale.Items.Contains("25"), "Elliott hides new djules-only controls");
        choices[ProviderId.Djules].Checked = true;
        Check(hybrid.Checked && hybridDepth.Checked && scale.SelectedItem as string == "25", "New djules controls survive changing tabs");
        versions[ProviderId.Djules].SelectedItem = "0.2.13.4";
        Check(!djulesOptions.Visible && !scale.Items.Contains("25") && scale.SelectedItem as string == "50"
            && !loadedGame.For(ProviderId.Djules).Hybrid, "Legacy version selection removes unsupported choices");
        versions[ProviderId.Djules].SelectedItem = "0.2.14.1";
        Check(!hybrid.Checked && !hybridDepth.Enabled, "Hybrid remains opt-in when returning to the newer version");
        state.Installations.Add(new(ProviderId.Djules, "0.2.13.4", output, Path.Combine(output, "OFXRBridgeTray.exe"), "Djules:0.2.13.4"));
        UpdateSelection();
        Check(providerUpdateHint.Visible && providerUpdateHint.Text.Contains("0.2.14.1"), "A newer compatible mod is announced next to the version selector");
        state.Installations.Clear(); UpdateSelection();
        overlayPosition.SelectedItem = "off"; SaveControls();
        SavePreview(Path.Combine(output, "more-options-dark.png"));
        moreOptions.SetExpanded(false); installationDetails.SetExpanded(false);
        foreach (var id in new[] { ProviderId.Off, ProviderId.VectorXR, ProviderId.Djules, ProviderId.Off, ProviderId.Djules }) {
            choices[id].Checked = true;
            installationDetails.SetExpanded(true); installationDetails.SetExpanded(false);
            if (id != ProviderId.VectorXR) {
                Check(moreOptions.HeaderBounds.Left == installationDetails.HeaderBounds.Left
                    && moreOptions.HeaderBounds.Width == installationDetails.HeaderBounds.Width,
                    "Disclosure headers stay aligned after switching to " + id);
            }
        }
        ClientSize = new Size(1040, 800);
        SetStatus("Right-click the active OFXR icon in the Windows notification area for advanced settings. Refresh WSG after making changes. "
            + "This long diagnostic fixture checks that a complete status message wraps when the window is narrow. "
            + "The final sentence must remain visible, with no clipped lines or missing words.");
        PerformLayout();
        Check(status.Height > 54 && status.Width <= status.Parent!.ClientSize.Width - status.Parent.Padding.Horizontal,
            "Long status grows beyond the old fixed height and stays inside the content width");
        foreach (var label in providerStatus.Values) {
            label.Text = "Installed • Not applied";
            Check(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height,
                "Provider status fits its complete text");
        }
        workspace.ScrollControlIntoView(status); SavePreview(Path.Combine(output, "status-wrapping-dark.png"));
        SetStatus(""); workspace.AutoScrollPosition = Point.Empty;
        choices[ProviderId.Off].Checked = true; SetLight(true);
        SavePreview(Path.Combine(output, "off-alignment-light.png"));
        choices[ProviderId.Djules].Checked = true; SetLight(false);
        SavePreview(Path.Combine(output, "ofxr-alignment-dark.png"));
        var compactApplyWidth = apply.Width;
        ClientSize = new Size(1380, 900); PerformLayout();
        Check(apply.Width > compactApplyWidth && apply.Width == installationDetails.HeaderBounds.Width
            && apply.RectangleToScreen(apply.ClientRectangle).Left == installationDetails.HeaderBounds.Left,
            "Apply stretches to the section-header width when the window is resized");
        SavePreview(Path.Combine(output, "apply-wide-dark.png"));
        ClientSize = new Size(1040, 800);
        updateStatus.Text = "djules OFXR: reviewed release.\nelliott OFXR: reviewed release.\nVectorXR: reviewed release.";
        installationDetails.SetExpanded(true); PerformLayout();
        File.WriteAllText(Path.Combine(output, "section-layout.json"), Json.Write(new {
            sectionHeight = installationDetails.Height,
            gapBelowDetails = apply.RectangleToScreen(apply.ClientRectangle).Top - updateStatus.RectangleToScreen(updateStatus.ClientRectangle).Bottom,
            scroll = workspace.VerticalScroll.Visible
        }));
        Check(recoveryButton is { Enabled: false }, "Recovery is disabled when no operation is pending");
        SafeFiles.AtomicWrite(storage.JournalPath, Json.Write(new SwitchJournal()));
        try { UpdateSelection(); Check(recoveryButton!.Enabled, "Recovery becomes available for an interrupted operation"); }
        finally { File.Delete(storage.JournalPath); UpdateSelection(); }
        updateStatus.Text = string.Join("\n", Enumerable.Repeat("Long release-status fixture", 12)); PerformLayout();
        var tallSection = installationDetails.Height;
        updateStatus.Text = "djules OFXR: reviewed release.\nelliott OFXR: reviewed release.\nVectorXR: reviewed release."; PerformLayout();
        Check(installationDetails.Height < tallSection, "Expanded installation section shrinks when its text becomes shorter");
        var gap = apply.RectangleToScreen(apply.ClientRectangle).Top - updateStatus.RectangleToScreen(updateStatus.ClientRectangle).Bottom;
        Check(gap <= 28 * DeviceDpi / 96, "No unused block remains below installation details");
        for (var i = 0; i < 4; i++) {
            ClientSize = new Size(1040, 800); moreOptions.SetExpanded(true); installationDetails.SetExpanded(true);
            PerformLayout(); workspace.ScrollControlIntoView(updateStatus);
            Check(workspace.VerticalScroll.Visible, "Expanded content scrolls at compact height, cycle " + i);
            ClientSize = new Size(1380, 1050); moreOptions.SetExpanded(false); installationDetails.SetExpanded(false);
            PerformLayout(); workspace.PerformLayout(); workspace.Update();
            Check(!workspace.VerticalScroll.Visible && workspace.AutoScrollPosition.Y == 0,
                "Resize and collapse remove the scrollbar and stale scroll offset, cycle " + i);
        }
        SavePreview(Path.Combine(output, "collapsed-after-resize-dark.png"));
        SetLight(true); installationDetails.SetExpanded(true); PerformLayout();
        SavePreview(Path.Combine(output, "expanded-compact-light.png"));
        File.WriteAllText(Path.Combine(output, "settings-ui.json"), Json.Write(new { passed = true, checks }));
    }
}
