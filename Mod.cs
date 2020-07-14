using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using TaleWorlds.InputSystem;
using TaleWorlds.MountAndBlade;
using static Bandit_Militias.Helper;
using static Bandit_Militias.Helper.Globals;

// ReSharper disable ConditionIsAlwaysTrueOrFalse  
// ReSharper disable ClassNeverInstantiated.Global  
// ReSharper disable UnusedMember.Local  
// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    public enum LogLevel
    {
        Disabled,
        Warning,
        Error,
        Debug
    }

    public class Mod : MBSubModuleBase
    {
        private static LogLevel logging = LogLevel.Disabled;
        private static readonly Harmony harmony = new Harmony("ca.gnivler.bannerlord.BanditMilitias");
        private static readonly string modDirectory = new FileInfo(@"..\..\Modules\Bandit Militias\").DirectoryName;

        internal static void Log(object input, LogLevel logLevel)
        {
            if (logging >= logLevel)
            {
                using (var sw = new StreamWriter(Path.Combine(modDirectory, "mod.log"), true))
                {
                    sw.WriteLine($"[{DateTime.Now:G}] {input ?? "null"}");
                }
            }
        }

        protected override void OnSubModuleLoad()
        {
            Log($"Startup {DateTime.Now.ToShortTimeString()}", LogLevel.Warning);
            try
            {
                Globals.Settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(Path.Combine(modDirectory, "mod_settings.json")));
                Log(Globals.Settings.XpGift + " " + DifficultyXpMap[Globals.Settings.XpGift], LogLevel.Warning);
                Log(Globals.Settings.GoldReward + " " + GoldMap[Globals.Settings.GoldReward], LogLevel.Warning);
            }
            catch (Exception ex)
            {
                Log(ex, LogLevel.Error);
                Helper.Globals.Settings = new Settings();
            }

            RunManualPatches();
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        private static void ReadConfig()
        {
            try
            {
                var fileName = Path.Combine(modDirectory, "mod_config.json");
                if (File.Exists(fileName))
                {
                    Helper.Globals.Settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(fileName));
                }
                else
                {
                    Log($"Configuration file expected at {fileName} but not found, using default settings", LogLevel.Error);
                    Helper.Globals.Settings = new Settings();
                }
            }
            catch (Exception ex)
            {
                Log(ex, LogLevel.Error);
            }
        }

        protected override void OnApplicationTick(float dt)
        {
            if ((Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl)) &&
                (Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt)) &&
                (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift)) &&
                Input.IsKeyPressed(InputKey.F11))
            {
                testingMode = !testingMode;
            }

            if ((Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl)) &&
                (Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt)) &&
                Input.IsKeyPressed(InputKey.N))
            {
                try
                {
                    Nuke();
                }
                catch (Exception ex)
                {
                    Log(ex, LogLevel.Error);
                }
            }
        }

        private static void RunManualPatches()
        {
            try
            {
            }
            catch (Exception ex)
            {
                Log(ex, LogLevel.Error);
            }
        }
    }
}
