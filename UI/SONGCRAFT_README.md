# Songcraft Mode (Procedural Bardic Melodies)

This patch adds an offline melody generator that writes **.mid** files you can play with in-game tools (e.g., MidiBard).

## Files
- `Services/Songcraft/MidiWriter.cs` — tiny SMF writer (format 0) with tempo/time signature, program, notes.
- `Services/Songcraft/SongcraftService.cs` — mood -> scale/tempo mapping, melody generator, file writer.
- `Configuration.Songcraft.partial.cs` — feature flags and defaults.
- `PluginMain.Songcraft.partial.cs` — initialization and an easy `TriggerSongcraft(text, mood)` entry point.

## Wiring
1. In `PluginMain` initialization, call:
   ```csharp
   InitializeSongcraft(_log);
   ```
2. When a reply is finalized (or when a special command is detected), call:
   ```csharp
   var path = TriggerSongcraft(finalReply, mood: null); // mood auto-guessed from text
   if (path != null) ChatWindow?.AddSystemLine($"[Songcraft] Saved: {path}");
   ```
   You can also pass an explicit mood: `"sorrow" | "light" | "playful" | "mystic" | "battle" | "triumph"`.

## Config
- `SongcraftEnabled` (default true)
- `SongcraftKey` (e.g., `C4`, `D#3`)
- `SongcraftTempoBpm` (fallback tempo)
- `SongcraftBars` (2..64)
- `SongcraftProgram` (0..127 GM program)
- `SongcraftSaveDir` (null = Memories dir)

## How it sounds
- Uses small, safe scales per mood; melody is a constrained random walk with occasional long notes.
- Deterministic per text+mood (seeded) for repeatable motifs.
- Output: **Format 0 MIDI**, 4/4, default PPQ=480.

You can later add multi-track harmonies or drum patterns—this is designed to be extended.
