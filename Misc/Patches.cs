using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;
using static Bandit_Militias.Helper.Globals;
using static Bandit_Militias.Helper;

// ReSharper disable UnusedMember.Global 
// ReSharper disable UnusedType.Global  
// ReSharper disable UnusedMember.Local   
// ReSharper disable RedundantAssignment  
// ReSharper disable InconsistentNaming

namespace Bandit_Militias.Misc
{
    public class Patches
    {
        [HarmonyPatch(typeof(MapScreen), "OnInitialize")]
        public static class CampaignOnInitializePatch
        {
            private static void Postfix()
            {
                try
                {
                    Mod.Log("MapScreen.OnInitialize", LogLevel.Debug);
                    var militias = MobileParty.All.Where(x => x != null && x.Name.Equals("Bandit Militia")).ToList();
                    Mod.Log($"Militias: {militias.Count}", LogLevel.Info);
                    Flush();
                    CalcMergeCriteria();
                    
                    // have to manually patch due to static class initialization
                    var original = AccessTools.Method(typeof(CampaignUIHelper), "GetCharacterCode");
                    var prefix = AccessTools.Method(typeof(Misc.Patches), nameof(Misc.Patches.GetCharacterCodePrefix));
                    Mod.Log($"Patching {original}", LogLevel.Debug);
                    Mod.harmony.Patch(original, new HarmonyMethod(prefix));
                }
                catch (Exception ex)
                {
                    Mod.Log(ex, LogLevel.Error);
                }
            }
        }

        private static void GetCharacterCodePrefix(ref CharacterObject character)
        {
            if (character.Equipment == null)
            {
                Traverse.Create(character?.HeroObject).Property("BattleEquipment")
                    .SetValue(MurderLordsForEquipment(null, false));
            }

            if (character.CivilianEquipments == null)
            {
                Traverse.Create(character?.HeroObject).Property("CivilianEquipment")
                    .SetValue(MurderLordsForEquipment(null, false));
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
                HourlyFlush();
                if (hoursPassed == 23)
                {
                    CalcMergeCriteria();
                    hoursPassed = 0;
                }

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

        internal static void IssueStayAliveConditionsPrefix(object __instance, ref Settlement ____settlement)
        {
            if (____settlement == null)
            {
                var stringId = (string) __instance.GetType().GetField("_settlementStringID", AccessTools.all)?.GetValue(__instance);
                ____settlement = Settlement.Find(stringId) ?? Settlement.FindFirst(x => x.StringId == stringId) ?? Settlement.GetFirst;
                Mod.Log(____settlement, LogLevel.Debug);
            }
        }


        [HarmonyPatch(typeof(HeroCreator), "CreateRelativeNotableHero")]
        public class HeroCreatorCreateRelativeNotableHero
        {
            private static bool Prefix(Hero relative) => relative.CharacterObject?.Occupation != Occupation.Outlaw;
        }

        //[HarmonyPatch(typeof(EnterSettlementAction), "ApplyInternal")]
        //public class EnterSettlementActionApplyInternalPatch
        //{
        //    private static bool Prefix(Hero hero) => hero != null;
        //}
        //
        [HarmonyPatch(typeof(UrbanCharactersCampaignBehavior), "ChangeDeadNotable")]
        public class UrbanCharactersCampaignBehaviorChangeDeadNotablePatch
        {
            private static bool Prefix(Hero deadNotable, Hero newNotable) => deadNotable == null && newNotable == null;
        }

        //
        //[HarmonyPatch(typeof(CommonArea), "CalculateIdealNumberOfTroops")]
        //public class CommonAreaCalculateIdealNumberOfTroopsPatch
        //{
        //    private static bool Prefix(CommonArea __instance) => __instance.Owner != null;
        //}
        //
        //[HarmonyPatch(typeof(CommonArea), "AddTroopsToCommonArea")]
        //public class CommonAreaAddTroopsToCommonAreaPatch
        //{
        //    private static bool Prefix(CommonArea __instance) => __instance.InsideParty != null;
        //}
        //
        //[HarmonyPatch(typeof(CommonArea), "PlaceInitialTradeGoods")]
        //public class CommonAreaPlaceInitialTradeGoodsPatch
        //{
        //    private static bool Prefix(CommonArea __instance) => __instance.InsideParty != null;
        //}
        //
        //[HarmonyPatch(typeof(DefaultPartyHealingModel), "GetDailyHealingForRegulars")]
        //public class DefaultPartyHealingModelGetDailyHealingForRegularsPatch
        //{
        //    private static bool Prefix(MobileParty party) => party.CurrentSettlement != null;
        //}
        //
        //[HarmonyPatch(typeof(DefaultPartyHealingModel), "GetDailyHealingHpForHeroes")]
        //public class DefaultPartyHealingModelGetDailyHealingHpForHeroesPatch
        //{
        //    private static bool Prefix(MobileParty party) => party.CurrentSettlement != null;
        //}
        //
        //[HarmonyPatch(typeof(BoardGameCampaignBehavior), "OnHeroKilled")]
        //public class BoardGameCampaignBehaviorOnHeroKilledPatch
        //{
        //    private static bool Prefix(object ____heroAndBoardGameTimeDictionary) => ____heroAndBoardGameTimeDictionary != null;
        //}
        //
        //[HarmonyPatch(typeof(DynamicBodyCampaignBehavior), "OnAfterDailyTick")]
        //public class DynamicBodyCampaignBehaviorOnAfterDailyTickPatch
        //{
        //    private static Exception Finalizer()
        //    {
        //        Mod.Log("Suppressing exception DynamicBodyCampaignBehaviorOnAfterDailyTickPatch", LogLevel.Debug);
        //        Debug.Print("Bandit Militias is suppressing an exception at DynamicBodyCampaignBehaviorOnAfterDailyTickPatch");
        //        return null;
        //    }
        //}
        //
        //[HarmonyPatch(typeof(PartyUpgrader), "UpgradeReadyTroops")]
        //public class MapEventManagerUpgradeReadyTroopsPatch
        //{
        //    private static bool Prefix(PartyBase party) => party.Owner != null;
        //}
        //
        [HarmonyPatch(typeof(DestroyPartyAction), "ApplyInternal")]
        public class DestroyPartyActionApplyInternalPatch
        {
            private static Exception Finalizer()
            {
                return null;
            }
        }
    }
}
