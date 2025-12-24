using Gam.Core.Abstractions;
using Gam.Storage.Postgres.Retrievers;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gam.Storage.Postgres;

/// <summary>
/// Extension methods for registering PostgreSQL storage services.
/// </summary>
public static class PostgresServiceCollectionExtensions
{
    /// <summary>
    /// Add PostgreSQL storage services with pgvector and pg_search support.
    /// </summary>
    public static IServiceCollection AddGamPostgresStorage(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton(_ =>
        {
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            builder.UseVector();
            return builder.Build();
        });
        
        services.AddSingleton<IMemoryStore, PostgresMemoryStore>();
        services.AddSingleton<IKeywordRetriever, PostgresKeywordRetriever>();
        services.AddSingleton<IVectorRetriever, PostgresVectorRetriever>();
        services.AddSingleton<IPageIndexRetriever, PostgresPageIndexRetriever>();
        
        return services;
    }
}
