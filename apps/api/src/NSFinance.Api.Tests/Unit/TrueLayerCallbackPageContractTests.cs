using System.Text;

namespace NSFinance.Api.Tests.Unit;

public class TrueLayerCallbackPageContractTests
{
    [Fact]
    public void CallbackPage_ContainsStaleTabAndAutoCloseSafetyHandling()
    {
        var source = ReadCallbackEndpointSource();
        Assert.Contains("sessionStorage", source, StringComparison.Ordinal);
        Assert.Contains("already handled", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("window.close()", source, StringComparison.Ordinal);
        Assert.Contains("You can close this tab now", source, StringComparison.Ordinal);
        Assert.Contains("Reopening NSFinance now", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadCallbackEndpointSource()
    {
        var repoRoot = ResolveRepoRoot();
        var path = Path.Combine(
            repoRoot,
            "apps",
            "api",
            "src",
            "NSFinance.Api",
            "Modules",
            "Banking",
            "Endpoints",
            "TrueLayerCallbackEndpoint.cs");
        Assert.True(File.Exists(path), $"Expected callback endpoint source at {path}");
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
