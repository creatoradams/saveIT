using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SaveIT.Api.Data;
using Xunit;

namespace SaveIT.Api.Tests;

public sealed class ApiFactory(string connection, bool developmentIdentity = true, string environment = "Development") : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment(environment)
        .UseSetting("ConnectionStrings:App", connection)
        .UseSetting("DevelopmentIdentity:Enabled", developmentIdentity.ToString());
}

public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly string database = "saveit_tests_" + Guid.NewGuid().ToString("N");
    private readonly string bootstrap = Environment.GetEnvironmentVariable("SAVEIT_TEST_ADMIN")
        ?? "Host=127.0.0.1;Port=55432;Database=postgres;Username=postgres;Password=local-development-only";
    public string OwnerConnection { get; private set; } = "";
    public string AppConnection { get; private set; } = "";
    public string WorkerConnection { get; private set; } = "";
    public ApiFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await using var admin = new NpgsqlConnection(bootstrap);
        await admin.OpenAsync();
        // Only synthetic, isolated test databases. The application never performs this bootstrap.
        await using (var role = new NpgsqlCommand("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='saveit_app') THEN CREATE ROLE saveit_app LOGIN PASSWORD 'synthetic-app-only' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS; END IF; END $$", admin)) await role.ExecuteNonQueryAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin)) await create.ExecuteNonQueryAsync();
        await using (var workerRole = new NpgsqlCommand("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='saveit_search_worker') THEN CREATE ROLE saveit_search_worker LOGIN PASSWORD 'synthetic-worker-only' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS; END IF; END $$", admin)) await workerRole.ExecuteNonQueryAsync();
        OwnerConnection = new NpgsqlConnectionStringBuilder(bootstrap) { Database = database }.ConnectionString;
        AppConnection = new NpgsqlConnectionStringBuilder(OwnerConnection) { Username = "saveit_app", Password = "synthetic-app-only" }.ConnectionString;
        WorkerConnection = new NpgsqlConnectionStringBuilder(OwnerConnection) { Username = "saveit_search_worker", Password = "synthetic-worker-only" }.ConnectionString;
        var scope = new TenantScope();
        await using var db = new SaveItDb(new DbContextOptionsBuilder<SaveItDb>().UseNpgsql(OwnerConnection).Options, scope);
        await db.Database.MigrateAsync();
        await DevelopmentSeed.SeedAsync(db, scope);
        Factory = new ApiFactory(AppConnection);
    }

    public async Task<T> Scalar<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
    public async Task Execute(string sql)
    {
        await using var connection = new NpgsqlConnection(OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(bootstrap);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", admin);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition("database", DisableParallelization = true)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
