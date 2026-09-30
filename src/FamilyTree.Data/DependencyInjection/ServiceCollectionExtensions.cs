using FamilyTree.Data.Git;
using FamilyTree.Data.Images;
using FamilyTree.Data.Options;
using FamilyTree.Data.Parsing;
using FamilyTree.Data.Relationships;
using FamilyTree.Data.Repository;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FamilyTree.Data.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <param name="basePath">Used to resolve <see cref="FamilyTreeDataOptions.RepositoryPath"/> when it's a relative path — pass the host's content root.</param>
    public static IServiceCollection AddFamilyTreeData(this IServiceCollection services, IConfiguration configuration, string basePath)
    {
        services.Configure<FamilyTreeDataOptions>(configuration.GetSection(FamilyTreeDataOptions.SectionName));

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<FamilyTreeDataOptions>>().Value;
            var root = Path.IsPathRooted(options.RepositoryPath)
                ? options.RepositoryPath
                : Path.GetFullPath(Path.Combine(basePath, options.RepositoryPath));

            return new FamilyTreeDataPaths { RootPath = root };
        });

        services.AddSingleton<IGitRepositoryService, LibGit2GitRepositoryService>();
        services.AddSingleton<IPersonFileSerializer, YamlFrontMatterPersonSerializer>();
        services.AddSingleton<IRelationshipResolver, RelationshipGraphResolver>();
        services.AddSingleton<IFamilyGraphBuilder, FamilyGraphBuilder>();
        services.AddSingleton<IPersonRepository, FilePersonRepository>();
        services.AddSingleton<IPersonImageStore, LocalPersonImageStore>();

        return services;
    }
}
