using DotNetCoreSqlDb.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using DotNetCoreSqlDb.Services;

namespace TodoApp.Tests;

public sealed class TodoAppFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TodoAppFactory() => _connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:AZURE_SQL_CONNECTIONSTRING", "Server=unused;Database=tests;Integrated Security=true");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<MyDatabaseContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<MyDatabaseContext>>();
            services.AddDbContext<MyDatabaseContext>(options => options.UseSqlite(_connection));
            services.RemoveAll<ITodoSearch>();
            services.AddSingleton<FakeTodoSearch>();
            services.AddSingleton<ITodoSearch>(provider => provider.GetRequiredService<FakeTodoSearch>());
            var worker = services.SingleOrDefault(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(TodoSearchSyncWorker));
            if (worker is not null)
                services.Remove(worker);
        });
    }

    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MyDatabaseContext>().Database.EnsureCreatedAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }
}
