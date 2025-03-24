//DbContextFactory.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oracle.ManagedDataAccess.Client;
using System.Data;

namespace Shield.Estimator.Shared.Components.EntityFrameworkCore;

public interface IDbContextFactory
{
    Task<BaseDbContext> CreateDbContext(string dbType, string connectionString, string scheme = null);
}
public class DbContextFactory : IDbContextFactory
{
    public async Task<BaseDbContext> CreateDbContext(string dbType, string connectionString, string scheme = null)
    {
        return dbType switch
        {
            "Oracle" => await CreateOracleContext(connectionString, scheme),
            "Postgres" => CreatePostgresContext(connectionString),
            _ => throw new NotSupportedException($"Unsupported database type: {dbType}")
        };
    }
    private async Task<BaseDbContext> CreateOracleContext(string connectionString, string scheme)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OracleDbContext>();
        optionsBuilder
            .UseOracle(connectionString, providerOptions =>
            {
                providerOptions.CommandTimeout(60);
                providerOptions.UseRelationalNulls(true);
                //providerOptions.MinBatchSize(2); //размер 42 был найден как оптимальный для многих сценариев
                //providerOptions.UseOracleSQLCompatibility(OracleSQLCompatibility.DatabaseVersion21); // 
            })
            .EnableDetailedErrors(true)
            .EnableSensitiveDataLogging(false)
            //.LogTo(Console.WriteLine, LogLevel.Information)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

        var context = new OracleDbContext(optionsBuilder.Options/*, scheme*/);
        if (!string.IsNullOrEmpty(scheme))
        {
            await context.Database.OpenConnectionAsync();
            await context.Database.ExecuteSqlRawAsync($"ALTER SESSION SET CURRENT_SCHEMA = {scheme}");
            /*
            await using var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = $"ALTER SESSION SET CURRENT_SCHEMA = {scheme}";
            await command.ExecuteNonQueryAsync();
            */
        }
        return context;
    }

    private BaseDbContext CreatePostgresContext(string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PostgresDbContext>();
        optionsBuilder.UseNpgsql(connectionString, providerOptions =>
        {
            providerOptions.CommandTimeout(60);
            providerOptions.UseRelationalNulls(true);
            //providerOptions.MinBatchSize(2);
        })
            .EnableDetailedErrors(false)
            .EnableSensitiveDataLogging(false)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

        return new PostgresDbContext(optionsBuilder.Options);
    }
}
