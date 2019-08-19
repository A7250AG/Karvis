using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.VoiceNext;
using PuppeteerSharp;

namespace Karvis.Business.Commands
{
    public static class KarvisCommandExtensions
    {
        public static async Task<VoiceNextConnection> EnsureVoiceConnection(this CommandContext ctx)
        {
            try
            {
                var voiceChannel = ctx.Member?.VoiceState?.Channel;

                if (voiceChannel == null) throw new InvalidOperationException("You need to be connected to a voice channel.");

                var voiceConnection = ctx.Client.GetVoiceNext().GetConnection(ctx.Guild);

                if (voiceConnection != null && voiceConnection.Channel != voiceChannel)
                    throw new InvalidOperationException("Already connected to a different voice channel.");

                if (voiceConnection != null) return voiceConnection;

                voiceConnection = await voiceChannel.ConnectAsync();

                await ctx.RespondAsync("I've joined your voice channel.");

                return voiceConnection;
            }
            catch (Exception ex)
            {
                await ctx.RespondAsync($"Sorry, {ctx.User.Username}, I can't do that. \n\n``{ex.Message}``");

                throw;
            }
        }

        public static void LogInfo(this CommandContext ctx, string message)
        {
            ctx.Client.DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName, message, DateTime.Now);
        }

        public static void LogError(this CommandContext ctx, string message)
        {
            ctx.Client.DebugLogger.LogMessage(LogLevel.Error, Constants.ApplicationName, message, DateTime.Now);
        }

        public static async Task RespondWithHtmlAsImage(this CommandContext ctx, string html)
        {
            await new BrowserFetcher().DownloadAsync(BrowserFetcher.DefaultRevision);

            using (var browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                DefaultViewport = new ViewPortOptions()
                    { IsLandscape = true, Width = 1280, Height = 720 },
                Headless = true,
                Args = new[] { "--no-sandbox" }
            }))
            using (var page = await browser.NewPageAsync())
            {
                await page.SetContentAsync(html);
                var result = await page.GetContentAsync();
                await page.WaitForTimeoutAsync(500);
                var data = await page.ScreenshotDataAsync();

                using (var ms = new MemoryStream(data))
                {
                    await ctx.RespondWithFileAsync($"{Guid.NewGuid()}.jpg", ms);
                }
            }
        }
    }
}
