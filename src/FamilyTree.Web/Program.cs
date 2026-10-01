using FamilyTree.Data.DependencyInjection;
using FamilyTree.Data.ExternalSources;
using FamilyTree.Data.Git;
using FamilyTree.Data.Options;
using FamilyTree.Web.Infrastructure;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<HtmxLayoutResultFilter>();
});

builder.Services.AddFamilyTreeData(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddExternalDataSources(builder.Configuration);
builder.Services.AddSingleton<ICommitAuthorProvider, DefaultCommitAuthorProvider>();

var app = builder.Build();

var dataOptions = app.Services.GetRequiredService<IOptions<FamilyTreeDataOptions>>().Value;
if (string.IsNullOrWhiteSpace(dataOptions.DefaultCommitAuthorName) || string.IsNullOrWhiteSpace(dataOptions.DefaultCommitAuthorEmail))
{
    throw new InvalidOperationException(
        "Set FamilyTreeData:DefaultCommitAuthorName and FamilyTreeData:DefaultCommitAuthorEmail (appsettings.json or an environment override) before running the app.");
}

// Ensure the data repository exists and is a git repo before serving any requests.
app.Services.GetRequiredService<IGitRepositoryService>().EnsureInitialized();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

/// <summary>Marker so test projects (WebApplicationFactory&lt;Program&gt;) can reference this entry point.</summary>
public partial class Program;
