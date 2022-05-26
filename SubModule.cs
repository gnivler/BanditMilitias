using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BanditMilitias.Helpers;
using HarmonyLib;
using SandBox.View.Map;
using SandBox.ViewModelCollection.MobilePartyTracker;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using static BanditMilitias.Helpers.Helper;
using static BanditMilitias.Globals;
using Module = TaleWorlds.MountAndBlade.Module;

// ReSharper disable ConditionIsAlwaysTrueOrFalse
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable UnusedMember.Local
// ReSharper disable InconsistentNaming

namespace BanditMilitias
{
    public class SubModule : MBSubModuleBase
    {
        public static bool MEOWMEOW = Environment.MachineName == "MEOWMEOW";
        public static readonly Harmony harmony = new("ca.gnivler.bannerlord.BanditMilitias");

        // ReSharper disable once AssignNullToNotNullAttribute
        public static readonly string logFilename = Path.Combine(new FileInfo(@"..\..\Modules\BanditMilitias\").DirectoryName, "log.txt");

        protected override void OnSubModuleLoad()
        {
            if (MEOWMEOW)
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
            var BM = assemblies.First(a => a.FullName.StartsWith("BanditMilitias"));
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

            if (MEOWMEOW && Input.IsKeyPressed(InputKey.Tilde))
            {
                //Debugger.Break();
                //var crud = MobileParty.All.Where(m => m.Name.ToString().EndsWith("Bandit Militia")).ToList();
                //for (var i = 0; i < crud.Count; i++)
                //{
                //    Trash(crud[i]);
                //}


                Nuke();
                for (var i = 0; i < MobileParty.AllBanditParties.Count; i++)
                {
                    //Traverse.Create(MobileParty.AllBanditParties[i]).Property<MobileParty>("AiBehaviorPartyBase").Value = null;
                    //Traverse.Create(MobileParty.AllBanditParties[i].AiBehaviorPartyBase).Property<MobileParty>("MobileParty").Value = null;

                    MobileParty.AllBanditParties[i].MapEvent?.FinalizeEvent();
                    Trash(MobileParty.AllBanditParties[i]);
                }
            }

            if (superKey && Input.IsKeyPressed(InputKey.F11))
            {
                Globals.Settings.TestingMode = !Globals.Settings.TestingMode;
                InformationManager.AddQuickInformation(new TextObject("Testing mode: " + Globals.Settings.TestingMode));
            }

            if (superKey && Input.IsKeyPressed(InputKey.F10))
            {
                MobileParty.MainParty.ItemRoster.AddToCounts(MBObjectManager.Instance.GetObject<ItemObject>("grain"), 10000);
            }

            if (superKey && Input.IsKeyPressed(InputKey.F12))
            {
                foreach (var militia in MobileParty.All.WhereQ(m => m.IsBM()).OrderBy(x => x.MemberRoster.TotalManCount))
                {
                    Log($">> {militia.LeaderHero.Name,-30}: {militia.MemberRoster.TotalManCount:F1}/{militia.Party.TotalStrength:0}");
                    for (int tier = 1; tier <= 6; tier++)
                    {
                        var count = militia.MemberRoster.GetTroopRoster().Where(x => x.Character.Tier == tier).Sum(x => x.Number);
                        if (count > 0)
                        {
                            Log($"  Tier {tier}: {count}");
                        }
                    }

                    Log($"Cavalry: {NumMountedTroops(militia.MemberRoster)} ({(float)NumMountedTroops(militia.MemberRoster) / militia.MemberRoster.TotalManCount * 100}%)");
                    if ((float)NumMountedTroops(militia.MemberRoster) / (militia.MemberRoster.TotalManCount * 100) > militia.MemberRoster.TotalManCount / 2f)
                    {
                        Log(new string('*', 80));
                        Log(new string('*', 80));
                    }
                }

                Log($">>> Total {MobileParty.All.CountQ(m => m.IsBM())} = {MobileParty.All.WhereQ(m => m.IsBM()).Select(x => x.MemberRoster.TotalManCount).Sum()} ({MilitiaPowerPercent}%)");
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
                var internalType = AccessTools.TypeByName("<GetTrackDescription>d__11");
                var org = AccessTools.Method(internalType, "MoveNext");
                harmony.Patch(org, finalizer: new HarmonyMethod(AccessTools.Method(typeof(SubModule), "Finalizer")));
                //internalType = AccessTools.TypeByName("ManagedCallbacks.ScriptingInterfaceOfIScene");
                //org = AccessTools.Method(internalType, "GetPathBetweenAIFaceIndices");
                //harmony.Patch(org, prefix: new HarmonyMethod(AccessTools.Method(typeof(SubModule), "Prefix")));
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
            }
        }

        private static Exception Finalizer() => null;
    }
}
