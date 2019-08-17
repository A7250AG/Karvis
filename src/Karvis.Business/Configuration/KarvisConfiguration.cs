using System;
using System.Collections.Generic;
using System.Text;

namespace Karvis.Business.Configuration
{
    public class KarvisConfiguration
    {
        public AzureSpeechConfiguration AzureSpeechConfiguration { get; set; }
        public DiscordConfiguration DiscordConfiguration { get; set; }
        public GoogleAssistantConfiguration GoogleAssistantConfiguration { get; set; }
        public InfrastructureConfiguration InfrastructureConfiguration { get; set; }
    }
}
