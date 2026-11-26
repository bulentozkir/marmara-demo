using DotNetCoreSqlDb.Models;

namespace DotNetCoreSqlDb.Services;

public sealed record TodoSearchPage(IReadOnlyList<Guid> DocumentIds, long TotalMatches);

public interface ITodoSearch
{
    public const int PageSize = 25;

    Task<TodoSearchPage> SearchAsync(string query, string ownerId, int page, CancellationToken cancellationToken);
    Task ApplyChangesAsync(IReadOnlyList<Guid> documentIds, IReadOnlyList<Todo> currentTodos, CancellationToken cancellationToken);
}
