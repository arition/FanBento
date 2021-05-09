using System.IO;
using Microsoft.Extensions.Configuration;

namespace FanBento.TelegramBot
{
    public static class Configuration
    {
        public static IConfigurationRoot Config { get; private set; }

        public static void Init(string[] args)
        {
            Config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("settings.json")
                .AddEnvironmentVariables("FanBento")
                .AddCommandLine(args)
                .Build();
        }

        public static void Init()
        {
            Config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("settings.json")
                .AddEnvironmentVariables("FanBento")
                .Build();
        }
    }
}