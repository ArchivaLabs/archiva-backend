using System.Data.Common;
using Microsoft.Data.SqlClient;
using Respawn;
using Respawn.Graph;

namespace Archiva.Application.FunctionalTests.Infrastructure;

internal sealed class DatabaseResetter : IAsyncDisposable
{
    private readonly DbConnection _connection;
    private readonly Respawner _respawner;

    private DatabaseResetter(DbConnection connection, Respawner respawner)
    {
        _connection = connection;
        _respawner = respawner;
    }

    public static async Task<DatabaseResetter> CreateAsync(string connectionString)
    {
        var connection = new SqlConnection(connectionString);

        await connection.OpenAsync();
        var respawner = await Respawner.CreateAsync(
            connection,
            new RespawnerOptions
            {
                DbAdapter = DbAdapter.SqlServer,
                TablesToIgnore = [new Table("__EFMigrationsHistory")],
            }
        );
        await connection.CloseAsync();
        return new DatabaseResetter(connection, respawner);
    }

    public async Task ResetAsync()
    {
        await _connection.OpenAsync();
        try
        {
            await _respawner.ResetAsync(_connection);
        }
        finally
        {
            await _connection.CloseAsync();
        }
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}
