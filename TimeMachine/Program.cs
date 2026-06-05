using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace TimeMachine;

internal static class Program
{
    private static int Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Starting host");
            CreateHostBuilder(args).Build().Run();
            return 0;
        }
        catch (Exception e)
        {
            Log.Fatal(e, "Host terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static HostApplicationBuilder CreateHostBuilder(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddSerilog((services, loggerConfig) => loggerConfig
            .MinimumLevel.Debug()
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console());

        builder.Services.AddSingleton<ClientManager>();
        builder.Services.AddSingleton<GameManager>();

        builder.Services.AddHostedService<MatchmakerService>();

        return builder;
    }
}
