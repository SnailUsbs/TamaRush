using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TamaRush.Phone;
using UnityEngine;

namespace TamaRush
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency("CommonAPI", BepInDependency.DependencyFlags.HardDependency)]
    public class TamaRushPlugin : BaseUnityPlugin
    {
        private static string _modFolder;
        private Harmony _harmony;

        public static ConfigEntry<bool>   RunInBackground     { get; private set; }
        public static ConfigEntry<int>    GameSpeed           { get; private set; }
        public static ConfigEntry<bool>   AutoSave            { get; private set; }
        public static ConfigEntry<int>    LcdOption           { get; private set; }
        public static ConfigEntry<string> SelectedBackground  { get; private set; }
        public static ConfigEntry<string> SelectedIcons       { get; private set; }
        public static ConfigEntry<bool>   AudioEnabled        { get; private set; }
        public static ConfigEntry<int>    AudioVolume         { get; private set; }
        public static ConfigEntry<int>    PixelSize           { get; private set; }
        public static ConfigEntry<string> SelectedRom         { get; private set; }
        public static ConfigEntry<bool>   DebugMode           { get; private set; }
        public static ConfigEntry<bool>   VisualCPU           { get; private set; }

        public static string TamaRushFolderPath { get; private set; }

        public static string GetAppIconPath(string filename)
            => string.IsNullOrEmpty(_modFolder) ? null : Path.Combine(_modFolder, filename);

        public static string GetAssetSubfolderPath(string subfolder)
            => string.IsNullOrEmpty(_modFolder) ? null : Path.Combine(_modFolder, "Assets", subfolder);

        public static string GetAssetFolderFile(string subfolder)
        {
            if (string.IsNullOrEmpty(_modFolder)) return null;

            string configured = subfolder == "Background" ? SelectedBackground?.Value
                              : subfolder == "Icons"      ? SelectedIcons?.Value
                              : null;
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
                return configured;

            string dir = Path.Combine(_modFolder, "Assets", subfolder);
            if (!Directory.Exists(dir)) return null;
            foreach (string f in Directory.GetFiles(dir, "*.*"))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg") return f;
            }
            return null;
        }

        private void Awake()
        {
            _modFolder = Path.GetDirectoryName(Info.Location);

            RunInBackground    = Config.Bind("Game/ROM", "RunInBackground",    false, "When enabled, the Tamagotchi keeps running after you leave play mode.");
            GameSpeed          = Config.Bind("Game/ROM", "GameSpeed",          1,     "Emulation speed multiplier. 1=Normal, 2=2x, 4=4x, 6=6x.");
            AutoSave           = Config.Bind("Saves", "AutoSave",           true,  "When enabled, automatically saves every 3 minutes while the emulator is running.");
            LcdOption          = Config.Bind("Game/ROM", "LcdOption",          0,     "LCD color scheme. 0=Mono, 1=Classic, 2=Green, 3=Inverted.");
            SelectedBackground = Config.Bind("Customize", "SelectedBackground", "",    "Full path to the background image file.");
            SelectedIcons      = Config.Bind("Customize", "SelectedIcons",      "",    "Full path to the icons spritesheet file.");
            AudioEnabled       = Config.Bind("Audio", "AudioEnabled",       true,  "When enabled, the Tamagotchi buzzer sound plays.");
            AudioVolume        = Config.Bind("Audio", "AudioVolume",        5,     "Buzzer volume level 1-10.");
            PixelSize          = Config.Bind("Game/ROM", "PixelSize",          32,    "LCD pixel size in Unity units.");
            SelectedRom        = Config.Bind("Game/ROM", "SelectedRom",        "",    "Full path to the ROM to load.");
            DebugMode          = Config.Bind("DEBUG", "DebugMode",          false, "Extra options that may run bad on lower end devices.");
            VisualCPU          = Config.Bind("DEBUG", "Visual CPU Instructions",          false, "Will have add on screen text showing what the emulator's CPU is doing.");

            TamaRushFolderPath = Path.Combine(BepInEx.Paths.BepInExRootPath, "TamaRush");
            Directory.CreateDirectory(TamaRushFolderPath);
            Directory.CreateDirectory(Path.Combine(TamaRushFolderPath, "Saves"));
            Directory.CreateDirectory(Path.Combine(TamaRushFolderPath, "Roms"));
            Directory.CreateDirectory(Path.Combine(TamaRushFolderPath, "Backgrounds"));
            Directory.CreateDirectory(Path.Combine(TamaRushFolderPath, "Icons"));
            Directory.CreateDirectory(Path.Combine(TamaRushFolderPath, "Sprites"));

            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            _harmony.PatchAll();

            var playModeGo = new GameObject("TamaRush_PlayMode");
            playModeGo.AddComponent<TamaRushPlayMode>();

            AppTamaRush.Initialize();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }

    public static class PluginInfo
    {
        public const string PLUGIN_GUID    = "com.tamarush";
        public const string PLUGIN_NAME    = "TamaRush";
        public const string PLUGIN_VERSION = "1.0.0";
    }
}
