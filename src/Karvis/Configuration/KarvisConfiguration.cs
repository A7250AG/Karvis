using System;
using System.Collections.Generic;
using System.Text;

namespace Karvis.Configuration
{
    public class KarvisConfiguration
    {
        public AzureSpeechConfiguration AzureSpeechConfiguration { get; set; }
        public DiscordConfiguration DiscordConfiguration { get; set; }
        public GoogleAssistantConfiguration GoogleAssistantConfiguration { get; set; }
    }
}
