using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;
using static Bandit_Militias.Helper.Globals;
using static Bandit_Militias.Mod;
using static Bandit_Militias.Helper;

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
                    Log("Campaign.OnInitialize");
                    var militias = MobileParty.All.Where(x => x != null && x.Name.Equals("Bandit Militia")).ToList();
                    Log($"Militias: {militias.Count()}");
                    Log($"Homeless: {militias.Count(x => x.HomeSettlement == null)}");
                    militias.Where(x => x.HomeSettlement == null)
                        .Do(x =>
                        {
                            Log("Fixing null HomeSettlement (destroyed hideout)");
                            x.HomeSettlement = Game.Current.ObjectManager.GetObjectTypeList<Settlement>().Where(y => y.IsHideout()).GetRandomElement();
                        });
                    foreach (var militia in militias.Where(x => x.LeaderHero == null))
                    {
                        Log("Removing hero-less militia");
                        militia.RemoveParty();
                    }
                }
                catch (Exception ex)
                {
                    Log(ex);
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
                    Log("MapScreen.OnInitialize");
                    CalcMergeCriteria();
                }
                catch (Exception ex)
                {
                    Log(ex);
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
                    Log("Bandit Militias suppressing exception in Patches.cs MBObjectManagerUnregisterObjectPatch");
                    Log(__exception);
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
                    Log(ex);
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
                TempList.Clear();
                foreach (var mobileParty in MobileParty.All
                    .Where(x => x.MemberRoster.TotalManCount == 0))
                {
                    TempList.Add(mobileParty);
                }

                // this was apparently only necessary when the merge distance was smaller than the split min radius
                // in InitializeMobileParty
                PurgeList($"CampaignHourlyTickPatch Clearing {TempList.Count} empty parties");
                try
                {
                    foreach (var mobileParty in MobileParty.All
                        .Where(x => x.Name.Equals("Bandit Militia") &&
                                    x.MapFaction == CampaignData.NeutralFaction))
                    {
                        Log("This bandit shouldn't exist " + mobileParty + " size " + mobileParty.MemberRoster.TotalManCount);
                        TempList.Add(mobileParty);
                    }
                }
                catch (Exception ex)
                {
                    Log(ex);
                }

                PurgeList($"CampaignHourlyTickPatch Clearing {TempList.Count} weird neutral parties");
                FinalizeBadMapEvents();
                PurgeList($"CampaignHourlyTickPatch Clearing {TempList.Count} bad map events");

                if (hoursPassed == 23)
                {
                    CalcMergeCriteria();
                    hoursPassed = 0;
                }

                hoursPassed++;
            }
        }

        // just disperse small militias
        // bug changed method to HandleMapEventEnd, lightly tested
        [HarmonyPatch(typeof(MapEventSide), "HandleMapEventEnd")]
        public static class MapEventSideHandleMapEventEndForPartyPatch
        {
            private static void Postfix(MapEventSide __instance)
            {
                try
                {
                    foreach (var partyBase in __instance.Parties.Where(x => x.LeaderHero != null))
                    {
                        if (partyBase.LeaderHero.Name.Equals("Bandit Militia") &&
                            partyBase.MemberRoster.TotalManCount.IsBetween(0, 10))
                        {
                            Trace("Dispersing militia of " + partyBase.MemberRoster.TotalManCount);
                            Trash(partyBase.MobileParty);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }
        }
    }
}
