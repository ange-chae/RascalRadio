using RascalRadio.Core.Models;

namespace RascalRadio.Core.Systems;

public static class Level3
{
    public static readonly LevelDefinition Definition = new()
    {
        Id = "emergency_broadcast",
        Title = "LEVEL 3",
        QuotaGenre = Genre.Drama,   // TODO: change to this level's genre
        QuotaTarget = 4,
        SegmentTime = 11.0,
    };
}