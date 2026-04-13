using System.Text;

namespace NSFinance.Api.Tests.Unit;

public class MobileHomeEntranceAnimationContractTests
{
    [Fact]
    public void EntranceAnimation_HardensAgainstResumeDimming()
    {
        var source = ReadHookSource();
        Assert.Contains("AppState.addEventListener", source, StringComparison.Ordinal);
        Assert.Contains("currentOpacity >= 0.99", source, StringComparison.Ordinal);
        Assert.Contains("toValue: 1", source, StringComparison.Ordinal);
    }

    private static string ReadHookSource()
    {
        var repoRoot = ResolveRepoRoot();
        var path = Path.Combine(
            repoRoot,
            "apps",
            "mobile",
            "src",
            "hooks",
            "useEntranceAnimation.ts");
        Assert.True(File.Exists(path), $"Expected entrance animation hook at {path}");
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
