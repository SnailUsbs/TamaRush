using System;
using System.Diagnostics;

namespace TamaRush.TMEmulator
{
    public class TamaEmulator
    {
        public readonly bool[,] LcdMatrix = new bool[LCD_W, LCD_H];
        public readonly bool[]  LcdIcons  = new bool[ICON_NUM];
        public readonly object  Lock      = new object();
        public volatile bool    LcdDirty  = false;
        public volatile bool  BuzzerEnabled = false;
        public volatile float BuzzerFreqHz  = 0f;
        public int  LcdWriteCount    { get; private set; }
        public long StepCount        { get; private set; }
        public int  PC_Snapshot      { get; private set; }
        public int  IntCount         { get; private set; }
        public bool InterruptsEnabled => (_flags & FLAG_I) != 0;
        public const int LCD_W    = 32;
        public const int LCD_H    = 16;
        public const int ICON_NUM = 8;

        private const int TICK_FREQ   = 32768;
        private const int OSC1_FREQ   = 32768;
        private const int OSC3_FREQ   = 1000000;

        private const int TIMER_2HZ   = TICK_FREQ / 2;
        private const int TIMER_4HZ   = TICK_FREQ / 4;
        private const int TIMER_8HZ   = TICK_FREQ / 8;
        private const int TIMER_16HZ  = TICK_FREQ / 16;
        private const int TIMER_32HZ  = TICK_FREQ / 32;
        private const int TIMER_64HZ  = TICK_FREQ / 64;
        private const int TIMER_128HZ = TICK_FREQ / 128;
        private const int TIMER_256HZ = TICK_FREQ / 256;
        private const int MEM_RAM_ADDR    = 0x000;
        private const int MEM_RAM_SIZE    = 0x280;
        private const int MEM_DISP1_ADDR  = 0xE00;
        private const int MEM_DISP1_SIZE  = 0x050;
        private const int MEM_DISP2_ADDR  = 0xE80;
        private const int MEM_DISP2_SIZE  = 0x050;
        private const int MEM_IO_ADDR     = 0xF00;
        private const int MEM_IO_SIZE     = 0x080;
        private const int MEM_BUF_SIZE = (MEM_RAM_SIZE + MEM_DISP1_SIZE + MEM_DISP2_SIZE + MEM_IO_SIZE) / 2;
        private readonly byte[] _mem = new byte[MEM_BUF_SIZE];
        private const int REG_CLK_INT_FACTOR    = 0xF00;
        private const int REG_SW_INT_FACTOR     = 0xF01;
        private const int REG_PROG_INT_FACTOR   = 0xF02;
        private const int REG_SERIAL_INT_FACTOR = 0xF03;
        private const int REG_K00_INT_FACTOR    = 0xF04;
        private const int REG_K10_INT_FACTOR    = 0xF05;
        private const int REG_CLK_INT_MASK      = 0xF10;
        private const int REG_SW_INT_MASK       = 0xF11;
        private const int REG_PROG_INT_MASK     = 0xF12;
        private const int REG_SERIAL_INT_MASK   = 0xF13;
        private const int REG_K00_INT_MASK      = 0xF14;
        private const int REG_K10_INT_MASK      = 0xF15;
        private const int REG_CLK_TIMER_DATA1   = 0xF20;
        private const int REG_CLK_TIMER_DATA2   = 0xF21;
        private const int REG_PROG_TIMER_DATA_L = 0xF24;
        private const int REG_PROG_TIMER_DATA_H = 0xF25;
        private const int REG_PROG_TIMER_RLD_L  = 0xF26;
        private const int REG_PROG_TIMER_RLD_H  = 0xF27;
        private const int REG_K00_INPUT_PORT    = 0xF40;
        private const int REG_K00_INPUT_REL     = 0xF41;
        private const int REG_K10_INPUT_PORT    = 0xF42;
        private const int REG_CPU_OSC3_CTRL     = 0xF70;
        private const int REG_LCD_CTRL          = 0xF71;
        private const int REG_SVD_CTRL          = 0xF73;
        private const int REG_BUZZER_CTRL1      = 0xF74;
        private const int REG_BUZZER_CTRL2      = 0xF75;
        private const int REG_PROG_TIMER_CTRL   = 0xF78;
        private const int REG_PROG_TIMER_CLK    = 0xF79;
        private const int REG_R40_BZ_OUTPUT     = 0xF54;
        private int  _pc, _nextPc;
        private int  _x, _y;
        private byte _a, _b;
        private byte _np;
        private byte _sp;
        private byte _flags;
        private const byte FLAG_C = 0x1;
        private const byte FLAG_Z = 0x2;
        private const byte FLAG_D = 0x4;
        private const byte FLAG_I = 0x8;
        private const int INT_PROG   = 0;
        private const int INT_SERIAL = 1;
        private const int INT_K10    = 2;
        private const int INT_K00    = 3;
        private const int INT_SW     = 4;
        private const int INT_CLK    = 5;
        private const int INT_NUM    = 6;

        private readonly byte[] _intFactor    = new byte[INT_NUM];
        private readonly byte[] _intMask      = new byte[INT_NUM];
        private readonly bool[] _intTriggered = new bool[INT_NUM];
        private readonly byte[] _intVector    = { 0x0C, 0x0A, 0x08, 0x06, 0x04, 0x02 };
        private uint _tickCounter;
        private uint _clk2hz, _clk4hz, _clk8hz, _clk16hz, _clk32hz, _clk64hz, _clk128hz, _clk256hz;
        private uint _progTimerTs;
        private bool _progTimerEnabled;
        private byte _progTimerData, _progTimerRld;
        private long   _refTicks;
        private uint   _scaledCycleAcc;
        private int    _cpuFreq = OSC1_FREQ;
        private byte   _prevCycles;
        private bool   _cpuHalted;
        private byte _inputK0 = 0xF;
        private byte _inputK1 = 0xF;
        private ushort[] _rom;
        private static readonly byte[] SegPos = {
            0,1,2,3,4,5,6,7,32,8,9,10,11,12,13,14,15,33,34,35,
            31,30,29,28,27,26,25,24,36,23,22,21,20,19,18,17,16,37,38,39
        };
        private static readonly float[] _buzzerFreqTable = {
            4096.0f, 3276.8f, 2730.7f, 2340.6f, 2048.0f, 1638.4f, 1365.3f, 1170.3f
        };
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        public int  PC    { get => _pc;     set => _pc    = value; }
        public int  X     { get => _x;      set => _x     = value; }
        public int  Y     { get => _y;      set => _y     = value; }
        public byte A     { get => _a;      set => _a     = (byte)(value & 0xF); }
        public byte B     { get => _b;      set => _b     = (byte)(value & 0xF); }
        public byte NP    { get => _np;     set => _np    = (byte)(value & 0x1F); }
        public byte SP    { get => _sp;     set => _sp    = value; }
        public byte Flags { get => _flags;  set => _flags = (byte)(value & 0xF); }

        public uint TickCounter      { get => _tickCounter;      set => _tickCounter      = value; }
        public uint Clk2Hz           { get => _clk2hz;           set => _clk2hz           = value; }
        public uint Clk4Hz           { get => _clk4hz;           set => _clk4hz           = value; }
        public uint Clk8Hz           { get => _clk8hz;           set => _clk8hz           = value; }
        public uint Clk16Hz          { get => _clk16hz;          set => _clk16hz          = value; }
        public uint Clk32Hz          { get => _clk32hz;          set => _clk32hz          = value; }
        public uint Clk64Hz          { get => _clk64hz;          set => _clk64hz          = value; }
        public uint Clk128Hz         { get => _clk128hz;         set => _clk128hz         = value; }
        public uint Clk256Hz         { get => _clk256hz;         set => _clk256hz         = value; }
        public uint ProgTimerTs      { get => _progTimerTs;      set => _progTimerTs      = value; }
        public bool ProgTimerEnabled { get => _progTimerEnabled; set => _progTimerEnabled = value; }
        public byte ProgTimerData    { get => _progTimerData;    set => _progTimerData    = value; }
        public byte ProgTimerRld     { get => _progTimerRld;     set => _progTimerRld     = value; }

        public byte[] IntFactor    => _intFactor;
        public byte[] IntMask      => _intMask;
        public bool[] IntTriggered => _intTriggered;

        public byte GetRamNibble(int i)          => GetNibble(RamIdx(i), i);
        public void SetRamNibble(int i, byte v)  => SetNibble(RamIdx(i), i, v);
        public byte GetIONibble(int i)           => GetIOMem(MEM_IO_ADDR + i);
        public void SetIONibble(int i, byte v)   => SetIOMem(MEM_IO_ADDR + i, v);

        public void Init(ushort[] rom)
        {
            _rom = rom;
            Reset();
        }

        public void Reset()
        {
            Array.Clear(_mem, 0, _mem.Length);
            Array.Clear(_intFactor, 0, INT_NUM);
            Array.Clear(_intMask, 0, INT_NUM);
            Array.Clear(_intTriggered, 0, INT_NUM);

            _pc    = ToPC(0, 1, 0x00);
            _np    = ToNP(0, 1);
            _a = _b = 0; _x = _y = 0; _sp = 0; _flags = 0;
            _tickCounter = 0;
            _clk2hz = _clk4hz = _clk8hz = _clk16hz = 0;
            _clk32hz = _clk64hz = _clk128hz = _clk256hz = 0;
            _progTimerTs = 0; _progTimerEnabled = false;
            _progTimerData = _progTimerRld = 0;
            _cpuHalted = false; _cpuFreq = OSC1_FREQ;
            _scaledCycleAcc = 0; _prevCycles = 0;

            SetIOMem(REG_LCD_CTRL,       0x8);
            SetIOMem(REG_K00_INPUT_REL,  0xF);
            SetIOMem(REG_R40_BZ_OUTPUT,  0xF);

            _inputK0 = 0xF;
            _inputK1 = 0xF;

            _refTicks = _sw.ElapsedTicks;
        }
        public void Step(int speedMultiplier = 1)
        {
            int opIdx = 12;

            if (!_cpuHalted)
            {
                int romIdx = _pc & 0x1FFF;
                ushort op = romIdx < _rom.Length ? _rom[romIdx] : (ushort)0xFFB;

                opIdx = FindOp(op);
                if (opIdx < 0) opIdx = 12;

                _nextPc = (_pc + 1) & 0x1FFF;

                _refTicks = WaitForCycles(_refTicks, _prevCycles, speedMultiplier);

                ExecuteOp(opIdx, op);

                _pc = _nextPc;
                _prevCycles = _opCycles[opIdx];

                if (opIdx != 0)
                    _np = (byte)((_pc >> 8) & 0x1F);
            }
            else
            {
                _refTicks = WaitForCycles(_refTicks, 5, speedMultiplier);
                _prevCycles = 0;
                opIdx = -1;
            }

            HandleTimers();

            if ((_flags & FLAG_I) != 0 && opIdx != 0 && opIdx != 58)
                ProcessInterrupts(speedMultiplier);

            StepCount++;
            PC_Snapshot = _pc;
        }

        public void SetButton(TamaButton btn, bool pressed)
        {
            byte pinState = pressed ? (byte)0 : (byte)1;
            switch (btn)
            {
                case TamaButton.Left:   SetInputPin(0, 0, pinState); break;
                case TamaButton.Middle: SetInputPin(0, 1, pinState); break;
                case TamaButton.Right:  SetInputPin(0, 2, pinState); break;
                case TamaButton.Tap:    SetInputPin(0, 3, pinState); break;
            }
        }

        private int RamIdx(int n)   => (n - MEM_RAM_ADDR)   / 2;
        private int Disp1Idx(int n) => (n - MEM_DISP1_ADDR + MEM_RAM_SIZE) / 2;
        private int Disp2Idx(int n) => (n - MEM_DISP2_ADDR + MEM_RAM_SIZE + MEM_DISP1_SIZE) / 2;
        private int IOIdx(int n)    => (n - MEM_IO_ADDR     + MEM_RAM_SIZE + MEM_DISP1_SIZE + MEM_DISP2_SIZE) / 2;

        private byte GetNibble(int bufIdx, int addr)
        {
            int shift = (addr & 1) << 2;
            return (byte)((_mem[bufIdx] >> shift) & 0xF);
        }

        private void SetNibble(int bufIdx, int addr, byte v)
        {
            int shift = (addr & 1) << 2;
            _mem[bufIdx] = (byte)((_mem[bufIdx] & ~(0xF << shift)) | ((v & 0xF) << shift));
        }

        private byte GetMem(int n)
        {
            if (n < MEM_RAM_SIZE)                                          return GetNibble(RamIdx(n), n);
            if (n >= MEM_DISP1_ADDR && n < MEM_DISP1_ADDR + MEM_DISP1_SIZE) return GetNibble(Disp1Idx(n), n);
            if (n >= MEM_DISP2_ADDR && n < MEM_DISP2_ADDR + MEM_DISP2_SIZE) return GetNibble(Disp2Idx(n), n);
            if (n >= MEM_IO_ADDR    && n < MEM_IO_ADDR    + MEM_IO_SIZE)    return GetIO(n);
            return 0;
        }

        private void SetMem(int n, byte v)
        {
            if (n < MEM_RAM_SIZE)                                          { SetNibble(RamIdx(n), n, v); return; }
            if (n >= MEM_DISP1_ADDR && n < MEM_DISP1_ADDR + MEM_DISP1_SIZE) { SetNibble(Disp1Idx(n), n, v); SetLcd(n, v); return; }
            if (n >= MEM_DISP2_ADDR && n < MEM_DISP2_ADDR + MEM_DISP2_SIZE) { SetNibble(Disp2Idx(n), n, v); SetLcd(n, v); return; }
            if (n >= MEM_IO_ADDR    && n < MEM_IO_ADDR    + MEM_IO_SIZE)    { SetNibble(IOIdx(n), n, v); SetIO(n, v); }
        }

        private byte GetIOMem(int n) => GetNibble(IOIdx(n), n);
        private void SetIOMem(int n, byte v) => SetNibble(IOIdx(n), n, v);

        private byte GetIO(int n)
        {
            byte tmp;
            switch (n)
            {
                case REG_CLK_INT_FACTOR:    tmp = _intFactor[INT_CLK];    _intFactor[INT_CLK]    = 0; return tmp;
                case REG_SW_INT_FACTOR:     tmp = _intFactor[INT_SW];     _intFactor[INT_SW]     = 0; return tmp;
                case REG_PROG_INT_FACTOR:   tmp = _intFactor[INT_PROG];   _intFactor[INT_PROG]   = 0; return tmp;
                case REG_SERIAL_INT_FACTOR: tmp = _intFactor[INT_SERIAL]; _intFactor[INT_SERIAL] = 0; return tmp;
                case REG_K00_INT_FACTOR:    tmp = _intFactor[INT_K00];    _intFactor[INT_K00]    = 0; return tmp;
                case REG_K10_INT_FACTOR:    tmp = _intFactor[INT_K10];    _intFactor[INT_K10]    = 0; return tmp;
                case REG_CLK_INT_MASK:      return _intMask[INT_CLK];
                case REG_SW_INT_MASK:       return (byte)(_intMask[INT_SW] & 0x3);
                case REG_PROG_INT_MASK:     return (byte)(_intMask[INT_PROG] & 0x1);
                case REG_SERIAL_INT_MASK:   return (byte)(_intMask[INT_SERIAL] & 0x1);
                case REG_K00_INT_MASK:      return _intMask[INT_K00];
                case REG_K10_INT_MASK:      return _intMask[INT_K10];
                case REG_CLK_TIMER_DATA1:   return GetIOMem(n);
                case REG_CLK_TIMER_DATA2:   return GetIOMem(n);
                case REG_PROG_TIMER_DATA_L: return (byte)(_progTimerData & 0xF);
                case REG_PROG_TIMER_DATA_H: return (byte)((_progTimerData >> 4) & 0xF);
                case REG_PROG_TIMER_RLD_L:  return (byte)(_progTimerRld & 0xF);
                case REG_PROG_TIMER_RLD_H:  return (byte)((_progTimerRld >> 4) & 0xF);
                case REG_K00_INPUT_PORT:    return _inputK0;
                case REG_K00_INPUT_REL:     return GetIOMem(n);
                case REG_K10_INPUT_PORT:    return _inputK1;
                case REG_CPU_OSC3_CTRL:     return GetIOMem(n);
                case REG_LCD_CTRL:          return GetIOMem(n);
                case REG_SVD_CTRL:          return (byte)(GetIOMem(n) & 0x7);
                case REG_PROG_TIMER_CTRL:   return (byte)(_progTimerEnabled ? 1 : 0);
                default:                    return 0;
            }
        }

        private void SetIO(int n, byte v)
        {
            switch (n)
            {
                case REG_CLK_INT_MASK:    _intMask[INT_CLK]    = v; break;
                case REG_SW_INT_MASK:     _intMask[INT_SW]     = v; break;
                case REG_PROG_INT_MASK:   _intMask[INT_PROG]   = v; break;
                case REG_SERIAL_INT_MASK: _intMask[INT_SERIAL] = v; break;
                case REG_K00_INT_MASK:    _intMask[INT_K00]    = v; break;
                case REG_K10_INT_MASK:    _intMask[INT_K10]    = v; break;
                case REG_PROG_TIMER_RLD_L: _progTimerRld = (byte)((_progTimerRld & 0xF0) | (v & 0xF)); break;
                case REG_PROG_TIMER_RLD_H: _progTimerRld = (byte)((_progTimerRld & 0x0F) | ((v & 0xF) << 4)); break;
                case REG_CPU_OSC3_CTRL:
                    if ((v & 0x8) != 0 && _cpuFreq != OSC3_FREQ) { _cpuFreq = OSC3_FREQ; _scaledCycleAcc = 0; }
                    if ((v & 0x8) == 0 && _cpuFreq != OSC1_FREQ) { _cpuFreq = OSC1_FREQ; _scaledCycleAcc = 0; }
                    break;
                case REG_R40_BZ_OUTPUT:
                    BuzzerEnabled = (v & 0x8) == 0;
                    break;
                case REG_BUZZER_CTRL1:
                    BuzzerFreqHz = _buzzerFreqTable[v & 0x7];
                    break;
                case REG_PROG_TIMER_CTRL:                    if ((v & 0x2) != 0) _progTimerData = _progTimerRld;
                    if ((v & 0x1) != 0 && !_progTimerEnabled) _progTimerTs = _tickCounter;
                    _progTimerEnabled = (v & 0x1) != 0;
                    break;
            }
        }

        private void SetLcd(int n, byte v)
        {
            LcdWriteCount++;
            int seg  = ((n & 0x7F) >> 1);
            int com0 = (((n & 0x80) >> 7) * 8 + (n & 0x1) * 4);
            for (int i = 0; i < 4; i++)
                SetLcdPin(seg, com0 + i, (byte)((v >> i) & 0x1));
        }

        private void SetLcdPin(int seg, int com, byte val)
        {
            if (seg >= SegPos.Length) return;
            int mapped = SegPos[seg];
            if (mapped < LCD_W)
            {
                lock (Lock) { LcdMatrix[mapped, com] = val != 0; LcdDirty = true; }
            }
            else
            {
                if (seg == 8 && com < 4)
                    lock (Lock) { LcdIcons[com] = val != 0; LcdDirty = true; }
                else if (seg == 28 && com >= 12)
                    lock (Lock) { LcdIcons[com - 8] = val != 0; LcdDirty = true; }
            }
        }

        private void SetInputPin(int port, int bit, byte state)
        {
            if (port == 0)
            {
                byte old = (byte)((_inputK0 >> bit) & 0x1);
                if (state != old)
                {
                    byte rel = GetIOMem(REG_K00_INPUT_REL);
                    if (state != ((rel >> bit) & 0x1))
                        GenerateInterrupt(INT_K00, bit);
                }
                _inputK0 = (byte)((_inputK0 & ~(1 << bit)) | (state << bit));
            }
            else
            {
                byte old = (byte)((_inputK1 >> bit) & 0x1);
                if (state != old)
                {
                    if (state == 0)
                        GenerateInterrupt(INT_K10, bit);
                }
                _inputK1 = (byte)((_inputK1 & ~(1 << bit)) | (state << bit));
            }
        }

        private void GenerateInterrupt(int slot, int bit)
        {
            _intFactor[slot] |= (byte)(1 << bit);
            if ((_intMask[slot] & (1 << bit)) != 0)
                _intTriggered[slot] = true;
        }

        private void HandleTimers()
        {
            TickTimer(ref _clk2hz,   TIMER_2HZ,   () => {
                SetIOMem(REG_CLK_TIMER_DATA2, (byte)(GetIOMem(REG_CLK_TIMER_DATA2) ^ 0x8));
                if ((GetIOMem(REG_CLK_TIMER_DATA2) & 0x8) == 0) GenerateInterrupt(INT_CLK, 3);
            });
            TickTimer(ref _clk4hz,   TIMER_4HZ,   () => {
                SetIOMem(REG_CLK_TIMER_DATA2, (byte)(GetIOMem(REG_CLK_TIMER_DATA2) ^ 0x4));
                if ((GetIOMem(REG_CLK_TIMER_DATA2) & 0x4) == 0) GenerateInterrupt(INT_CLK, 2);
            });
            TickTimer(ref _clk8hz,   TIMER_8HZ,   () => {
                SetIOMem(REG_CLK_TIMER_DATA2, (byte)(GetIOMem(REG_CLK_TIMER_DATA2) ^ 0x2));
            });
            TickTimer(ref _clk16hz,  TIMER_16HZ,  () => {
                SetIOMem(REG_CLK_TIMER_DATA2, (byte)(GetIOMem(REG_CLK_TIMER_DATA2) ^ 0x1));
                if ((GetIOMem(REG_CLK_TIMER_DATA2) & 0x1) == 0) GenerateInterrupt(INT_CLK, 1);
            });
            TickTimer(ref _clk32hz,  TIMER_32HZ,  () => {
                SetIOMem(REG_CLK_TIMER_DATA1, (byte)(GetIOMem(REG_CLK_TIMER_DATA1) ^ 0x8));
            });
            TickTimer(ref _clk64hz,  TIMER_64HZ,  () => {
                SetIOMem(REG_CLK_TIMER_DATA1, (byte)(GetIOMem(REG_CLK_TIMER_DATA1) ^ 0x4));
                if ((GetIOMem(REG_CLK_TIMER_DATA1) & 0x4) == 0) GenerateInterrupt(INT_CLK, 0);
            });
            TickTimer(ref _clk128hz, TIMER_128HZ, () => {
                SetIOMem(REG_CLK_TIMER_DATA1, (byte)(GetIOMem(REG_CLK_TIMER_DATA1) ^ 0x2));
            });
            TickTimer(ref _clk256hz, TIMER_256HZ, () => {
                SetIOMem(REG_CLK_TIMER_DATA1, (byte)(GetIOMem(REG_CLK_TIMER_DATA1) ^ 0x1));
            });

            if (_progTimerEnabled)
            {
                while (_tickCounter - _progTimerTs >= TIMER_256HZ)
                {
                    _progTimerTs += TIMER_256HZ;
                    _progTimerData--;
                    if (_progTimerData == 0)
                    {
                        _progTimerData = _progTimerRld;
                        GenerateInterrupt(INT_PROG, 0);
                    }
                }
            }
        }

        private void TickTimer(ref uint ts, uint period, Action onTick)
        {
            if (_tickCounter - ts >= period)
            {
                do { ts += period; } while (_tickCounter - ts >= period);
                onTick();
            }
        }

        private void ProcessInterrupts(int speedMultiplier = 1)
        {
            for (int i = 0; i < INT_NUM; i++)
            {
                if (!_intTriggered[i]) continue;
                SetMem((_sp - 1) & 0xFF, PCP);
                SetMem((_sp - 2) & 0xFF, PCSH);
                SetMem((_sp - 3) & 0xFF, PCSL);
                _sp = (byte)((_sp - 3) & 0xFF);
                _flags &= unchecked((byte)~FLAG_I);
                _np = ToNP(NBP, 1);
                _pc = ToPC(PCB, 1, _intVector[i]);
                _cpuHalted = false;
                IntCount++;
                _refTicks = WaitForCycles(_refTicks, 12, speedMultiplier);
                _intTriggered[i] = false;
                return;
            }
        }

        private long GetTimestampUs() => _sw.ElapsedTicks * 1_000_000L / Stopwatch.Frequency;

        private long WaitForCycles(long sinceTicks, byte cycles, int speedMultiplier = 1)
        {
            if (cycles == 0) return sinceTicks;

            _scaledCycleAcc += (uint)cycles * TICK_FREQ;
            uint ticksPending = _scaledCycleAcc / (uint)_cpuFreq;
            if (ticksPending > 0)
            {
                _tickCounter += ticksPending;
                _scaledCycleAcc -= ticksPending * (uint)_cpuFreq;
            }

            int speed = speedMultiplier > 0 ? speedMultiplier : 1;
            long deadlineTicks = sinceTicks + (long)cycles * Stopwatch.Frequency / (_cpuFreq * speed);

            if (speedMultiplier == 0)
                return _sw.ElapsedTicks;

            long remainingTicks = deadlineTicks - _sw.ElapsedTicks;
            if (remainingTicks > 0)
            {
                long remainingMs = remainingTicks * 1000L / Stopwatch.Frequency;
                if (remainingMs > 1)
                    System.Threading.Thread.Sleep((int)(remainingMs - 1));
                while (_sw.ElapsedTicks < deadlineTicks) { }
            }

            return deadlineTicks;
        }

        private byte PCS  => (byte)(_pc & 0xFF);
        private byte PCSL => (byte)(_pc & 0xF);
        private byte PCSH => (byte)((_pc >> 4) & 0xF);
        private byte PCP  => (byte)((_pc >> 8) & 0xF);
        private byte PCB  => (byte)((_pc >> 12) & 0x1);
        private byte NBP  => (byte)((_np >> 4) & 0x1);
        private byte NPP  => (byte)(_np & 0xF);
        private byte XHL  => (byte)(_x & 0xFF);
        private byte XL   => (byte)(_x & 0xF);
        private byte XH   => (byte)((_x >> 4) & 0xF);
        private byte XP   => (byte)((_x >> 8) & 0xF);
        private byte YHL  => (byte)(_y & 0xFF);
        private byte YL   => (byte)(_y & 0xF);
        private byte YH   => (byte)((_y >> 4) & 0xF);
        private byte YP   => (byte)((_y >> 8) & 0xF);
        private byte SPL  => (byte)(_sp & 0xF);
        private byte SPH  => (byte)((_sp >> 4) & 0xF);

        private bool C => (_flags & FLAG_C) != 0;
        private bool Z => (_flags & FLAG_Z) != 0;
        private bool D => (_flags & FLAG_D) != 0;

        private static int  ToPC(int bank, int page, int step) => (step & 0xFF) | ((page & 0xF) << 8) | ((bank & 0x1) << 12);
        private static byte ToNP(int bank, int page)           => (byte)((page & 0xF) | ((bank & 0x1) << 4));

        private byte GetRQ(int rq)
        {
            switch (rq & 0x3)
            {
                case 0: return _a;
                case 1: return _b;
                case 2: return GetMem(_x);
                case 3: return GetMem(_y);
            }
            return 0;
        }

        private void SetRQ(int rq, byte v)
        {
            switch (rq & 0x3)
            {
                case 0: _a = (byte)(v & 0xF); break;
                case 1: _b = (byte)(v & 0xF); break;
                case 2: SetMem(_x, v); break;
                case 3: SetMem(_y, v); break;
            }
        }
        private static readonly (int code, int mask, int shiftArg0, int maskArg0, byte cycles)[] _ops =
        {
            (0xE40, 0xFE0, 0, 0,     5),
            (0x000, 0xF00, 0, 0,     5),  // 1  JP
            (0x200, 0xF00, 0, 0,     5),  // 2  JP_C
            (0x300, 0xF00, 0, 0,     5),  // 3  JP_NC
            (0x600, 0xF00, 0, 0,     5),  // 4  JP_Z
            (0x700, 0xF00, 0, 0,     5),  // 5  JP_NZ
            (0xFE8, 0xFFF, 0, 0,     5),  // 6  JPBA
            (0x400, 0xF00, 0, 0,     7),  // 7  CALL
            (0x500, 0xF00, 0, 0,     7),  // 8  CALZ
            (0xFDF, 0xFFF, 0, 0,     7),  // 9  RET
            (0xFDE, 0xFFF, 0, 0,    12),  // 10 RETS
            (0x100, 0xF00, 0, 0,    12),  // 11 RETD
            (0xFFB, 0xFFF, 0, 0,     5),  // 12 NOP5
            (0xFFF, 0xFFF, 0, 0,     7),  // 13 NOP7
            (0xFF8, 0xFFF, 0, 0,     5),  // 14 HALT
            (0xEE0, 0xFFF, 0, 0,     5),  // 15 INC_X
            (0xEF0, 0xFFF, 0, 0,     5),  // 16 INC_Y
            (0xB00, 0xF00, 0, 0,     5),  // 17 LD_X
            (0x800, 0xF00, 0, 0,     5),  // 18 LD_Y
            (0xE80, 0xFFC, 0, 0,     5),  // 19 LD_XP_R
            (0xE84, 0xFFC, 0, 0,     5),  // 20 LD_XH_R
            (0xE88, 0xFFC, 0, 0,     5),  // 21 LD_XL_R
            (0xE90, 0xFFC, 0, 0,     5),  // 22 LD_YP_R
            (0xE94, 0xFFC, 0, 0,     5),  // 23 LD_YH_R
            (0xE98, 0xFFC, 0, 0,     5),  // 24 LD_YL_R
            (0xEA0, 0xFFC, 0, 0,     5),  // 25 LD_R_XP
            (0xEA4, 0xFFC, 0, 0,     5),  // 26 LD_R_XH
            (0xEA8, 0xFFC, 0, 0,     5),  // 27 LD_R_XL
            (0xEB0, 0xFFC, 0, 0,     5),  // 28 LD_R_YP
            (0xEB4, 0xFFC, 0, 0,     5),  // 29 LD_R_YH
            (0xEB8, 0xFFC, 0, 0,     5),  // 30 LD_R_YL
            (0xA00, 0xFF0, 0, 0,     7),  // 31 ADC_XH
            (0xA10, 0xFF0, 0, 0,     7),  // 32 ADC_XL
            (0xA20, 0xFF0, 0, 0,     7),  // 33 ADC_YH
            (0xA30, 0xFF0, 0, 0,     7),  // 34 ADC_YL
            (0xA40, 0xFF0, 0, 0,     7),  // 35 CP_XH
            (0xA50, 0xFF0, 0, 0,     7),  // 36 CP_XL
            (0xA60, 0xFF0, 0, 0,     7),  // 37 CP_YH
            (0xA70, 0xFF0, 0, 0,     7),  // 38 CP_YL
            (0xE00, 0xFC0, 4, 0x030, 5),  // 39 LD_R_I
            (0xEC0, 0xFF0, 2, 0x00C, 5),  // 40 LD_R_Q
            (0xFA0, 0xFF0, 0, 0,     5),  // 41 LD_A_MN
            (0xFB0, 0xFF0, 0, 0,     5),  // 42 LD_B_MN
            (0xF80, 0xFF0, 0, 0,     5),  // 43 LD_MN_A
            (0xF90, 0xFF0, 0, 0,     5),  // 44 LD_MN_B
            (0xE60, 0xFF0, 0, 0,     5),  // 45 LDPX_MX
            (0xEE0, 0xFF0, 2, 0x00C, 5),  // 46 LDPX_R
            (0xE70, 0xFF0, 0, 0,     5),  // 47 LDPY_MY
            (0xEF0, 0xFF0, 2, 0x00C, 5),  // 48 LDPY_R
            (0x900, 0xF00, 0, 0,     5),  // 49 LBPX
            (0xF40, 0xFF0, 0, 0,     7),  // 50 SET
            (0xF50, 0xFF0, 0, 0,     7),  // 51 RST
            (0xF41, 0xFFF, 0, 0,     7),  // 52 SCF
            (0xF5E, 0xFFF, 0, 0,     7),  // 53 RCF
            (0xF42, 0xFFF, 0, 0,     7),  // 54 SZF
            (0xF5D, 0xFFF, 0, 0,     7),  // 55 RZF
            (0xF44, 0xFFF, 0, 0,     7),  // 56 SDF
            (0xF5B, 0xFFF, 0, 0,     7),  // 57 RDF
            (0xF48, 0xFFF, 0, 0,     7),  // 58 EI
            (0xF57, 0xFFF, 0, 0,     7),  // 59 DI
            (0xFDB, 0xFFF, 0, 0,     5),  // 60 INC_SP
            (0xFCB, 0xFFF, 0, 0,     5),  // 61 DEC_SP
            (0xFC0, 0xFFC, 0, 0,     5),  // 62 PUSH_R
            (0xFC4, 0xFFF, 0, 0,     5),  // 63 PUSH_XP
            (0xFC5, 0xFFF, 0, 0,     5),  // 64 PUSH_XH
            (0xFC6, 0xFFF, 0, 0,     5),  // 65 PUSH_XL
            (0xFC7, 0xFFF, 0, 0,     5),  // 66 PUSH_YP
            (0xFC8, 0xFFF, 0, 0,     5),  // 67 PUSH_YH
            (0xFC9, 0xFFF, 0, 0,     5),  // 68 PUSH_YL
            (0xFCA, 0xFFF, 0, 0,     5),  // 69 PUSH_F
            (0xFD0, 0xFFC, 0, 0,     5),  // 70 POP_R
            (0xFD4, 0xFFF, 0, 0,     5),  // 71 POP_XP
            (0xFD5, 0xFFF, 0, 0,     5),  // 72 POP_XH
            (0xFD6, 0xFFF, 0, 0,     5),  // 73 POP_XL
            (0xFD7, 0xFFF, 0, 0,     5),  // 74 POP_YP
            (0xFD8, 0xFFF, 0, 0,     5),  // 75 POP_YH
            (0xFD9, 0xFFF, 0, 0,     5),  // 76 POP_YL
            (0xFDA, 0xFFF, 0, 0,     5),  // 77 POP_F
            (0xFE0, 0xFFC, 0, 0,     5),  // 78 LD_SPH_R
            (0xFF0, 0xFFC, 0, 0,     5),  // 79 LD_SPL_R
            (0xFE4, 0xFFC, 0, 0,     5),  // 80 LD_R_SPH
            (0xFF4, 0xFFC, 0, 0,     5),  // 81 LD_R_SPL
            (0xC00, 0xFC0, 4, 0x030, 7),  // 82 ADD_R_I
            (0xA80, 0xFF0, 2, 0x00C, 7),  // 83 ADD_R_Q
            (0xC40, 0xFC0, 4, 0x030, 7),  // 84 ADC_R_I
            (0xA90, 0xFF0, 2, 0x00C, 7),  // 85 ADC_R_Q
            (0xAA0, 0xFF0, 2, 0x00C, 7),  // 86 SUB
            (0xD40, 0xFC0, 4, 0x030, 7),  // 87 SBC_R_I
            (0xAB0, 0xFF0, 2, 0x00C, 7),  // 88 SBC_R_Q
            (0xC80, 0xFC0, 4, 0x030, 7),  // 89 AND_R_I
            (0xAC0, 0xFF0, 2, 0x00C, 7),  // 90 AND_R_Q
            (0xCC0, 0xFC0, 4, 0x030, 7),  // 91 OR_R_I
            (0xAD0, 0xFF0, 2, 0x00C, 7),  // 92 OR_R_Q
            (0xD00, 0xFC0, 4, 0x030, 7),  // 93 XOR_R_I
            (0xAE0, 0xFF0, 2, 0x00C, 7),  // 94 XOR_R_Q
            (0xDC0, 0xFC0, 4, 0x030, 7),  // 95 CP_R_I
            (0xF00, 0xFF0, 2, 0x00C, 7),  // 96 CP_R_Q
            (0xD80, 0xFC0, 4, 0x030, 7),  // 97 FAN_R_I
            (0xF10, 0xFF0, 2, 0x00C, 7),  // 98 FAN_R_Q
            (0xAF0, 0xFF0, 0, 0,     7),  // 99 RLC
            (0xE8C, 0xFFC, 0, 0,     5),  // 100 RRC
            (0xF60, 0xFF0, 0, 0,     7),  // 101 INC_MN
            (0xF70, 0xFF0, 0, 0,     7),  // 102 DEC_MN
            (0xF28, 0xFFC, 0, 0,     7),  // 103 ACPX
            (0xF2C, 0xFFC, 0, 0,     7),  // 104 ACPY
            (0xF38, 0xFFC, 0, 0,     7),  // 105 SCPX
            (0xF3C, 0xFFC, 0, 0,     7),  // 106 SCPY
            (0xD0F, 0xFCF, 4, 0,     7),  // 107 NOT
        };

        private static readonly byte[] _opCycles;

        static TamaEmulator()
        {
            _opCycles = new byte[_ops.Length];
            for (int i = 0; i < _ops.Length; i++)
                _opCycles[i] = _ops[i].cycles;
        }

        private int FindOp(ushort op)
        {
            for (int i = 0; i < _ops.Length; i++)
                if ((op & _ops[i].mask) == _ops[i].code) return i;
            return -1;
        }

        private void ExecuteOp(int opIdx, int op)
        {
            var entry = _ops[opIdx];
            int arg0 = entry.maskArg0 != 0
                ? (op & entry.maskArg0) >> entry.shiftArg0
                : (op & ~entry.mask) >> entry.shiftArg0;
            int arg1 = entry.maskArg0 != 0 ? op & ~(entry.mask | entry.maskArg0) : 0;

            switch (opIdx)
            {
                case 0:  _np = (byte)(arg0 & 0x1F); break;
                case 1:  _nextPc = (op & 0xFF) | (_np << 8); break;
                case 2:  if (C) _nextPc = (op & 0xFF) | (_np << 8); break;
                case 3:  if (!C) _nextPc = (op & 0xFF) | (_np << 8); break;
                case 4:  if (Z) _nextPc = (op & 0xFF) | (_np << 8); break;
                case 5:  if (!Z) _nextPc = (op & 0xFF) | (_np << 8); break;
                case 6:  _nextPc = _a | (_b << 4) | (_np << 8); break;
                case 7:
                {
                    int pc1 = (_pc + 1) & 0x1FFF;
                    SetMem((_sp - 1) & 0xFF, (byte)((pc1 >> 8) & 0xF));
                    SetMem((_sp - 2) & 0xFF, (byte)((pc1 >> 4) & 0xF));
                    SetMem((_sp - 3) & 0xFF, (byte)(pc1 & 0xF));
                    _sp = (byte)((_sp - 3) & 0xFF);
                    _nextPc = ToPC(PCB, NPP, op & 0xFF);
                    break;
                }
                case 8:
                {
                    int pc1 = (_pc + 1) & 0x1FFF;
                    SetMem((_sp - 1) & 0xFF, (byte)((pc1 >> 8) & 0xF));
                    SetMem((_sp - 2) & 0xFF, (byte)((pc1 >> 4) & 0xF));
                    SetMem((_sp - 3) & 0xFF, (byte)(pc1 & 0xF));
                    _sp = (byte)((_sp - 3) & 0xFF);
                    _nextPc = ToPC(PCB, 0, op & 0xFF);
                    break;
                }
                case 9:
                    _nextPc = GetMem(_sp) | (GetMem((_sp + 1) & 0xFF) << 4) | (GetMem((_sp + 2) & 0xFF) << 8) | (PCB << 12);
                    _sp = (byte)((_sp + 3) & 0xFF);
                    break;
                case 10:
                    _nextPc = GetMem(_sp) | (GetMem((_sp + 1) & 0xFF) << 4) | (GetMem((_sp + 2) & 0xFF) << 8) | (PCB << 12);
                    _sp = (byte)((_sp + 3) & 0xFF);
                    _nextPc = (_nextPc + 1) & 0x1FFF;
                    break;
                case 11:
                {
                    _nextPc = GetMem(_sp) | (GetMem((_sp + 1) & 0xFF) << 4) | (GetMem((_sp + 2) & 0xFF) << 8) | (PCB << 12);
                    _sp = (byte)((_sp + 3) & 0xFF);
                    byte imm = (byte)(op & 0xFF);
                    SetMem(_x, (byte)(imm & 0xF));
                    SetMem(((_x + 1) & 0xFF) | (XP << 8), (byte)((imm >> 4) & 0xF));
                    _x = ((_x + 2) & 0xFF) | (XP << 8);
                    break;
                }
                case 12: break;
                case 13: break;
                case 14: _cpuHalted = true; break;
                case 15: _x = ((_x + 1) & 0xFF) | (XP << 8); break;
                case 16: _y = ((_y + 1) & 0xFF) | (YP << 8); break;
                case 17: _x = (op & 0xFF) | (XP << 8); break;
                case 18: _y = (op & 0xFF) | (YP << 8); break;
                case 19: _x = XHL | (GetRQ(op & 0x3) << 8); break;
                case 20: _x = XL | (GetRQ(op & 0x3) << 4) | (XP << 8); break;
                case 21: _x = GetRQ(op & 0x3) | (XH << 4) | (XP << 8); break;
                case 22: _y = YHL | (GetRQ(op & 0x3) << 8); break;
                case 23: _y = YL | (GetRQ(op & 0x3) << 4) | (YP << 8); break;
                case 24: _y = GetRQ(op & 0x3) | (YH << 4) | (YP << 8); break;
                case 25: SetRQ(op & 0x3, XP); break;
                case 26: SetRQ(op & 0x3, XH); break;
                case 27: SetRQ(op & 0x3, XL); break;
                case 28: SetRQ(op & 0x3, YP); break;
                case 29: SetRQ(op & 0x3, YH); break;
                case 30: SetRQ(op & 0x3, YL); break;
                case 31:
                {
                    int tmp = XH + (op & 0xF) + (C ? 1 : 0);
                    _x = XL | ((tmp & 0xF) << 4) | (XP << 8);
                    if ((tmp >> 4) != 0) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C);
                    if ((tmp & 0xF) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                }
                case 32:
                {
                    int tmp = XL + (op & 0xF) + (C ? 1 : 0);
                    _x = (tmp & 0xF) | (XH << 4) | (XP << 8);
                    if ((tmp >> 4) != 0) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C);
                    if ((tmp & 0xF) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                }
                case 33:
                {
                    int tmp = YH + (op & 0xF) + (C ? 1 : 0);
                    _y = YL | ((tmp & 0xF) << 4) | (YP << 8);
                    if ((tmp >> 4) != 0) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C);
                    if ((tmp & 0xF) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                }
                case 34:
                {
                    int tmp = YL + (op & 0xF) + (C ? 1 : 0);
                    _y = (tmp & 0xF) | (YH << 4) | (YP << 8);
                    if ((tmp >> 4) != 0) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C);
                    if ((tmp & 0xF) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                }
                case 35:
                {
                    int imm = op & 0xF;
                    if (XH < imm) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C);
                    if (XH == imm) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                }
                case 36:
                {
                    int imm = op & 0xF;
                    if (XL < imm) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C);
                    if (XL == imm) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                }
                case 37:
                {
                    int imm = op & 0xF;
                    if (YH < imm) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C);
                    if (YH == imm) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                }
                case 38:
                {
                    int imm = op & 0xF;
                    if (YL < imm) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C);
                    if (YL == imm) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                }
                case 39: SetRQ(arg0, (byte)(arg1 & 0xF)); break;
                case 40: SetRQ(arg0, GetRQ(arg1)); break;
                case 41: _a = (byte)(GetMem(op & 0xF) & 0xF); break;
                case 42: _b = (byte)(GetMem(op & 0xF) & 0xF); break;
                case 43: SetMem(op & 0xF, _a); break;
                case 44: SetMem(op & 0xF, _b); break;
                case 45:
                    SetMem(_x, (byte)(op & 0xF));
                    _x = ((_x + 1) & 0xFF) | (XP << 8);
                    break;
                case 46:
                    SetRQ(arg0, GetRQ(arg1));
                    _x = ((_x + 1) & 0xFF) | (XP << 8);
                    break;
                case 47:
                    SetMem(_y, (byte)(op & 0xF));
                    _y = ((_y + 1) & 0xFF) | (YP << 8);
                    break;
                case 48:
                    SetRQ(arg0, GetRQ(arg1));
                    _y = ((_y + 1) & 0xFF) | (YP << 8);
                    break;
                case 49:
                {
                    byte imm = (byte)(op & 0xFF);
                    SetMem(_x, (byte)(imm & 0xF));
                    SetMem(((_x + 1) & 0xFF) | (XP << 8), (byte)((imm >> 4) & 0xF));
                    _x = ((_x + 2) & 0xFF) | (XP << 8);
                    break;
                }
                case 50: _flags |= (byte)(op & 0xF); break;
                case 51: _flags &= (byte)(op & 0xF); break;
                case 52: _flags |= FLAG_C; break;
                case 53: _flags &= unchecked((byte)~FLAG_C); break;
                case 54: _flags |= FLAG_Z; break;
                case 55: _flags &= unchecked((byte)~FLAG_Z); break;
                case 56: _flags |= FLAG_D; break;
                case 57: _flags &= unchecked((byte)~FLAG_D); break;
                case 58: _flags |= FLAG_I; break;
                case 59: _flags &= unchecked((byte)~FLAG_I); break;
                case 60: _sp = (byte)((_sp + 1) & 0xFF); break;
                case 61: _sp = (byte)((_sp - 1) & 0xFF); break;
                case 62: _sp = (byte)((_sp - 1) & 0xFF); SetMem(_sp, GetRQ(op & 0x3)); break;
                case 63: _sp = (byte)((_sp - 1) & 0xFF); SetMem(_sp, XP); break;
                case 64: _sp = (byte)((_sp - 1) & 0xFF); SetMem(_sp, XH); break;
                case 65: _sp = (byte)((_sp - 1) & 0xFF); SetMem(_sp, XL); break;
                case 66: _sp = (byte)((_sp - 1) & 0xFF); SetMem(_sp, YP); break;
                case 67: _sp = (byte)((_sp - 1) & 0xFF); SetMem(_sp, YH); break;
                case 68: _sp = (byte)((_sp - 1) & 0xFF); SetMem(_sp, YL); break;
                case 69: _sp = (byte)((_sp - 1) & 0xFF); SetMem(_sp, _flags); break;
                case 70: SetRQ(op & 0x3, GetMem(_sp)); _sp = (byte)((_sp + 1) & 0xFF); break;
                case 71: _x = XL | (XH << 4) | (GetMem(_sp) << 8); _sp = (byte)((_sp + 1) & 0xFF); break;
                case 72: _x = XL | (GetMem(_sp) << 4) | (XP << 8); _sp = (byte)((_sp + 1) & 0xFF); break;
                case 73: _x = GetMem(_sp) | (XH << 4) | (XP << 8); _sp = (byte)((_sp + 1) & 0xFF); break;
                case 74: _y = YL | (YH << 4) | (GetMem(_sp) << 8); _sp = (byte)((_sp + 1) & 0xFF); break;
                case 75: _y = YL | (GetMem(_sp) << 4) | (YP << 8); _sp = (byte)((_sp + 1) & 0xFF); break;
                case 76: _y = GetMem(_sp) | (YH << 4) | (YP << 8); _sp = (byte)((_sp + 1) & 0xFF); break;
                case 77: _flags = (byte)(GetMem(_sp) & 0xF); _sp = (byte)((_sp + 1) & 0xFF); break;
                case 78: _sp = (byte)(SPL | (GetRQ(op & 0x3) << 4)); break;
                case 79: _sp = (byte)(GetRQ(op & 0x3) | (SPH << 4)); break;
                case 80: SetRQ(op & 0x3, SPH); break;
                case 81: SetRQ(op & 0x3, SPL); break;
                case 82:
                {
                    int tmp = GetRQ(arg0) + arg1;
                    if (D) { if (tmp >= 10) { SetRQ(arg0, (byte)((tmp - 10) & 0xF)); _flags |= FLAG_C; } else { SetRQ(arg0, (byte)tmp); _flags &= unchecked((byte)~FLAG_C); } }
                    else   { SetRQ(arg0, (byte)(tmp & 0xF)); if ((tmp >> 4) != 0) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C); }
                    if (GetRQ(arg0) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                }
                case 83:
                {
                    int tmp = GetRQ(arg0) + GetRQ(arg1);
                    if (D) { if (tmp >= 10) { SetRQ(arg0, (byte)((tmp - 10) & 0xF)); _flags |= FLAG_C; } else { SetRQ(arg0, (byte)tmp); _flags &= unchecked((byte)~FLAG_C); } }
                    else   { SetRQ(arg0, (byte)(tmp & 0xF)); if ((tmp >> 4) != 0) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C); }
                    if (GetRQ(arg0) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                }
                case 84:
                {
                    int tmp = GetRQ(arg0) + arg1 + (C ? 1 : 0);
                    if (D) { if (tmp >= 10) { SetRQ(arg0, (byte)((tmp - 10) & 0xF)); _flags |= FLAG_C; } else { SetRQ(arg0, (byte)tmp); _flags &= unchecked((byte)~FLAG_C); } }
                    else   { SetRQ(arg0, (byte)(tmp & 0xF)); if ((tmp >> 4) != 0) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C); }
                    if (GetRQ(arg0) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                }
                case 85:
                {
                    int tmp = GetRQ(arg0) + GetRQ(arg1) + (C ? 1 : 0);
                    if (D) { if (tmp >= 10) { SetRQ(arg0, (byte)((tmp - 10) & 0xF)); _flags |= FLAG_C; } else { SetRQ(arg0, (byte)tmp); _flags &= unchecked((byte)~FLAG_C); } }
                    else   { SetRQ(arg0, (byte)(tmp & 0xF)); if ((tmp >> 4) != 0) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C); }
                    if (GetRQ(arg0) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                }
                case 86:
                {
                    int tmp = GetRQ(arg0) - GetRQ(arg1);
                    if (D) { if ((tmp >> 4) != 0) SetRQ(arg0, (byte)((tmp - 6) & 0xF)); else SetRQ(arg0, (byte)(tmp & 0xF)); }
                    else   { SetRQ(arg0, (byte)(tmp & 0xF)); }
                    if ((tmp >> 4) != 0) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C);
                    if (GetRQ(arg0) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                }
                case 87:
                {
                    int tmp = GetRQ(arg0) - arg1 - (C ? 1 : 0);
                    if (D) { if ((tmp >> 4) != 0) SetRQ(arg0, (byte)((tmp - 6) & 0xF)); else SetRQ(arg0, (byte)(tmp & 0xF)); }
                    else   { SetRQ(arg0, (byte)(tmp & 0xF)); }
                    if ((tmp >> 4) != 0) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C);
                    if (GetRQ(arg0) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                }
                case 88:
                {
                    int tmp = GetRQ(arg0) - GetRQ(arg1) - (C ? 1 : 0);
                    if (D) { if ((tmp >> 4) != 0) SetRQ(arg0, (byte)((tmp - 6) & 0xF)); else SetRQ(arg0, (byte)(tmp & 0xF)); }
                    else   { SetRQ(arg0, (byte)(tmp & 0xF)); }
                    if ((tmp >> 4) != 0) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C);
                    if (GetRQ(arg0) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                }
                case 89:
                    SetRQ(arg0, (byte)(GetRQ(arg0) & arg1));
                    if (GetRQ(arg0) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                case 90:
                    SetRQ(arg0, (byte)(GetRQ(arg0) & GetRQ(arg1)));
                    if (GetRQ(arg0) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                case 91:
                    SetRQ(arg0, (byte)(GetRQ(arg0) | arg1));
                    if (GetRQ(arg0) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                case 92:
                    SetRQ(arg0, (byte)(GetRQ(arg0) | GetRQ(arg1)));
                    if (GetRQ(arg0) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                case 93:
                    SetRQ(arg0, (byte)(GetRQ(arg0) ^ arg1));
                    if (GetRQ(arg0) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                case 94:
                    SetRQ(arg0, (byte)(GetRQ(arg0) ^ GetRQ(arg1)));
                    if (GetRQ(arg0) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                case 95:
                    if (GetRQ(arg0) < arg1)        _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C);
                    if (GetRQ(arg0) == arg1)        _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                case 96:
                    if (GetRQ(arg0) < GetRQ(arg1)) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C);
                    if (GetRQ(arg0) == GetRQ(arg1)) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                case 97:
                    if ((GetRQ(arg0) & arg1) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                case 98:
                    if ((GetRQ(arg0) & GetRQ(arg1)) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                case 99:
                {
                    byte r = GetRQ(op & 0x3);
                    byte newC = (byte)((r >> 3) & 0x1);
                    SetRQ(op & 0x3, (byte)(((r << 1) | (C ? 1 : 0)) & 0xF));
                    if (newC != 0) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C);
                    break;
                }
                case 100:
                {
                    byte r = GetRQ(op & 0x3);
                    byte newC = (byte)(r & 0x1);
                    SetRQ(op & 0x3, (byte)(((r >> 1) | ((C ? 1 : 0) << 3)) & 0xF));
                    if (newC != 0) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C);
                    break;
                }
                case 101:
                {
                    int tmp = GetMem(op & 0xF) + 1;
                    SetMem(op & 0xF, (byte)(tmp & 0xF));
                    if ((tmp >> 4) != 0) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C);
                    if ((tmp & 0xF) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                }
                case 102:
                {
                    int tmp = GetMem(op & 0xF) - 1;
                    SetMem(op & 0xF, (byte)(tmp & 0xF));
                    if ((tmp >> 4) != 0) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C);
                    if ((tmp & 0xF) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                }
                case 103:
                {
                    int tmp = GetMem(_x) + GetRQ(op & 0x3) + (C ? 1 : 0);
                    if (D) { if (tmp >= 10) { SetMem(_x, (byte)((tmp - 10) & 0xF)); _flags |= FLAG_C; } else { SetMem(_x, (byte)(tmp & 0xF)); _flags &= unchecked((byte)~FLAG_C); } }
                    else   { SetMem(_x, (byte)(tmp & 0xF)); if ((tmp >> 4) != 0) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C); }
                    if (GetMem(_x) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    _x = ((_x + 1) & 0xFF) | (XP << 8);
                    break;
                }
                case 104:
                {
                    int tmp = GetMem(_y) + GetRQ(op & 0x3) + (C ? 1 : 0);
                    if (D) { if (tmp >= 10) { SetMem(_y, (byte)((tmp - 10) & 0xF)); _flags |= FLAG_C; } else { SetMem(_y, (byte)(tmp & 0xF)); _flags &= unchecked((byte)~FLAG_C); } }
                    else   { SetMem(_y, (byte)(tmp & 0xF)); if ((tmp >> 4) != 0) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C); }
                    if (GetMem(_y) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    _y = ((_y + 1) & 0xFF) | (YP << 8);
                    break;
                }
                case 105:
                {
                    int tmp = GetMem(_x) - GetRQ(op & 0x3) - (C ? 1 : 0);
                    if (D) { if ((tmp >> 4) != 0) SetMem(_x, (byte)((tmp - 6) & 0xF)); else SetMem(_x, (byte)(tmp & 0xF)); }
                    else   { SetMem(_x, (byte)(tmp & 0xF)); }
                    if ((tmp >> 4) != 0) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C);
                    if (GetMem(_x) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    _x = ((_x + 1) & 0xFF) | (XP << 8);
                    break;
                }
                case 106:
                {
                    int tmp = GetMem(_y) - GetRQ(op & 0x3) - (C ? 1 : 0);
                    if (D) { if ((tmp >> 4) != 0) SetMem(_y, (byte)((tmp - 6) & 0xF)); else SetMem(_y, (byte)(tmp & 0xF)); }
                    else   { SetMem(_y, (byte)(tmp & 0xF)); }
                    if ((tmp >> 4) != 0) _flags |= FLAG_C; else _flags &= unchecked((byte)~FLAG_C);
                    if (GetMem(_y) == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    _y = ((_y + 1) & 0xFF) | (YP << 8);
                    break;
                }
                case 107:
                {
                    byte r = (byte)((~GetRQ(arg0)) & 0xF);
                    SetRQ(arg0, r);
                    if (r == 0) _flags |= FLAG_Z; else _flags &= unchecked((byte)~FLAG_Z);
                    break;
                }
            }
        }
    }
}
