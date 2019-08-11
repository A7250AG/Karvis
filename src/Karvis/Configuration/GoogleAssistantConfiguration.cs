using Grpc.Core;

namespace Karvis.Configuration
{
    public class GoogleAssistantConfiguration
    {
        public string DeviceId { get; set; }
        public string DeviceModelId { get; set; }
        public string PathToCredentials { get; set; }
    }
}