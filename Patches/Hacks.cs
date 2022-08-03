using System;
using BanditMilitias.Helpers;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using static BanditMilitias.Helpers.Helper;

namespace BanditMilitias.Patches
{
    public class Hacks

    {
        public static readonly AccessTools.FieldRef<MobileParty, int> numberOfRecentFleeingFromAParty = AccessTools.FieldRefAccess<MobileParty, int>("_numberOfRecentFleeingFromAParty");

        [HarmonyPatch(typeof(BasicCharacterObject), "GetSkillValue")]
        public class BasicCharacterObjectGetSkillValue
        {
            public static Exception Finalizer(Exception __exception, SkillObject skill)
            {
                if (__exception is not null) Log(__exception);
                return null;
            }
        }

        [HarmonyPatch(typeof(MapEventSide), "ApplySimulatedHitRewardToSelectedTroop")]
        public class MapEventSideApplySimulatedHitRewardToSelectedTroop
        {
            public static Exception Finalizer(Exception __exception, CharacterObject strikerTroop, CharacterObject attackedTroop)
            {
                if (__exception is not null) Log(__exception);
                return null;
            }
        }

        [HarmonyPatch(typeof(TroopRoster), "ClampXp")]
        public static class TroopRosterClampXp
        {
            public static Exception Finalizer(Exception __exception, TroopRoster __instance)
            {
                if (__exception is not null) Log(__exception);
                return null;
            }
        }

        [HarmonyPatch(typeof(BanditPartyComponent), "get_PartyOwner")]
        public class BanditPartyComponentPartyOwner
        {
            public static Exception Finalizer(Exception __exception, BanditPartyComponent __instance)
            {
                if (__exception is not null) Log(__exception);
                return null;
            }
        }

        [HarmonyPatch(typeof(MobileParty), "CalculateContinueChasingScore")]
        public class MobilePartyCalculateContinueChasingScore
        {
            // copied from 1.8.0 assembly because the 2nd ternary doesn't account for any IsBandit that doesn't have a BanditPartyComponent
            public static bool Prefix(MobileParty __instance, MobileParty enemyParty, ref float __result)
            {
                var num1 = __instance.Army == null || __instance.Army.LeaderParty != __instance ? __instance.Party.TotalStrength : __instance.Army.TotalStrength;
                var num2 = (float)((enemyParty.Army == null || enemyParty.Army.LeaderParty != __instance ? enemyParty.Party.TotalStrength : (double)enemyParty.Army.TotalStrength) / (num1 + 0.00999999977648258));
                var num3 = (float)(1.0 + 0.00999999977648258 * numberOfRecentFleeingFromAParty(enemyParty));
                var num4 = Math.Min(1f, (__instance.Position2D - enemyParty.Position2D).Length / 3f);
                Settlement toSettlement;
                if (__instance.GetBM() is { } BM)
                {
                    toSettlement = BM.HomeSettlement;
                }
                else if (__instance.IsBandit)
                {
                    toSettlement = __instance.BanditPartyComponent.Hideout?.Settlement;
                }
                else if (!__instance.IsLordParty
                         || __instance.LeaderHero == null
                         || !__instance.LeaderHero.IsMinorFactionHero)
                {
                    toSettlement = SettlementHelper.FindNearestFortification(x => x.MapFaction == __instance.MapFaction);
                }
                else
                {
                    toSettlement = __instance.MapFaction.FactionMidSettlement;
                }


                var num5 = Campaign.AverageDistanceBetweenTwoFortifications * 3f;
                if (toSettlement != null)
                    num5 = Campaign.Current.Models.MapDistanceModel.GetDistance(__instance, toSettlement);
                var num6 = num5 / (Campaign.AverageDistanceBetweenTwoFortifications * 3f);
                var num7 = MBMath.Map(1f + (float)Math.Pow(enemyParty.Speed / (__instance.Speed - 0.25), 3.0), 0.0f, 5.2f, 0.0f, 2f);
                var num8 = 60000f;
                var num9 = 10000f;
                var num10 = (enemyParty.LeaderHero != null ? enemyParty.PartyTradeGold + enemyParty.LeaderHero.Gold : (double)enemyParty.PartyTradeGold) / (enemyParty.IsCaravan ? num9 : (double)num8);
                var num11 = enemyParty.LeaderHero != null ? (enemyParty.LeaderHero.IsFactionLeader ? 1.5f : 1f) : 0.75f;
                var num12 = num2 * num6 * num7 * num3 * num4;
                double num13 = num11;
                __result = MBMath.ClampFloat((float)(num10 * num13 / (num12 + 1.0 / 1000.0)), 0.005f, 3f);
                return false;
            }
        }

        // throws during nuke
        [HarmonyPatch(typeof(TroopRoster), "ClampXp")]
        public static class TroopRosterClampXpPatch
        {
            public static Exception Finalizer(Exception __exception, TroopRoster __instance)
            {
                if (__exception is not null) Log(__exception);
                return null;
            }
        }

        public static Exception ExperienceFinalizer(DefaultPartyTrainingModel __instance, Exception __exception, MobileParty mobileParty, TroopRosterElement troop)
        {
            if (__exception is not null) Meow();
            return null;
        }

        public static Exception GetTrackDescriptionMoveNext(Exception __exception)
        {
            if (__exception is not null) Meow();
            return null;
        }
    }
}
