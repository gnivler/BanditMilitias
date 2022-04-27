using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SandBox.View.Map;
using SandBox.ViewModelCollection.MobilePartyTracker;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static Bandit_Militias.Globals;
using static Bandit_Militias.Helpers.Helper;
using Module = TaleWorlds.MountAndBlade.Module;

// ReSharper disable ConditionIsAlwaysTrueOrFalse
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable UnusedMember.Local
// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    public class Mod : MBSubModuleBase
    {
        internal static readonly Harmony harmony = new("ca.gnivler.bannerlord.BanditMilitias");

        // ReSharper disable once AssignNullToNotNullAttribute
        private static readonly string logFilename = Path.Combine(new FileInfo(@"..\..\Modules\Bandit Militias\").DirectoryName, "log.txt");

        internal static void Log(object input)
        {
            if (Globals.Settings is null
                || Globals.Settings?.Debug is false)
            {
                return;
            }

            using var sw = new StreamWriter(logFilename, true);
            sw.WriteLine($"[{DateTime.Now.ToLongTimeString()}] {(string.IsNullOrEmpty(input.ToString()) ? "IsNullOrEmpty" : input)}");
        }

        protected override void OnSubModuleLoad()
        {
            if (Environment.MachineName == "MEOWMEOW")
            {
                AccessTools.Field(typeof(Module), "_splashScreenPlayed").SetValue(Module.CurrentModule, true);
            }

            CacheBanners();
            RunManualPatches();
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        // need to cache the banners before CEK adds background colours which
        // causes custom banners to crash for reasons unknown
        private static void CacheBanners()
        {
            for (var i = 0; i < 5000; i++)
            {
                Banners.Add(Banner.CreateRandomBanner(Rng.Next()));
            }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            Globals.Settings = Settings.Instance;
            if (File.Exists(logFilename))
            {
                try
                {
                    File.Copy(logFilename, $"{logFilename}.old", true);
                    File.Delete(logFilename);
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }

            Log($"{Globals.Settings?.DisplayName} starting up...");
        }

        // Calradia Expanded: Kingdoms
        private static void AdjustForLoadOrder()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var BM = assemblies.First(a => a.FullName.StartsWith("Bandit Militias"));
            var CEK = assemblies.FirstOrDefault(x => x.FullName.StartsWith("CalradiaExpandedKingdoms"));
            if (CEK is not null)
            {
                if (assemblies.FindIndex(a => a == BM) > assemblies.FindIndex(a => a == CEK))
                {
                    Globals.Settings.RandomBanners = false;
                }
            }
        }

        protected override void OnApplicationTick(float dt)
        {
            var superKey = Campaign.Current != null
                           && (Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl))
                           && (Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt))
                           && (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift));

            if (superKey && Input.IsKeyPressed(InputKey.F9))
            {
                // debug to show all parties on map
                foreach (var m in MobileParty.All)
                {
                    Globals.MobilePartyTrackerVM.Trackers.Add(new MobilePartyTrackItemVM(m, MapScreen.Instance.MapCamera, null));
                }
            }

            if (superKey && Input.IsKeyPressed(InputKey.F11))
            {
                Globals.Settings.TestingMode = !Globals.Settings.TestingMode;
                InformationManager.AddQuickInformation(new TextObject("Testing mode: " + Globals.Settings.TestingMode));
            }

            if (superKey && Input.IsKeyPressed(InputKey.F10))
            {
                foreach (var militia in PartyMilitiaMap.Values.WhereQ(m => m.MobileParty.MemberRoster.TotalManCount < Globals.Settings.MinPartySize).OrderByDescending(x => x.MobileParty.MemberRoster.TotalManCount))
                {
                    //if (militia.MobileParty.MemberRoster.TotalManCount >= Globals.Settings.MinPartySize)
                    //{
                    //    continue;
                    //}

                    Log(militia.MobileParty.MemberRoster.TotalManCount + " " + militia.MobileParty.Food);
                }
            }

            if (superKey && Input.IsKeyPressed(InputKey.F12))
            {
                foreach (var militia in PartyMilitiaMap.Values.OrderBy(x => x.MobileParty.MemberRoster.TotalManCount))
                {
                    Log($">> {militia.Hero.Name,-30}: {militia.MobileParty.MemberRoster.TotalManCount:F1}/{militia.MobileParty.Party.TotalStrength:0}");
                    for (int tier = 1; tier <= 6; tier++)
                    {
                        var count = militia.MobileParty.MemberRoster.GetTroopRoster().Where(x => x.Character.Tier == tier).Sum(x => x.Number);
                        if (count > 0)
                        {
                            Log($"  Tier {tier}: {count}");
                        }
                    }

                    Log($"Cavalry: {NumMountedTroops(militia.MobileParty.MemberRoster)} ({(float)NumMountedTroops(militia.MobileParty.MemberRoster) / militia.MobileParty.MemberRoster.TotalManCount * 100}%)");
                    if ((float)NumMountedTroops(militia.MobileParty.MemberRoster) / (militia.MobileParty.MemberRoster.TotalManCount * 100) > militia.MobileParty.MemberRoster.TotalManCount / 2f)
                    {
                        Log(new string('*', 80));
                        Log(new string('*', 80));
                    }
                }

                Log($">>> Total {PartyMilitiaMap.Values.Count} = {PartyMilitiaMap.Values.Select(x => x.MobileParty.MemberRoster.TotalManCount).Sum()} ({MilitiaPowerPercent}%)");
            }

            if ((Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl)) &&
                (Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt)) &&
                Input.IsKeyPressed(InputKey.N))
            {
                try
                {
                    Log("Clearing mod data.");
                    // no idea why it takes several iterations to clean up certain situations but it does
                    for (var index = 0; index < 2; index++)
                    {
                        Nuke();
                    }

                    DoPowerCalculations(true);
                    InformationManager.AddQuickInformation(new TextObject("BANDIT MILITIAS CLEARED"));
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            if (gameStarterObject is CampaignGameStarter gameStarter)
            {
                gameStarter.AddBehavior(new MilitiaBehavior());
                gameStarter.AddModel(new ModBanditMilitiaKillModel());
            }
        }

        private static void RunManualPatches()
        {
            try
            {
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
            }
        }
    }
}
