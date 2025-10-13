using System;
using System.Collections.Generic;
using System.IO;

namespace NunuTheAICompanion.Services.Songcraft
{
    /// <summary>
    /// Super-minimal Standard MIDI File (SMF) writer (format 0).
    /// Enough to write a single track melody with note on/off + tempo + timesig.
    /// </summary>
    public static class MidiWriter
    {
        private static void WriteBE(BinaryWriter bw, int value, int bytes)
        {
            for (int i = bytes - 1; i >= 0; i--)
                bw.Write((byte)((value >> (i * 8)) & 0xFF));
        }

        private static void WriteVLQ(BinaryWriter bw, int value)
        {
            // Variable Length Quantity
            int buffer = value & 0x7F;
            while ((value >>= 7) > 0)
            {
                buffer <<= 8;
                buffer |= ((value & 0x7F) | 0x80);
            }
            while (true)
            {
                bw.Write((byte)buffer);
                if ((buffer & 0x80) > 0) buffer >>= 8;
                else break;
            }
        }

        public sealed class Note
        {
            public int Tick { get; set; }      // start tick
            public int Duration { get; set; }  // duration in ticks
            public int Pitch { get; set; }     // MIDI note number
            public int Velocity { get; set; } = 96;
        }

        public sealed class TrackData
        {
            public int TicksPerQuarter { get; set; } = 480;
            public int TempoUsPerQuarter { get; set; } = 500000; // 120 BPM
            public byte Numerator { get; set; } = 4;
            public byte DenominatorPow2 { get; set; } = 2; // 2 -> 4/4
            public byte Channel { get; set; } = 0;
            public byte Program { get; set; } = 24; // Nylon Guitar-ish
            public List<Note> Notes { get; } = new();
        }

        public static void WriteSingleTrack(string path, TrackData trk)
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var bw = new BinaryWriter(fs);

            // Header chunk MThd
            bw.Write(new byte[] { (byte)'M', (byte)'T', (byte)'h', (byte)'d' });
            WriteBE(bw, 6, 4); // header length
            WriteBE(bw, 0, 2); // format 0
            WriteBE(bw, 1, 2); // one track
            WriteBE(bw, trk.TicksPerQuarter, 2);

            // Prepare track bytes
            using var ms = new MemoryStream();
            using (var tbw = new BinaryWriter(ms))
            {
                // Tempo
                tbw.Write((byte)0x00); // delta
                tbw.Write((byte)0xFF); tbw.Write((byte)0x51); tbw.Write((byte)0x03);
                tbw.Write((byte)((trk.TempoUsPerQuarter >> 16) & 0xFF));
                tbw.Write((byte)((trk.TempoUsPerQuarter >> 8) & 0xFF));
                tbw.Write((byte)(trk.TempoUsPerQuarter & 0xFF));

                // Time Signature
                tbw.Write((byte)0x00);
                tbw.Write((byte)0xFF); tbw.Write((byte)0x58); tbw.Write((byte)0x04);
                tbw.Write(trk.Numerator);
                tbw.Write(trk.DenominatorPow2);
                tbw.Write((byte)24); // MIDI clocks per metronome click
                tbw.Write((byte)8);  // 32nd notes per quarter

                // Program change
                tbw.Write((byte)0x00);
                tbw.Write((byte)(0xC0 | (trk.Channel & 0x0F)));
                tbw.Write(trk.Program);

                // Notes (sorted by start)
                var notes = new List<Note>(trk.Notes);
                notes.Sort((a,b) => a.Tick.CompareTo(b.Tick));

                int lastTick = 0;
                foreach (var n in notes)
                {
                    int deltaOn = Math.Max(0, n.Tick - lastTick);
                    WriteVLQ(tbw, deltaOn);
                    tbw.Write((byte)(0x90 | (trk.Channel & 0x0F)));
                    tbw.Write((byte)(n.Pitch & 0x7F));
                    tbw.Write((byte)(n.Velocity & 0x7F));

                    lastTick = n.Tick;

                    // Note off event delta
                    int offTick = n.Tick + Math.Max(1, n.Duration);
                    int deltaOff = Math.Max(0, offTick - lastTick);
                    WriteVLQ(tbw, deltaOff);
                    tbw.Write((byte)(0x80 | (trk.Channel & 0x0F)));
                    tbw.Write((byte)(n.Pitch & 0x7F));
                    tbw.Write((byte)0x40);

                    lastTick = offTick;
                }

                // End of track
                tbw.Write((byte)0x00);
                tbw.Write((byte)0xFF); tbw.Write((byte)0x2F); tbw.Write((byte)0x00);
                tbw.Flush();
            }

            // Track chunk
            bw.Write(new byte[] { (byte)'M', (byte)'T', (byte)'r', (byte)'k' });
            WriteBE(bw, (int)ms.Length, 4);
            ms.Position = 0;
            ms.CopyTo(fs);
        }
    }
}
