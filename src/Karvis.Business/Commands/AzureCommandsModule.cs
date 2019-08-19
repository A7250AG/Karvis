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
    public class AzureCommandsModule : BaseCommandModule
    {
        private readonly KarvisConfiguration KarvisConfiguration;

        private static readonly ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, AsyncEventHandler<UserSpeakingEventArgs>>> UserSpeakingHandlers
            = new ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, AsyncEventHandler<UserSpeakingEventArgs>>>();

        public AzureCommandsModule(IKarvisConfigurationService configuration)
        {
            KarvisConfiguration = configuration.Configuration;
        }

        [Command("azurespeech")]
        public async Task ToggleAzureSpeech(CommandContext ctx, string value = "on")
        {
            AsyncEventHandler<UserSpeakingEventArgs> handler = null;

            if (UserSpeakingHandlers.ContainsKey(ctx.User.Id))
            {
                if (UserSpeakingHandlers[ctx.User.Id].ContainsKey(ctx.Channel.Id))
                {
                    handler = UserSpeakingHandlers[ctx.User.Id][ctx.Channel.Id];
                }
            }

            if (value?.ToLowerInvariant().Trim() == "on")
            {
                var voiceConnection = await ctx.EnsureVoiceConnection();

                if (handler == null)
                {
                    if (!UserSpeakingHandlers.ContainsKey(ctx.User.Id))
                        UserSpeakingHandlers.TryAdd(ctx.User.Id, new ConcurrentDictionary<ulong, AsyncEventHandler<UserSpeakingEventArgs>>());
                    if (!UserSpeakingHandlers[ctx.User.Id].ContainsKey(ctx.Channel.Id))
                        UserSpeakingHandlers[ctx.User.Id].TryAdd(ctx.Channel.Id, (args) => OnUserSpeaking(args, ctx.Channel));

                    voiceConnection.VoiceReceived += OnVoiceReceived;
                    voiceConnection.UserSpeaking += UserSpeakingHandlers[ctx.User.Id][ctx.Channel.Id];
                }
            }
            else
            {
                var voiceConnection = await ctx.EnsureVoiceConnection();

                voiceConnection.VoiceReceived -= OnVoiceReceived;

                if (handler != null)
                {
                    voiceConnection.UserSpeaking -= handler;
                    UserSpeakingHandlers[ctx.User.Id].TryRemove(ctx.Channel.Id, out var _);
                }
            }
        }

        [Command("say")]
        public async Task Say(CommandContext ctx, [RemainingText] string text)
        {
            try
            {
                // Guard inputs
                if (string.IsNullOrWhiteSpace(text))
                    throw new InvalidOperationException("No text to speak.");

                var voiceConnection = await ctx.EnsureVoiceConnection();

                // Process the speech request
                await ctx.RespondAsync(DiscordEmoji.FromName(ctx.Client, ":thinking:"));

                var buffer = await new AzureSpeechModule(KarvisConfiguration, ctx.Client.DebugLogger).TextToAudioAsync(text);

                if (buffer.Length == 0)
                {
                    await ctx.RespondAsync(DiscordEmoji.FromName(ctx.Client, ":thumbsdown:"));
                    return;
                }
                else
                {
                    await ctx.RespondAsync(DiscordEmoji.FromName(ctx.Client, ":thumbsup:"));

                    voiceConnection.SendSpeaking(); // send a speaking indicator
                    await voiceConnection.GetTransmitStream().WriteAsync(buffer, 0, buffer.Length);
                    await voiceConnection.GetTransmitStream().FlushAsync();
                    voiceConnection.SendSpeaking(false); // end the speaking indicator
                }
            }
            catch (Exception ex)
            {
                await ctx.RespondAsync($"Sorry, {ctx.User.Username}, I can't say. \n\n``{ex.Message}``");
            }
        }

        [Command("rawsay")]
        public async Task Raw(CommandContext ctx, string force = null)
        {
            try
            {
                var voiceConnection = await ctx.EnsureVoiceConnection();
                var audio = ctx.Services.GetService<IProvideAudioState>();

                if (!audio.SpeechFromUser.ContainsKey(ctx.User.Id) || audio.SpeechFromUser[ctx.User.Id].IsEmpty)
                    throw new InvalidOperationException("You haven't said or preserved anything for me to say.");

                var buff = audio.SpeechFromUser[ctx.User.Id].ToArray();

                voiceConnection.SendSpeaking();
                await voiceConnection.GetTransmitStream().WriteAsync(buff, 0, buff.Length);
                await voiceConnection.GetTransmitStream().FlushAsync();
                voiceConnection.SendSpeaking(false);
            }
            catch (Exception ex)
            {
                await ctx.RespondAsync($"Sorry, {ctx.User.Username}, I can't rawsay. \n\n``{ex.Message}``");
            }
        }

        [Command("simonsay")]
        public async Task SimonSay(CommandContext ctx, int @in = 4000, int @out = 16000, int inChan = 2, int outChan = 1, string force = null)
        {
            try
            {
                var voiceConnection = await ctx.EnsureVoiceConnection();
                var audio = ctx.Services.GetService<IProvideAudioState>();

                if (!audio.SpeechFromUser.ContainsKey(ctx.User.Id) || audio.SpeechFromUser[ctx.User.Id].IsEmpty)
                    throw new InvalidOperationException("You haven't said or preserved anything for me to say.");

                var buff = audio.SpeechFromUser[ctx.User.Id].ToArray();

                byte[] resampled = AudioConverter.Resample(buff, @in, @out, inChan, outChan);

                voiceConnection.SendSpeaking();
                await voiceConnection.GetTransmitStream().WriteAsync(resampled, 0, resampled.Length);
                await voiceConnection.GetTransmitStream().FlushAsync();
                voiceConnection.SendSpeaking(false);
            }
            catch (Exception ex)
            {
                await ctx.RespondAsync($"Sorry, {ctx.User.Username}, I can't simonsay. \n\n``{ex.Message}``");
            }
        }

        [Command("transcribe")]
        public async Task Transcribe(CommandContext ctx)
        {
            var audio = ctx.Services.GetService<IProvideAudioState>();

            if (!audio.IsSpeechPreservedForUser[ctx.User.Id]) return;

            var buff = audio.SpeechFromUser[ctx.User.Id].ToArray();

            var resampled = AudioConverter.Resample(buff, 4000, 16000, 2, 1);

            await ctx.RespondAsync("I think I heard you say: " + await new AzureSpeechModule(KarvisConfiguration, ctx.Client.DebugLogger).AudioToTextAsync(resampled));
        }

        [Command("preserve")]
        public async Task PreserveSpeech(CommandContext ctx, bool preserve = true)
        {
            var audio = ctx.Services.GetService<IProvideAudioState>();

            audio.IsSpeechPreservedForUser[ctx.User.Id] = preserve;

            if (!preserve) audio.SpeechFromUser[ctx.User.Id] = new ConcurrentQueue<byte>();
        }

        [Command("clear")]
        public async Task ClearSpeech(CommandContext ctx)
        {
            var audio = ctx.Services.GetService<IProvideAudioState>();

            if (audio.SpeechFromUser.ContainsKey(ctx.User.Id)) audio.SpeechFromUser[ctx.User.Id] = new ConcurrentQueue<byte>();
        }

        public async Task OnVoiceReceivedPassthrough(VoiceReceiveEventArgs args, VoiceNextConnection voiceConnection)
        {
            var buff = args.PcmData.ToArray();

            await voiceConnection.GetTransmitStream().WriteAsync(buff, 0, buff.Length);
            await voiceConnection.GetTransmitStream().FlushAsync();
        }

        public async Task OnVoiceReceived(VoiceReceiveEventArgs args)
        {
            if (args.User != null)
            {
                var audio = args.Client.GetCommandsNext().Services.GetService<IProvideAudioState>();

                var user = true;
                if (!audio.SpeechFromUser.ContainsKey(args.User.Id))
                {
                    user = audio.SpeechFromUser.TryAdd(args.User.Id, new ConcurrentQueue<byte>());
                }

                if (user)
                {
                    var buff = args.PcmData.ToArray();
                    foreach (var b in buff)
                        audio.SpeechFromUser[args.User.Id].Enqueue(b);
                }
            }
        }

        private async Task OnUserSpeaking(UserSpeakingEventArgs args, DiscordChannel responseChannel)
        {
            if (args.User != null)
            {
                if (args.Speaking == false)
                {
                    var audio = args.Client.GetCommandsNext().Services.GetService<IProvideAudioState>();

                    var buff = audio.SpeechFromUser[args.User.Id].ToArray();

                    byte[] resampled = AudioConverter.Resample(buff, 4000, 16000, 2, 1);

                    var text = await new AzureSpeechModule(KarvisConfiguration, args.Client.DebugLogger).AudioToTextAsync(resampled);

                    await args.Client.SendMessageAsync(responseChannel, args.User.Username + ", I think I heard you say: " + text);

                    if (audio.IsSpeechPreservedForUser.ContainsKey(args.User.Id) && !args.Client.GetCommandsNext().Services.GetService<IProvideAudioState>().IsSpeechPreservedForUser[args.User.Id]) audio.SpeechFromUser[args.User.Id] = new ConcurrentQueue<byte>();
                }
            }
        }
    }
}
