public enum TaskKind { Callers, Keypad, Memory, Piano, Quality, Signal, Soundboard }

public class LevelTask
{
    public TaskKind Kind;
    public int Target;
    public int Progress;
    public bool Done;
    public bool Failed;     // health tasks only: Quality or Signal dipped
    public int Mistakes;
    public bool IsHealth => Kind == TaskKind.Quality || Kind == TaskKind.Signal;
}