using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Interactivity;
using DSharpPlus.Lavalink;
using DSharpPlus.Lavalink.EventArgs;
using DSharpPlus.Net;
using DSharpPlus.VoiceNext;
using DSharpPlus.VoiceNext.EventArgs;
using Karvis.Business.Audio;
using Karvis.Business.Speech;
using NAudio.Wave;
using Karvis.Business.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Google.Assistant.Embedded.V1Alpha2;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Protobuf;
using Grpc;
using Grpc.Auth;
using Grpc.Core;
using Grpc.Core.Utils;
using Karvis.Business.Infrastructure;
using Microsoft.Win32.SafeHandles;
using PuppeteerSharp;

namespace Karvis.Business.Commands
{
    public class CommandsModule : BaseCommandModule
    {
        private readonly KarvisConfiguration KarvisConfiguration;

        public CommandsModule(IKarvisConfigurationService configuration)
        {
            //ServiceProvider = serviceProvider;
            KarvisConfiguration = configuration.Configuration;
        }

        [Command("hi")]
        public async Task Hi(CommandContext ctx)
        {
            await ctx.RespondAsync($"👋 Hi, {ctx.User.Mention}!");

            var interactivity = ctx.Client.GetInteractivity();
            var msg = await interactivity.WaitForMessageAsync(xm => xm.Author.Id == ctx.User.Id && xm.Content.ToLower() == "how are you?", TimeSpan.FromMinutes(1));
            if (msg.Result != null)
                await ctx.RespondAsync($"I'm fine, thank you!");
        }

        [Command("random")]
        public async Task Random(CommandContext ctx, int min, int max)
        {
            var rnd = new Random();
            await ctx.RespondAsync($"🎲 Your random number is: {rnd.Next(min, max)}");
        }

        [Command("join")]
        public async Task Join(CommandContext ctx)
        {
            _ = await ctx.EnsureVoiceConnection();
        }

        [Command("leave")]
        public async Task Leave(CommandContext ctx, string force = null)
        {
            try
            {
                var voiceConnection = ctx.Client.GetVoiceNext().GetConnection(ctx.Guild);

                if (voiceConnection == null || (voiceConnection.Channel != ctx.Member?.VoiceState?.Channel && force?.ToLowerInvariant() != "force"))
                    throw new InvalidOperationException("I'm not connected to your voice channel.");

                voiceConnection.Disconnect();

                await ctx.RespondAsync("I've left your voice channel.");
            }
            catch (Exception ex)
            {
                await ctx.Channel.SendMessageAsync($"Sorry, {ctx.User.Username}, I can't leave. \n\n``{ex.Message}``");
            }
        }
    }
}
