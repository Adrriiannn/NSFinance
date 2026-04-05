using System.Text;

namespace NSFinance.Api.Tests.Unit;

public class MobileSemanticResolverContractTests
{
    [Fact]
    public void MobileSemanticResolver_DeterministicBranch_DoesNotDependOnTaxonomyFields()
    {
        var source = ReadSemanticResolverSource();
        var deterministicBranchStart = source.IndexOf(
            "if (transaction.deterministicClassificationStatus === \"classified_matched_rule\")",
            StringComparison.Ordinal);
        Assert.True(deterministicBranchStart >= 0, "Could not locate deterministic branch in mobile semantic resolver.");

        var deterministicBranchEnd = source.IndexOf(
            "if (transaction.deterministicClassificationStatus === \"deferred_waiting_for_counterparty\"",
            deterministicBranchStart,
            StringComparison.Ordinal);
        Assert.True(deterministicBranchEnd > deterministicBranchStart, "Could not isolate deterministic branch.");

        var deterministicBranch = source[deterministicBranchStart..deterministicBranchEnd];
        Assert.DoesNotContain("taxonomyCategory", deterministicBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("taxonomySubcategory", deterministicBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("displaySemantic", deterministicBranch, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileSemanticResolver_NoLegacyTransferStylingFallback_IsPresent()
    {
        var source = ReadSemanticResolverSource();
        Assert.DoesNotContain("allowLegacyFallback", source, StringComparison.Ordinal);
        Assert.DoesNotContain("reasonSource: \"legacy_fallback\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileSemanticResolver_PresentationDiagnostics_ExposesSingleStyleSource()
    {
        var source = ReadSemanticResolverSource();
        Assert.Contains("export function resolveCanonicalPresentationDiagnostics", source, StringComparison.Ordinal);
        Assert.Contains("stylingSource", source, StringComparison.Ordinal);
        Assert.Contains("\"deterministic_semantic\"", source, StringComparison.Ordinal);
        Assert.Contains("\"taxonomy_fallback\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileSemanticResolver_TaxonomyFallback_NeverCreatesTransferFamilyStyling()
    {
        var source = ReadSemanticResolverSource();
        var semanticFunctionStart = source.IndexOf(
            "export function resolveCanonicalTransactionSemantic",
            StringComparison.Ordinal);
        Assert.True(semanticFunctionStart >= 0, "Could not locate resolveCanonicalTransactionSemantic.");

        var semanticFunctionEnd = source.IndexOf(
            "export function resolveCanonicalPresentationDiagnostics",
            semanticFunctionStart,
            StringComparison.Ordinal);
        Assert.True(semanticFunctionEnd > semanticFunctionStart, "Could not isolate semantic resolver function.");

        var semanticFunction = source[semanticFunctionStart..semanticFunctionEnd];
        Assert.DoesNotContain("taxonomyCategoryName", semanticFunction, StringComparison.Ordinal);
        Assert.DoesNotContain("taxonomySubcategoryName", semanticFunction, StringComparison.Ordinal);
    }

    private static string ReadSemanticResolverSource()
    {
        var repoRoot = ResolveRepoRoot();
        var resolverPath = Path.Combine(
            repoRoot,
            "apps",
            "mobile",
            "src",
            "features",
            "transactions",
            "semanticResolver.ts");
        Assert.True(File.Exists(resolverPath), $"Expected mobile semantic resolver at {resolverPath}");
        return File.ReadAllText(resolverPath, Encoding.UTF8);
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
