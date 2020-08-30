using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static Bandit_Militias.Helpers.Helper;
using static Bandit_Militias.Globals;

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

        protected override void OnSubModuleLoad()
        {
            Log($"Startup {DateTime.Now.ToShortTimeString()}", LogLevel.Info);
            try
            {
                ReadConfig();
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
                var fileName = Path.Combine(modDirectory, "mod_settings.json");
                if (File.Exists(fileName))
                {
                    Globals.Settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(fileName));
                    ClampSettingsValues(ref Globals.Settings);
                    PrintValidatedSettings(Globals.Settings);
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
                TestingMode = !TestingMode;
                InformationManager.AddQuickInformation(new TextObject("Testing mode: " + TestingMode));
            }

            if ((Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl)) &&
                (Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt)) &&
                (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift)) &&
                Input.IsKeyPressed(InputKey.F12))
            {
                Log($"Total {Militias.Count}");
                foreach (var militia in Militias)
                {
                    Log($"{militia.Hero.Name,-30}: {militia.MobileParty.MemberRoster.TotalManCount}/{militia.MobileParty.Party.TotalStrength}");
                }
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
