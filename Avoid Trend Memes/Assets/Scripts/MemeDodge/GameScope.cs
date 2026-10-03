using VContainer;
using VContainer.Unity;

namespace MemeDodge
{
    public sealed class GameScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<GameSession>(Lifetime.Singleton);
            builder.Register<GameAssets>(Lifetime.Singleton);
            builder.RegisterBuildCallback(container => container.Inject(GetComponent<GameController>()));
        }
    }
}
