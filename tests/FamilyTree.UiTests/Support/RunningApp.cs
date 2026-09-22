using System.Diagnostics;
using System.Net.Sockets;

namespace FamilyTree.UiTests.Support;

/// <summary>
/// Launches the real FamilyTree.Web app as a child process listening on a real loopback port, so
/// Playwright's browser has an actual URL to navigate to. (A plain WebApplicationFactory only serves
/// requests through an in-memory TestServer with no real socket; forcing it to use Kestrel instead ran
/// into a WebApplicationFactory internal that still expects to cast the server to TestServer even
/// after UseKestrel() — a real child process sidesteps that entirely, and is arguably more honest
/// end-to-end testing anyway.)
/// </summary>
public sealed class RunningApp : IDisposable
{
    private readonly Process _process;

    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "FamilyTreeUiTests", Guid.NewGuid().ToString("N"));

    public string BaseUrl { get; }

    public RunningApp()
    {
        BaseUrl = $"http://127.0.0.1:{GetFreeTcpPort()}";

        var (projectDirectory, outputDirectory) = FindWebProjectDirectories();

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            // ASP.NET Core's static web assets manifest resolves physical file locations relative to
            // the content root, which defaults to the process's working directory — not the .dll's own
            // folder. Running from bin/<cfg>/<tfm> (instead of the project directory) silently breaks
            // static file serving (confirmed: htmx.min.js 404s and `window.htmx` stays undefined),
            // so this must be the project source directory, with the built .dll referenced by path.
            WorkingDirectory = projectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(Path.Combine(outputDirectory, "FamilyTree.Web.dll"));
        startInfo.EnvironmentVariables["ASPNETCORE_URLS"] = BaseUrl;
        startInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.EnvironmentVariables["FamilyTreeData__RepositoryPath"] = DataDirectory;
        startInfo.EnvironmentVariables["FamilyTreeData__DefaultCommitAuthorName"] = "UI Test";
        startInfo.EnvironmentVariables["FamilyTreeData__DefaultCommitAuthorEmail"] = "ui-test@example.com";

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start FamilyTree.Web.dll.");

        WaitUntilListening();
    }

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5000);
        }

        _process.Dispose();

        if (!Directory.Exists(DataDirectory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(DataDirectory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(DataDirectory, recursive: true);
    }

    private void WaitUntilListening()
    {
        using var client = new HttpClient();
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
            {
                var stderr = _process.StandardError.ReadToEnd();
                throw new InvalidOperationException($"FamilyTree.Web exited early (code {_process.ExitCode}).\n{stderr}");
            }

            try
            {
                using var response = client.GetAsync(BaseUrl).GetAwaiter().GetResult();
                return;
            }
            catch (HttpRequestException)
            {
                Thread.Sleep(200);
            }
        }

        throw new TimeoutException($"FamilyTree.Web did not start listening on {BaseUrl} within 30 seconds.");
    }

    /// <summary>Mirrors this test assembly's own bin/&lt;Configuration&gt;/&lt;TFM&gt; folder name onto
    /// the sibling FamilyTree.Web project, rather than assuming "Debug"/a specific TFM outright.
    /// Returns both the project source directory (used as the process's working directory) and its
    /// build output directory (where FamilyTree.Web.dll actually lives).</summary>
    private static (string ProjectDirectory, string OutputDirectory) FindWebProjectDirectories()
    {
        var outputDir = new DirectoryInfo(AppContext.BaseDirectory);
        var tfm = outputDir.Name;
        var configuration = outputDir.Parent!.Name;
        var repoRoot = outputDir.Parent!.Parent!.Parent!.Parent!.Parent!; // bin/<cfg>/<tfm> -> UiTests -> tests -> repo root

        var projectDirectory = Path.Combine(repoRoot.FullName, "src", "FamilyTree.Web");
        var webOutputDirectory = Path.Combine(projectDirectory, "bin", configuration, tfm);

        return (projectDirectory, webOutputDirectory);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
