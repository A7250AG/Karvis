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
    public class GoogleCommandsModule : BaseCommandModule
    {
        private static readonly ConcurrentDictionary<ulong, EmbeddedAssistant.EmbeddedAssistantClient> UserEmbeddedAssistantClients = new ConcurrentDictionary<ulong, EmbeddedAssistant.EmbeddedAssistantClient>();
        private static readonly ConcurrentDictionary<ulong, int> UserGoogleAuthRetries = new ConcurrentDictionary<ulong, int>();

        private readonly KarvisConfiguration KarvisConfiguration;
        private readonly AssistConfig AssistantConfig;

        public GoogleCommandsModule(IKarvisConfigurationService configurationService)
        {
            KarvisConfiguration = configurationService.Configuration;

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
                            _ = Task.Run(async () =>
                              {
                                  ctx.Client.DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName,
                                      $"GoogleAssistant: Received screen data.",
                                      DateTime.Now);

                                  await new BrowserFetcher().DownloadAsync(BrowserFetcher.DefaultRevision);
                                  using (var browser = await Puppeteer.LaunchAsync(new LaunchOptions
                                  {
                                      DefaultViewport = new ViewPortOptions()
                                      { IsLandscape = true, Width = 1280, Height = 720 },
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
                              });
                        }

                        if (response.AudioOut?.AudioData != null)
                        {
                            if (!string.IsNullOrWhiteSpace(response.DialogStateOut?.SupplementalDisplayText))
                            {
                                ctx.Client.DebugLogger.LogMessage(LogLevel.Info, Constants.ApplicationName,
                                    $"GoogleAssistant: Received supplemental text.",
                                    DateTime.Now);

                                await ctx.RespondAsync(response.DialogStateOut?.SupplementalDisplayText);
                            }

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
                        var voiceConnection = await ctx.EnsureVoiceConnection();

                        if (voiceConnection == null || (voiceConnection.Channel != ctx.Member?.VoiceState?.Channel))
                            throw new InvalidOperationException($"I'm not connected to your voice channel, so I can't speak.");


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

                            using (IAuthorizationCodeFlow flow =
                                new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                                {
                                    ClientSecrets = new ClientSecrets
                                    {
                                        ClientId = KarvisConfiguration.GoogleAssistantConfiguration.ClientId,
                                        ClientSecret = KarvisConfiguration.GoogleAssistantConfiguration.ClientSecret
                                    },
                                    Scopes = new[] {"https://www.googleapis.com/auth/assistant-sdk-prototype"}
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

                                await TextAssist(ctx, query);
                            }
                        }
                        else
                            UserGoogleAuthRetries[ctx.User.Id] = 0;
                    }
                }
                catch (Exception ex)
                {
                    await ctx.Channel.SendMessageAsync($"Sorry, {ctx.User.Username}, I can't google. \n\n``{ex.Message}``");
                }
            }
        }

        [Command("assist")]
        public async Task Assist(CommandContext ctx, int @in = 22000, int @out = 44100, int inChan = 1, int outChan = 1, string raw = null)
        {
            if (!ctx.Services.GetService<IProvideAudioState>().SpeechFromUser.ContainsKey(ctx.User.Id)) return;

            var buff = ctx.Services.GetService<IProvideAudioState>().SpeechFromUser[ctx.User.Id].ToArray();

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
    }
}
