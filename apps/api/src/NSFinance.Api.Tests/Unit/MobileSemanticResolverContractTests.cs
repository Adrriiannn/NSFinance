using System.Text;

namespace NSFinance.Api.Tests.Unit;

public class MobileSemanticResolverContractTests
{
    [Fact]
    public void MobileSemanticResolver_DeterministicSavingsSubtype_IgnoresDisplaySemantic()
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

        var source = File.ReadAllText(resolverPath, Encoding.UTF8);
        var deterministicBranchStart = source.IndexOf(
            "if (transaction.deterministicClassificationStatus === \"classified_matched_rule\")",
            StringComparison.Ordinal);
        Assert.True(deterministicBranchStart >= 0, "Could not locate deterministic branch in mobile semantic resolver.");

        var fallbackStart = source.IndexOf("const allowLegacyFallback =", deterministicBranchStart, StringComparison.Ordinal);
        Assert.True(fallbackStart > deterministicBranchStart, "Could not locate legacy fallback branch in mobile semantic resolver.");

        var deterministicBranch = source[deterministicBranchStart..fallbackStart];
        Assert.DoesNotContain("displaySemantic", deterministicBranch, StringComparison.Ordinal);
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
