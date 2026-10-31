using Godot;
using System;

public class ModuleSlot
{
    public string Title;
    public Func<Control> Build;
    public Control Instance;   // built once, reused
    public bool Solved;
}