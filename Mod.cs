using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.InputSystem;
using TaleWorlds.MountAndBlade;
using static Bandit_Militias.Helper;
using static Bandit_Militias.Helper.Globals;
using Patches = Bandit_Militias.Misc.Patches;

// ReSharper disable ConditionIsAlwaysTrueOrFalse  
// ReSharper disable ClassNeverInstantiated.Global  
// ReSharper disable UnusedMember.Local  
// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    public enum LogLevel
    {
        Disabled,
        Info,
        Error,
        Debug
    }

    public class Mod : MBSubModuleBase
    {
        internal static readonly Harmony harmony = new Harmony("ca.gnivler.bannerlord.BanditMilitias");

        internal static void Log(object input, LogLevel logLevel)
        {
            if (logging >= logLevel)
            {
                FileLog.Log($"[Bandit Militias] {input ?? "null"}");
            }
        }

        protected override void OnSubModuleLoad()
        {
            Log($"Startup {DateTime.Now.ToShortTimeString()}", LogLevel.Info);
            RunManualPatches();
            harmony.PatchAll(Assembly.GetExecutingAssembly());
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
                var original = AccessTools.TypeByName("LordNeedsGarrisonTroopsIssue").GetMethod("IssueStayAliveConditions");
                Log(original, LogLevel.Debug);
                var prefix = AccessTools.Method(typeof(Patches), nameof(Patches.IssueStayAliveConditionsPrefix));
                Log($"Patching {original}", LogLevel.Debug);
                harmony.Patch(original, new HarmonyMethod(prefix));
            }
            catch (Exception ex)
            {
                Log(ex, LogLevel.Error);
            }
        }
    }
}
