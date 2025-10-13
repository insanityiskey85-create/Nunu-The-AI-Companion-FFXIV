using System;
using System.Collections.Generic;
using System.IO;
using Dalamud.Plugin.Services;

namespace NunuTheAICompanion.Services.Songcraft
{
    /// <summary>
    /// Generates simple melodic lines from text mood and writes a MIDI file.
    /// Designed to be fast, deterministic, and offline.
    /// </summary>
    public sealed class SongcraftService
    {
        private readonly IPluginLog _log;
        private readonly Configuration _cfg;

        private static readonly Dictionary<string, int[]> ScaleMap = new()
        {
            // Major & minor pentatonic-ish palettes for safe in-game vibes
            ["light"]     = new [] {0, 2, 4, 7, 9},   // Ionian-ish
            ["playful"]   = new [] {0, 3, 5, 7, 10},  // Mixolydian/minor mix
            ["sorrow"]    = new [] {0, 3, 5, 7, 8},   // Aeolian-ish
            ["mystic"]    = new [] {0, 1, 5, 6, 10},  // Locrian/Phrygian hints
            ["battle"]    = new [] {0, 2, 3, 5, 7, 10},
            ["triumph"]   = new [] {0, 2, 4, 7, 11},  // Lydian touch
        };

        private static readonly Dictionary<string, (int bpm, byte prog)> MoodDefaults = new()
        {
            ["light"] = (96, 24),     // Nylon Guitar
            ["playful"] = (108, 73),  // Flute
            ["sorrow"] = (72, 48),    // Strings
            ["mystic"] = (84, 89),    // Pad 2 (warm)
            ["battle"] = (120, 30),   // Distortion Guitar
            ["triumph"] = (126, 56),  // Trumpet
        };

        public SongcraftService(Configuration cfg, IPluginLog log)
        {
            _cfg = cfg;
            _log = log;
        }

        public string ComposeToFile(string text, string? moodHint, string baseDir, string fileLabel = "nunu_song")
        {
            var mood = NormalizeMood(moodHint ?? GuessMood(text));
            var (bpm, program) = MoodDefaults.TryGetValue(mood, out var d) ? d : ( _cfg.SongcraftTempoBpm, (byte)_cfg.SongcraftProgram );
            var key = NoteNameToMidi(_cfg.SongcraftKey ?? "C4");
            var scale = ScaleMap.TryGetValue(mood, out var s) ? s : ScaleMap["light"];

            var ticksQ = 480;
            var usPerQ = (int)(60_000_000.0 / Math.Clamp(bpm, 40, 200));
            var lengthBars = Math.Max(2, Math.Min(64, _cfg.SongcraftBars));

            var trk = new MidiWriter.TrackData
            {
                TicksPerQuarter = ticksQ,
                TempoUsPerQuarter = usPerQ,
                Numerator = 4,
                DenominatorPow2 = 2,
                Channel = 0,
                Program = (byte)program
            };

            var rng = new Random(SeedFrom(text + mood));
            int barTicks = ticksQ * 4;
            int eighth = ticksQ / 2;
            int sixteenth = ticksQ / 4;

            int t = 0;
            int lastDegree = 0;

            for (int bar = 0; bar < lengthBars; bar++)
            {
                for (int step = 0; step < 8; step++) // eighth notes
                {
                    int degree = NextDegree(rng, scale, lastDegree);
                    lastDegree = degree;
                    int pitch = key + scale[degree % scale.Length] + 12 * (degree / scale.Length);
                    var dur = rng.NextDouble() < 0.15 ? ticksQ : eighth; // occasional long notes
                    trk.Notes.Add(new MidiWriter.Note { Tick = t, Duration = (int)dur, Pitch = pitch, Velocity = 96 });
                    t += (int)eighth;
                }
            }

            Directory.CreateDirectory(baseDir);
            var path = Path.Combine(baseDir, $"{fileLabel}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.mid");
            MidiWriter.WriteSingleTrack(path, trk);
            _log.Info("[Songcraft] wrote: {Path} (mood={Mood}, bpm={Bpm}, program={Prog})", path, mood, bpm, program);
            return path;
        }

        private static string NormalizeMood(string m)
        {
            if (string.IsNullOrWhiteSpace(m)) return "light";
            m = m.ToLowerInvariant();
            if (ScaleMap.ContainsKey(m)) return m;
            return m switch
            {
                "sad" or "melancholy" or "mourning" => "sorrow",
                "happy" or "bright" or "gentle" => "light",
                "mysterious" or "void" => "mystic",
                "fight" or "angry" or "rage" => "battle",
                "victory" or "heroic" => "triumph",
                _ => "playful"
            };
        }

        public string GuessMood(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "light";
            var t = text.ToLowerInvariant();
            if (t.Contains("farewell") || t.Contains("alone") || t.Contains("loss") || t.Contains("tears")) return "sorrow";
            if (t.Contains("storm") || t.Contains("battle") || t.Contains("strike") || t.Contains("blood")) return "battle";
            if (t.Contains("myst") || t.Contains("abyss") || t.Contains("void") || t.Contains("whisper")) return "mystic";
            if (t.Contains("victory") || t.Contains("glory") || t.Contains("cheers")) return "triumph";
            if (t.Contains("joke") || t.Contains("dance") || t.Contains("play")) return "playful";
            return "light";
        }

        private static int SeedFrom(string s)
        {
            unchecked
            {
                int h = 23;
                foreach (var ch in s) h = h * 31 + ch;
                return h;
            }
        }

        private static int NextDegree(Random rng, int[] scale, int last)
        {
            // small random walk with occasional leap
            var step = rng.NextDouble() < 0.8 ? (rng.Next(0, 2) == 0 ? -1 : 1) : rng.Next(-3, 4);
            var deg = Math.Max(0, Math.Min(last + step, 10));
            return deg;
        }

        private static int NoteNameToMidi(string name)
        {
            // e.g., C4, D#3, Bb3
            if (string.IsNullOrWhiteSpace(name)) return 60;
            name = name.Trim();
            int[] offsets = { 9, 11, 0, 2, 4, 5, 7 }; // A,B,C,D,E,F,G (C=0)
            int i = 0;
            char l = char.ToUpperInvariant(name[i++]);
            int baseIdx = l switch { 'C'=>2,'D'=>3,'E'=>4,'F'=>5,'G'=>6,'A'=>0,'B'=>1, _=>2 };
            int semis = offsets[baseIdx];
            if (i < name.Length && (name[i] == '#' || name[i] == 'b'))
            {
                if (name[i] == '#') semis += 1;
                else semis -= 1;
                i++;
            }
            int octave = 4;
            if (i < name.Length && int.TryParse(name[i..], out var o)) octave = o;
            return (octave + 1) * 12 + semis; // MIDI note number
        }
    }
}
