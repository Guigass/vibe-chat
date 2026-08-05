using System.Data.Common;
using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using VibeChat.BuildingBlocks;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

/// <summary>
/// Applies <c>app.tenant_id</c> / <c>app.user_id</c> / <c>app.job_role</c> on every pooled connection open (SEC-RLS-RUNTIME).
/// </summary>
public sealed class RlsConnectionInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await RlsSession.ApplyAsync(connection, tenantContext, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        RlsSession.ApplyAsync(connection, tenantContext, CancellationToken.None).GetAwaiter().GetResult();
        base.ConnectionOpened(connection, eventData);
    }
}

public static class RlsSession
{
    public static async Task ApplyAsync(DbConnection connection, ITenantContext tenantContext, CancellationToken cancellationToken)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
              set_config('app.tenant_id', @tenant, false),
              set_config('app.user_id', @user_id, false),
              set_config('app.job_role', @job_role, false)
            """;
        var tenant = command.CreateParameter();
        tenant.ParameterName = "tenant";
        tenant.Value = tenantContext.HasTenant ? tenantContext.TenantId.Value.ToString() : string.Empty;
        command.Parameters.Add(tenant);

        var user = command.CreateParameter();
        user.ParameterName = "user_id";
        user.Value = tenantContext.HasUser ? tenantContext.UserId.Value.ToString() : string.Empty;
        command.Parameters.Add(user);

        var job = command.CreateParameter();
        job.ParameterName = "job_role";
        job.Value = tenantContext.JobRole ?? string.Empty;
        command.Parameters.Add(job);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static Task ApplyAsync(VibeChatDbContext dbContext, ITenantContext tenantContext, CancellationToken cancellationToken) =>
        ApplyAsync(dbContext.Database.GetDbConnection(), tenantContext, cancellationToken);

    public static async Task EnsureAppliedAsync(VibeChatDbContext dbContext, ITenantContext tenantContext, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        await ApplyAsync(connection, tenantContext, cancellationToken);
    }
}

/// <summary>
/// Bootstraps PostgreSQL roles, applies migrations with the migrator role, installs the RLS catalog,
/// and validates that the runtime connection is not privileged (SEC-RLS-RUNTIME).
/// </summary>
public static class DatabaseBootstrap
{
    public const string MigratorConnectionName = "DatabaseMigrator";
    public const string RuntimeConnectionName = "Database";

    public static string ResolveMigratorConnectionString(IConfiguration configuration)
    {
        var migrator = configuration.GetConnectionString(MigratorConnectionName);
        if (!string.IsNullOrWhiteSpace(migrator))
        {
            return migrator;
        }

        // Fallback keeps local DX working before .env is updated; runtime validation still enforces app role.
        return configuration.GetConnectionString(RuntimeConnectionName)
               ?? throw new InvalidOperationException("ConnectionStrings:Database is required.");
    }

    public static string ResolveRuntimeConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(RuntimeConnectionName)
        ?? throw new InvalidOperationException("ConnectionStrings:Database is required.");

    public static async Task MigrateSeedAndProtectAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseBootstrap");
        var bootstrap = configuration.GetConnectionString("DatabaseBootstrap")
                        ?? configuration["POSTGRES_BOOTSTRAP_CONNECTION"]
                        ?? BuildBootstrapFromEnv(configuration)
                        ?? ResolveMigratorConnectionString(configuration);

        await EnsureRolesAsync(bootstrap, configuration, logger, cancellationToken);

        var migratorCs = ResolveMigratorConnectionString(configuration);
        var migratorUser = configuration["POSTGRES_MIGRATOR_USER"] ?? "vibechat_migrator";
        await using (var migrator = CreateMigratorContext(migratorCs))
        {
            await migrator.Database.MigrateAsync(cancellationToken);
        }

        // Table ownership must sit with migrator so FORCE RLS applies to former bootstrap owner paths
        // and so GRANT to the app role succeeds without superuser on every request.
        await ReassignBusinessObjectOwnershipAsync(bootstrap, migratorUser, logger, cancellationToken);
        await ApplyRlsCatalogAsync(migratorCs, configuration, logger, cancellationToken);

        if (configuration.GetValue("Seed:Enabled", false))
        {
            await using var scope = services.CreateAsyncScope();
            // Seed uses a dedicated migrator-scoped context (BYPASSRLS) so FORCE RLS does not block bootstrap rows.
            var seedContext = CreateMigratorContext(migratorCs);
            await using (seedContext)
            {
                var seed = ActivatorUtilities.CreateInstance<SeedData>(
                    scope.ServiceProvider,
                    seedContext,
                    scope.ServiceProvider.GetRequiredService<IClock>(),
                    scope.ServiceProvider.GetRequiredService<ILogger<SeedData>>());
                await seed.SeedAsync(cancellationToken);
            }
        }

        var runtimeCs = ResolveRuntimeConnectionString(configuration);
        await ValidateRuntimeRoleAsync(runtimeCs, logger, cancellationToken);

        if (!environment.IsDevelopment() && IsSameRole(migratorCs, runtimeCs))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Database must use the app runtime role, not the migrator/bootstrap owner.");
        }
    }

    public static async Task ValidateRuntimeRoleAsync(string runtimeConnectionString, ILogger logger, CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(runtimeConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT
              current_user,
              session_user,
              EXISTS (
                SELECT 1 FROM pg_roles
                WHERE rolname = current_user AND (rolsuper OR rolbypassrls)
              ) AS privileged,
              EXISTS (
                SELECT 1
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'messaging' AND c.relname = 'messages' AND c.relowner = (SELECT oid FROM pg_roles WHERE rolname = current_user)
              ) AS owns_messages
            """;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Unable to inspect PostgreSQL runtime role.");
        }

        var user = reader.GetString(0);
        var privileged = reader.GetBoolean(2);
        var ownsMessages = reader.GetBoolean(3);
        if (privileged || ownsMessages)
        {
            throw new InvalidOperationException(
                $"Runtime PostgreSQL role '{user}' must not be superuser, BYPASSRLS, or owner of tenant tables (SEC-RLS-RUNTIME).");
        }

        logger.LogInformation("PostgreSQL runtime role {Role} accepted (no superuser/BYPASSRLS/ownership)", user);
    }

    private static VibeChatDbContext CreateMigratorContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<VibeChatDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new VibeChatDbContext(options, new TenantContext());
    }

    private static async Task EnsureRolesAsync(
        string bootstrapConnectionString,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var appUser = configuration["POSTGRES_APP_USER"] ?? "vibechat_app";
        var appPassword = configuration["POSTGRES_APP_PASSWORD"] ?? "vibechat_app_password_change_me";
        var migratorUser = configuration["POSTGRES_MIGRATOR_USER"] ?? "vibechat_migrator";
        var migratorPassword = configuration["POSTGRES_MIGRATOR_PASSWORD"] ?? "vibechat_migrator_password_change_me";
        var backupUser = configuration["POSTGRES_BACKUP_USER"] ?? "vibechat_backup";
        var backupPassword = configuration["POSTGRES_BACKUP_PASSWORD"] ?? "vibechat_backup_password_change_me";

        await using var conn = new NpgsqlConnection(bootstrapConnectionString);
        await conn.OpenAsync(cancellationToken);

        await ExecAsync(conn,
            $"""
             DO $do$
             BEGIN
               IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{EscapeLiteral(migratorUser)}') THEN
                 CREATE ROLE {QuoteIdent(migratorUser)} LOGIN PASSWORD '{EscapeLiteral(migratorPassword)}' BYPASSRLS;
               ELSE
                 ALTER ROLE {QuoteIdent(migratorUser)} WITH LOGIN BYPASSRLS PASSWORD '{EscapeLiteral(migratorPassword)}';
               END IF;
               IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{EscapeLiteral(appUser)}') THEN
                 CREATE ROLE {QuoteIdent(appUser)} LOGIN PASSWORD '{EscapeLiteral(appPassword)}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
               ELSE
                 ALTER ROLE {QuoteIdent(appUser)} WITH LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE PASSWORD '{EscapeLiteral(appPassword)}';
               END IF;
               IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{EscapeLiteral(backupUser)}') THEN
                 CREATE ROLE {QuoteIdent(backupUser)} LOGIN PASSWORD '{EscapeLiteral(backupPassword)}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
               ELSE
                 ALTER ROLE {QuoteIdent(backupUser)} WITH LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE PASSWORD '{EscapeLiteral(backupPassword)}';
               END IF;
             END
             $do$;
             """,
            cancellationToken);

        await ExecAsync(conn, $"GRANT CONNECT ON DATABASE {QuoteIdent(conn.Database)} TO {QuoteIdent(migratorUser)}, {QuoteIdent(appUser)}, {QuoteIdent(backupUser)};", cancellationToken);
        await ExecAsync(conn, $"GRANT CREATE ON DATABASE {QuoteIdent(conn.Database)} TO {QuoteIdent(migratorUser)};", cancellationToken);
        await ExecAsync(conn, $"GRANT {QuoteIdent(migratorUser)} TO CURRENT_USER;", cancellationToken);

        // Ensure migrator can manage existing objects created by the bootstrap owner.
        foreach (var schema in new[]
                 {
                     "public", "tenancy", "directory", "conversations", "messaging", "files",
                     "building_blocks", "audit", "ai", "notifications", "integrations", "identity"
                 })
        {
            await ExecAsync(conn,
                $"""
                 DO $do$
                 BEGIN
                   IF EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = '{EscapeLiteral(schema)}') THEN
                     GRANT ALL ON SCHEMA {QuoteIdent(schema)} TO {QuoteIdent(migratorUser)};
                     GRANT ALL ON ALL TABLES IN SCHEMA {QuoteIdent(schema)} TO {QuoteIdent(migratorUser)};
                     GRANT ALL ON ALL SEQUENCES IN SCHEMA {QuoteIdent(schema)} TO {QuoteIdent(migratorUser)};
                     EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT ALL ON TABLES TO %I', '{EscapeLiteral(schema)}', '{EscapeLiteral(migratorUser)}');
                     EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT ALL ON SEQUENCES TO %I', '{EscapeLiteral(schema)}', '{EscapeLiteral(migratorUser)}');
                   END IF;
                 END
                 $do$;
                 """,
                cancellationToken);
        }

        logger.LogInformation(
            "Ensured PostgreSQL roles {Migrator}, {App}, {Backup}",
            migratorUser,
            appUser,
            backupUser);
    }

    private static async Task ReassignBusinessObjectOwnershipAsync(
        string bootstrapConnectionString,
        string migratorUser,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(bootstrapConnectionString);
        await conn.OpenAsync(cancellationToken);
        await ExecAsync(conn,
            $"""
             DO $do$
             DECLARE
               r record;
             BEGIN
               FOR r IN
                 SELECT c.oid, n.nspname, c.relname, c.relkind
                 FROM pg_class c
                 JOIN pg_namespace n ON n.oid = c.relnamespace
                 WHERE n.nspname = ANY (ARRAY[
                   'tenancy','directory','conversations','messaging','files',
                   'building_blocks','audit','ai','notifications','integrations','identity','app'
                 ])
                   AND c.relkind IN ('r','p','S','v','m')
                   AND c.relowner = (SELECT oid FROM pg_roles WHERE rolname = current_user)
               LOOP
                 IF r.relkind = 'S' THEN
                   EXECUTE format('ALTER SEQUENCE %I.%I OWNER TO %I', r.nspname, r.relname, '{EscapeLiteral(migratorUser)}');
                 ELSE
                   EXECUTE format('ALTER TABLE %I.%I OWNER TO %I', r.nspname, r.relname, '{EscapeLiteral(migratorUser)}');
                 END IF;
               END LOOP;

               FOR r IN
                 SELECT n.nspname
                 FROM pg_namespace n
                 WHERE n.nspname = ANY (ARRAY[
                   'tenancy','directory','conversations','messaging','files',
                   'building_blocks','audit','ai','notifications','integrations','identity','app'
                 ])
                   AND n.nspowner = (SELECT oid FROM pg_roles WHERE rolname = current_user)
               LOOP
                 EXECUTE format('ALTER SCHEMA %I OWNER TO %I', r.nspname, '{EscapeLiteral(migratorUser)}');
               END LOOP;
             END
             $do$;
             """,
            cancellationToken);
        logger.LogInformation("Reassigned business schema ownership to {Migrator}", migratorUser);
    }

    private static async Task ApplyRlsCatalogAsync(
        string migratorConnectionString,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var sql = await LoadRlsCatalogSqlAsync(cancellationToken);
        var appUser = configuration["POSTGRES_APP_USER"] ?? "vibechat_app";
        var backupUser = configuration["POSTGRES_BACKUP_USER"] ?? "vibechat_backup";

        await using var conn = new NpgsqlConnection(migratorConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using (var preamble = conn.CreateCommand())
        {
            preamble.CommandText =
                """
                SELECT set_config('vibechat.app_role', @app, false),
                       set_config('vibechat.backup_role', @backup, false)
                """;
            preamble.Parameters.AddWithValue("app", appUser);
            preamble.Parameters.AddWithValue("backup", backupUser);
            await preamble.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        logger.LogInformation("Applied RLS catalog (FORCE + WITH CHECK + grants) for app role {AppRole}", appUser);
    }

    private static async Task<string> LoadRlsCatalogSqlAsync(CancellationToken cancellationToken)
    {
        var assembly = typeof(DatabaseBootstrap).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(x => x.EndsWith("03-rls.sql", StringComparison.OrdinalIgnoreCase));
        if (resourceName is not null)
        {
            await using var stream = assembly.GetManifestResourceStream(resourceName)
                                     ?? throw new InvalidOperationException($"Missing embedded RLS catalog {resourceName}");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync(cancellationToken);
        }

        var path = FindRepoFile(Path.Combine("infra", "compose", "postgres", "03-rls.sql"));
        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate {relativePath}");
    }

    private static string? BuildBootstrapFromEnv(IConfiguration configuration)
    {
        var host = configuration["POSTGRES_HOST"] ?? "localhost";
        var port = configuration["POSTGRES_PORT"] ?? "5432";
        var db = configuration["POSTGRES_DB"] ?? "vibechat";
        var user = configuration["POSTGRES_USER"];
        var password = configuration["POSTGRES_PASSWORD"];
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        return $"Host={host};Port={port};Database={db};Username={user};Password={password}";
    }

    private static bool IsSameRole(string left, string right)
    {
        var l = new NpgsqlConnectionStringBuilder(left);
        var r = new NpgsqlConnectionStringBuilder(right);
        return string.Equals(l.Username, r.Username, StringComparison.OrdinalIgnoreCase)
               && string.Equals(l.Database, r.Database, StringComparison.OrdinalIgnoreCase)
               && string.Equals(l.Host, r.Host, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string EscapeLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    private static string QuoteIdent(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
