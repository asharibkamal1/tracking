using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TaxpayerAnalytics.Shared.Data;

public static class DbInitializerExtensions
{
    /// <summary>
    /// Auto-applies db/schema.sql to the configured ConnectionStrings:AnalyticsDb on
    /// startup. Disabled by setting Database:AutoApplySchema = false (e.g. when a DBA
    /// manages migrations out-of-band in production).
    /// </summary>
    public static async Task InitializeAnalyticsDatabaseAsync(this IServiceProvider services)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DbInitializerExtensions).FullName!);

        if (!config.GetValue("Database:AutoApplySchema", true))
        {
            logger.LogInformation("Database auto-apply disabled (Database:AutoApplySchema=false)");
            return;
        }

        var connStr = config.GetConnectionString("AnalyticsDb");
        if (string.IsNullOrWhiteSpace(connStr))
        {
            logger.LogError("ConnectionStrings:AnalyticsDb is not set; cannot initialise database");
            return;
        }

        // schema.sql is copied next to the binary by each app's csproj
        // (<None Include="..\..\db\schema.sql" Link="DbScripts\schema.sql" ...>).
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "DbScripts", "schema.sql");

        try
        {
            await SqlSchemaInitializer.EnsureDatabaseAndSchemaAsync(connStr, schemaPath, logger);
        }
        catch (Exception ex)
        {
            // Don't crash the host — let the app start so the developer can still see the
            // error in browser/Swagger and fix their connection string.
            logger.LogError(ex, "Database initialisation failed; the app will start but database calls will fail");
        }
    }
}
