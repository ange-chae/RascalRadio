using RascalRadio.Core.Models;

namespace RascalRadio.Core.Systems;

public static class Level2
{
    public static readonly LevelDefinition Definition = new()
    {
        Id = "afternoon_broadcast",
        Title = "LEVEL 2",
        QuotaGenre = Genre.Drama,   // TODO: change to this level's genre
        QuotaTarget = 4,
        SegmentTime = 11.0,
    };
}