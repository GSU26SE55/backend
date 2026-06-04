using MassTransit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TicketService.Infrastructure.Persistence;

namespace TicketService.IntergrationTests.Fixtures;

public class TicketApiFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "SkipMigration", "true" }
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove real DB context
            services.RemoveAll<DbContextOptions<TicketDbContext>>();
            services.RemoveAll<TicketDbContext>();

            // Create and open a SQLite connection for in-memory testing
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            // Add SQLite DB context for testing
            services.AddDbContext<TicketDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            // Add TimeProvider (default to system, can be overridden)
            services.TryAddSingleton<TimeProvider>(TimeProvider.System);

            // --- FIX SLOWNESS: Override MassTransit and HostedServices ---
            services.AddMassTransitTestHarness();

            var backgroundServices = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
            foreach (var service in backgroundServices)
            {
                services.Remove(service);
            }

            // Ensure schema is created using the current model
            var sp = services.BuildServiceProvider();
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
                db.Database.EnsureCreated();
            }

            // Add Test Auth
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connection?.Dispose();
        }
        base.Dispose(disposing);
    }
}
