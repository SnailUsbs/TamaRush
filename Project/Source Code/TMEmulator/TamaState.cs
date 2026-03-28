using System;
using System.Collections.Generic;
using System.IO;

namespace TamaRush.TMEmulator
{
    public static class TamaState
    {
        private const string Magic   = "TLST";
        private const byte   Version = 3;

        private const int INT_SLOT_NUM = 6;
        private const int MEM_RAM_SIZE = 0x280;
        private const int MEM_IO_SIZE  = 0x080;
        private const int MEM_IO_ADDR  = 0xF00;

        private const int MaxSaveSlots = 5;
        public static byte[] Snapshot(TamaEmulator emu)
        {
            using (var ms = new MemoryStream())
            using (var w  = new BinaryWriter(ms))
            {
                WriteState(w, emu);
                return ms.ToArray();
            }
        }
        public static void WriteSnapshot(byte[] snapshot, string savesFolder, string romName)
        {
            if (!Directory.Exists(savesFolder))
                Directory.CreateDirectory(savesFolder);

            string path = NextSavePath(savesFolder, romName);
            File.WriteAllBytes(path, snapshot);

            var slots = GetSortedSlots(savesFolder, romName);
            while (slots.Count > MaxSaveSlots)
            {
                File.Delete(slots[0]);
                slots.RemoveAt(0);
            }
        }
        public static void Save(TamaEmulator emu, string savesFolder, string romName)
        {
            WriteSnapshot(Snapshot(emu), savesFolder, romName);
        }
        public static bool Load(TamaEmulator emu, string savesFolder, string romName)
        {
            string path = LastSavePath(savesFolder, romName);
            if (path == null) return false;
            return LoadFrom(emu, path);
        }
        public static bool LoadFrom(TamaEmulator emu, string path)
        {
            if (!File.Exists(path)) return false;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var r  = new BinaryReader(fs))
                {
                    byte[] magic = r.ReadBytes(4);
                    for (int i = 0; i < 4; i++)
                        if (magic[i] != (byte)Magic[i]) return false;

                    if (r.ReadByte() != Version) return false;

                    lock (emu.Lock)
                    {
                        emu.PC    = r.ReadByte() | ((r.ReadByte() & 0x1F) << 8);
                        emu.X     = r.ReadByte() | ((r.ReadByte() & 0x0F) << 8);
                        emu.Y     = r.ReadByte() | ((r.ReadByte() & 0x0F) << 8);
                        emu.A     = (byte)(r.ReadByte() & 0x0F);
                        emu.B     = (byte)(r.ReadByte() & 0x0F);
                        emu.NP    = (byte)(r.ReadByte() & 0x1F);
                        emu.SP    = r.ReadByte();
                        emu.Flags = (byte)(r.ReadByte() & 0x0F);

                        emu.TickCounter = ReadU32(r);
                        emu.Clk2Hz      = ReadU32(r);
                        emu.Clk4Hz      = ReadU32(r);
                        emu.Clk8Hz      = ReadU32(r);
                        emu.Clk16Hz     = ReadU32(r);
                        emu.Clk32Hz     = ReadU32(r);
                        emu.Clk64Hz     = ReadU32(r);
                        emu.Clk128Hz    = ReadU32(r);
                        emu.Clk256Hz    = ReadU32(r);
                        emu.ProgTimerTs = ReadU32(r);

                        emu.ProgTimerEnabled = r.ReadByte() != 0;
                        emu.ProgTimerData    = r.ReadByte();
                        emu.ProgTimerRld     = r.ReadByte();

                        ReadU32(r);

                        for (int i = 0; i < INT_SLOT_NUM; i++)
                        {
                            emu.IntFactor[i]    = (byte)(r.ReadByte() & 0x0F);
                            emu.IntMask[i]      = (byte)(r.ReadByte() & 0x0F);
                            emu.IntTriggered[i] = r.ReadByte() != 0;
                        }

                        for (int i = 0; i < MEM_RAM_SIZE; i++)
                            emu.SetRamNibble(i, (byte)(r.ReadByte() & 0x0F));

                        for (int i = 0; i < MEM_IO_SIZE; i++)
                            emu.SetIONibble(i, (byte)(r.ReadByte() & 0x0F));
                    }
                }
                return true;
            }
            catch { return false; }
        }

        private static void WriteState(BinaryWriter w, TamaEmulator emu)
        {
            foreach (char c in Magic) w.Write((byte)c);
            w.Write(Version);

            lock (emu.Lock)
            {
                w.Write((byte)( emu.PC       & 0xFF));
                w.Write((byte)((emu.PC >> 8) & 0x1F));

                w.Write((byte)( emu.X       & 0xFF));
                w.Write((byte)((emu.X >> 8) & 0x0F));

                w.Write((byte)( emu.Y       & 0xFF));
                w.Write((byte)((emu.Y >> 8) & 0x0F));

                w.Write((byte)(emu.A     & 0x0F));
                w.Write((byte)(emu.B     & 0x0F));
                w.Write((byte)(emu.NP    & 0x1F));
                w.Write((byte)(emu.SP    & 0xFF));
                w.Write((byte)(emu.Flags & 0x0F));

                WriteU32(w, emu.TickCounter);
                WriteU32(w, emu.Clk2Hz);
                WriteU32(w, emu.Clk4Hz);
                WriteU32(w, emu.Clk8Hz);
                WriteU32(w, emu.Clk16Hz);
                WriteU32(w, emu.Clk32Hz);
                WriteU32(w, emu.Clk64Hz);
                WriteU32(w, emu.Clk128Hz);
                WriteU32(w, emu.Clk256Hz);
                WriteU32(w, emu.ProgTimerTs);

                w.Write((byte)(emu.ProgTimerEnabled ? 1 : 0));
                w.Write((byte)(emu.ProgTimerData & 0xFF));
                w.Write((byte)(emu.ProgTimerRld  & 0xFF));

                WriteU32(w, 0);

                for (int i = 0; i < INT_SLOT_NUM; i++)
                {
                    w.Write((byte)(emu.IntFactor[i]    & 0x0F));
                    w.Write((byte)(emu.IntMask[i]      & 0x0F));
                    w.Write((byte)(emu.IntTriggered[i] ? 1 : 0));
                }

                for (int i = 0; i < MEM_RAM_SIZE; i++)
                    w.Write((byte)(emu.GetRamNibble(i) & 0x0F));

                for (int i = 0; i < MEM_IO_SIZE; i++)
                    w.Write((byte)(emu.GetIONibble(i) & 0x0F));
            }
        }

        public static string NextSavePath(string savesFolder, string romName)
        {
            var slots = GetSortedSlots(savesFolder, romName);
            int next = slots.Count == 0 ? 0 : ParseSlot(slots[slots.Count - 1], romName) + 1;
            return SavePath(savesFolder, romName, next);
        }

        public static string LastSavePath(string savesFolder, string romName)
        {
            var slots = GetSortedSlots(savesFolder, romName);
            return slots.Count == 0 ? null : slots[slots.Count - 1];
        }

        private static List<string> GetSortedSlots(string savesFolder, string romName)
        {
            if (!Directory.Exists(savesFolder)) return new List<string>();
            var files = Directory.GetFiles(savesFolder, $"{romName}_save*.bin");
            var list = new List<string>(files);
            list.Sort((a, b) => ParseSlot(a, romName).CompareTo(ParseSlot(b, romName)));
            return list;
        }

        private static int ParseSlot(string path, string romName)
        {
            string filename = Path.GetFileNameWithoutExtension(path);
            string prefix = romName + "_save";
            if (filename.StartsWith(prefix) && int.TryParse(filename.Substring(prefix.Length), out int n))
                return n;
            return 0;
        }

        public static string SavePath(string savesFolder, string romName, int slot)
            => Path.Combine(savesFolder, $"{romName}_save{slot}.bin");

        private static void WriteU32(BinaryWriter w, uint v)
        {
            w.Write((byte)( v        & 0xFF));
            w.Write((byte)((v >>  8) & 0xFF));
            w.Write((byte)((v >> 16) & 0xFF));
            w.Write((byte)((v >> 24) & 0xFF));
        }

        private static uint ReadU32(BinaryReader r)
        {
            uint b0 = r.ReadByte();
            uint b1 = r.ReadByte();
            uint b2 = r.ReadByte();
            uint b3 = r.ReadByte();
            return b0 | (b1 << 8) | (b2 << 16) | (b3 << 24);
        }
    }
}
