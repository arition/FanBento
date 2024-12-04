﻿using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyModel;
using Serilog;
using Serilog.Settings.Configuration;

namespace FanBento.TelegramBot;

internal class Program
{
    private static void InitConfiguration(string[] args)
    {
        Configuration.Init(args);
    }

    private static void InitLogger()
    {
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(Configuration.Config, new ConfigurationReaderOptions(new DependencyContextFilter(DependencyContext.Default)))
            .CreateLogger();
    }

    private static async Task Main(string[] args)
    {
        InitConfiguration(args);
        InitLogger();
        await new Worker().WorkOnce();
    }

    // Custom DependencyContext without Anotar.Serilog.Fody
    // Source: https://github.com/Fody/Anotar/issues/656
    private class DependencyContextFilter : DependencyContext
    {
        public DependencyContextFilter(DependencyContext other) :
            base(other.Target, other.CompilationOptions, other.CompileLibraries,
                other.RuntimeLibraries.Where(rl => rl.Name != "Anotar.Serilog.Fody"), other.RuntimeGraph)
        {
        }
    }
}