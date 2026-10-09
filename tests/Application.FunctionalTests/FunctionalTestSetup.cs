using DataMart.Application.FunctionalTests.Fixtures;
using DataMart.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataMart.Application.FunctionalTests;

public class FunctionalTestSetup : IAsyncLifetime
{
    internal static IServiceScopeFactory ScopeFactory { get; private set; } = null!;
    internal static DatabaseResetter? DbResetter { get; private set; }

    private static WebApiFactory? _factory;
    private static DistributedApplication? _app;

    public async Task InitializeAsync()
    {
        // Materialise the test data sets (jeux de tests) to disk on the first run so the
        // deliverable .xlsx files exist for the live demo. Idempotent: only writes missing
        // files, so committed fixtures are preserved on subsequent runs. Non-fatal: tests
        // use the same in-memory bytes regardless, so a disk-write failure must not abort
        // the test suite.
        try { ClientAccountFixtures.EnsureOnDisk(); }
        catch (Exception ex)
        {
            Console.WriteLine($"[warn] Could not materialise fixtures to disk: {ex.Message}");
        }

        await AspireTestSupport.ResetPersistentDatabaseAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = cts.Token;

        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.TestAppHost>(
                args: [],
                configureBuilder: (options, _) =>
                {
                    options.DisableDashboard = true;
                });

        builder.Configuration["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "true";

        _app = await builder
            .BuildAsync(cancellationToken)
            .WaitAsync(cancellationToken);

        await _app
            .StartAsync(cancellationToken)
            .WaitAsync(cancellationToken);

        await _app.ResourceNotifications.WaitForResourceHealthyAsync(
            Services.Database, cancellationToken);

        var connectionString = (await _app.GetConnectionStringAsync(Services.Database))!;

        _factory = new WebApiFactory(connectionString);
        ScopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        DbResetter = await DatabaseResetter.CreateAsync(connectionString);
    }

    public async Task DisposeAsync()
    {
        if (DbResetter is not null) await DbResetter.DisposeAsync();
        if (_app is not null) await _app.DisposeAsync();
        if (_factory is not null) await _factory.DisposeAsync();
    }
}

[CollectionDefinition("FunctionalTests")]
public class FunctionalCollection : ICollectionFixture<FunctionalTestSetup>
{
}
