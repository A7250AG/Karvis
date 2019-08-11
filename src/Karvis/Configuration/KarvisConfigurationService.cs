using System.IO;
using Microsoft.Extensions.Configuration;

namespace Karvis.Configuration
{
    public class KarvisConfigurationService : IKarvisConfigurationService
    {
        public KarvisConfigurationService()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("karvis.json", optional: true, reloadOnChange: true);

            var configuration = new KarvisConfiguration();

            builder.Build().Bind(configuration);

            Configuration = configuration;
        }

        public KarvisConfiguration Configuration { get; }
    }
}