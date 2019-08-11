using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Interactivity;
using DSharpPlus.Lavalink;
using DSharpPlus.VoiceNext;
using Karvis.Commands;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Karvis.Configuration;

namespace Karvis
{
    class Program
    {
        private static KarvisConfiguration karvisConfiguration;
        static DiscordClient discord;
        static CommandsNextExtension commands;
        static InteractivityExtension interactivity;
        static VoiceNextExtension voice;
        static LavalinkExtension lavalink;

        static void Main(string[] args)
        {
            MainAsync(args).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        static async Task MainAsync(string[] args)
        {
            // Create service collection and configure our services
            var serviceProvider = new ServiceCollection()
                .AddSingleton<IKarvisConfigurationService>(new KarvisConfigurationService())
                .BuildServiceProvider();

            karvisConfiguration = serviceProvider.GetService<IKarvisConfigurationService>().Configuration;

            discord = new DiscordClient(new DSharpPlus.DiscordConfiguration()
            {
                Token = karvisConfiguration.DiscordConfiguration.Token,
                TokenType = TokenType.Bot,
                UseInternalLogHandler = true,
                LogLevel = LogLevel.Debug,
                AutoReconnect = true
            });

            var audioConfig = new VoiceNextConfiguration();
            audioConfig.AudioFormat = new AudioFormat(48000, 2, VoiceApplication.Music);
            audioConfig.EnableIncoming = true;
            voice = discord.UseVoiceNext(audioConfig);
        
            discord.MessageCreated += async e =>
            {
                if (e.Message.Content.ToLower().StartsWith("ping"))
                    await e.Message.RespondAsync("pong!");
            };

            commands = discord.UseCommandsNext(new CommandsNextConfiguration
            {
                StringPrefixes = new List<string>() { ";;", "->" },
                EnableDms = false, // required for UseVoiceNext?
                Services = serviceProvider
            });

            commands.RegisterCommands<CommandsModule>();

            interactivity = discord.UseInteractivity(new InteractivityConfiguration {
            });

            lavalink = discord.UseLavalink();

            discord.Ready += Client_Ready;
            discord.GuildAvailable += Client_GuildAvailable;
            discord.ClientErrored += Client_ClientError;

            await discord.ConnectAsync();
            await Task.Delay(-1); // infinite wait to prevent exit
        }

        private static async Task Client_Ready(ReadyEventArgs e)
        {
            // let's log the fact that this event occured
            e.Client.DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName, "Client is ready to process events.", DateTime.Now);

            await discord.SendMessageAsync(await discord.GetChannelAsync(Constants.Channel_General_Text), "Ready to process events.");
            //await discord.SendMessageAsync(await discord.GetChannelAsync(Constants.User_a7250ag), "Ready to process events.");

            //await ((DiscordMember)await discord.GetUserAsync(Constants.User_a7250ag)).SendMessageAsync("Ready to process events.");
            await (await (await e.Client.GetGuildAsync(Constants.Guild_Karvis)).GetMemberAsync(Constants.User_a7250ag)).SendMessageAsync("Ready to process events");
        }

        private async static Task Client_GuildAvailable(GuildCreateEventArgs e)
        {
            // let's log the name of the guild that was just
            // sent to our client
            e.Client.DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName, $"Guild available: {e.Guild.Name}", DateTime.Now);

            await discord.SendMessageAsync(await discord.GetChannelAsync(Constants.Channel_General_Text), $"Guild available: {e.Guild.Name}");
        }

        private static Task Client_ClientError(ClientErrorEventArgs e)
        {
            // let's log the details of the error that just 
            // occured in our client
            e.Client.DebugLogger.LogMessage(LogLevel.Error, Constants.ApplicationName, $"Exception occured: {e.Exception.GetType()}: {e.Exception.Message}, Inner Exception = ${e.Exception.InnerException.Message}", DateTime.Now);

            // since this method is not async, let's return
            // a completed task, so that no additional work
            // is done
            return Task.CompletedTask;
        }
    }
}
