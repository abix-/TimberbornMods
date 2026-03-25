// TimberbotConfigurator.cs -- Bindito DI registration.
//
// Timberborn uses Bindito (a custom DI framework) to wire game services.
// [Context("Game")] means this runs when a game is loaded (not on the main menu).
// It registers TimberbotService as a singleton, which triggers Bindito to
// resolve all 35 constructor parameters from the game's service container.

using Bindito.Core;

namespace Timberbot
{
    [Context("Game")]
    public class TimberbotConfigurator : Configurator
    {
        public override void Configure()
        {
            Bind<TimberbotService>().AsSingleton();
        }
    }
}
