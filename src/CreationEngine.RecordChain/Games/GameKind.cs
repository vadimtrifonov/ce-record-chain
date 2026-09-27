using CreationEngine.RecordChain.Games.Skyrim;
using CreationEngine.RecordChain.Games.Starfield;
using Mutagen.Bethesda.Skyrim;

namespace CreationEngine.RecordChain.Games;

internal enum GameKind
{
    SkyrimSE,
    SkyrimVR,
    Starfield
}

internal static class GameSupport
{
    internal static IGameSupport Create(GameKind game) => game switch
    {
        GameKind.SkyrimSE => new SkyrimGame(SkyrimRelease.SkyrimSE),
        GameKind.SkyrimVR => new SkyrimGame(SkyrimRelease.SkyrimVR),
        GameKind.Starfield => new StarfieldGame(),
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, null)
    };
}
