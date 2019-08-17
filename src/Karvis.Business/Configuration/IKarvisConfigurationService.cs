using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Karvis.Business.Configuration
{
    public interface IKarvisConfigurationService
    {
        KarvisConfiguration Configuration { get; }
    }
}
