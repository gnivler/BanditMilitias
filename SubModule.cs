using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using BanditMilitias.Helpers;
using BanditMilitias.Patches;
using HarmonyLib;
using SandBox.View.Map;
using SandBox.ViewModelCollection.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using static BanditMilitias.Helpers.Helper;
using static BanditMilitias.Globals;
using Debug = System.Diagnostics.Debug;
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
                AccessTools.Field(typeof(Module), "_splashScreenPlayed").SetValue(Module.CurrentModule, true);
            RunManualPatches();
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        // need to cache the banners before CEK adds background colours which
        // causes custom banners to crash for reasons unknown
        private static void CacheBanners()
        {
            for (var i = 0; i < 5000; i++)
            {
                Banners.Add((Banner)AccessTools.Method(typeof(Banner), "CreateRandomBannerInternal")
                    .Invoke(typeof(Banner), new object[] { Rng.Next(0, int.MaxValue), -1 }));
            }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            Globals.Settings = Settings.Instance;
            if (File.Exists(logFilename))
                try
                {
                    File.Copy(logFilename, $"{logFilename}.old", true);
                    File.Delete(logFilename);
                }
                catch (Exception ex)
                {
                    Debug.Print(ex.ToString());
                }

            DeferringLogger.Instance.Debug?.Log($"{Globals.Settings?.DisplayName} starting up...");
        }

        // Calradia Expanded: Kingdoms
        private static void AdjustForLoadOrder()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var BM = assemblies.First(a => a.FullName.StartsWith("BanditMilitias"));
            var CEK = assemblies.FirstOrDefaultQ(x => x.FullName.StartsWith("CalradiaExpandedKingdoms"));
            if (CEK is not null)
                if (assemblies.FindIndex(a => a == BM) > assemblies.FindIndex(a => a == CEK))
                    Globals.Settings.RandomBanners = false;
        }

        protected override void OnApplicationTick(float dt)
        {
            var superKey = Campaign.Current != null
                           && (Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl))
                           && (Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt))
                           && (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift));

            // debug to show all parties on map
            if (MEOWMEOW && superKey && Input.IsKeyPressed(InputKey.F9))
                foreach (var m in MobileParty.All)
                    Globals.MapMobilePartyTrackerVM.Trackers.Add(new MobilePartyTrackItemVM(m, MapScreen.Instance.MapCamera, null));

            if (MEOWMEOW && Input.IsKeyPressed(InputKey.F1))
            {
                //var party = GetCachedBMs(true)?.GetRandomElementInefficiently()?.MobileParty;
                var party = MobileParty.All.WhereQ(m => m.Army != null).GetRandomElementInefficiently();
                if (party is not null)
                    MobileParty.MainParty.Position2D = party.Position2D;
                //party.Position2D = MobileParty.MainParty.Position2D;
            }

            if (MEOWMEOW && Input.IsKeyPressed(InputKey.F2))
            {
                foreach (var mobileParty in MobileParty.AllBanditParties)
                {
                    if (mobileParty.StringId.StartsWith("Bandit_Militia"))
                        continue;
                    mobileParty.Position2D = MobileParty.MainParty.Position2D;
                }

                Hacks.PurgeBadTroops();
            }

            if (MEOWMEOW && Input.IsKeyPressed(InputKey.F3))
            {
                //try
                //{
                //    MobileParty.MainParty.Position2D = Settlement.All.WhereQ(s => s.IsHideout && s.Hideout.IsInfested).GetRandomElementInefficiently().GatePosition;
                //}
                //catch
                //{
                //    //ignore
                //}
                MobileParty.MainParty.Position2D = Hero.AllAliveHeroes.FirstOrDefaultQ(h => h.IsWanderer && h.CurrentSettlement is not null).CurrentSettlement.GatePosition;
            }

            if (MEOWMEOW && Input.IsKeyPressed(InputKey.Tilde))
                Debugger.Break();

            if (superKey && Input.IsKeyPressed(InputKey.F11))
            {
                Globals.Settings.TestingMode = !Globals.Settings.TestingMode;
                InformationManager.DisplayMessage(new InformationMessage("Testing mode: " + Globals.Settings.TestingMode));
            }

            if (MEOWMEOW && superKey && Input.IsKeyPressed(InputKey.F10))
                MobileParty.MainParty.ItemRoster.AddToCounts(MBObjectManager.Instance.GetObject<ItemObject>("grain"), 10000);

            if (superKey && Input.IsKeyPressed(InputKey.F12))
            {
                foreach (var militia in MobileParty.All.WhereQ(m => m.IsBM()).OrderBy(x => x.MemberRoster.TotalManCount))
                {
                    DeferringLogger.Instance.Debug?.Log($">> {militia.LeaderHero.Name,-30}: {militia.MemberRoster.TotalManCount:F1}/{militia.Party.TotalStrength:0}");
                    for (var tier = 1; tier <= 6; tier++)
                    {
                        // ReSharper disable once AccessToModifiedClosure
                        var count = militia.MemberRoster.GetTroopRoster().WhereQ(x => x.Character.Tier == tier).SumQ(x => x.Number);
                        if (count > 0)
                        {
                            DeferringLogger.Instance.Debug?.Log($"  Tier {tier}: {count}");
                        }
                    }

                    DeferringLogger.Instance.Debug?.Log($"Cavalry: {NumMountedTroops(militia.MemberRoster)} ({(float)NumMountedTroops(militia.MemberRoster) / militia.MemberRoster.TotalManCount * 100}%)");
                    if ((float)NumMountedTroops(militia.MemberRoster) / (militia.MemberRoster.TotalManCount * 100) > militia.MemberRoster.TotalManCount / 2f)
                    {
                        DeferringLogger.Instance.Debug?.Log(new string('*', 80));
                        DeferringLogger.Instance.Debug?.Log(new string('*', 80));
                    }
                }

                DeferringLogger.Instance.Debug?.Log($">>> Total {MobileParty.All.CountQ(m => m.IsBM())} = {MobileParty.All.WhereQ(m => m.IsBM()).SelectQ(x => x.MemberRoster.TotalManCount).Sum()} ({MilitiaPowerPercent}%)");
            }

            if ((Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl)) &&
                (Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt)) &&
                Input.IsKeyPressed(InputKey.N))
            {
                try
                {
                    DeferringLogger.Instance.Debug?.Log("Clearing mod data.");
                    // no idea why it takes several iterations to clean up certain situations but it does
                    for (var index = 0; index < 1; index++)
                    {
                        Nuke();
                    }

                    DoPowerCalculations(true);
                }
                catch (Exception ex)
                {
                    DeferringLogger.Instance.Debug?.Log(ex);
                }
            }
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            if (gameStarterObject is CampaignGameStarter gameStarter)
            {
                gameStarter.AddBehavior(new MilitiaBehavior());
                //gameStarter.AddModel(new ModBanditMilitiaKillModel());
            }
        }

        public override void OnGameInitializationFinished(Game game)
        {
            base.OnGameInitializationFinished(game);
            CacheBanners();
        }

        private static void RunManualPatches()
        {
            try
            {
                Dev.RunDevPatches();
            }
            catch (Exception ex)
            {
                DeferringLogger.Instance.Debug?.Log(ex);
            }
        }
    }
}
