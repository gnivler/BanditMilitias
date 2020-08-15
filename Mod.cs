using System;
using System.IO;
using System.Reflection;
using Bandit_Militias.Helpers;
using HarmonyLib;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static Bandit_Militias.Helpers.Helper;
using static Bandit_Militias.Helpers.Globals;

// ReSharper disable ConditionIsAlwaysTrueOrFalse  
// ReSharper disable ClassNeverInstantiated.Global  
// ReSharper disable UnusedMember.Local  
// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    // ReSharper disable once UnusedMember.Global
    public enum LogLevel
    {
        Disabled,
        Info,
        Error,
        Debug
    }

    public class Mod : MBSubModuleBase
    {
        private const LogLevel logging = LogLevel.Disabled;
        private static readonly Harmony harmony = new Harmony("ca.gnivler.bannerlord.BanditMilitias");
        private static readonly string modDirectory = new FileInfo(@"..\..\Modules\Bandit Militias\").DirectoryName;

        internal static void Log(object input, LogLevel logLevel = LogLevel.Debug)
        {
            try
            {
                if (logging < logLevel)
                {
                    return;
                }

                FileLog.Log($"[Bandit Militias] {input ?? "null"}");
                using (var sw = new StreamWriter(Path.Combine(modDirectory, "mod.log"), true))
                {
                    sw.WriteLine($"[{DateTime.Now:G}] {input ?? "null"}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }
        }

        protected override void OnSubModuleLoad()
        {
            Log($"Startup {DateTime.Now.ToShortTimeString()}", LogLevel.Info);
            try
            {
                Globals.Settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(Path.Combine(modDirectory, "mod_settings.json")));
                Globals.Settings.CooldownHours = MathF.Clamp(Globals.Settings.CooldownHours, 1, float.MaxValue);
                Log(Globals.Settings.XpGift + " " + DifficultyXpMap[Globals.Settings.XpGift]);
                Log(Globals.Settings.GoldReward + " " + GoldMap[Globals.Settings.GoldReward]);
            }
            catch (Exception ex)
            {
                Log(ex, LogLevel.Error);
                Globals.Settings = new Settings();
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
                    Globals.Settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(fileName));
                }
                else
                {
                    Log($"Configuration file expected at {fileName} but not found, using default settings", LogLevel.Error);
                    Globals.Settings = new Settings();
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
                InformationManager.AddQuickInformation(new TextObject("Testing mode: " + testingMode));
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

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            ((CampaignGameStarter) gameStarterObject).AddBehavior(new MilitiaBehavior());
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
