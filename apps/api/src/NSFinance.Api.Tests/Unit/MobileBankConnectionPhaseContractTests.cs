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
        Assert.Contains("You can leave this page.", source, StringComparison.Ordinal);
        Assert.Contains("finishing the connection in the background", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConnectBankScreen_HidesSafeCloseCardDuringIdleAndPreAuth()
    {
        var source = ReadConnectBankSource();
        Assert.Contains("showSafeCloseCard", source, StringComparison.Ordinal);
        Assert.Contains("uiState !== \"not_connected\"", source, StringComparison.Ordinal);
        Assert.Contains("uiState !== \"opening_bank\"", source, StringComparison.Ordinal);
        Assert.Contains("uiState !== \"awaiting_consent\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectBankScreen_RemovesLifecycleChecklistDuplication()
    {
        var source = ReadConnectBankSource();
        Assert.DoesNotContain("timelineCard", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorized with bank", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Balances fetched", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Activity organized", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectBankScreen_RemovesManualCloseAction()
    {
        var source = ReadConnectBankSource();
        Assert.DoesNotContain("secondaryActionLabel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("modal_close", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectBankScreen_DetailsCard_IsReducedAndUsesInlinePlaceholder()
    {
        var source = ReadConnectBankSource();
        Assert.Contains("Waiting for accounts", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Provider note:", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Date added:", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Last sync:", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectBankScreen_UsesCenteredTitleAndAutoReturnOnSuccess()
    {
        var source = ReadConnectBankSource();
        Assert.Contains("justifyContent: \"center\"", source, StringComparison.Ordinal);
        Assert.Contains("autoReturnArmed", source, StringComparison.Ordinal);
        Assert.Contains("navigateBackToOrigin(\"connection_success\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectBankScreen_ConfirmsSpecificAttemptAfterDeepLinkReturn()
    {
        var source = ReadConnectBankSource();
        Assert.Contains("attemptId?: string;", source, StringComparison.Ordinal);
        Assert.Contains("setPendingAttemptId(response.attemptId)", source, StringComparison.Ordinal);
        Assert.Contains("confirmAttemptReturnMutation", source, StringComparison.Ordinal);
        Assert.Contains("attempt_return_confirmed", source, StringComparison.Ordinal);
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
