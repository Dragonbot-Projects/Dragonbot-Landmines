using DragonBot;
using DragonBot.Core;
using DragonBot.Modules;

namespace Landmines
{
    public class ModuleMain : IModuleMain
    {
        public static Dictionary<Type, Action<object>> Initilizers { get; } = new()
        {
            { typeof(ICommand), static (module) => ((ICommand)module).RegisterCommands() }
        };

        public static void InitAssembly()
        {
            ModuleRegistrar.Initializers = ModuleRegistrar.Initializers.Merge(Initilizers);
        }
    }
}
