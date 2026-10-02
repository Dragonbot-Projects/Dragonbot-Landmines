using Discord;
using Discord.Net;
using Discord.WebSocket;
using DragonBot.Core;
using DragonBot.Instance;
using DragonBot.Modules;
using System.Text.Json;

namespace Landmines
{
    [RegisterModule]
    internal sealed class Landmines : ModuleBase, IModule<Landmines>, ICommand
    {
#if DEBUG
        const string COMMAND_NAME = "landmine-test";
#elif RELEASE
        const string COMMAND_NAME = "landmine";
#endif
        public static string Name { get; } = "Landmines";
        private LandmineConfig Config { get; }

        private readonly Dictionary<ulong, uint> LandmineData = [];
        private Landmines(Bot bot) : base(bot)
        {
            bot.Client.SlashCommandExecuted += HandleCommands;
            bot.Client.MessageReceived += OnMessageReceived;
            LandmineData = StateManager.LoadState<Dictionary<ulong, uint>>(Name, [])?.Data ?? [];
            Config = ConfigManager.LoadConfig<LandmineConfig>(Name)?.Data ?? new LandmineConfig();
        }

        public static Landmines Create(Bot bot)
        {
            return new Landmines(bot);
        }

        public async Task RegisterCommands()
        {
            if (!bot.Util.IsCommandRegistered(COMMAND_NAME))
            {
                var guild = bot.Client.GetGuild(bot.BotConfig.GuildID);
                SlashCommandBuilder builder = new();
                builder.WithName(COMMAND_NAME)
                    .WithDescription("Add/Remove landmines from the current channel.")
                    .WithDefaultMemberPermissions(GuildPermission.Administrator)
                    .WithContextTypes(InteractionContextType.Guild)
                    .AddOption(new SlashCommandOptionBuilder()
                        .WithName("plant")
                        .WithDescription("Plant x landmines in the current channel.")
                        .WithType(ApplicationCommandOptionType.SubCommand)
                        .AddOption("amount", ApplicationCommandOptionType.Integer, "The amount of landmines to plant.", isRequired: true)
                    ).AddOption(new SlashCommandOptionBuilder()
                        .WithName("defuse")
                        .WithDescription("Defuse all landmines in the current channel.")
                        .WithType(ApplicationCommandOptionType.SubCommand)
                    ).AddOption(new SlashCommandOptionBuilder()
                        .WithName("info")
                        .WithDescription("Get information about landmines in the current channel.")
                        .WithType(ApplicationCommandOptionType.SubCommand)
                    ).AddOption(new SlashCommandOptionBuilder()
                        .WithName("config")
                        .WithDescription("Get or set the configuration for the landmines module.")
                        .WithType(ApplicationCommandOptionType.SubCommandGroup)
                        .AddOption(new SlashCommandOptionBuilder()
                            .WithName("get")
                            .WithDescription("Get the current configuration for the landmines module.")
                            .WithType(ApplicationCommandOptionType.SubCommand)
                            .AddOption("<option_name>", ApplicationCommandOptionType.String, "The option to get.", isRequired: true)
                        ).AddOption(new SlashCommandOptionBuilder()
                            .WithName("set")
                            .WithDescription("Set the configuration for the landmines module.")
                            .WithType(ApplicationCommandOptionType.SubCommand)
                            .AddOption("<option_name>", ApplicationCommandOptionType.String, "The option to set.", isRequired: true)
                            .AddOption("<option_value>", ApplicationCommandOptionType.String, "The value for the option.", isRequired: true)
                        )
                    );
#pragma warning disable CS0618 // Type or member is obsolete
                try
                {
                    await guild.CreateApplicationCommandAsync(builder.Build());
                }
                catch (ApplicationCommandException ex)
                {
                    var json = JsonSerializer.Serialize(ex.Errors);
                    await Logger.Log($@"ApplicationCommandException thrown during command registration for command {builder.Name}
                    Errors reported: {json}", LogSeverity.Error);
                }
                catch (Exception ex)
                {
                    await Logger.Log($"Exeption {ex} thrown during command registration for command {builder.Name}", LogSeverity.Error);
                }
            }
#pragma warning restore CS0618 // Type or member is obsolete
        }
        private async Task HandleCommands(SocketSlashCommand command)
        {
            var commandName = command.Data.Options.First().Name;
            var ChannelID = command.ChannelId ?? 0;
            switch (commandName)
            {
                case "plant":
                    var count = (uint)command.Data.Options.First().Options.First().Value;
                    if (count == 0)
                    {
                        await command.RespondAsync("You cannot plant 0 landmines.", ephemeral: true);
                    }
                    if (LandmineData.TryAdd(ChannelID, count))
                    {
                        await command.RespondAsync($"Planted {count} landmines in this channel.", ephemeral: true);
                    }
                    else
                    {
                        LandmineData[ChannelID] += count;
                        await command.RespondAsync($"Planted {count} additional landmines in this channel. Total landmines: {LandmineData[ChannelID]}", ephemeral: true);
                    }
                    break;
                case "defuse":
                    if (LandmineData.Remove(ChannelID))
                    {
                        await command.RespondAsync("All landmines in this channel have been defused.", ephemeral: true);
                    }
                    else
                    {
                        await command.RespondAsync("There are no landmines in this channel to defuse.", ephemeral: true);
                    }
                    break;
                case "info":
                    if (LandmineData.TryGetValue(ChannelID, out uint landmineCount))
                    {
                        await command.RespondAsync($"There are {landmineCount} landmines in this channel.", ephemeral: true);
                    }
                    else
                    {
                        await command.RespondAsync("There are no landmines in this channel.", ephemeral: true);
                    }
                    break;
                case "config":
                    var subCommand = command.Data.Options.First().Options.First();
                    var optionName = subCommand.Options.First().Name;
                    switch (subCommand.Name)
                    {
                        case "get":
                            var currentValue = Config.GetType().GetProperty(optionName)?.GetValue(null);
                            if (currentValue is not null)
                            {
                                await command.RespondAsync($"Current value for configuration option '{optionName}': {currentValue}", ephemeral: true);
                            }
                            else
                            {
                                await command.RespondAsync($"Configuration option '{optionName}' not found.", ephemeral: true);
                            }
                            break;
                        case "set":
                            var property = Config.GetType().GetProperty(optionName);
                            var value = subCommand.Options.First().Value;
                            if (property is null)
                            {
                                await command.RespondAsync($"Configuration option '{optionName}' not found.", ephemeral: true);
                                break;
                            }
                            Type type = property.PropertyType;
                            if (property?.CanWrite == true && property.PropertyType.IsValueType)
                            {
                                try
                                {
                                    property.SetValue(Config, Convert.ChangeType(value, type));
                                }
                                catch (InvalidCastException)
                                {
                                    await command.RespondAsync($"Failed to set configuration option '{optionName}',{value.GetType()} is not convertable to {type}.", ephemeral: true);
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    await command.RespondAsync($"Failed to set configuration option '{optionName}': {ex.Message}", ephemeral: true);
                                    break;
                                }
                                await ConfigManager.SaveConfig(ModuleConfig<LandmineConfig>.CreateConfig(Config, Name, new Semver("1.0.0")));
                                await command.RespondAsync($"Configuration option '{optionName}' set to {value}.", ephemeral: true);
                                break;
                            }
                            else
                            {
                                await command.RespondAsync($"Configuration option '{optionName}' not found or not writable.", ephemeral: true);
                            }
                            break;
                        default:
                            await command.RespondAsync($"Unknown subcommand: {subCommand}", ephemeral: true);
                            break;
                    }
                    break;
                default:
                    await command.RespondAsync($"Unknown subcommand: {commandName}", ephemeral: true);
                    break;
            }
            await StateManager.SaveState(ModuleState<Dictionary<ulong, uint>>.CreateState(LandmineData, Name, new Semver("1.0.0")));
        }
        private async Task OnMessageReceived(SocketMessage message)
        {
            _ = LandmineData.TryGetValue(message.Channel.Id, out uint landmineCount);
            if (landmineCount > 0)
            {
                Random rand = new();
                if (message.Author is SocketGuildUser guildUser && Config.TimeoutDuration > 0 && (!guildUser.GuildPermissions.Administrator || !guildUser.GuildPermissions.ModerateMembers) && rand.NextDouble() < Config.LandmineProbability)
                {
                    LandmineData[message.Channel.Id]--;
                    await message.Channel.SendMessageAsync($"💥{message.Author.Mention} triggered a landmine and has been timed out for {Config.TimeoutDuration} seconds! {landmineCount - 1} landmines remaining in this channel.");
                    await guildUser.SetTimeOutAsync(TimeSpan.FromSeconds(Config.TimeoutDuration));
                }
                if (LandmineData[message.Channel.Id] == 0)
                {
                    LandmineData.Remove(message.Channel.Id);
                    await message.Channel.SendMessageAsync("All landmines in this channel have been triggered and are now gone.");
                }
                await StateManager.SaveState(ModuleState<Dictionary<ulong, uint>>.CreateState(LandmineData, Name, new Semver("1.0.0")));
            }
        }
    }
    [Serializable]
    internal readonly record struct LandmineConfig(uint TimeoutDuration = 30, double LandmineProbability = 0.01f);
}
