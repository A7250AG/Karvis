using Grpc.Core;

namespace Karvis.Business.Configuration
{
    public class GoogleAssistantConfiguration
    {
        public string DeviceId { get; set; }
        public string DeviceModelId { get; set; }
        public string PathToCredentials { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string DebugUser { get; set; }
        public string DebugToken { get; set; }
    }
}