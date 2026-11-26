using System.Net;
using System.Text.RegularExpressions;
using DotNetCoreSqlDb.Data;
using DotNetCoreSqlDb.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Azure;
using DotNetCoreSqlDb.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace TodoApp.Tests;

public class AccountAndTodoTests
{
    private const string Password = "Clear-Day!4826";
    private const string NewPassword = "Brighter-Day!7632";

    [Fact]
    public async Task AnonymousUsersSeeHelpButCannotAccessTodosOrProfile()
    {
        await using var app = new TodoAppFactory();
        await app.InitializeDatabaseAsync();
        using var client = app.NewClient();

        foreach (var path in new[] { "/", "/Todos", "/Account/Profile" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
        }
        foreach (var path in new[] { "/Home/Help", "/Home/Support", "/Home/DisplayOptions", "/Account/Login", "/Account/Register", "/health" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task DisplayPreferencesHaveTheirOwnPublicPageAndTopMenuLink()
    {
        await using var app = new TodoAppFactory();
        await app.InitializeDatabaseAsync();
        using var client = app.NewClient();
        var login = await client.GetStringAsync("/Account/Login");
        Assert.Contains("href=\"/Home/DisplayOptions\"", login);
        Assert.DoesNotContain("data-reading-action=", login);
        var display = await client.GetStringAsync("/Home/DisplayOptions");
        Assert.Contains("data-reading-action=\"increase\"", display);
        Assert.Contains("data-reading-action=\"contrast\"", display);
        Assert.Contains("aria-live=\"polite\"", display);
    }

    [Fact]
    public async Task RegistrationStoresHashAndIssuesSecureCookieWithoutExternalRedirect()
    {
        await using var app = new TodoAppFactory();
        await app.InitializeDatabaseAsync();
        using var client = app.NewClient();
        var response = await Register(client, "alice", "https://untrusted.example");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.ToString());
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("Marmara.Todo.Auth=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        using var scope = app.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<MyDatabaseContext>().Users.SingleAsync();
        Assert.NotNull(user.PasswordHash);
        Assert.NotEqual(Password, user.PasswordHash);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task TodosAreOwnerScopedAndForgedOwnerOrForeignDeleteCannotBypassIsolation()
    {
        await using var app = new TodoAppFactory();
        await app.InitializeDatabaseAsync();
        using var alice = app.NewClient();
        using var bob = app.NewClient();
        await Register(alice, "alice");
        await Register(bob, "bob");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDatabaseContext>();
        var bobId = (await db.Users.SingleAsync(user => user.UserName == "bob")).Id;
        db.Todo.Add(new Todo { Description = "Legacy unassigned task" });
        await db.SaveChangesAsync();

        await Post(alice, "/", "/Todos/Create", new()
        {
            ["NewTodo.Description"] = "Alice private task",
            ["NewTodo.OwnerId"] = bobId
        });
        var todo = await db.Todo.SingleAsync(item => item.Description == "Alice private task");
        Assert.NotEqual(bobId, todo.OwnerId);
        var alicePage = await alice.GetStringAsync("/");
        var bobPage = await bob.GetStringAsync("/");
        Assert.Contains("Alice private task", alicePage);
        Assert.DoesNotContain("Alice private task", bobPage);
        Assert.DoesNotContain("Legacy unassigned task", alicePage);
        Assert.DoesNotContain("Legacy unassigned task", bobPage);

        var denied = await Post(bob, "/", $"/Todos/Delete/{todo.ID}", new());
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.True(await db.Todo.AnyAsync(item => item.ID == todo.ID));
        Assert.Equal(HttpStatusCode.Redirect, (await Post(alice, "/", $"/Todos/Delete/{todo.ID}", new())).StatusCode);
        Assert.False(await db.Todo.AnyAsync(item => item.ID == todo.ID));
    }

    [Fact]
    public async Task UnsafeActionsRequireAntiforgeryAndLogoutRemovesAccess()
    {
        await using var app = new TodoAppFactory();
        await app.InitializeDatabaseAsync();
        using var client = app.NewClient();
        var missingToken = await client.PostAsync("/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        await Register(client, "alice");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsync("/Todos/Create", new FormUrlEncodedContent(new Dictionary<string, string> { ["NewTodo.Description"] = "Not allowed" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Account/Logout", null)).StatusCode);
        await Post(client, "/", "/Account/Logout", new());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task PasswordChangeRequiresOldPasswordAndInvalidatesOtherSessions()
    {
        await using var app = new TodoAppFactory();
        await app.InitializeDatabaseAsync();
        using var current = app.NewClient();
        using var otherSession = app.NewClient();
        await Register(current, "alice");
        Assert.Equal(HttpStatusCode.Redirect, (await Login(otherSession, "alice", Password)).StatusCode);

        var denied = await Post(current, "/Account/Profile", "/Account/ChangePassword", new()
        {
            ["Password.CurrentPassword"] = "Wrong-Password!123",
            ["Password.NewPassword"] = NewPassword,
            ["Password.ConfirmPassword"] = NewPassword
        });
        Assert.Equal(HttpStatusCode.OK, denied.StatusCode);
        Assert.Contains("Incorrect password", await denied.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var changed = await Post(current, "/Account/Profile", "/Account/ChangePassword", new()
        {
            ["Password.CurrentPassword"] = Password,
            ["Password.NewPassword"] = NewPassword,
            ["Password.ConfirmPassword"] = NewPassword
        });
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await current.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await otherSession.GetAsync("/")).StatusCode);
        using var fresh = app.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await Login(fresh, "alice", Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Login(fresh, "alice", NewPassword)).StatusCode);
    }

    [Fact]
    public async Task UsernameChangeRequiresCurrentPasswordAndPreservesTodos()
    {
        await using var app = new TodoAppFactory();
        await app.InitializeDatabaseAsync();
        using var client = app.NewClient();
        await Register(client, "alice");
        await Post(client, "/", "/Todos/Create", new() { ["NewTodo.Description"] = "Still mine" });
        var denied = await Post(client, "/Account/Profile", "/Account/UpdateProfile", new()
        {
            ["Account.Username"] = "alice-updated", ["Account.CurrentPassword"] = "Wrong-Password!123"
        });
        Assert.Equal(HttpStatusCode.OK, denied.StatusCode);
        var updated = await Post(client, "/Account/Profile", "/Account/UpdateProfile", new()
        {
            ["Account.Username"] = "alice-updated", ["Account.CurrentPassword"] = Password
        });
        Assert.Equal(HttpStatusCode.Redirect, updated.StatusCode);
        Assert.Contains("Still mine", await client.GetStringAsync("/"));
        using var fresh = app.NewClient();
        Assert.Equal(HttpStatusCode.Redirect, (await Login(fresh, "alice-updated", Password)).StatusCode);
    }

    [Fact]
    public async Task DeletingAccountRequiresConfirmationAndRemovesOnlyItsOwnData()
    {
        await using var app = new TodoAppFactory();
        await app.InitializeDatabaseAsync();
        using var alice = app.NewClient();
        using var bob = app.NewClient();
        await Register(alice, "alice");
        await Register(bob, "bob");
        await Post(alice, "/", "/Todos/Create", new() { ["NewTodo.Description"] = "Alice task" });
        await Post(bob, "/", "/Todos/Create", new() { ["NewTodo.Description"] = "Bob task" });

        var denied = await Post(alice, "/Account/Profile", "/Account/DeleteAccount", new()
        {
            ["Deletion.CurrentPassword"] = Password, ["Deletion.ConfirmDeletion"] = "false"
        });
        Assert.Equal(HttpStatusCode.OK, denied.StatusCode);
        Assert.Contains("Alice task", await alice.GetStringAsync("/"));
        var deleted = await Post(alice, "/Account/Profile", "/Account/DeleteAccount", new()
        {
            ["Deletion.CurrentPassword"] = Password, ["Deletion.ConfirmDeletion"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await alice.GetAsync("/")).StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDatabaseContext>();
        Assert.False(await db.Users.AnyAsync(user => user.UserName == "alice"));
        Assert.False(await db.Todo.AnyAsync(todo => todo.Description == "Alice task"));
        Assert.True(await db.Users.AnyAsync(user => user.UserName == "bob"));
        Assert.Contains("Bob task", await bob.GetStringAsync("/"));
    }

    [Fact]
    public async Task SearchUsesCurrentOwnerAndRejectsForeignOrDeletedResultsFromTheIndex()
    {
        await using var app = new TodoAppFactory();
        await app.InitializeDatabaseAsync();
        using var alice = app.NewClient();
        using var bob = app.NewClient();
        await Register(alice, "alice");
        await Register(bob, "bob");
        await Post(alice, "/", "/Todos/Create", new() { ["NewTodo.Description"] = "Alice searchable" });
        await Post(bob, "/", "/Todos/Create", new() { ["NewTodo.Description"] = "Bob private" });
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDatabaseContext>();
        var own = await db.Todo.SingleAsync(todo => todo.Description == "Alice searchable");
        var foreign = await db.Todo.SingleAsync(todo => todo.Description == "Bob private");
        var search = app.Services.GetRequiredService<FakeTodoSearch>();
        search.Result = new([own.SearchDocumentId, foreign.SearchDocumentId], 2);

        var page = await alice.GetStringAsync("/?q=searchable");
        Assert.Equal(own.OwnerId, search.LastOwnerId);
        Assert.Contains("Alice searchable", page);
        Assert.DoesNotContain("Bob private", page);
        await Post(alice, "/", $"/Todos/Delete/{own.ID}", new());
        var stale = await alice.GetStringAsync("/?q=searchable");
        Assert.DoesNotContain("Alice searchable", stale);
        Assert.DoesNotContain("Bob private", stale);
    }

    [Fact]
    public async Task SearchFailureIsExplicitAndDoesNotPretendToReturnSqlSearchResults()
    {
        await using var app = new TodoAppFactory();
        await app.InitializeDatabaseAsync();
        using var client = app.NewClient();
        await Register(client, "alice");
        await Post(client, "/", "/Todos/Create", new() { ["NewTodo.Description"] = "Private result" });
        app.Services.GetRequiredService<FakeTodoSearch>().Failure = new RequestFailedException(503, "Unavailable");
        var response = await client.GetAsync("/?q=result");
        var page = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Search is temporarily unavailable", page);
        Assert.DoesNotContain("Private result", page);
    }

    [Fact]
    public async Task DurableSearchQueueSurvivesFailureAndSynchronizesDeletesFromCurrentSqlState()
    {
        await using var app = new TodoAppFactory();
        await app.InitializeDatabaseAsync();
        using var client = app.NewClient();
        await Register(client, "alice");
        await Post(client, "/", "/Todos/Create", new() { ["NewTodo.Description"] = "Durable search update" });
        var search = app.Services.GetRequiredService<FakeTodoSearch>();
        var worker = new TodoSearchSyncWorker(app.Services.GetRequiredService<IServiceScopeFactory>(),
            search, NullLogger<TodoSearchSyncWorker>.Instance);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDatabaseContext>();
        var todo = await db.Todo.SingleAsync();
        search.Failure = new RequestFailedException(503, "Unavailable");
        await Assert.ThrowsAsync<RequestFailedException>(() => worker.SynchronizePendingAsync(CancellationToken.None));
        Assert.Equal(1, await db.SearchOperations.CountAsync());

        search.Failure = null;
        Assert.Equal(1, await worker.SynchronizePendingAsync(CancellationToken.None));
        Assert.Equal(todo.SearchDocumentId, Assert.Single(search.AppliedTodos).SearchDocumentId);
        Assert.Equal(0, await db.SearchOperations.CountAsync());
        await Post(client, "/", $"/Todos/Delete/{todo.ID}", new());
        Assert.Equal(1, await worker.SynchronizePendingAsync(CancellationToken.None));
        Assert.Empty(search.AppliedTodos);
        Assert.Equal(todo.SearchDocumentId, Assert.Single(search.AppliedIds));
        Assert.Equal(0, await db.SearchOperations.CountAsync());
    }

    [Fact]
    public async Task LivenessDoesNotRunDatabaseChecksButReadinessDoes()
    {
        await using var app = new TodoAppFactory();
        await app.InitializeDatabaseAsync();
        var databaseChecks = 0;
        using var hosted = app.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddHealthChecks().AddCheck("paused-database", () =>
            {
                Interlocked.Increment(ref databaseChecks);
                return HealthCheckResult.Unhealthy("Paused");
            }, tags: ["ready"])));
        using var client = hosted.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(0, databaseChecks);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/ready")).StatusCode);
        Assert.Equal(1, databaseChecks);
    }

    [Fact]
    public async Task FrontDoorOriginControlsAuthenticationRedirectsInsteadOfUntrustedHeaders()
    {
        await using var app = new TodoAppFactory();
        using var hosted = app.WithWebHostBuilder(builder =>
            builder.UseSetting("PUBLIC_ORIGIN", "https://marmara.example"));
        using var client = hosted.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "untrusted.example");
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://marmara.example/Account/Login", response.Headers.Location?.ToString());
    }

    private static Task<HttpResponseMessage> Register(HttpClient client, string username, string? returnUrl = null) =>
        Post(client, "/Account/Register", "/Account/Register", new()
        {
            ["Username"] = username, ["Password"] = Password, ["ConfirmPassword"] = Password,
            ["ReturnUrl"] = returnUrl ?? ""
        });

    private static Task<HttpResponseMessage> Login(HttpClient client, string username, string password) =>
        Post(client, "/Account/Login", "/Account/Login", new() { ["Username"] = username, ["Password"] = password });

    private static async Task<HttpResponseMessage> Post(HttpClient client, string formPath, string target,
        Dictionary<string, string> fields)
    {
        var page = await client.GetStringAsync(formPath);
        var match = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "The form must provide an anti-forgery token.");
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value);
        return await client.PostAsync(target, new FormUrlEncodedContent(fields));
    }
}
