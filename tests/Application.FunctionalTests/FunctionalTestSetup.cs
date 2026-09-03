using DataMart.Application.FunctionalTests.Fixtures;
using DataMart.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace DataMart.Application.FunctionalTests;

[SetUpFixture]
public class FunctionalTestSetup
{
    internal static IServiceScopeFactory ScopeFactory { get; private set; } = null!;
    internal static DatabaseResetter? DbResetter { get; private set; }

    private static WebApiFactory? _factory;
    private static DistributedApplication? _app;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
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

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (DbResetter is not null) await DbResetter.DisposeAsync();
        if (_app is not null) await _app.DisposeAsync();
        if (_factory is not null) await _factory.DisposeAsync();
    }
}
