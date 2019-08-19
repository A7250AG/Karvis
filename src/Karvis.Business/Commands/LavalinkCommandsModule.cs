using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Lavalink;
using DSharpPlus.Lavalink.EventArgs;
using DSharpPlus.Net;
using DSharpPlus.VoiceNext;

namespace Karvis.Business.Commands
{
    public class LavalinkCommandsModule : BaseCommandModule
    {
        private LavalinkNodeConnection Lavalink { get; set; }
        private LavalinkGuildConnection LavalinkVoice { get; set; }

        [Command("ljoin")]
        public async Task Join(CommandContext ctx)
        {
            try
            {
                var voiceConnection = await ctx.EnsureVoiceConnection();

                if (this.Lavalink != null)
                {
                    this.LavalinkVoice = await this.Lavalink.ConnectAsync(voiceConnection.Channel);
                    this.LavalinkVoice.PlaybackFinished += (args) => this.LavalinkVoice_PlaybackFinished(args, ctx.Channel);

                    await ctx.RespondAsync("I've connected to lavalink.");
                }
            }
            catch (Exception ex)
            {
                await ctx.RespondAsync($"Sorry, {ctx.User.Username}, I can't join. \n\n``{ex.Message}``");
            }
        }

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
    }
}
