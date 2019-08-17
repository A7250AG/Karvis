using Microsoft.Extensions.Configuration;

namespace Karvis.Business.Configuration
{
    public class AzureSpeechConfiguration
    {
        public string SubscriptionKey { get; set; }
        public string Region { get; set; }
    }
}