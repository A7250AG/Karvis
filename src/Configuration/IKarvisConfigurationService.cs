using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Karvis.Configuration
{
    public interface IKarvisConfigurationService
    {
        KarvisConfiguration Configuration { get; }
    }
}
