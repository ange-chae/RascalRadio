namespace RascalRadio.Core.Models;

public class Caller
{
    public string Name { get; set; } = "";
    public string Story { get; set; } = "";
    public Genre Topic { get; set; }
    public bool IsRaccoon { get; set; }
    public string Clue { get; set; } = "";

    public Caller(Genre topic) { Topic = topic; }
}