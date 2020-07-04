using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;
using static Bandit_Militias.Helper.Globals;
using static Bandit_Militias.Helper;

// ReSharper disable UnusedType.Global  
// ReSharper disable UnusedMember.Local   
// ReSharper disable RedundantAssignment  
// ReSharper disable InconsistentNaming

namespace Bandit_Militias.Misc
{
    public class Patches
    {
        [HarmonyPatch(typeof(Campaign), "OnInitialize")]
        public static class CampaignOnInitializePatch
        {
            private static void Postfix()
            {
                try
                {
                    Mod.Log("Campaign.OnInitialize", LogLevel.Debug);
                    var militias = MobileParty.All.Where(x => x != null && x.Name.Equals("Bandit Militia")).ToList();
                    Mod.Log($"Militias: {militias.Count}", LogLevel.Info);
                    Mod.Log($"Homeless: {militias.Count(x => x.HomeSettlement == null)}", LogLevel.Info);
                    militias.Where(x => x.HomeSettlement == null)
                        .Do(x =>
                        {
                            Mod.Log("Fixing null HomeSettlement (destroyed hideout)", LogLevel.Info);
                            x.HomeSettlement = Game.Current.ObjectManager.GetObjectTypeList<Settlement>().Where(y => y.IsHideout()).GetRandomElement();
                        });
                    foreach (var militia in militias.Where(x => x.LeaderHero == null))
                    {
                        Mod.Log("Removing hero-less militia", LogLevel.Info);
                        militia.RemoveParty();
                    }


                    foreach (var m in Militia.All)
                    {
                        if (m.MobileParty == null)
                        {
                            Mod.Log("Trashing", LogLevel.Debug);
                            Trash(m.MobileParty);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Mod.Log(ex, LogLevel.Error);
                }
            }
        }

        // init section.  NRE without setting leaders again here for whatever reason
        [HarmonyPatch(typeof(MapScreen), "OnInitialize")]
        public static class MapScreenOnInitializePatch
        {
            private static void Postfix()
            {
                try
                {
                    Mod.Log("MapScreen.OnInitialize", LogLevel.Debug);
                    CalcMergeCriteria();
                }
                catch (Exception ex)
                {
                    Mod.Log(ex, LogLevel.Error);
                }
            }
        }

        // BUG some parties were throwing when exiting post-battle loot menu 1.4.2b
        [HarmonyPatch(typeof(MBObjectManager), "UnregisterObject")]
        public static class MBObjectManagerUnregisterObjectPatch
        {
            private static Exception Finalizer(Exception __exception)
            {
                if (__exception is ArgumentNullException)
                {
                    Mod.Log("Bandit Militias suppressing exception in Patches.cs MBObjectManagerUnregisterObjectPatch", LogLevel.Debug);
                    Mod.Log(__exception, LogLevel.Debug);
                    Debug.Print("Bandit Militias suppressing exception in Patches.cs MBObjectManagerUnregisterObjectPatch");
                    Debug.Print(__exception.ToString());
                    return null;
                }

                return __exception;
            }
        }

        // BUG more vanilla patching
        //[HarmonyPatch(typeof(BanditsCampaignBehavior), "OnSettlementEntered")]
        //public static class BanditsCampaignBehaviorOnSettlementEnteredPatch
        //{
        //    private static bool Prefix(ref MobileParty mobileParty, Hero hero)
        //    {
        //        if (mobileParty == null)
        //        {
        //            Trace("Fixing vanilla call with null MobileParty at BanditsCampaignBehavior.OnSettlementEntered");
        //            if (hero == null)
        //            {
        //                Trace("Hero is also null");
        //                return false;
        //            }
        //
        //            var parties = hero.OwnedParties?.ToList();
        //            if (parties == null ||
        //                parties.Count == 0)
        //            {
        //                Trace("Unable to fix call with null MobileParty at BanditsCampaignBehavior.OnSettlementEntered");
        //                return false;
        //            }
        //
        //            mobileParty = parties[0]?.MobileParty;
        //            Trace($"New party: {mobileParty}");
        //            if (mobileParty == null)
        //            {
        //                // some shit still falls through this far
        //                Trace("Fall-through");
        //                return false;
        //            }
        //        }
        //
        //        return true;
        //    }
        //}

        [HarmonyPatch(typeof(MapEventManager), "OnAfterLoad")]
        public static class MapEventManagerCtorPatch
        {
            private static void Postfix(List<MapEvent> ___mapEvents)
            {
                try
                {
                    MapEvents = ___mapEvents;
                    FinalizeBadMapEvents();
                }
                catch (Exception ex)
                {
                    Mod.Log(ex, LogLevel.Error);
                }
            }
        }

        // prevents vanilla NRE from parties without Owner trying to pay for upgrades... 
        //[HarmonyPatch(typeof(PartyUpgrader), "UpgradeReadyTroops", typeof(PartyBase))]
        //public static class PartyUpgraderUpgradeReadyTroopsPatch
        //{
        //    private static bool Prefix(PartyBase party)
        //    {
        //        if (party.MobileParty == null)
        //        {
        //            Trace("party.MobileParty == null");
        //            return false;
        //        }
        //
        //        if (party.Owner == null)
        //        {
        //            Trace("party.Owner == null, that throws vanilla in 1.4.2b, Prefix false");
        //            return false;
        //        }
        //
        //        return true;
        //    }
        //}

        [HarmonyPatch(typeof(FactionManager), "IsAtWarAgainstFaction")]
        public static class FactionManagerIsAtWarAgainstFactionPatch
        {
            // 1.4.2b vanilla code not optimized and checks against own faction
            private static bool Prefix(IFaction faction1, IFaction faction2, ref bool __result)
            {
                if (faction1 == faction2)
                {
                    __result = false;
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(FactionManager), "IsAlliedWithFaction")]
        public static class FactionManagerIsAlliedWithFactionPatch
        {
            // 1.4.2b vanilla code not optimized and checks against own faction  
            private static bool Prefix(IFaction faction1, IFaction faction2, ref bool __result)
            {
                if (faction1 == faction2)
                {
                    __result = true;
                    return false;
                }

                return true;
            }
        }


        [HarmonyPatch(typeof(Campaign), "HourlyTick")]
        public static class CampaignHourlyTickPatch
        {
            private static int hoursPassed;

            private static void Postfix()
            {
                var tempList = new List<MobileParty>();
                foreach (var mobileParty in MobileParty.All
                    .Where(x => x.MemberRoster.TotalManCount == 0))
                {
                    tempList.Add(mobileParty);
                }

                // this was apparently only necessary when the merge distance was smaller than the split min radius
                // in InitializeMobileParty
                // TODO probably remove
                PurgeList($"CampaignHourlyTickPatch Clearing {tempList.Count} empty parties", tempList);
                try
                {
                    foreach (var mobileParty in MobileParty.All
                        .Where(x => x.Name.Equals("Bandit Militia") &&
                                    x.MapFaction == CampaignData.NeutralFaction))
                    {
                        Mod.Log("This bandit shouldn't exist " + mobileParty + " size " + mobileParty.MemberRoster.TotalManCount, LogLevel.Debug);
                        tempList.Add(mobileParty);
                    }
                }
                catch (Exception ex)
                {
                    Mod.Log(ex, LogLevel.Error);
                }

                PurgeList($"CampaignHourlyTickPatch Clearing {tempList.Count} weird neutral parties", tempList);
                // bug: I don't think this is needed anymore, so I commented it
                //FinalizeBadMapEvents();

                if (hoursPassed == 23)
                {
                    CalcMergeCriteria();
                    hoursPassed = 0;
                }

                //PurgeNullRefDescriptionIssues();
                hoursPassed++;
            }
        }

        // just disperse small militias
        [HarmonyPatch(typeof(MapEventSide), "HandleMapEventEndForParty")]
        public static class MapEventSideHandleMapEventEndForPartyPatch
        {
            private static void Postfix(MapEventSide __instance, PartyBase party)
            {
                try
                {
                    if (!party.Name.Equals("Bandit Militia"))
                    {
                        return;
                    }

                    if (party.MemberRoster.TotalHealthyCount < 20 &&
                        party.MemberRoster.TotalHealthyCount > 0 &&
                        party.PrisonRoster.Count < 20 &&
                        __instance.Casualties > party.MemberRoster.Count / 2)
                    {
                        Mod.Log($"Dispersing militia of {party.MemberRoster.TotalHealthyCount}+{party.MemberRoster.TotalWounded}w+{party.PrisonRoster.Count}p", LogLevel.Debug);
                        Trash(party.MobileParty);
                    }
                    else if (party.MemberRoster.Count >= 20 &&
                             party.LeaderHero == null)
                    {
                        var militias = Militia.All.Where(x => x.MobileParty == party.MobileParty);
                        foreach (var militia in militias)
                        {
                            Mod.Log("Reconfiguring", LogLevel.Debug);
                            militia.Configure();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Mod.Log(ex, LogLevel.Error);
                }
            }
        }

        // vanilla patch, doesn't check MobileParty for null
        // this happens when Clan governor's change and get teleported into a settlement without a party
        [HarmonyPatch(typeof(BanditsCampaignBehavior), "CheckForSpawningBanditBoss")]
        public class BanditsCampaignBehaviorCheckForSpawningBanditBossPatch
        {
            private static bool Prefix(BanditsCampaignBehavior __instance, Settlement settlement, MobileParty mobileParty)
            {
                if (mobileParty == null ||
                    !settlement.IsHideout() ||
                    !mobileParty.IsBandit ||
                    !settlement.Hideout.IsInfested ||
                    settlement.Parties.Any(x => x.IsBanditBossParty) ||
                    settlement.Parties.Count(x => x.IsBandit) !=
                    Campaign.Current.Models.BanditDensityModel.NumberOfMinimumBanditPartiesInAHideoutToInfestIt)
                {
                    return false;
                }

                Traverse.Create(__instance).Method("AddBossParty", settlement, mobileParty).GetValue();
                return false;
            }
        }

        // for whatever reason I'm seeing apparently-vanilla data causing NREs
        [HarmonyPatch(typeof(IssueManager), "InitializeForSavedGame")]
        public class IssueManagerInitializeForSavedGamePatch
        {
            private static void Prefix()
            {
                try
                {
                    PurgeNullRefDescriptionIssues();
                }
                catch (Exception ex)
                {
                    Mod.Log(ex, LogLevel.Error);
                }
            }
        }
    }
}
