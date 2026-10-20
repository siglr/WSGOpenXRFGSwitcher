using Wsg.OpenXR;

namespace Wsg.Switcher;

public sealed partial class MainForm
{
    bool sessionReady, returnedRefresh, startupUpdatesChecked, monitoringPreview;
    BackgroundCheckMode checkMode;
    int periodicChecks, returnChecks;
    CancellationTokenSource? visibleUpdateCheck;
    DateTimeOffset lastUpdateCheck;

    void RefreshPollingMode(bool? removalForTest = null) {
        if (IsDisposed || Disposing) return;
        if (!Visible) visibleUpdateCheck?.Cancel();
        checkMode = preview && !monitoringPreview ? BackgroundCheckMode.Stopped
            : BackgroundMonitoring.Mode(Visible && WindowState != FormWindowState.Minimized, busy,
                removalForTest ?? (!preview && providers.VectorRemovalPending));
        statusTimer.Enabled = checkMode != BackgroundCheckMode.Stopped;
    }
    void PollStatus() {
        if (busy || IsDisposed) return;
        periodicChecks++;
        if (preview) return; // Isolated timer checks never touch providers.
        if (checkMode == BackgroundCheckMode.VisibleStatus && Visible) UpdateCurrentSetup();
        else if (checkMode == BackgroundCheckMode.PendingRemoval) {
            try { providers.TryCompleteVectorRemoval(state, SetStatus); }
            catch (Exception ex) { storage.Log("Removal check: " + ex.Message); }
        }
        RefreshPollingMode();
    }
    void RefreshOnReturn() {
        if (returnedRefresh || busy || !Visible) return;
        returnedRefresh = true;
        try {
            returnChecks++;
            if (!preview) {
                // Observation only: never reconcile/write external provider settings on focus.
                if (!providers.InstallationRecoveryPending && !providers.RemovalRecoveryPending)
                    state.Installations = providers.Discover(state).DistinctBy(i => i.Executable, StringComparer.OrdinalIgnoreCase).ToList();
                runtime.Text = "OpenXR runtime: " + Path.GetFileNameWithoutExtension(OpenXrRegistry.Runtime());
                PopulateVersions(); UpdateCurrentSetup();
            }
        } catch (Exception ex) { observed = ""; SetStatus("Could not refresh setup: " + ex.Message); }
        finally { returnedRefresh = false; RefreshPollingMode(); }
        if (!preview) _ = CheckVisibleStartupUpdates();
    }
    async Task CheckVisibleStartupUpdates() {
        if (!Visible || busy || startupUpdatesChecked && DateTimeOffset.UtcNow - lastUpdateCheck < TimeSpan.FromHours(1)) return;
        startupUpdatesChecked = true;
        lastUpdateCheck = DateTimeOffset.UtcNow;
        using var cancellation = new CancellationTokenSource();
        visibleUpdateCheck = cancellation;
        try { await CheckUpdates(false, cancellation.Token); }
        catch (OperationCanceledException) { startupUpdatesChecked = false; updateStatus.Text = "Update check deferred until WSG is opened."; }
        catch (Exception ex) { storage.Log("Background update check: " + ex.Message); }
        finally { visibleUpdateCheck = null; }
    }
    void RequireObservedSetup() {
        if (preview) return;
        var current = coordinator.Fingerprint();
        if (observed.Length == 0 || current != observed) {
            observed = "";
            throw new InvalidOperationException("Setup changed outside WSG. Use Rescan setup and review before making changes.");
        }
    }
}
