using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
using Karvis.Audio;
using Karvis.Speech;
using NAudio.Wave;

namespace Karvis.Commands
{
    public class CommandsModule : BaseCommandModule
    {
        private static ConcurrentDictionary<ulong, bool> UserPreserveSpeech = new ConcurrentDictionary<ulong, bool>();
        private static ConcurrentDictionary<ulong, ConcurrentQueue<byte>> UserSpeech = new ConcurrentDictionary<ulong, ConcurrentQueue<byte>>();

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
        }

        [Command("leave")]
        public async Task Leave(CommandContext ctx, string force = null)
        {
            var discordClient = ctx.Client;
            var voiceClient = discordClient.GetVoiceNext();
            var voiceConnection = voiceClient.GetConnection(ctx.Guild);

            if (voiceConnection == null || (voiceConnection.Channel != ctx.Member?.VoiceState?.Channel && force?.ToLowerInvariant() != "force"))
                throw new InvalidOperationException("I'm not connected to your voice channel.");

            voiceConnection.Disconnect();

            await ctx.RespondAsync("I've left your voice channel.");
        }

        [Command("say")]
        public async Task Say(CommandContext ctx, [RemainingText] string text)
        {
            // Guard inputs
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("No text to speak.");

            // Get a VoiceNextConnection object
            var vnc = ctx.Client.GetVoiceNext().GetConnection(ctx.Guild);
            if (vnc == null)
                throw new InvalidOperationException("Not connected in this guild.");

            // Process the speech request
            await ctx.RespondAsync(DiscordEmoji.FromName(ctx.Client, ":thinking:"));

            var buffer = await new AzureSpeechModule(ctx.Client.DebugLogger).TextToAudioAsync(text);

            if (buffer.Length == 0)
            {
                await ctx.RespondAsync(DiscordEmoji.FromName(ctx.Client, ":thumbsdown:"));
                return;
            }
            else
            {
                await ctx.RespondAsync(DiscordEmoji.FromName(ctx.Client, ":thumbsup:"));

                vnc.SendSpeaking(); // send a speaking indicator

                await vnc.GetTransmitStream().WriteAsync(buffer);
                await vnc.GetTransmitStream().FlushAsync();

                vnc.SendSpeaking(false); // end the speaking indicator
            }
        }

        [Command("simonsay")]
        public async Task SimonSay(CommandContext ctx, string force = null, int @in = 22000, int @out = 44100)
        {
            var discordClient = ctx.Client;
            var voiceClient = discordClient.GetVoiceNext();
            var voiceConnection = voiceClient.GetConnection(ctx.Guild);

            if (voiceConnection == null || (voiceConnection.Channel != ctx.Member?.VoiceState?.Channel && force?.ToLowerInvariant() != "force"))
                throw new InvalidOperationException("I'm not connected to your voice channel.");

            if (!UserSpeech.ContainsKey(ctx.User.Id) || UserSpeech[ctx.User.Id].IsEmpty) return;

            var buff = UserSpeech[ctx.User.Id].ToArray();

            byte[] resampled;
            // NAudio resampling from Azure Speech default to Opus default 
            using (var output = new MemoryStream())
            using (var ms = new MemoryStream(buff))
            using (var rs = new RawSourceWaveStream(ms, new WaveFormat(@in, 16, 2)))
            using (var resampler = new MediaFoundationResampler(rs, new WaveFormat(@out, 16, 2)))
            {
                // thanks https://csharp.hotexamples.com/examples/NAudio.Wave/MediaFoundationResampler/Read/php-mediafoundationresampler-read-method-examples.html#0xe8c3188aa82ab5c60c681c14b7336b52f1b3546fd75d133baef6572074b6028c-125,,155,
                byte[] bytes = new byte[rs.WaveFormat.AverageBytesPerSecond * 4];
                while (true)
                {
                    int bytesRead = resampler.Read(bytes, 0, bytes.Length);
                    if (bytesRead == 0)
                        break;
                    output.Write(bytes, 0, bytesRead);
                }

                resampled = output.GetBuffer();
            }

            voiceConnection.SendSpeaking();
            await voiceConnection.GetTransmitStream().WriteAsync(resampled);
            await voiceConnection.GetTransmitStream().FlushAsync();
            voiceConnection.SendSpeaking(false);
        }

        [Command("transcribe")]
        public async Task Transcribe(CommandContext ctx)
        {
            if (!UserSpeech.ContainsKey(ctx.User.Id)) return;

            var buff = UserSpeech[ctx.User.Id].ToArray();

            var resampled = AudioConverter.Resample(buff, 22000, 44100, 2, 2);

            await ctx.RespondAsync("I think I heard you say: " + await new AzureSpeechModule(ctx.Client.DebugLogger).AudioToTextAsync(resampled));
        }

        [Command("preserve")]
        public async Task PreserveSpeech(CommandContext ctx, bool preserve = true)
        {
            UserPreserveSpeech[ctx.User.Id] = preserve;

            if (!preserve) UserSpeech[ctx.User.Id] = new ConcurrentQueue<byte>();

        }

        public async Task OnVoiceReceivedDoPassthrough(VoiceReceiveEventArgs ea, VoiceNextConnection voiceConnection)
        {
            var buff = ea.PcmData.ToArray();

            await voiceConnection.GetTransmitStream().WriteAsync(buff);
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

                    byte[] resampled = AudioConverter.Resample(buff, 22000, 44100, 2, 2);

                    await args.Client.SendMessageAsync(responseChannel, args.User.Username + ", I think I heard you say: " + await new AzureSpeechModule(client.DebugLogger).AudioToTextAsync(resampled));


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
