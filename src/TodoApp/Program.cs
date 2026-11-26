using Microsoft.EntityFrameworkCore;
using DotNetCoreSqlDb.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
var builder = WebApplication.CreateBuilder(args);

// Add database context
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDbContext<MyDatabaseContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("MyDbConnection")));
}
else
{
    var connectionString = builder.Configuration.GetConnectionString("AZURE_SQL_CONNECTIONSTRING")
        ?? throw new InvalidOperationException("AZURE_SQL_CONNECTIONSTRING is required in production.");
    builder.Services.AddDbContext<MyDatabaseContext>(options =>
        options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));
}

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
})
    .AddEntityFrameworkStores<MyDatabaseContext>()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.Cookie.Name = "Marmara.Todo.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.Zero);
builder.Services.AddAntiforgery(options => options.Cookie.SecurePolicy = CookieSecurePolicy.Always);
builder.Services.AddHealthChecks().AddDbContextCheck<MyDatabaseContext>("sql", tags: ["ready"]);

// Add App Service logging
builder.Logging.AddAzureWebAppDiagnostics();

var app = builder.Build();

var publicOriginValue = builder.Configuration["PUBLIC_ORIGIN"];
if (!string.IsNullOrEmpty(publicOriginValue))
{
    if (!Uri.TryCreate(publicOriginValue, UriKind.Absolute, out var publicOrigin)
        || publicOrigin.Scheme != Uri.UriSchemeHttps || publicOrigin.AbsolutePath != "/"
        || publicOrigin.UserInfo.Length != 0 || publicOrigin.Query.Length != 0 || publicOrigin.Fragment.Length != 0)
    {
        throw new InvalidOperationException("PUBLIC_ORIGIN must be an HTTPS origin without a path, query, or credentials.");
    }

    // Use the configured public origin, not caller-supplied forwarding headers, for authentication redirects.
    app.Use((context, next) =>
    {
        context.Request.Scheme = publicOrigin.Scheme;
        context.Request.Host = HostString.FromUriComponent(publicOrigin);
        return next(context);
    });
}

app.MapPost("/_deployment/initialize-database", async Task<IResult> (HttpRequest request) =>
{
    if (!System.Net.Http.Headers.AuthenticationHeaderValue.TryParse(request.Headers.Authorization.ToString(), out var authorization)
        || !authorization.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase)
        || string.IsNullOrWhiteSpace(authorization.Parameter))
    {
        app.Logger.LogWarning("Rejected database bootstrap request without a bearer token.");
        return Results.Unauthorized();
    }

    try
    {
        var result = await AzureDatabaseBootstrap.InitializeAsync(
            authorization.Parameter,
            app.Configuration["SQL_SERVER_FQDN"] ?? throw new InvalidOperationException("SQL_SERVER_FQDN is required."),
            app.Configuration["SQL_DATABASE_NAME"] ?? throw new InvalidOperationException("SQL_DATABASE_NAME is required."),
            app.Configuration["SQL_APP_CLIENT_ID"] ?? throw new InvalidOperationException("SQL_APP_CLIENT_ID is required."),
            app.Configuration["SQL_APP_PRINCIPAL_NAME"] ?? throw new InvalidOperationException("SQL_APP_PRINCIPAL_NAME is required."));
        return Results.Ok(result);
    }
    catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number == 18456)
    {
        app.Logger.LogWarning("Rejected database bootstrap request: SQL authentication failed.");
        return Results.Unauthorized();
    }
    catch (UnauthorizedAccessException)
    {
        app.Logger.LogWarning("Rejected database bootstrap request: SQL administrator access is required.");
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
}).AllowAnonymous();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Todos}/{action=Index}/{id?}");

app.Run();

public partial class Program { }
