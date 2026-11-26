using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Search.Documents;
using DotNetCoreSqlDb.Services;

namespace TodoApp.Tests;

public class AzureTodoSearchTests
{
    [Fact]
    public async Task AzureRequestAlwaysIncludesEscapedOwnerFilterAndReturnsOnlyDocumentIds()
    {
        var documentId = Guid.NewGuid();
        using var handler = new SearchHandler(documentId);
        using var http = new HttpClient(handler);
        var options = new SearchClientOptions { Transport = new HttpClientTransport(http) };
        options.Retry.MaxRetries = 0;
        var client = new SearchClient(new Uri("https://example.search.windows.net"), "todos",
            new TestCredential(), options);
        var search = new AzureTodoSearch(client);

        var result = await search.SearchAsync("rent", "owner'123", 2, CancellationToken.None);
        using var payload = JsonDocument.Parse(handler.Body
            ?? throw new InvalidOperationException("The Azure Search request was not sent."));
        Assert.Equal("ownerId eq 'owner''123'", payload.RootElement.GetProperty("filter").GetString());
        Assert.Equal("id", payload.RootElement.GetProperty("select").GetString());
        Assert.Equal(ITodoSearch.PageSize, payload.RootElement.GetProperty("skip").GetInt32());
        Assert.Equal(documentId, Assert.Single(result.DocumentIds));
    }

    private sealed class SearchHandler(Guid documentId) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await (request.Content ?? throw new InvalidOperationException("Missing Search request body."))
                .ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"@odata.count":1,"value":[{"@search.score":1.0,"id":"{{documentId:N}}"}]}""",
                    Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class TestCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
