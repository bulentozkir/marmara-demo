using DotNetCoreSqlDb.Models;
using DotNetCoreSqlDb.Services;

namespace TodoApp.Tests;

public sealed class FakeTodoSearch : ITodoSearch
{
    public TodoSearchPage Result { get; set; } = new([], 0);
    public string? LastOwnerId { get; private set; }
    public Exception? Failure { get; set; }
    public IReadOnlyList<Guid> AppliedIds { get; private set; } = [];
    public IReadOnlyList<Todo> AppliedTodos { get; private set; } = [];

    public Task<TodoSearchPage> SearchAsync(string query, string ownerId, int page, CancellationToken cancellationToken)
    {
        LastOwnerId = ownerId;
        if (Failure is not null)
            return Task.FromException<TodoSearchPage>(Failure);
        return Task.FromResult(Result);
    }

    public Task ApplyChangesAsync(IReadOnlyList<Guid> documentIds, IReadOnlyList<Todo> currentTodos,
        CancellationToken cancellationToken)
    {
        if (Failure is not null)
            return Task.FromException(Failure);
        AppliedIds = documentIds;
        AppliedTodos = currentTodos;
        return Task.CompletedTask;
    }
}
