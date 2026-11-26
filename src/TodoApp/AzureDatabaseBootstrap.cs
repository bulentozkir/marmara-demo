using DotNetCoreSqlDb.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

internal static class AzureDatabaseBootstrap
{
    public static async Task<DatabaseBootstrapResult> InitializeAsync(
        string token, string server, string database, string clientId, string principalName)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("The deployment SQL access token is empty.");
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(principalName, @"\A[a-z][a-z0-9-]{2,127}\z"))
        {
            throw new InvalidOperationException("The configured SQL application identity name is invalid.");
        }

        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = $"tcp:{server},1433",
            InitialCatalog = database,
            Encrypt = true,
            TrustServerCertificate = false,
            ConnectTimeout = 60
        };
        await using var connection = new SqlConnection(connectionString.ConnectionString)
        {
            AccessToken = token
        };
        var options = new DbContextOptionsBuilder<MyDatabaseContext>()
            .UseSqlServer(connection, sql => sql.EnableRetryOnFailure())
            .Options;
        await using var context = new MyDatabaseContext(options);
        await context.Database.OpenConnectionAsync();
        await using var authorization = connection.CreateCommand();
        authorization.CommandText = "SELECT CASE WHEN USER_NAME() = N'dbo' OR IS_ROLEMEMBER(N'db_owner') = 1 THEN 1 ELSE 0 END;";
        if (await authorization.ExecuteScalarAsync() is not int membership || membership != 1)
        {
            throw new UnauthorizedAccessException("SQL database administrator access is required.");
        }
        await context.Database.MigrateAsync();
        await context.Users.AnyAsync();

        // Service-principal SIDs use the client ID, not the Entra object ID, without a Graph lookup.
        var sid = Convert.ToHexString(Guid.Parse(clientId).ToByteArray());
        var grantSql = $"""
            IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'{principalName}' AND sid <> 0x{sid})
                THROW 50000, 'The existing application user belongs to a different identity.', 1;
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'{principalName}')
                CREATE USER [{principalName}] WITH SID = 0x{sid}, TYPE = E, DEFAULT_SCHEMA = [dbo];
            IF IS_ROLEMEMBER(N'db_datareader', N'{principalName}') <> 1
                ALTER ROLE [db_datareader] ADD MEMBER [{principalName}];
            IF IS_ROLEMEMBER(N'db_datawriter', N'{principalName}') <> 1
                ALTER ROLE [db_datawriter] ADD MEMBER [{principalName}];
            IF IS_ROLEMEMBER(N'db_owner', N'{principalName}') = 1 OR IS_ROLEMEMBER(N'db_ddladmin', N'{principalName}') = 1
                THROW 50001, 'The application identity must not have database owner or DDL permissions.', 1;
            """;
        await context.Database.ExecuteSqlRawAsync(grantSql);
        await context.Database.OpenConnectionAsync();
        await using var verification = connection.CreateCommand();
        verification.CommandText = $"""
            SELECT CONVERT(uniqueidentifier, sid),
                IS_ROLEMEMBER(N'db_datareader', N'{principalName}'),
                IS_ROLEMEMBER(N'db_datawriter', N'{principalName}'),
                DB_NAME()
            FROM sys.database_principals WHERE name = N'{principalName}';
            """;
        await using var reader = await verification.ExecuteReaderAsync();
        if (!await reader.ReadAsync() || reader.GetGuid(0) != Guid.Parse(clientId)
            || reader.GetInt32(1) != 1 || reader.GetInt32(2) != 1 || reader.GetString(3) != database)
        {
            throw new InvalidOperationException("The application SQL identity or database roles were not configured correctly.");
        }
        Console.WriteLine("Database migrations completed; app identity has SQL reader/writer permissions.");
        return new DatabaseBootstrapResult(true, principalName, reader.GetGuid(0), database, 2);
    }
}

internal sealed record DatabaseBootstrapResult(bool Initialized, string SqlUser, Guid ClientId, string Database, int SchemaVersion);
