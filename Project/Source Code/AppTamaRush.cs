using System;
using System.IO;
using System.Threading;
using System.Collections;
using CommonAPI.Phone;
using TamaRush.TMEmulator;
using UnityEngine;
using UnityEngine.UI;
using Reptile;
using TMPro;

namespace TamaRush.Phone
{
    public class AppTamaRush : CustomApp
    {
        private TamaEmulator _emu;
        private Thread        _emuThread;
        private volatile bool _emuRunning;
        private TamaRushAudio _audio;

        private GameObject _gameScreen;
        private RawImage   _lcdImage;
        private Texture2D  _lcdTex;
        private Image[]    _iconImages;

        private const int   IconSheetCols = 4;
        private const int   IconSrcSize   = 64;
        private const float IconSize      = 144f;
        private const float IconRowGap    = 8f;

        private static float LcdDisplayW => TamaRushPlugin.PixelSize.Value * TamaEmulator.LCD_W;
        private static float LcdDisplayH => TamaRushPlugin.PixelSize.Value * TamaEmulator.LCD_H;
        private static float TotalH      => LcdDisplayH + (IconSize + IconRowGap) * 2f;

        private string _romName;
        
        private GameObject _canvasGo;
        private TextMeshProUGUI _label;
        private Coroutine _hideCoroutine;

        public static void Initialize()
        {
            Sprite icon = null;
            string iconPath = TamaRushPlugin.GetAppIconPath("AppIcon.png");
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(iconPath);
                    var texture = new Texture2D(2, 2);
                    if (texture.LoadImage(bytes))
                    {
                        texture.wrapMode = TextureWrapMode.Clamp;
                        texture.filterMode = FilterMode.Bilinear;
                        texture.Apply();
                        icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    }
                }
                catch { }
            }
            if (icon != null) PhoneAPI.RegisterApp<AppTamaRush>("TamaRush", icon);
            else              PhoneAPI.RegisterApp<AppTamaRush>("TamaRush");
        }

        public override void OnAppInit()
        {
            base.OnAppInit();
            CreateIconlessTitleBar("TamaRush");
            ScrollView = PhoneScrollView.Create(this);
            TamaRushPlayMode.OnExitPlayMode += OnExitPlayMode;
            _audio = TamaRushPlayMode.Instance.gameObject.GetComponent<TamaRushAudio>()
                  ?? TamaRushPlayMode.Instance.gameObject.AddComponent<TamaRushAudio>();
            ShowMainMenu();
        }

        public override void OnAppEnable()
        {
            base.OnAppEnable();
            if (!TamaRushPlayMode.IsActive) ShowMainMenu();
        }

        public override void OnAppDisable()
        {
            base.OnAppDisable();
            if (!TamaRushPlugin.RunInBackground.Value) StopEmulator();
        }

        public override void OnPressLeft()   { if (TamaRushPlayMode.IsActive) _emu?.SetButton(TamaButton.Left,   true);  else base.OnPressLeft(); }
        public override void OnReleaseLeft() { if (TamaRushPlayMode.IsActive) _emu?.SetButton(TamaButton.Left,   false); else base.OnReleaseLeft(); }
        public override void OnPressUp()     { if (TamaRushPlayMode.IsActive) _emu?.SetButton(TamaButton.Middle, true);  else base.OnPressUp(); }
        public override void OnReleaseUp()   { if (TamaRushPlayMode.IsActive) _emu?.SetButton(TamaButton.Middle, false); else base.OnReleaseUp(); }
        public override void OnPressRight()  { if (TamaRushPlayMode.IsActive) _emu?.SetButton(TamaButton.Right,  true);  else base.OnPressRight(); }
        public override void OnReleaseRight(){ if (TamaRushPlayMode.IsActive) _emu?.SetButton(TamaButton.Right,  false); else base.OnReleaseRight(); }
        public override void OnPressDown()   { if (TamaRushPlayMode.IsActive) _emu?.SetButton(TamaButton.Tap,    true);  else base.OnPressDown(); }
        public override void OnReleaseDown() { if (TamaRushPlayMode.IsActive) _emu?.SetButton(TamaButton.Tap,    false); else base.OnReleaseDown(); }

        private readonly Color32[] _lcdPixels  = new Color32[TamaEmulator.LCD_W * TamaEmulator.LCD_H];
        private readonly bool[]    _iconStates  = new bool[TamaEmulator.ICON_NUM];
        private float        _autoSaveTimer;
        private bool         _emuStartedThisSession;
        private const float  AutoSaveInterval = 180f;
        private volatile int _cachedGameSpeed  = 1;

        private void Update()
        {
            if (_emuStartedThisSession && _emuRunning && _emu != null && _romName != null && TamaRushPlugin.AutoSave.Value)
            {
                _autoSaveTimer += Time.deltaTime;
                if (_autoSaveTimer >= AutoSaveInterval) { _autoSaveTimer = 0f; SaveAsync(); }
            }

            _cachedGameSpeed = TamaRushPlugin.GameSpeed?.Value ?? 1;
            if (_cachedGameSpeed < 0) _cachedGameSpeed = 1;

            if (!TamaRushPlayMode.IsActive || _emu == null || _lcdTex == null || !_emuRunning) return;
            if (!_emu.LcdDirty) return;
            _emu.LcdDirty = false;

            bool[] iconStates = _iconImages != null ? _iconStates : null;
            int lcdOption = TamaRushPlugin.LcdOption?.Value ?? 0;
            lock (_emu.Lock)
            {
                for (int x = 0; x < TamaEmulator.LCD_W; x++)
                for (int y = 0; y < TamaEmulator.LCD_H; y++)
                {
                    int flippedY = TamaEmulator.LCD_H - 1 - y;
                    _lcdPixels[flippedY * TamaEmulator.LCD_W + x] = GetLcdPixelColor(_emu.LcdMatrix[x, y], lcdOption);
                }
                if (iconStates != null)
                    for (int i = 0; i < TamaEmulator.ICON_NUM; i++)
                        iconStates[i] = _emu.LcdIcons[i];
            }

            _lcdTex.SetPixels32(_lcdPixels);
            _lcdTex.Apply(false, false);

            if (iconStates != null)
                for (int i = 0; i < TamaEmulator.ICON_NUM; i++)
                    _iconImages[i].color = iconStates[i] ? new Color(1f, 1f, 1f, 1f) : new Color(1f, 1f, 1f, 0.5f);
        }

        private void ShowMainMenu()
        {
            SetGameScreenVisible(false);
            ScrollView.RemoveAllButtons();

            var playBtn = PhoneUIUtility.CreateSimpleButton("Play");
            playBtn.OnConfirm += OnPlay;
            ScrollView.AddButton(playBtn);

            if (_emuRunning && _emu != null)
            {
                var saveBtn = PhoneUIUtility.CreateSimpleButton("Save Game");
                saveBtn.OnConfirm += () => SaveAsync();
                ScrollView.AddButton(saveBtn);
            }

            var settingsBtn = PhoneUIUtility.CreateSimpleButton("Settings");
            settingsBtn.OnConfirm += ShowSettingsMenu;
            ScrollView.AddButton(settingsBtn);
        }

        private void ShowSettingsMenu()
        {
            ScrollView.RemoveAllButtons();

            var gameRomBtn = PhoneUIUtility.CreateSimpleButton("Game/ROM Options");
            gameRomBtn.OnConfirm += ShowGameRomOptionsMenu;
            ScrollView.AddButton(gameRomBtn);

            var saveOptionsBtn = PhoneUIUtility.CreateSimpleButton("Save Options");
            saveOptionsBtn.OnConfirm += ShowSaveOptionsMenu;
            ScrollView.AddButton(saveOptionsBtn);

            var customizeBtn = PhoneUIUtility.CreateSimpleButton("Customize Options");
            customizeBtn.OnConfirm += ShowCustomizeMenu;
            ScrollView.AddButton(customizeBtn);

            var audioOptionsBtn = PhoneUIUtility.CreateSimpleButton("Audio Options");
            audioOptionsBtn.OnConfirm += ShowAudioOptionsMenu;
            ScrollView.AddButton(audioOptionsBtn);

            if (TamaRushPlugin.DebugMode.Value == true)
            {
                var DebugOptionsBtn = PhoneUIUtility.CreateSimpleButton("Debug Options");
                DebugOptionsBtn.OnConfirm += ShowDebugOptionsMenu;
                ScrollView.AddButton(DebugOptionsBtn);
            }

            var backBtn = PhoneUIUtility.CreateSimpleButton("Back");
            backBtn.OnConfirm += ShowMainMenu;
            ScrollView.AddButton(backBtn);
        }

        private void ShowSaveOptionsMenu()
        {
            ScrollView.RemoveAllButtons();

            var autoSaveBtn = PhoneUIUtility.CreateSimpleButton(GetAutoSaveLabel());
            autoSaveBtn.OnConfirm += () => { TamaRushPlugin.AutoSave.Value = !TamaRushPlugin.AutoSave.Value; autoSaveBtn.Label.text = GetAutoSaveLabel(); };
            ScrollView.AddButton(autoSaveBtn);

            var backupBtn = PhoneUIUtility.CreateSimpleButton("Backup Save");
            backupBtn.OnConfirm += SaveFileBackedUp;
            ScrollView.AddButton(backupBtn);

            var openSavesFolderBtn = PhoneUIUtility.CreateSimpleButton("Open Saves Folder");
            openSavesFolderBtn.OnConfirm += OpenSavesFolder;
            ScrollView.AddButton(openSavesFolderBtn);

            var openBackupsFolderBtn = PhoneUIUtility.CreateSimpleButton("Open Backups Folder");
            openBackupsFolderBtn.OnConfirm += OpenBackupsFolder;
            ScrollView.AddButton(openBackupsFolderBtn);

            var deleteBtn = PhoneUIUtility.CreateSimpleButton("Delete Save File");
            deleteBtn.OnConfirm += SaveFileDeleted;
            ScrollView.AddButton(deleteBtn);

            var backBtn = PhoneUIUtility.CreateSimpleButton("Back");
            backBtn.OnConfirm += ShowMainMenu;
            ScrollView.AddButton(backBtn);
        }

        private void ShowGameRomOptionsMenu()
        {
            ScrollView.RemoveAllButtons();

            var romBtn = PhoneUIUtility.CreateSimpleButton(GetSelectedRomLabel());
            romBtn.OnConfirm += ShowRomSelectMenu;
            ScrollView.AddButton(romBtn);

            var ribBtn = PhoneUIUtility.CreateSimpleButton(GetRunInBackgroundLabel());
            ribBtn.OnConfirm += () => { TamaRushPlugin.RunInBackground.Value = !TamaRushPlugin.RunInBackground.Value; ribBtn.Label.text = GetRunInBackgroundLabel(); };
            ScrollView.AddButton(ribBtn);

            var speedBtn = PhoneUIUtility.CreateSimpleButton(GetGameSpeedLabel());
            speedBtn.OnConfirm += () =>
            {
                int cur = TamaRushPlugin.GameSpeed.Value;
                TamaRushPlugin.GameSpeed.Value = cur == 1 ? 2 : cur == 2 ? 4 : cur == 4 ? 6 : 1;
                speedBtn.Label.text = GetGameSpeedLabel();
            };
            ScrollView.AddButton(speedBtn);

            var gameFolderBtn = PhoneUIUtility.CreateSimpleButton("Game Folder");
            gameFolderBtn.OnConfirm += OpenRomsFolder;
            ScrollView.AddButton(gameFolderBtn);

            var backBtn = PhoneUIUtility.CreateSimpleButton("Back");
            backBtn.OnConfirm += ShowSettingsMenu;
            ScrollView.AddButton(backBtn);
        }

        private void ShowRomSelectMenu()
        {
            ScrollView.RemoveAllButtons();

            string romsFolder = Path.Combine(TamaRushPlugin.TamaRushFolderPath, "Roms");
            string[] roms = Directory.Exists(romsFolder) ? Directory.GetFiles(romsFolder, "*.bin") : new string[0];

            if (roms.Length == 0)
            {
                ScrollView.AddButton(PhoneUIUtility.CreateSimpleButton("No ROMs found"));
            }
            else
            {
                string effective = GetEffectiveRomPath();
                foreach (string romPath in roms)
                {
                    string captured = romPath;
                    string name = Path.GetFileNameWithoutExtension(romPath);
                    bool isSelected = string.Equals(captured, effective, StringComparison.OrdinalIgnoreCase);
                    var btn = PhoneUIUtility.CreateSimpleButton((isSelected ? "> " : "") + name);
                    btn.OnConfirm += () => { TamaRushPlugin.SelectedRom.Value = captured; ShowGameRomOptionsMenu(); };
                    ScrollView.AddButton(btn);
                }
            }

            var backBtn = PhoneUIUtility.CreateSimpleButton("Back");
            backBtn.OnConfirm += ShowGameRomOptionsMenu;
            ScrollView.AddButton(backBtn);
        }

        private void ShowDebugOptionsMenu()
        {
            ScrollView.RemoveAllButtons();

            var DebugspeedBtn = PhoneUIUtility.CreateSimpleButton(GetDebugGameSpeedLabel());
            DebugspeedBtn.OnConfirm += () =>
            {
                int cur = TamaRushPlugin.GameSpeed.Value;
                TamaRushPlugin.GameSpeed.Value = cur == 1 ? 2 : cur == 2 ? 4 : cur == 4 ? 6 : cur == 6 ? 0 : 1;
                DebugspeedBtn.Label.text = GetDebugGameSpeedLabel();
            };
            ScrollView.AddButton(DebugspeedBtn);

            var backBtn = PhoneUIUtility.CreateSimpleButton("Back");
            backBtn.OnConfirm += ShowSettingsMenu;
            ScrollView.AddButton(backBtn);
        }


        private void ShowAssetSelectMenu(string subfolder, BepInEx.Configuration.ConfigEntry<string> config, System.Action onSelected)
        {
            ScrollView.RemoveAllButtons();

            string dir = Path.Combine(TamaRushPlugin.GetAssetSubfolderPath(), subfolder);
            var images = new System.Collections.Generic.List<string>();
            if (Directory.Exists(dir))
                foreach (string f in Directory.GetFiles(dir, "*.*"))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg") images.Add(f);
                }

            if (images.Count == 0)
            {
                ScrollView.AddButton(PhoneUIUtility.CreateSimpleButton("No files found"));
            }
            else
            {
                string effective = TamaRushPlugin.GetAssetFolderFile(subfolder);
                foreach (string filePath in images)
                {
                    string captured = filePath;
                    string name = Path.GetFileNameWithoutExtension(filePath);
                    bool isSelected = string.Equals(captured, effective, StringComparison.OrdinalIgnoreCase);
                    var btn = PhoneUIUtility.CreateSimpleButton((isSelected ? "> " : "") + name);
                    btn.OnConfirm += () => { config.Value = captured; onSelected?.Invoke(); ShowCustomizeMenu(); };
                    ScrollView.AddButton(btn);
                }
            }

            var backBtn = PhoneUIUtility.CreateSimpleButton("Back");
            backBtn.OnConfirm += ShowCustomizeMenu;
            ScrollView.AddButton(backBtn);
        }

        private void RebuildGameScreen()
        {
            if (_gameScreen != null)
            {
                UnityEngine.Object.Destroy(_gameScreen);
                _gameScreen = null;
                _lcdImage = null;
                _lcdTex = null;
                _iconImages = null;
            }
            var bg = Content.Find("TamaRush_BG");
            if (bg != null) UnityEngine.Object.Destroy(bg.gameObject);
            if (_emuRunning) { EnsureGameScreen(); SetGameScreenVisible(true); }
        }

        private static string GetRunInBackgroundLabel() => "Run In Background: " + (TamaRushPlugin.RunInBackground.Value ? "On" : "Off");
        private static string GetGameSpeedLabel() { int s = TamaRushPlugin.GameSpeed.Value; return "Game Speed: " + (s >= 6 ? "6x" : s >= 4 ? "4x" : s >= 2 ? "2x" : "Normal"); }
        private static string GetDebugGameSpeedLabel() { int s = TamaRushPlugin.GameSpeed.Value; return "Game Speed: " + (s == 0 ? "Unlimited" : s >= 6 ? "6x" : s >= 4 ? "4x" : s >= 2 ? "2x" : "Normal"); }
        private void ShowCustomizeMenu()
        {
            ScrollView.RemoveAllButtons();

            var lcdBtn = PhoneUIUtility.CreateSimpleButton(GetLcdOptionLabel());
            lcdBtn.OnConfirm += () => { TamaRushPlugin.LcdOption.Value = (TamaRushPlugin.LcdOption.Value + 1) % 4; lcdBtn.Label.text = GetLcdOptionLabel(); if (_emu != null) _emu.LcdDirty = true; };
            ScrollView.AddButton(lcdBtn);

            var bgBtn = PhoneUIUtility.CreateSimpleButton(GetSelectedAssetLabel("Background", TamaRushPlugin.SelectedBackground?.Value));
            bgBtn.OnConfirm += () => ShowAssetSelectMenu("Background", TamaRushPlugin.SelectedBackground, RebuildGameScreen);
            ScrollView.AddButton(bgBtn);

            var iconsBtn = PhoneUIUtility.CreateSimpleButton(GetSelectedAssetLabel("Icons", TamaRushPlugin.SelectedIcons?.Value));
            iconsBtn.OnConfirm += () => ShowAssetSelectMenu("Icons", TamaRushPlugin.SelectedIcons, RebuildGameScreen);
            ScrollView.AddButton(iconsBtn);

            var pixelBtn = PhoneUIUtility.CreateSimpleButton(GetPixelSizeLabel());
            pixelBtn.OnConfirm += () =>
            {
                int[] sizes = { 8, 10, 12, 14, 16, 18, 20, 24, 28, 32 };
                int cur = TamaRushPlugin.PixelSize.Value;
                int idx = System.Array.IndexOf(sizes, cur);
                TamaRushPlugin.PixelSize.Value = sizes[(idx + 1) % sizes.Length];
                pixelBtn.Label.text = GetPixelSizeLabel();
                RebuildGameScreen();
            };
            ScrollView.AddButton(pixelBtn);

            var backBtn = PhoneUIUtility.CreateSimpleButton("Back");
            backBtn.OnConfirm += ShowSettingsMenu;
            ScrollView.AddButton(backBtn);
        }

        private void ShowAudioOptionsMenu()
        {
            ScrollView.RemoveAllButtons();

            var audioBtn = PhoneUIUtility.CreateSimpleButton(GetAudioLabel());
            audioBtn.OnConfirm += () => { TamaRushPlugin.AudioEnabled.Value = !TamaRushPlugin.AudioEnabled.Value; audioBtn.Label.text = GetAudioLabel(); };
            ScrollView.AddButton(audioBtn);

            var volumeBtn = PhoneUIUtility.CreateSimpleButton(GetVolumeLabel());
            volumeBtn.OnConfirm += () => { int v = TamaRushPlugin.AudioVolume.Value; TamaRushPlugin.AudioVolume.Value = v >= 10 ? 1 : v + 1; volumeBtn.Label.text = GetVolumeLabel(); };
            ScrollView.AddButton(volumeBtn);

            var backBtn = PhoneUIUtility.CreateSimpleButton("Back");
            backBtn.OnConfirm += ShowSettingsMenu;
            ScrollView.AddButton(backBtn);
        }

        private static string GetAutoSaveLabel()        => "Auto Save: "         + (TamaRushPlugin.AutoSave.Value    ? "On" : "Off");
        private static string GetAudioLabel()           => "Audio: "             + (TamaRushPlugin.AudioEnabled.Value ? "On" : "Off");
        private static string GetVolumeLabel()          => $"Volume: {TamaRushPlugin.AudioVolume.Value}/10";
        private static string GetPixelSizeLabel()       => $"Pixel Size: {(TamaRushPlugin.PixelSize.Value == 32 ? "Default" : TamaRushPlugin.PixelSize.Value.ToString())}";
        private static string GetLcdOptionLabel()       { string[] n = { "Mono", "Classic", "Green", "Inverted" }; return "LCD: " + n[(TamaRushPlugin.LcdOption?.Value ?? 0) % n.Length]; }

        private static string GetSelectedRomLabel()
        {
            string path = GetEffectiveRomPath();
            return "Selected ROM: " + (path != null ? Path.GetFileNameWithoutExtension(path) : "None");
        }

        private static string GetSelectedAssetLabel(string assetType, string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return $"{assetType}: {Path.GetFileNameWithoutExtension(path)}";
            string fallback = TamaRushPlugin.GetAssetFolderFile(assetType);
            return $"{assetType}: " + (fallback != null ? Path.GetFileNameWithoutExtension(fallback) : "None");
        }

        private static string GetEffectiveRomPath()
        {
            string romsFolder = Path.Combine(TamaRushPlugin.TamaRushFolderPath, "Roms");
            string selected = TamaRushPlugin.SelectedRom.Value;
            if (!string.IsNullOrEmpty(selected) && File.Exists(selected)) return selected;
            if (!Directory.Exists(romsFolder)) return null;
            string[] roms = Directory.GetFiles(romsFolder, "*.bin");
            return roms.Length > 0 ? roms[0] : null;
        }

        private static Color32 GetLcdPixelColor(bool on, int option)
        {
            switch (option)
            {
                case 1:  return on ? new Color32(0,   0,   128, 255) : new Color32(216, 216, 192, 255);
                case 2:  return on ? new Color32(15,  56,  15,  255) : new Color32(139, 172, 15,  255);
                case 3:  return on ? new Color32(255, 255, 255, 255) : new Color32(0,   0,   0,   255);
                default: return on ? new Color32(0,   0,   0,   255) : new Color32(255, 255, 255, 255);
            }
        }

        private void OnPlay()
        {
            if (_emuRunning && _emu != null)
            {
                SetGameScreenVisible(true);
                TamaRushPlayMode.Enter();
                return;
            }

            string romPath = GetEffectiveRomPath();
            if (romPath == null) return;
            _romName = Path.GetFileNameWithoutExtension(romPath);

            byte[] romBytes = File.ReadAllBytes(romPath);
            ushort[] rom = new ushort[romBytes.Length / 2];
            for (int i = 0; i < rom.Length; i++)
                rom[i] = (ushort)(romBytes[i * 2 + 1] | ((romBytes[i * 2] & 0xF) << 8));

            _emu = new TamaEmulator();
            _emu.Init(rom);

            TamaState.Load(_emu, GetSavesFolder(), _romName);

            EnsureGameScreen();
            SetGameScreenVisible(true);
            TamaRushPlayMode.Enter();
            _audio?.SetEmulator(_emu);
            
            StartEmulator();
        }

        private void OnExitPlayMode()
        {
            if (TamaRushPlugin.RunInBackground.Value)
            {
                SetGameScreenVisible(false);
                ShowMainMenu();
            }
            else
            {
                if (_emu != null && _romName != null) SaveAsync();
                StopEmulator();
                ShowMainMenu();
            }
        }

        private string GetSavesFolder() =>
            Path.Combine(TamaRushPlugin.TamaRushFolderPath, "Saves", _romName ?? "unknown");

        private void SaveAsync()
        {
            if (_emu == null || _romName == null) return;
            string savesFolder = GetSavesFolder();
            byte[] snapshot = TamaState.Snapshot(_emu);
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try { TamaState.WriteSnapshot(snapshot, savesFolder, _romName); }
                catch (Exception e) { UnityEngine.Debug.LogError("[TamaRush] Save failed: " + e); }
            });
        }

        private void StartEmulator()
        {
            _emuRunning = true;
            _emuStartedThisSession = true;
            _emuThread = new Thread(() =>
            {
                try { while (_emuRunning) _emu.Step(_cachedGameSpeed); }
                catch (Exception e) { _emuRunning = false; UnityEngine.Debug.LogError("[TamaRush] Emulator thread crashed: " + e); }
            });
            _emuThread.IsBackground = true;
            _emuThread.Start();
        }

        private void StopEmulator()
        {
            _emuRunning = false;
            _emuThread?.Join(200);
            _emuThread = null;
            _audio?.SetEmulator(null);
        }

        private void EnsureGameScreen()
        {
            if (_gameScreen != null) return;

            var bgGo = new GameObject("TamaRush_BG", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var bgRect = bgGo.GetComponent<RectTransform>();
            bgRect.SetParent(Content, false);
            bgRect.anchorMin = bgRect.anchorMax = bgRect.pivot = new Vector2(0.5f, 0.5f);
            bgRect.anchoredPosition = new Vector2(0f, -40f);
            bgRect.sizeDelta = new Vector2(2000f, 2000f);
            bgRect.SetAsFirstSibling();
            var bgImage = bgGo.GetComponent<RawImage>();
            bgImage.raycastTarget = false;
            Texture2D bgTex = LoadTexture(TamaRushPlugin.GetAssetFolderFile("Background"));
            if (bgTex != null) bgImage.texture = bgTex;
            else               bgImage.color = new Color(0.85f, 0.85f, 0.75f);

            _gameScreen = new GameObject("TamaRush_GameScreen", typeof(RectTransform));
            var rootRect = _gameScreen.GetComponent<RectTransform>();
            rootRect.SetParent(Content, false);
            rootRect.anchorMin = rootRect.anchorMax = rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = new Vector2(0f, -70f);
            rootRect.sizeDelta = new Vector2(LcdDisplayW, TotalH);

            var lcdGo = new GameObject("LCD", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var lcdRect = lcdGo.GetComponent<RectTransform>();
            lcdRect.SetParent(rootRect, false);
            lcdRect.anchorMin = lcdRect.anchorMax = lcdRect.pivot = new Vector2(0.5f, 0.5f);
            lcdRect.anchoredPosition = Vector2.zero;
            lcdRect.sizeDelta = new Vector2(LcdDisplayW, LcdDisplayH);
            _lcdImage = lcdGo.GetComponent<RawImage>();
            _lcdImage.color = Color.white;
            _lcdImage.raycastTarget = false;

            _lcdTex = new Texture2D(TamaEmulator.LCD_W, TamaEmulator.LCD_H, TextureFormat.RGBA32, false);
            _lcdTex.filterMode = FilterMode.Point;
            _lcdTex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[TamaEmulator.LCD_W * TamaEmulator.LCD_H];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            _lcdTex.SetPixels32(pixels);
            _lcdTex.Apply(false, false);
            _lcdImage.texture = _lcdTex;

            Texture2D iconSheet = LoadTexture(TamaRushPlugin.GetAssetFolderFile("Icons"));
            _iconImages = new Image[TamaEmulator.ICON_NUM];

            float iconRowY_top    =  (LcdDisplayH * 0.5f) + IconRowGap + (IconSize * 0.5f);
            float iconRowY_bottom = -(LcdDisplayH * 0.5f) - IconRowGap - (IconSize * 0.5f);
            float iconSpacing = LcdDisplayW / 4f;

            Sprite[] iconSprites = null;
            if (iconSheet != null)
            {
                iconSprites = new Sprite[TamaEmulator.ICON_NUM];
                int sheetRows = iconSheet.height / IconSrcSize;
                for (int i = 0; i < TamaEmulator.ICON_NUM; i++)
                {
                    int col = i % IconSheetCols;
                    int row = i / IconSheetCols;
                    int flippedRow = sheetRows - 1 - row;
                    var rect = new Rect(col * IconSrcSize, flippedRow * IconSrcSize, IconSrcSize, IconSrcSize);
                    iconSprites[i] = Sprite.Create(iconSheet, rect, new Vector2(0.5f, 0.5f));
                }
            }

            for (int i = 0; i < TamaEmulator.ICON_NUM; i++)
            {
                int col = i % IconSheetCols;
                int row = i / IconSheetCols;
                float x = -LcdDisplayW * 0.5f + iconSpacing * col + iconSpacing * 0.5f;
                float y = row == 1 ? iconRowY_bottom : iconRowY_top;

                var iconGo = new GameObject($"Icon{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var iconRect = iconGo.GetComponent<RectTransform>();
                iconRect.SetParent(rootRect, false);
                iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.anchoredPosition = new Vector2(x, y);
                iconRect.sizeDelta = new Vector2(IconSize, IconSize);

                var img = iconGo.GetComponent<Image>();
                img.raycastTarget = false;
                if (iconSprites != null) img.sprite = iconSprites[i];
                img.color = new Color(1f, 1f, 1f, 0.5f);
                _iconImages[i] = img;
            }

            _gameScreen.SetActive(false);
        }

        private static Texture2D LoadTexture(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2);
                if (!tex.LoadImage(bytes)) return null;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                tex.Apply();
                return tex;
            }
            catch { return null; }
        }

        private void SetGameScreenVisible(bool visible)
        {
            if (_gameScreen != null) _gameScreen.SetActive(visible);
            var bg = Content.Find("TamaRush_BG");
            if (bg != null) bg.gameObject.SetActive(visible);
            if (ScrollView != null) ScrollView.gameObject.SetActive(!visible);
        }

        private void OpenSavesFolder()
        {
            var path = _romName != null ? GetSavesFolder() : Path.Combine(TamaRushPlugin.TamaRushFolderPath, "Saves");
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            Application.OpenURL("file:///" + path.Replace('\\', '/'));
        }

        private void OpenBackupsFolder()
        {
            var path = _romName != null ? GetSavesFolder() : Path.Combine(TamaRushPlugin.TamaRushFolderPath, "Saves");
            path = Path.Combine(path, "backups");
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            Application.OpenURL("file:///" + path.Replace('\\', '/'));
        }

        private void OpenSpritesFolder()
        {
            var path = Path.Combine(TamaRushPlugin.TamaRushFolderPath, "Sprites");
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            Application.OpenURL("file:///" + path.Replace('\\', '/'));
        }

        private static void OpenRomsFolder()
        {
            var path = Path.Combine(TamaRushPlugin.TamaRushFolderPath, "Roms");
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            Application.OpenURL("file:///" + path.Replace('\\', '/'));
        }

        private void SaveFileBackedUp()
        {
            if (_emu == null || _romName == null) return;
            string backupFolder = Path.Combine(GetSavesFolder(), "backups");
            byte[] snapshot = TamaState.Snapshot(_emu);
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (!Directory.Exists(backupFolder)) Directory.CreateDirectory(backupFolder);
                    string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string path = Path.Combine(backupFolder, $"{_romName}_backup_{timestamp}.bin");
                    File.WriteAllBytes(path, snapshot);
                }
                catch (Exception e) { UnityEngine.Debug.LogError("[TamaRush] Backup failed: " + e); }
            });

            if (_canvasGo == null)
            {
                var uiManager = Core.Instance?.UIManager?.transform;
                if (uiManager == null) return;

                _canvasGo = new GameObject("TamaRush_PlayModeHint");
                _canvasGo.transform.SetParent(uiManager, false);

                var canvas = _canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 200;
                _canvasGo.AddComponent<CanvasScaler>();
                _canvasGo.AddComponent<GraphicRaycaster>();

                var textGo = new GameObject("HintLabel");
                textGo.transform.SetParent(_canvasGo.transform, false);

                textGo.AddComponent<CanvasRenderer>();

                _label = textGo.AddComponent<TextMeshProUGUI>();
                _label.fontSize = 22;
                _label.color = Color.green;
                _label.alignment = TextAlignmentOptions.TopRight;
                _label.enableWordWrapping = true;

                var rect = textGo.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
                rect.sizeDelta = new Vector2(400f, 80f);
                rect.anchoredPosition = new Vector2(-16f, -10f);
            }

            _label.text = "Your Save was backed up!";
            _canvasGo.SetActive(true);
            if (_hideCoroutine != null)
                StopCoroutine(_hideCoroutine);
            
            _hideCoroutine = StartCoroutine(HideAfterDelay(4f));
                
        }


        private void SaveFileDeleted()
        {
            string savesFolder = _romName != null ? GetSavesFolder() : Path.Combine(TamaRushPlugin.TamaRushFolderPath, "Saves");
            if (Directory.Exists(savesFolder))
            {
                foreach (string file in Directory.GetFiles(savesFolder, "*.bin"))
                {
                    try { File.Delete(file); } catch { /* Log error if file is locked */ }
                }
            }

            if (_canvasGo == null)
            {
                var uiManager = Core.Instance?.UIManager?.transform;
                if (uiManager == null) return;

                _canvasGo = new GameObject("TamaRush_PlayModeHint");
                _canvasGo.transform.SetParent(uiManager, false);

                var canvas = _canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 200;
                _canvasGo.AddComponent<CanvasScaler>();
                _canvasGo.AddComponent<GraphicRaycaster>();

                var textGo = new GameObject("HintLabel");
                textGo.transform.SetParent(_canvasGo.transform, false);

                textGo.AddComponent<CanvasRenderer>();

                _label = textGo.AddComponent<TextMeshProUGUI>();
                _label.fontSize = 22;
                _label.color = Color.red;
                _label.alignment = TextAlignmentOptions.TopRight;
                _label.enableWordWrapping = true;

                var rect = textGo.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
                rect.sizeDelta = new Vector2(400f, 80f);
                rect.anchoredPosition = new Vector2(-16f, -10f);
            }

            _label.text = "Your save file(s) has been deleted.";
            _canvasGo.SetActive(true);
            if (_hideCoroutine != null)
                StopCoroutine(_hideCoroutine);
            
            _hideCoroutine = StartCoroutine(HideAfterDelay(4f));
                
        }

        private IEnumerator HideAfterDelay(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (_canvasGo != null)
                _canvasGo.SetActive(false);
            if (_label != null)
                _label.text = "";
            _hideCoroutine = null;
        }

    }
}


