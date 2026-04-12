using System.Text;

namespace NSFinance.Api.Tests.Unit;

public class MobileBankConnectionPhaseContractTests
{
    [Fact]
    public void ConnectBankScreen_PrefersLifecyclePhaseOverRawSyncStatus()
    {
        var source = ReadConnectBankSource();
        var phaseMapIndex = source.IndexOf(
            "mapSyncLifecyclePhaseToUiState(evidence.syncLifecyclePhase)",
            StringComparison.Ordinal);
        Assert.True(phaseMapIndex >= 0, "Expected lifecycle-phase mapping in deriveUiState.");

        var statusSwitchIndex = source.IndexOf(
            "switch (connection?.status)",
            StringComparison.Ordinal);
        Assert.True(statusSwitchIndex >= 0, "Expected fallback switch over raw connection status.");
        Assert.True(
            phaseMapIndex < statusSwitchIndex,
            "Lifecycle-phase reconciliation should run before raw status fallback.");
    }

    [Fact]
    public void ConnectBankScreen_ReconcilesQueuedAndOrganizingStages()
    {
        var source = ReadConnectBankSource();
        Assert.Contains("\"queued_for_sync\"", source, StringComparison.Ordinal);
        Assert.Contains("\"categorizing\"", source, StringComparison.Ordinal);
        Assert.Contains("\"import_complete_enrichment_queued\"", source, StringComparison.Ordinal);
        Assert.Contains("\"organizing_transactions\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectBankScreen_HasStaleSyncProtectionState()
    {
        var source = ReadConnectBankSource();
        Assert.Contains("syncingStaleThresholdMs", source, StringComparison.Ordinal);
        Assert.Contains("\"sync_taking_longer_than_expected\"", source, StringComparison.Ordinal);
        Assert.Contains("Date.now() - syncEvidenceMs >= syncingStaleThresholdMs", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionStatusIndicator_ExposesPostImportStatuses()
    {
        var source = ReadConnectionStatusIndicatorSource();
        Assert.Contains("import_complete_enrichment_queued", source, StringComparison.Ordinal);
        Assert.Contains("organizing_transactions", source, StringComparison.Ordinal);
        Assert.Contains("sync_taking_longer_than_expected", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectBankScreen_UsesPassiveSafeCloseMessaging()
    {
        var source = ReadConnectBankSource();
        Assert.Contains("safeCloseMessage", source, StringComparison.Ordinal);
        Assert.Contains("You can close this page now", source, StringComparison.Ordinal);
        Assert.Contains("You can leave this page at any time", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectBankScreen_RendersLifecycleTimelineStages()
    {
        var source = ReadConnectBankSource();
        Assert.Contains("Authorized with bank", source, StringComparison.Ordinal);
        Assert.Contains("Connection secured", source, StringComparison.Ordinal);
        Assert.Contains("Balances fetched", source, StringComparison.Ordinal);
        Assert.Contains("Transactions imported", source, StringComparison.Ordinal);
        Assert.Contains("Activity organized", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectBankScreen_CloseActionRemainsOptionalDuringSync()
    {
        var source = ReadConnectBankSource();
        Assert.DoesNotContain("disabled={isSyncingInProgress}", source, StringComparison.Ordinal);
    }

    private static string ReadConnectBankSource()
    {
        var repoRoot = ResolveRepoRoot();
        var path = Path.Combine(
            repoRoot,
            "apps",
            "mobile",
            "app",
            "(tabs)",
            "accounts",
            "connect-bank.tsx");
        Assert.True(File.Exists(path), $"Expected connect-bank screen at {path}");
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static string ReadConnectionStatusIndicatorSource()
    {
        var repoRoot = ResolveRepoRoot();
        var path = Path.Combine(
            repoRoot,
            "apps",
            "mobile",
            "src",
            "components",
            "ui",
            "ConnectionStatusIndicator.tsx");
        Assert.True(File.Exists(path), $"Expected connection indicator source at {path}");
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static string ResolveRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var workspaceMarker = Path.Combine(directory.FullName, "pnpm-workspace.yaml");
            if (File.Exists(workspaceMarker))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root from test runtime directory.");
    }
}
