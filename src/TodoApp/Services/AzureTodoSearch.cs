using System.Text.Json.Serialization;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using DotNetCoreSqlDb.Models;

namespace DotNetCoreSqlDb.Services;

public sealed class AzureTodoSearch(SearchClient client) : ITodoSearch
{
    public async Task<TodoSearchPage> SearchAsync(string query, string ownerId, int page, CancellationToken cancellationToken)
    {
        var options = new SearchOptions
        {
            Filter = SearchFilter.Create($"ownerId eq {ownerId}"),
            IncludeTotalCount = true,
            Size = ITodoSearch.PageSize,
            Skip = (Math.Clamp(page, 1, 400) - 1) * ITodoSearch.PageSize,
            SearchMode = SearchMode.All
        };
        options.Select.Add("id");
        options.SearchFields.Add("description");
        var response = await client.SearchAsync<TodoSearchDocument>(query, options, cancellationToken);
        var ids = new List<Guid>();
        await foreach (var hit in response.Value.GetResultsAsync().WithCancellation(cancellationToken))
            ids.Add(Guid.Parse(hit.Document.Id));

        return new TodoSearchPage(ids, response.Value.TotalCount
            ?? throw new InvalidOperationException("Azure AI Search did not return the requested result count."));
    }

    public async Task ApplyChangesAsync(IReadOnlyList<Guid> documentIds, IReadOnlyList<Todo> currentTodos,
        CancellationToken cancellationToken)
    {
        var current = currentTodos.ToDictionary(todo => todo.SearchDocumentId);
        var actions = documentIds.Distinct().Select(id =>
        {
            if (current.TryGetValue(id, out var todo) && todo.OwnerId is not null)
            {
                return IndexDocumentsAction.Upload(new TodoSearchDocument
                {
                    Id = id.ToString("N"),
                    OwnerId = todo.OwnerId,
                    Description = todo.Description
                });
            }
            return IndexDocumentsAction.Delete(new TodoSearchDocument { Id = id.ToString("N") });
        }).ToArray();
        if (actions.Length == 0)
            return;

        await client.IndexDocumentsAsync(IndexDocumentsBatch.Create(actions),
            new IndexDocumentsOptions { ThrowOnAnyError = true }, cancellationToken);
    }
}

public sealed class TodoSearchDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("ownerId")]
    public string OwnerId { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
}
