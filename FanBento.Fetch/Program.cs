using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyModel;
using Serilog;

namespace FanBento.Fetch;

// Custom DependencyContext without Anotar.Serilog.Fody
// Source: https://github.com/Fody/Anotar/issues/656
internal class DependencyContextFilter : DependencyContext
{
    public DependencyContextFilter(DependencyContext other) :
        base(other.Target, other.CompilationOptions, other.CompileLibraries,
            other.RuntimeLibraries.Where(rl => rl.Name != "Anotar.Serilog.Fody"), other.RuntimeGraph)
    {
    }
}

internal class Program
{
    private static void InitConfiguration(string[] args)
    {
        Configuration.Init(args);
    }

    private static void InitLogger()
    {
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(Configuration.Config, new DependencyContextFilter(DependencyContext.Default))
            .CreateLogger();
    }

    private static async Task Main(string[] args)
    {
        InitConfiguration(args);
        InitLogger();
        var exitCode = Microsoft.Playwright.Program.Main(new[] { "install" });
        if (exitCode != 0) throw new Exception($"Playwright exited with code {exitCode}");
        await new Worker().WorkOnce();
    }
}