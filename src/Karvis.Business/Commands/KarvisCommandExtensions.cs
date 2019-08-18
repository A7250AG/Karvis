using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using DSharpPlus.VoiceNext;

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
                await ctx.Channel.SendMessageAsync($"Sorry, {ctx.User.Username}, I can't do that. \n\n``{ex.Message}``");

                throw;
            }
        }
    }
}
