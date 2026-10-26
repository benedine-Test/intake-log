using Azure.Data.Tables;
using IntakeLog.Api;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IDocumentStore>(sp =>
{
    var connection = builder.Configuration["TABLES_CONNECTION_STRING"];
    if (!string.IsNullOrWhiteSpace(connection))
        return new TableDocumentStore(new TableServiceClient(connection));

    // No storage configured: keep the API usable for a quick try-out, but say loudly that nothing persists.
    sp.GetRequiredService<ILoggerFactory>().CreateLogger("Startup")
      .LogWarning("TABLES_CONNECTION_STRING is not set. Synced data is kept in memory only and lost on restart.");
    return new InMemoryDocumentStore();
});
builder.Services.AddSingleton<SyncHandler>();

builder.Build().Run();
