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
        private static ConcurrentDictionary<ulong, bool> UserPreserveSpeech = new ConcurrentDictionary<ulong, bool>();
        private static ConcurrentDictionary<ulong, ConcurrentQueue<byte>> UserSpeech = new ConcurrentDictionary<ulong, ConcurrentQueue<byte>>();
        private static ConcurrentDictionary<ulong, EmbeddedAssistant.EmbeddedAssistantClient> UserEmbeddedAssistantClients = new ConcurrentDictionary<ulong, EmbeddedAssistant.EmbeddedAssistantClient>();

        private readonly KarvisConfiguration KarvisConfiguration;
        private readonly AssistConfig AssistantConfig;

        public CommandsModule(IKarvisConfigurationService configuration)
        {
            //ServiceProvider = serviceProvider;
            KarvisConfiguration = configuration.Configuration;
            AssistantConfig = new AssistConfig()
            {
                AudioInConfig = new AudioInConfig()
                {
                    Encoding = AudioInConfig.Types.Encoding.Linear16,
                    SampleRateHertz = 24000
                },
                AudioOutConfig = new AudioOutConfig()
                {
                    Encoding = AudioOutConfig.Types.Encoding.Linear16,
                    SampleRateHertz = 24000,
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
                DebugConfig =  new DebugConfig()
                {
                    ReturnDebugInfo = true
                },
                ScreenOutConfig = new ScreenOutConfig()
                {
                    ScreenMode = ScreenOutConfig.Types.ScreenMode.Playing
                }
            };
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
            try
            {
                var discordClient = ctx.Client;
                var voiceClient = discordClient.GetVoiceNext();
                var voiceConnection = voiceClient.GetConnection(ctx.Guild);

                if (voiceConnection != null) throw new InvalidOperationException("I'm already connected to a voice channel in this guild.");

                var voiceChannel = ctx.Member?.VoiceState?.Channel;

                if (voiceChannel == null) throw new InvalidOperationException("You need to be connected to a voice channel for me to join it.");

                voiceConnection = await voiceChannel.ConnectAsync();

                await ctx.RespondAsync("I've joined your voice channel.");

                voiceConnection.VoiceReceived += OnVoiceReceived;
                voiceConnection.UserSpeaking += (args) => OnUserSpeaking(args, discordClient, ctx.Channel);

                if (this.Lavalink != null)
                {
                    this.LavalinkVoice = await this.Lavalink.ConnectAsync(voiceChannel);
                    this.LavalinkVoice.PlaybackFinished += (args) => this.LavalinkVoice_PlaybackFinished(args, ctx.Channel);

                    await ctx.RespondAsync("I've connected to lavalink.");
                }
            }
            catch (Exception ex)
            {
                await ctx.Channel.SendMessageAsync($"Sorry, {ctx.User.Username}, I can't join. \n\n``{ex.Message}``");
            }
        }

        [Command("leave")]
        public async Task Leave(CommandContext ctx, string force = null)
        {
            try
            {
                var discordClient = ctx.Client;
                var voiceClient = discordClient.GetVoiceNext();
                var voiceConnection = voiceClient.GetConnection(ctx.Guild);

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

        [Command("say")]
        public async Task Say(CommandContext ctx, [RemainingText] string text)
        {
            try
            {
                // Guard inputs
                if (string.IsNullOrWhiteSpace(text))
                    throw new InvalidOperationException("No text to speak.");

                // Get a VoiceNextConnection object
                var voiceConnection = ctx.Client.GetVoiceNext().GetConnection(ctx.Guild);
                if (voiceConnection == null || voiceConnection.Channel != ctx.Member?.VoiceState?.Channel)
                    throw new InvalidOperationException("Not connected to your voice channel in this guild.");

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
                await ctx.Channel.SendMessageAsync($"Sorry, {ctx.User.Username}, I can't say. \n\n``{ex.Message}``");
            }
        }

        [Command("rawsay")]
        public async Task Raw(CommandContext ctx, string force = null)
        {
            try
            {
                var discordClient = ctx.Client;
                var voiceClient = discordClient.GetVoiceNext();
                var voiceConnection = voiceClient.GetConnection(ctx.Guild);

                if (voiceConnection == null || (voiceConnection.Channel != ctx.Member?.VoiceState?.Channel && force?.ToLowerInvariant() != "force"))
                    throw new InvalidOperationException("I'm not connected to your voice channel.");

                if (!UserSpeech.ContainsKey(ctx.User.Id) || UserSpeech[ctx.User.Id].IsEmpty)
                    throw new InvalidOperationException("You haven't said or preserved anything for me to say.");

                var buff = UserSpeech[ctx.User.Id].ToArray();

                voiceConnection.SendSpeaking();
                await voiceConnection.GetTransmitStream().WriteAsync(buff, 0, buff.Length);
                await voiceConnection.GetTransmitStream().FlushAsync();
                voiceConnection.SendSpeaking(false);
            }
            catch (Exception ex)
            {
                await ctx.Channel.SendMessageAsync($"Sorry, {ctx.User.Username}, I can't rawsay. \n\n``{ex.Message}``");
            }
        }

        [Command("simonsay")]
        public async Task SimonSay(CommandContext ctx, int @in = 22000, int @out = 44100, int inChan = 1, int outChan = 1, string force = null)
        {
            try
            {
                var discordClient = ctx.Client;
                var voiceClient = discordClient.GetVoiceNext();
                var voiceConnection = voiceClient.GetConnection(ctx.Guild);

                if (voiceConnection == null || (voiceConnection.Channel != ctx.Member?.VoiceState?.Channel && force?.ToLowerInvariant() != "force"))
                    throw new InvalidOperationException("I'm not connected to your voice channel.");

                if (!UserSpeech.ContainsKey(ctx.User.Id) || UserSpeech[ctx.User.Id].IsEmpty)
                    throw new InvalidOperationException("You haven't said or preserved anything for me to say.");

                var buff = UserSpeech[ctx.User.Id].ToArray();

                byte[] resampled = AudioConverter.Resample(buff, @in, @out, inChan, outChan);

                voiceConnection.SendSpeaking();
                await voiceConnection.GetTransmitStream().WriteAsync(resampled, 0, resampled.Length);
                await voiceConnection.GetTransmitStream().FlushAsync();
                voiceConnection.SendSpeaking(false);
            }
            catch (Exception ex)
            {
                await ctx.Channel.SendMessageAsync($"Sorry, {ctx.User.Username}, I can't simonsay. \n\n``{ex.Message}``");
            }
        }

        [Command("transcribe")]
        public async Task Transcribe(CommandContext ctx)
        {
            if (!UserSpeech.ContainsKey(ctx.User.Id)) return;

            var buff = UserSpeech[ctx.User.Id].ToArray();

            var resampled = AudioConverter.Resample(buff, 22000, 44100, 1, 1);

            await ctx.RespondAsync("I think I heard you say: " + await new AzureSpeechModule(KarvisConfiguration, ctx.Client.DebugLogger).AudioToTextAsync(resampled));
        }

        public ConcurrentDictionary<ulong, int> UserGoogleAuthRetries = new ConcurrentDictionary<ulong, int>();

        [Command("google")]
        public async Task TextAssist(CommandContext ctx, [RemainingText] string query)
        {
            if (ctx.User?.Id == null) return;

            var token = new GoogleOAuth(KarvisConfiguration).GetTokenForUser(ctx.User.Id);
            if (string.IsNullOrWhiteSpace(token))
            {
                await ctx.Channel.SendMessageAsync("Sorry, you have not signed up.");
                return;
            }

            if (!UserEmbeddedAssistantClients.ContainsKey(ctx.User.Id))
            {
                var channelCredentials = ChannelCredentials.Create(new SslCredentials(), GoogleGrpcCredentials.FromAccessToken(token));
                UserEmbeddedAssistantClients[ctx.User.Id] = new EmbeddedAssistant.EmbeddedAssistantClient(new Channel("embeddedassistant.googleapis.com", 443, channelCredentials));
            }

            // Working
            //if (!UserEmbeddedAssistantClients.ContainsKey(ctx.User.Id))
            //{
            //    var channelCredentials = ChannelCredentials.Create(new SslCredentials(), GoogleGrpcCredentials.FromAccessToken(token));
            //    UserEmbeddedAssistantClients[ctx.User.Id] = new EmbeddedAssistant.EmbeddedAssistantClient(new Channel("embeddedassistant.googleapis.com", 443, channelCredentials));
            //}

            AssistantConfig.AudioInConfig = null;
            AssistantConfig.TextQuery = query;
            
            using (var call = UserEmbeddedAssistantClients[ctx.User.Id].Assist())
            {
                try
                {
                    var request = new AssistRequest()
                    {
                        AudioIn = ByteString.Empty,
                        Config = AssistantConfig
                    };

                    ctx.Client.DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName,
                        $"GoogleAssistant: Sending config message: Audio IsEmpty: {request.AudioIn.IsEmpty}, Request Size: {request.CalculateSize()}",
                        DateTime.Now);

                    await call.RequestStream.WriteAsync(request);

                    await call.RequestStream.CompleteAsync();

                    ctx.Client.DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName,
                        $"GoogleAssistant: Completing request and awaiting response.",
                        DateTime.Now);

                    var audioOut = new List<byte>();

                    await call.ResponseStream.ForEachAsync(async (response) =>
                    {
                        ctx.Client.DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName,
                            $"GoogleAssistant: Received response: Event Type: {response.EventType.ToString()}, Debug Info: {response.DebugInfo?.ToString()}, Size: {response.CalculateSize()}",
                            DateTime.Now);

                        if (!string.IsNullOrWhiteSpace(response.DialogStateOut?.SupplementalDisplayText))
                            await ctx.Channel.SendMessageAsync(response.DialogStateOut.SupplementalDisplayText);

                        if (response.ScreenOut != null)
                        {
                            await new BrowserFetcher().DownloadAsync(BrowserFetcher.DefaultRevision);
                            using (var browser = await Puppeteer.LaunchAsync(new LaunchOptions
                            {
                                DefaultViewport = new ViewPortOptions() { IsLandscape = true, Width = 1280, Height = 720 },
                                Headless = true
                            }))
                            using (var page = await browser.NewPageAsync())
                            {
                                await page.SetContentAsync(response.ScreenOut.Data.ToStringUtf8());
                                var result = await page.GetContentAsync();
                                await page.WaitForTimeoutAsync(500);
                                var data = await page.ScreenshotDataAsync();

                                using (var ms = new MemoryStream(data))
                                {
                                    await ctx.RespondWithFileAsync($"{Guid.NewGuid()}.jpg", ms);
                                }
                            }
                        }

                        if (response.AudioOut?.AudioData != null)
                        {
                            var discordClient = ctx.Client;
                            var voiceClient = discordClient.GetVoiceNext();
                            var voiceConnection = voiceClient.GetConnection(ctx.Guild);

                            if (voiceConnection == null || (voiceConnection.Channel != ctx.Member?.VoiceState?.Channel))
                                throw new InvalidOperationException($"I'm not connected to your voice channel, so I can't speak, but: {response.DialogStateOut?.SupplementalDisplayText}.");

                            audioOut.AddRange(response.AudioOut.AudioData.ToByteArray());
                        }
                    });

                    try
                    {
                        var status = call.GetStatus();
                        ctx.Client.DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName,
                            $"GoogleAssistant: Final Status: {status.StatusCode}, Detail: {status.Detail}.",
                            DateTime.Now);
                    }
                    catch (InvalidOperationException ex)
                    {

                    }

                    if (audioOut.Any())
                    {
                        var discordClient = ctx.Client;
                        var voiceClient = discordClient.GetVoiceNext();
                        var voiceConnection = voiceClient.GetConnection(ctx.Guild);

                        if (voiceConnection == null || (voiceConnection.Channel != ctx.Member?.VoiceState?.Channel))
                            throw new InvalidOperationException("I'm not connected to your voice channel.");

                        var audio = AudioConverter.Resample(audioOut.ToArray(),
                            AssistantConfig.AudioOutConfig.SampleRateHertz, 48000, 1, 2);

                        voiceConnection.SendSpeaking();
                        await voiceConnection.GetTransmitStream().WriteAsync(audio, 0, audio.Length);
                        await voiceConnection.GetTransmitStream().FlushAsync();
                        voiceConnection.SendSpeaking(false);
                    }
                }
                catch (Grpc.Core.RpcException ex)
                {
                    ctx.Client.DebugLogger.LogMessage(LogLevel.Error, Constants.ApplicationName,
                        $"GoogleAssistant: Exception: {ex.StatusCode}, {ex.Status.Detail}, {ex.Message}.",
                        DateTime.Now);

                    if (ex.StatusCode == StatusCode.Unauthenticated)
                    {
                        if (!UserGoogleAuthRetries.ContainsKey(ctx.User.Id))
                            UserGoogleAuthRetries[ctx.User.Id] = 0;

                        if (UserGoogleAuthRetries[ctx.User.Id] < 3)
                        {
                            UserGoogleAuthRetries[ctx.User.Id]++;

                            ctx.Client.DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName,
                                $"GoogleAssistant: Attempting to refresh access token.",
                                DateTime.Now);

                            IAuthorizationCodeFlow flow =
                                new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                                {
                                    ClientSecrets = new ClientSecrets
                                    {
                                        ClientId = KarvisConfiguration.GoogleAssistantConfiguration.ClientId,
                                        ClientSecret = KarvisConfiguration.GoogleAssistantConfiguration.ClientSecret
                                    },
                                    Scopes = new[] {"https://www.googleapis.com/auth/assistant-sdk-prototype"}
                                });

                            var tokenResponse = await flow.RefreshTokenAsync(
                                KarvisConfiguration.GoogleAssistantConfiguration.DebugUser,
                                new GoogleOAuth(KarvisConfiguration).GetRefreshTokenForUser(ctx.User.Id),
                                new CancellationToken());
                            new GoogleOAuth(KarvisConfiguration).StoreCredentialsForUser(
                                KarvisConfiguration.GoogleAssistantConfiguration.DebugUser, tokenResponse.AccessToken,
                                tokenResponse.RefreshToken, ctx.User.Id);

                            if (!UserEmbeddedAssistantClients.ContainsKey(ctx.User.Id))
                            {
                                var channelCredentials = ChannelCredentials.Create(new SslCredentials(),
                                    GoogleGrpcCredentials.FromAccessToken(tokenResponse.AccessToken));
                                UserEmbeddedAssistantClients[ctx.User.Id] =
                                    new EmbeddedAssistant.EmbeddedAssistantClient(
                                        new Channel("embeddedassistant.googleapis.com", 443, channelCredentials));
                            }

                            await TextAssist(ctx, query);
                        }
                        else
                            UserGoogleAuthRetries[ctx.User.Id] = 0;
                    }
                }
            }
        }

        [Command("assist")]
        public async Task Assist(CommandContext ctx, int @in = 22000, int @out = 44100, int inChan = 1, int outChan = 1, string raw = null)
        {
            if (!UserSpeech.ContainsKey(ctx.User.Id)) return;

            var buff = UserSpeech[ctx.User.Id].ToArray();

            AssistantConfig.AudioInConfig = new AudioInConfig()
            {
                Encoding = AudioInConfig.Types.Encoding.Linear16,
                SampleRateHertz = 24000
            };

            if (raw != "raw")
            {
                AssistantConfig.AudioInConfig.SampleRateHertz = @in;
                buff = AudioConverter.Resample(buff, @in, @out, inChan, outChan);
            }
            else
            {
                AssistantConfig.AudioInConfig.SampleRateHertz = AudioFormat.Default.SampleRate;
            }

            if (!UserEmbeddedAssistantClients.ContainsKey(ctx.User.Id))
            {
                var channelCredentials = ChannelCredentials.Create(new SslCredentials(), GoogleGrpcCredentials.FromAccessToken(KarvisConfiguration.GoogleAssistantConfiguration.DebugToken));
                UserEmbeddedAssistantClients[ctx.User.Id] = new EmbeddedAssistant.EmbeddedAssistantClient(new Channel("embeddedassistant.googleapis.com", 443, channelCredentials));
            }

            using (var call = UserEmbeddedAssistantClients[ctx.User.Id].Assist())
            {
                var configRequest = new AssistRequest()
                {
                    AudioIn = ByteString.Empty,
                    Config = AssistantConfig
                };

                ctx.Client.DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName,
                    $"GoogleAssistant: Sending config message: Audio IsEmpty: {configRequest.AudioIn.IsEmpty}, Request Size: {configRequest.CalculateSize()}",
                    DateTime.Now);

                await call.RequestStream.WriteAsync(configRequest);

                const int frameSize = 1600;
                for (var i = 0; i < buff.Length; i += frameSize)
                {
                    var remaining = i + frameSize > buff.Length ? buff.Length % frameSize : 0;

                    await call.RequestStream.WriteAsync(new AssistRequest()
                    {
                        AudioIn = ByteString.CopyFrom(buff, i, remaining == 0 ? frameSize : remaining)
                    });
                }

                ctx.Client.DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName,
                    $"GoogleAssistant: Full audio sent: Buffer Length: {buff.Length}",
                    DateTime.Now);

                await call.RequestStream.CompleteAsync();

                ctx.Client.DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName,
                    $"GoogleAssistant: Completing request and awaiting response.",
                    DateTime.Now);

                await call.ResponseStream.ForEachAsync(async (response) =>
                {
                    try
                    {
                        if (response.EventType == AssistResponse.Types.EventType.EndOfUtterance)
                        {
                            ctx.Client.DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName,
                                $"GoogleAssistant: Utterance detected: Event Type: {response.EventType.ToString()}, {response.DialogStateOut?.SupplementalDisplayText}, {response.SpeechResults?.FirstOrDefault()?.Transcript} , Debug Info: {response.DebugInfo?.ToString()}",
                                DateTime.Now);
                            await ctx.Client.SendMessageAsync(ctx.Channel,
                                $"{ctx.User.Username}, utterance detected: {response.SpeechResults?.FirstOrDefault()?.Transcript}. {response.ScreenOut?.Data}");
                        }
                        else
                        {
                            ctx.Client.DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName,
                                $"GoogleAssistant: Received response: Event Type: {response.EventType.ToString()}, Microphone Mode: {response.DialogStateOut?.MicrophoneMode}, Debug Info: {response.DebugInfo}",
                                DateTime.Now);
                        }

                    }
                    catch (RpcException ex)
                    {
                        ctx.Client.DebugLogger.LogMessage(LogLevel.Error, Constants.ApplicationName,
                            $"GoogleAssistant: Exception: {ex.StatusCode}, {ex.Status.Detail}, {ex.Message}.",
                            DateTime.Now);
                    }
                });

                try
                {
                    var status = call.GetStatus();
                    ctx.Client.DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName,
                        $"GoogleAssistant: Final Status: {status.StatusCode}, Detail: {status.Detail}.",
                        DateTime.Now);

                }
                catch (InvalidOperationException ex)
                {

                }
            }
        }

        [Command("preserve")]
        public async Task PreserveSpeech(CommandContext ctx, bool preserve = true)
        {
            UserPreserveSpeech[ctx.User.Id] = preserve;

            if (!preserve) UserSpeech[ctx.User.Id] = new ConcurrentQueue<byte>();

        }

        [Command("clear")]
        public async Task ClearSpeech(CommandContext ctx)
        {
            if (UserSpeech.ContainsKey(ctx.User.Id)) UserSpeech[ctx.User.Id] = new ConcurrentQueue<byte>();

        }

        public async Task OnVoiceReceivedPassthrough(VoiceReceiveEventArgs ea, VoiceNextConnection voiceConnection)
        {
            var buff = ea.PcmData.ToArray();

            await voiceConnection.GetTransmitStream().WriteAsync(buff, 0, buff.Length);
            await voiceConnection.GetTransmitStream().FlushAsync();
        }

        public async Task OnVoiceReceived(VoiceReceiveEventArgs ea)
        {
            if (ea.User != null)
            {
                var user = true;
                if (!UserSpeech.ContainsKey(ea.User.Id))
                {
                    user = UserSpeech.TryAdd(ea.User.Id, new ConcurrentQueue<byte>());
                }

                if (user)
                {
                    var buff = ea.PcmData.ToArray();
                    foreach (var b in buff)
                        UserSpeech[ea.User.Id].Enqueue(b);
                }
            }
        }

        private async Task OnUserSpeaking(UserSpeakingEventArgs args, DiscordClient client, DiscordChannel responseChannel)
        {
            if (args.User != null)
            {
                if (args.Speaking == false)
                {
                    var buff = UserSpeech[args.User.Id].ToArray();

                    byte[] resampled = AudioConverter.Resample(buff, 22000, 44100, 1, 1);

                    var text = await new AzureSpeechModule(KarvisConfiguration, client.DebugLogger).AudioToTextAsync(resampled);

                    await args.Client.SendMessageAsync(responseChannel, args.User.Username + ", I think I heard you say: " + text);

                    if (UserPreserveSpeech.ContainsKey(args.User.Id) && !UserPreserveSpeech[args.User.Id]) UserSpeech[args.User.Id] = new ConcurrentQueue<byte>();
                }
            }
        }

        #region Lavalink

        private LavalinkNodeConnection Lavalink { get; set; }
        private LavalinkGuildConnection LavalinkVoice { get; set; }

        [Command("connectl"), Description("Connects to Lavalink")]
        public async Task ConnectAsync(CommandContext ctx, string hostname, int port, string password)
        {
            if (this.Lavalink != null)
                return;

            var lava = ctx.Client.GetLavalink();
            if (lava == null)
            {
                await ctx.RespondAsync("Lavalink is not enabled.").ConfigureAwait(false);
                return;
            }

            this.Lavalink = await lava.ConnectAsync(new LavalinkConfiguration
            {
                RestEndpoint = new ConnectionEndpoint(hostname, port),
                SocketEndpoint = new ConnectionEndpoint(hostname, port),
                Password = password
            }).ConfigureAwait(false);
            this.Lavalink.Disconnected += this.Lavalink_Disconnected;
            await ctx.RespondAsync("Connected to lavalink node.").ConfigureAwait(false);
        }

        [Command("joinl"), Description("Joins a voice channel.")]
        public async Task JoinAsync(CommandContext ctx, DiscordChannel chn = null)
        {
            if (this.Lavalink == null)
            {
                await ctx.RespondAsync("Lavalink is not connected.").ConfigureAwait(false);
                return;
            }

            var vc = chn ?? ctx.Member.VoiceState.Channel;
            if (vc == null)
            {
                await ctx.RespondAsync("You are not in a voice channel or you did not specify a voice channel.").ConfigureAwait(false);
                return;
            }

            this.LavalinkVoice = await this.Lavalink.ConnectAsync(vc);
            this.LavalinkVoice.PlaybackFinished += (args) => this.LavalinkVoice_PlaybackFinished(args, ctx.Channel);
            await ctx.RespondAsync("Connected.").ConfigureAwait(false);
        }

        [Command("play"), Description("Queues tracks for playback.")]
        public async Task PlayAsync(CommandContext ctx, [RemainingText] Uri uri)
        {
            //// Connect to Lavalink
            //var hostname = "localhost";
            //var port = 2333;
            //var password = "youshallnotguessme";

            if (this.LavalinkVoice == null)
                return;

            var trackLoad = await this.Lavalink.GetTracksAsync(uri);
            var track = trackLoad.Tracks.First();
            this.LavalinkVoice.Play(track);

            await ctx.RespondAsync($"Now playing: {Formatter.Bold(Formatter.Sanitize(track.Title))} by {Formatter.Bold(Formatter.Sanitize(track.Author))}.").ConfigureAwait(false);
        }

        [Command("stop")]
        public async Task StopAsync(CommandContext ctx)
        {
            if (this.Lavalink == null)
                return;

            this.LavalinkVoice.Stop();
        }

        #region Lavalink Events

        private Task Lavalink_Disconnected(NodeDisconnectedEventArgs e)
        {
            this.Lavalink = null;
            this.LavalinkVoice = null;
            return Task.CompletedTask;
        }

        private async Task LavalinkVoice_PlaybackFinished(TrackFinishEventArgs e, DiscordChannel responseChannel)
        {
            if (responseChannel == null)
                return;

            await responseChannel.SendMessageAsync($"Playback of {Formatter.Bold(Formatter.Sanitize(e.Track.Title))} by {Formatter.Bold(Formatter.Sanitize(e.Track.Author))} finished.").ConfigureAwait(false);
        }

        #endregion

        #endregion
    }
}
