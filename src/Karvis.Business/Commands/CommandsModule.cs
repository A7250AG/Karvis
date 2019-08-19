using System;
using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Interactivity;
using DSharpPlus.VoiceNext;
using Karvis.Business.Configuration;

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
                await ctx.RespondAsync($"Sorry, {ctx.User.Username}, I can't leave. \n\n``{ex.Message}``");
            }
        }
    }
}
