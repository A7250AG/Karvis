﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.VoiceNext;
using DSharpPlus.VoiceNext.EventArgs;
using Karvis.Business.Audio;
using Karvis.Business.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Google.Assistant.Embedded.V1Alpha2;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Protobuf;
using Grpc.Auth;
using Grpc.Core;
using Grpc.Core.Utils;
using Karvis.Business.Infrastructure;
using Karvis.Business.Speech;

namespace Karvis.Business.Commands
{
    public class GoogleCommandsModule : BaseCommandModule
    {
        private static readonly ConcurrentDictionary<ulong, EmbeddedAssistant.EmbeddedAssistantClient> UserEmbeddedAssistantClients = new ConcurrentDictionary<ulong, EmbeddedAssistant.EmbeddedAssistantClient>();
        private static readonly ConcurrentDictionary<ulong, int> UserGoogleAuthRetries = new ConcurrentDictionary<ulong, int>();
        private static readonly ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, AsyncEventHandler<UserSpeakingEventArgs>>> UserSpeakingHandlers
            = new ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, AsyncEventHandler<UserSpeakingEventArgs>>>();

        private readonly KarvisConfiguration KarvisConfiguration;
        private readonly AssistConfig AssistantConfig;

        public GoogleCommandsModule(IKarvisConfigurationService configurationService)
        {
            KarvisConfiguration = configurationService.Configuration;

            AssistantConfig = new AssistConfig()
            {
                AudioOutConfig = new AudioOutConfig()
                {
                    Encoding = AudioOutConfig.Types.Encoding.Linear16,
                    SampleRateHertz = 16000,
                    VolumePercentage = 100
                },
                DeviceConfig = new DeviceConfig()
                {
                    DeviceId = KarvisConfiguration.GoogleAssistantConfiguration.DeviceId,
                    DeviceModelId = KarvisConfiguration.GoogleAssistantConfiguration.DeviceModelId
                },
                DialogStateIn = new DialogStateIn()
                {
                    IsNewConversation = true,
                    LanguageCode = "en-US"
                },
                DebugConfig = new DebugConfig()
                {
                    ReturnDebugInfo = true
                },
                ScreenOutConfig = new ScreenOutConfig()
                {
                    ScreenMode = ScreenOutConfig.Types.ScreenMode.Playing
                }
            };
        }

        [Command("googlespeech")]
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
                        UserSpeakingHandlers[ctx.User.Id].TryAdd(ctx.Channel.Id, (args) => OnUserSpeaking(args, ctx));

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

        [Command("google")]
        public async Task TextAssist(CommandContext ctx, [RemainingText] string query)
        {
            if (ctx.User?.Id == null) return;

            var token = new GoogleOAuth(KarvisConfiguration).GetTokenForUser(ctx.User.Id);
            if (string.IsNullOrWhiteSpace(token))
            {
                await ctx.RespondAsync("Sorry, you have not signed up.");
                return;
            }

            if (!UserEmbeddedAssistantClients.ContainsKey(ctx.User.Id))
            {
                var channelCredentials = ChannelCredentials.Create(new SslCredentials(), GoogleGrpcCredentials.FromAccessToken(token));
                UserEmbeddedAssistantClients[ctx.User.Id] = new EmbeddedAssistant.EmbeddedAssistantClient(new Channel("embeddedassistant.googleapis.com", 443, channelCredentials));
            }

            using (var call = UserEmbeddedAssistantClients[ctx.User.Id].Assist())
            {
                try
                {
                    AssistantConfig.AudioInConfig = null;
                    AssistantConfig.TextQuery = query;

                    var request = new AssistRequest()
                    {
                        AudioIn = ByteString.Empty,
                        Config = AssistantConfig
                    };

                    ctx.LogInfo($"GoogleAssistant: Sending config message: Audio IsEmpty: {request.AudioIn.IsEmpty}, Request Size: {request.CalculateSize()}");

                    await call.RequestStream.WriteAsync(request);

                    await call.RequestStream.CompleteAsync();

                    ctx.LogInfo($"GoogleAssistant: Completing request and awaiting response.");

                    var audioOut = new List<byte>();

                    await call.ResponseStream.ForEachAsync((response) => ProcessTextAssistResponse(response, ctx, ref audioOut));

                    try
                    {
                        var status = call.GetStatus();
                        ctx.LogInfo($"GoogleAssistant: Final Status: {status.StatusCode}, Detail: {status.Detail}.");
                    }
                    catch (InvalidOperationException ex)
                    {

                    }

                    if (audioOut.Any())
                    {
                        var voiceConnection = await ctx.EnsureVoiceConnection();

                        if (voiceConnection == null || (voiceConnection.Channel != ctx.Member?.VoiceState?.Channel))
                            throw new InvalidOperationException($"I'm not connected to your voice channel, so I can't speak.");

                        var audio = AudioConverter.Resample(audioOut.ToArray(), 
                            16000, 48000, 
                            1, 2);

                        voiceConnection.SendSpeaking();
                        voiceConnection.GetTransmitStream().Write(audio, 0, audio.Length);
                        voiceConnection.GetTransmitStream().Flush();
                        voiceConnection.SendSpeaking(false);
                    }
                }
                catch (RpcException ex)
                {
                    ctx.LogError($"GoogleAssistant: Exception: {ex.StatusCode}, Detail: {ex.Status.Detail}, Message: {ex.Message}.");

                    if (ex.StatusCode == StatusCode.Unauthenticated)
                    {
                        if (!UserGoogleAuthRetries.ContainsKey(ctx.User.Id))
                            UserGoogleAuthRetries[ctx.User.Id] = 0;

                        if (UserGoogleAuthRetries[ctx.User.Id] < 3)
                        {
                            UserGoogleAuthRetries[ctx.User.Id]++;

                            await ReAuth(ctx);

                            await TextAssist(ctx, query);
                        }
                        else
                            UserGoogleAuthRetries[ctx.User.Id] = 0;
                    }

                    ctx.LogError($"GoogleAssistant: Exception: {ex.StatusCode}, Detail: {ex.Status.Detail}, Message: {ex.Message}.");
                }
                catch (Exception ex)
                {
                    await ctx.RespondAsync($"Sorry, {ctx.User.Username}, I can't google. \n\n``{ex.Message}``");
                }
            }
        }

        [Command("assist")]
        public async Task Assist(CommandContext ctx, int @in = 48000, int @out = 16000, int inChan = 1, int outChan = 1, string raw = null)
        {
            if (!ctx.Services.GetService<IProvideAudioState>().SpeechFromUser.ContainsKey(ctx.User.Id)) return;

            var buff = ctx.Services.GetService<IProvideAudioState>().SpeechFromUser[ctx.User.Id].ToArray();

            AssistantConfig.AudioInConfig = new AudioInConfig()
            {
                Encoding = AudioInConfig.Types.Encoding.Linear16,
                SampleRateHertz = 16000
            };

            if (raw != "raw")
                buff = AudioConverter.Resample(buff, @in, @out, inChan, outChan);
            else
                AssistantConfig.AudioInConfig.SampleRateHertz = AudioFormat.Default.SampleRate;

            var token = new GoogleOAuth(KarvisConfiguration).GetTokenForUser(ctx.User.Id);
            if (string.IsNullOrWhiteSpace(token))
            {
                await ctx.RespondAsync("Sorry, you have not signed up.");
                return;
            }

            if (!UserEmbeddedAssistantClients.ContainsKey(ctx.User.Id))
            {
                var channelCredentials = ChannelCredentials.Create(new SslCredentials(), GoogleGrpcCredentials.FromAccessToken(token));
                UserEmbeddedAssistantClients[ctx.User.Id] = new EmbeddedAssistant.EmbeddedAssistantClient(new Channel("embeddedassistant.googleapis.com", 443, channelCredentials));
            }

            using (var call = UserEmbeddedAssistantClients[ctx.User.Id].Assist())
            {
                try
                {
                    var configRequest = new AssistRequest()
                    {
                        AudioIn = ByteString.Empty,
                        Config = AssistantConfig
                    };

                    ctx.LogInfo($"GoogleAssistant: Sending config message: Audio IsEmpty: {configRequest.AudioIn.IsEmpty}, Request Size: {configRequest.CalculateSize()}");

                    await call.RequestStream.WriteAsync(configRequest);

                    await SendAudioInChunks(call, ctx, buff);

                    await call.RequestStream.CompleteAsync();

                    ctx.LogInfo($"GoogleAssistant: Completing request and awaiting response.");

                    var audioOut = new List<byte>();

                    await call.ResponseStream.ForEachAsync((response) => ProcessVoiceAssistResponse(response, ctx, ref audioOut));

                    if (audioOut.Any())
                    {
                        var voiceConnection = await ctx.EnsureVoiceConnection();

                        if (voiceConnection == null || (voiceConnection.Channel != ctx.Member?.VoiceState?.Channel))
                            throw new InvalidOperationException($"I'm not connected to your voice channel, so I can't speak.");

                        var audio = AudioConverter.Resample(audioOut.ToArray(),
                            16000, 48000,
                            1, 2);

                        voiceConnection.SendSpeaking();
                        voiceConnection.GetTransmitStream().Write(audio, 0, audio.Length);
                        voiceConnection.GetTransmitStream().Flush();
                        voiceConnection.SendSpeaking(false);
                    }

                    try
                    {
                        var status = call.GetStatus();

                        ctx.LogInfo($"GoogleAssistant: Final Status: {status.StatusCode}, Detail: {status.Detail}.");
                    }
                    catch (InvalidOperationException ex)
                    {

                    }
                }
                catch (RpcException ex)
                {
                    ctx.LogError($"GoogleAssistant: Exception: {ex.StatusCode}, {ex.Status.Detail}, {ex.Message}.");

                    if (ex.StatusCode == StatusCode.Unauthenticated)
                    {
                        if (!UserGoogleAuthRetries.ContainsKey(ctx.User.Id))
                            UserGoogleAuthRetries[ctx.User.Id] = 0;

                        if (UserGoogleAuthRetries[ctx.User.Id] < 3)
                        {
                            UserGoogleAuthRetries[ctx.User.Id]++;

                            await ReAuth(ctx);

                            await Assist(ctx, @in, @out, inChan, outChan, raw);
                        }
                        else
                            UserGoogleAuthRetries[ctx.User.Id] = 0;
                    }
                }
                catch (Exception ex)
                {
                    await ctx.RespondAsync($"Sorry, {ctx.User.Username}, I can't google. \n\n``{ex.Message}``");
                }
            }
        }

        private static Task ProcessTextAssistResponse(AssistResponse response, CommandContext ctx, ref List<byte> audioOut)
        {
            ctx.LogInfo(
                $"GoogleAssistant: Received response: Event Type: {response.EventType.ToString()}, Debug Info: {response.DebugInfo}, Size: {response.CalculateSize()}");

            var tasks = new List<Task>();

            if (!string.IsNullOrWhiteSpace(response.DialogStateOut?.SupplementalDisplayText))
            {
                ctx.LogInfo($"GoogleAssistant: Received supplemental text.");

                tasks.Add(Task.Run(() => ctx.RespondAsync(response.DialogStateOut?.SupplementalDisplayText)));
            }

            if (response.ScreenOut != null)
            {
                ctx.LogInfo($"GoogleAssistant: Received screen data.");

                tasks.Add(Task.Run(() => ctx.RespondWithHtmlAsImage(response.ScreenOut.Data.ToStringUtf8())));
            }

            if (response.AudioOut?.AudioData != null)
            {
                ctx.LogInfo($"GoogleAssistant: Received audio data.");

                audioOut.AddRange(response.AudioOut.AudioData.ToByteArray());
            }

            return Task.WhenAll(tasks);
        }

        private static Task ProcessVoiceAssistResponse(AssistResponse response, CommandContext ctx, ref List<byte> audioOut)
        {
            var tasks = new List<Task>();

            try
            {
                if (response.EventType == AssistResponse.Types.EventType.EndOfUtterance)
                {
                    ctx.LogInfo($"GoogleAssistant: Utterance detected: Event Type: {response.EventType.ToString()}, Supplemental Text: {response.DialogStateOut?.SupplementalDisplayText}, Transcript: {response.SpeechResults?.FirstOrDefault()?.Transcript} , Debug Info: {response.DebugInfo?.ToString()}");

                    tasks.Add(ctx.Client.SendMessageAsync(ctx.Channel, $"{ctx.User.Username}, utterance detected: {response.SpeechResults?.FirstOrDefault()?.Transcript}, Screen Out: {response.ScreenOut?.Data}"));
                }
                else
                {
                    ctx.LogInfo($"GoogleAssistant: Received response: Event Type: {response.EventType.ToString()}, Microphone Mode: {response.DialogStateOut?.MicrophoneMode}, Debug Info: {response.DebugInfo}");

                    if (!string.IsNullOrWhiteSpace(response.DialogStateOut?.SupplementalDisplayText))
                    {
                        ctx.LogInfo($"GoogleAssistant: Received supplemental text.");

                        tasks.Add(Task.Run(() => ctx.RespondAsync(response.DialogStateOut?.SupplementalDisplayText)));
                    }

                    if (response.ScreenOut != null)
                    {
                        ctx.LogInfo($"GoogleAssistant: Received screen data.");

                        tasks.Add(Task.Run(() => ctx.RespondWithHtmlAsImage(response.ScreenOut.Data.ToStringUtf8())));
                    }

                    if (response.AudioOut?.AudioData != null)
                    {
                        ctx.LogInfo($"GoogleAssistant: Received audio data.");

                        audioOut.AddRange(response.AudioOut.AudioData.ToByteArray());
                    }
                }
            }
            catch (RpcException ex)
            {
                ctx.LogError($"GoogleAssistant: Exception: {ex.StatusCode}, Detail: {ex.Status.Detail}, Message: {ex.Message}.");
            }

            return Task.WhenAll(tasks);
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

        private async Task OnUserSpeaking(UserSpeakingEventArgs args, CommandContext ctx)
        {
            if (args.User != null)
            {
                if (args.Speaking == false)
                {
                    var audio = args.Client.GetCommandsNext().Services.GetService<IProvideAudioState>();
                    
                    await Assist(ctx);
                    
                    if (audio.IsSpeechPreservedForUser.ContainsKey(args.User.Id) && !args.Client.GetCommandsNext().Services.GetService<IProvideAudioState>().IsSpeechPreservedForUser[args.User.Id]) audio.SpeechFromUser[args.User.Id] = new ConcurrentQueue<byte>();
                }
            }
        }

        private static async Task SendAudioInChunks(AsyncDuplexStreamingCall<AssistRequest, AssistResponse> call, CommandContext ctx, byte[] buff)
        {
            const int frameSize = 1600;
            for (var i = 0; i < buff.Length; i += frameSize)
            {
                var remaining = i + frameSize > buff.Length ? buff.Length % frameSize : 0;

                await call.RequestStream.WriteAsync(new AssistRequest()
                {
                    AudioIn = ByteString.CopyFrom(buff, i, remaining == 0 ? frameSize : remaining)
                });
            }

            ctx.LogInfo($"GoogleAssistant: Full audio sent: Buffer Length: {buff.Length}");
        }

        private async Task ReAuth(CommandContext ctx)
        {
            ctx.LogInfo($"GoogleAssistant: Attempting to refresh access token.");

            using (IAuthorizationCodeFlow flow =
                new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = KarvisConfiguration.GoogleAssistantConfiguration.ClientId,
                        ClientSecret = KarvisConfiguration.GoogleAssistantConfiguration.ClientSecret
                    },
                    Scopes = new[] { "https://www.googleapis.com/auth/assistant-sdk-prototype" }
                }))
            {
                var tokenResponse = await flow.RefreshTokenAsync(
                    KarvisConfiguration.GoogleAssistantConfiguration.DebugUser,
                    new GoogleOAuth(KarvisConfiguration).GetRefreshTokenForUser(ctx.User.Id),
                    new CancellationToken());

                new GoogleOAuth(KarvisConfiguration).StoreCredentialsForUser(
                    KarvisConfiguration.GoogleAssistantConfiguration.DebugUser, tokenResponse.AccessToken,
                    tokenResponse.RefreshToken, ctx.User.Id);

                var channelCredentials = ChannelCredentials.Create(new SslCredentials(),
                    GoogleGrpcCredentials.FromAccessToken(tokenResponse.AccessToken));

                UserEmbeddedAssistantClients[ctx.User.Id] =
                    new EmbeddedAssistant.EmbeddedAssistantClient(
                        new Channel("embeddedassistant.googleapis.com", 443, channelCredentials));
            }
        }
    }
}
