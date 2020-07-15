using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.SandBox.Issues;
using TaleWorlds.Core;
using TaleWorlds.Library;
using static Bandit_Militias.Helper;
using static Bandit_Militias.Helper.Globals;

// ReSharper disable UnusedMember.Global 
// ReSharper disable UnusedType.Global   
// ReSharper disable UnusedMember.Local   
// ReSharper disable RedundantAssignment  
// ReSharper disable InconsistentNaming

namespace Bandit_Militias.Militias
{
    public class Patches
    {
        // swapped (copied) two very similar methods in assemblies, one was throwing one wasn't
        [HarmonyPatch(typeof(NearbyBanditBaseIssueBehavior), "FindSuitableHideout")]
        public static class NearbyBanditBaseIssueBehaviorFindSuitableHideoutPatch
        {
            private const float floatMaxValue = float.MaxValue;

            // taken from CapturedByBountyHuntersIssue because this class' version throws
            private static bool Prefix(Hero issueOwner, ref Settlement __result)
            {
                foreach (var settlement in Settlement.FindAll(x => x.Hideout != null))
                {
                    if (Campaign.Current.Models.MapDistanceModel.GetDistance(issueOwner.GetMapPoint(),
                            settlement, 55f, out var num2) &&
                        num2 < floatMaxValue)
                    {
                        __result = settlement;
                    }
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(MobileParty), "DailyTick")]
        public static class MobilePartyDailyTickPatch
        {
            private static void Postfix(MobileParty __instance)
            {
                if (!IsValidParty(__instance))
                {
                    return;
                }

                // check daily each bandit party against the size factor and a random chance to split up
                TrySplitUpParty(__instance);
            }
        }

        // where militias try to find each other and merge
        [HarmonyPatch(typeof(MobileParty), "HourlyTick")]
        public static class MobilePartyHourlyTickPatch
        {
            private static void Postfix(MobileParty __instance)
            {
                if (!IsValidParty(__instance))
                {
                    return;
                }

                var targetParty = MobileParty.FindPartiesAroundPosition(__instance.Position2D, MergeDistance * 1.25f,
                    x => x != __instance && x.IsBandit && IsValidParty(x)).GetRandomElement()?.Party;

                // "nobody" is a valid answer
                if (targetParty == null)
                {
                    return;
                }

                if (!targetParty.MobileParty.IsAlone())
                {
                    return;
                }

                if (Campaign.Current.Models.MapDistanceModel.GetDistance(targetParty.MobileParty, __instance) > MergeDistance)
                {
                    Mod.Log($"{__instance} Seeking target {targetParty.MobileParty}", LogLevel.Debug);
                    __instance.SetMoveGoToPoint(targetParty.Position2D);
                    return;
                }

                // conditions check.  separated for speed
                var roster = new TroopRoster
                {
                    __instance.MemberRoster,
                    targetParty.MemberRoster
                };
                var militiaTotalCount = roster.TotalManCount;
                if (militiaTotalCount > Helper.Globals.Settings.MaxPartySize ||
                    militiaTotalCount > CalculatedMaxPartySize ||
                    __instance.Party.TotalStrength > CalculatedMaxPartyStrength ||
                    NumMountedTroops(roster) > roster.TotalManCount / 2)
                {
                    return;
                }

                if (Settlement.FindSettlementsAroundPosition(__instance.Position2D, MinDistanceFromHideout, x => x.IsHideout()).Any())
                {
                    return;
                }

                // create a new party merged from the two
                var rosters = MergeRosters(__instance, targetParty);
                var militia = new Militia(__instance.Position2D, rosters[0], rosters[1]);

                // teleport new militias near the player
                if (testingMode)
                {
                    militia.MobileParty.Position2D = Hero.MainHero.PartyBelongedTo.Position2D +
                                                     new Vec2(MBRandom.RandomFloatRanged(-3f, 3f), MBRandom.RandomFloatRanged(-3f, 3));
                }

                militia.MobileParty.Party.Visuals.SetMapIconAsDirty();
                Trash(__instance);
                Trash(targetParty.MobileParty);
            }
        }

        // the game has no way of actually fully removing heroes so we tack this on...
        // and it doesn't match anything so yeah it's useless
        // trying to find the source of these heroes appearing that have no party associated
        [HarmonyPatch(typeof(MobileParty), "RemoveParty")]
        public class MobilePartyRemovePartyPatch
        {
            private static void Postfix(MobileParty __instance)
            {
                if (__instance.Name.Equals("Bandit Militia"))
                {
                    if (__instance.LeaderHero != null)
                    {
                        Mod.Log("Removing Bandit Militia hero at MobileParty.RemoveParty", LogLevel.Debug);
                        __instance.LeaderHero?.KillHero();
                    }
                }
            }
        }
    }
}
