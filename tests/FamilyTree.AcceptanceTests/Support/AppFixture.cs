using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace FamilyTree.AcceptanceTests.Support;

/// <summary>One of these is created per scenario (Reqnroll's context-injection: any binding class that
/// takes this as a constructor parameter shares the same instance for that scenario). It boots the app
/// against a fresh temp data directory and git repo, so scenarios never see each other's data.</summary>
public sealed class AppFixture : IDisposable
{
    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "FamilyTreeAcceptanceTests", Guid.NewGuid().ToString("N"));

    public WebApplicationFactory<Program> Factory { get; }

    public HttpClient Client { get; }

    public AppFixture()
    {
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FamilyTreeData:RepositoryPath"] = DataDirectory,
                    ["FamilyTreeData:DefaultCommitAuthorName"] = "Acceptance Test",
                    ["FamilyTreeData:DefaultCommitAuthorEmail"] = "acceptance-test@example.com",
                });
            });
        });

        Client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();

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
}
