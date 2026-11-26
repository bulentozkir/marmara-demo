using System.Data.Common;
using Azure;
using Azure.Identity;
using DotNetCoreSqlDb.Data;
using Microsoft.EntityFrameworkCore;

namespace DotNetCoreSqlDb.Services;

public sealed class TodoSearchSyncWorker(IServiceScopeFactory scopes, ITodoSearch search,
    ILogger<TodoSearchSyncWorker> logger) : BackgroundService
{
    public async Task<int> SynchronizePendingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDatabaseContext>();
        var operations = await db.SearchOperations.OrderBy(operation => operation.ID)
            .Take(100).ToListAsync(cancellationToken);
        if (operations.Count == 0)
            return 0;

        var ids = operations.Select(operation => operation.DocumentId).Distinct().ToArray();
        var currentTodos = await db.Todo.AsNoTracking()
            .Where(todo => todo.OwnerId != null && ids.Contains(todo.SearchDocumentId))
            .ToListAsync(cancellationToken);
        await search.ApplyChangesAsync(ids, currentTodos, cancellationToken);
        db.SearchOperations.RemoveRange(operations);
        await db.SaveChangesAsync(cancellationToken);
        return operations.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            do
            {
                try
                {
                    await SynchronizePendingAsync(stoppingToken);
                }
                catch (RequestFailedException exception)
                {
                    logger.LogError(exception, "Azure AI Search synchronization failed; durable operations remain queued.");
                }
                catch (AuthenticationFailedException exception)
                {
                    logger.LogError(exception, "Search identity authentication failed; durable operations remain queued.");
                }
                catch (DbException exception)
                {
                    logger.LogError(exception, "Search synchronization is waiting for SQL readiness.");
                }
                catch (DbUpdateException exception)
                {
                    logger.LogError(exception, "Search synchronization could not acknowledge its durable SQL operations.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
