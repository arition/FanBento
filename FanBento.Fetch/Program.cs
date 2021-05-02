using System.Threading.Tasks;
using Serilog;

namespace FanBento.Fetch
{
    internal class Program
    {
        private static void InitConfiguration(string[] args)
        {
            Configuration.Init(args);
        }

        private static void InitLogger()
        {
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(Configuration.Config)
                .CreateLogger();
        }

        private static async Task Main(string[] args)
        {
            InitConfiguration(args);
            InitLogger();
            await new Worker().WorkOnce();
        }
    }
}