# Rascal Radio: Memory Module (Godot 4 C#)

Simon-says style memory puzzle for 2 players. All UI is built in code.

- **Operator** sees 4 colored buttons in a 2x2 pad. One blinks. Positions: 1 top-left, 2 top-right, 3 bottom-left, 4 bottom-right.
- **Technician** reads the manual: "If BLUE blinks at stage 3, press ...".
- 5 stages. Later stages refer back to earlier presses. Wrong press = static and back to stage 1 (3 static = signal lost).
- Every color also has a shape and name (■ Red, ● Blue, ▲ Yellow, ★ Green) so it never relies on color alone.

## Files
| File | Purpose |
|---|---|
| MemoryRules.cs | Colors, shapes, and the stage/blink rule table (edit rules here; the manual updates automatically) |
| MemoryModule.cs | Operator screen and game logic |
| MemoryManual.cs | Technician manual page |
| MemoryDemo.cs / .tscn | Test scene showing both side by side |

## Setup
1. Copy this folder into your project as `res://MemoryModule/` (if you use another path, fix the path inside MemoryDemo.tscn).
2. Build the C# solution, then run `MemoryDemo.tscn`.
3. In your game, add `new MemoryModule()` to the Operator screen and `new MemoryManual()` to the Technician screen. Namespace is `RascalRadio.Modules`.

## Options on MemoryModule
- `PenaltiesEnabled = false` for the tutorial (wrong press just says try again).
- `ShowLog = true` shows your earlier presses (easier mode).
- `MaxStrikes` (default 3).
- Signals: `Solved`, `Strike(int strikes)`, `Failed`. Call `Restart()` to reset.

## Notes
- The blinking color and button layout are random per stage on the Operator's side. The manual is static, so only the Operator needs the random state.
- If ■ ● ▲ ★ do not show in your font, assign a font that has them or swap the strings in `MemoryRules.Shapes`.
