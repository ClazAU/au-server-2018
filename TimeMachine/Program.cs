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

        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;

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

    // The receive path runs on thread pool threads, where an escaped exception tears the process down before
    // anything is written to the log.
    private static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs unhandledExceptionEventArgs)
    {
        Log.Fatal(
            unhandledExceptionEventArgs.ExceptionObject as Exception,
            "Unhandled exception, terminating: {IsTerminating}",
            unhandledExceptionEventArgs.IsTerminating);

        Log.CloseAndFlush();
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
