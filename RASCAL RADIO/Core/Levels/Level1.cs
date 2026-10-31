using RascalRadio.Core.Models;

namespace RascalRadio.Core.Systems;

public static class Level1
{
    public static readonly LevelDefinition Definition = new()
    {
        Id = "morning_broadcast",
        Title = "LEVEL 1",
        QuotaGenre = Genre.Drama,   // TODO: change to this level's genre
        QuotaTarget = 4,
        SegmentTime = 11.0,
    };
}