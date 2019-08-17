using System;
using Karvis.Web.Areas.Identity.Data;
using Karvis.Web.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: HostingStartup(typeof(Karvis.Web.Areas.Identity.IdentityHostingStartup))]
namespace Karvis.Web.Areas.Identity
{
    public class IdentityHostingStartup : IHostingStartup
    {
        public void Configure(IWebHostBuilder builder)
        {
            builder.ConfigureServices((context, services) =>
            {
                services.AddDbContext<KarvisWebContext>();

                services.AddDefaultIdentity<KarvisWebUser>()
                    .AddEntityFrameworkStores<KarvisWebContext>();
            });
        }
    }
}