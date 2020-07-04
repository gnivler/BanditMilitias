using System;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using SandBox.CampaignBehaviors.Towns;
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
        // fixing vanilla?
        [HarmonyPatch(typeof(IssuesCampaignBehavior), "OnSettlementEntered")]
        public static class IssuesCampaignBehaviorOnSettlementEnteredPatch
        {
            private static void Prefix(ref Settlement settlement)
            {
                if (settlement.OwnerClan == null)
                {
                    Mod.Log("Fixing bad settlement: " + settlement.Name, LogLevel.Debug);
                    settlement.OwnerClan = Clan.BanditFactions.ToList()[Rng.Next(1, 5)];
                }
            }
        }

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
                try
                {
                    if (!IsValidParty(__instance))
                    {
                        return;
                    }

                    // check daily each bandit party against the size factor and a random chance to split up
                    TrySplitUpParty(__instance);
                }
                catch (Exception ex)
                {
                    Mod.Log(ex, LogLevel.Error);
                }
            }
        }

        [HarmonyPatch(typeof(MobileParty), "HourlyTick")]
        public static class MobilePartyHourlyTickPatch
        {
            private static void Postfix(MobileParty __instance)
            {
                try
                {
                    if (!IsValidParty(__instance))
                    {
                        return;
                    }

                    var targetParty = MobileParty.FindPartiesAroundPosition(
                            __instance.Position2D,
                            SearchRadius,
                            x => x != __instance &&
                                 x.IsBandit &&
                                 IsValidParty(x)
                        )
                        .FirstOrDefault()?.Party;

                    // "nobody" is a valid answer
                    if (targetParty == null)
                    {
                        return;
                    }

                    // prevents snowballing
                    // bug doesn't even fire
                    if (__instance.ShortTermBehavior == AiBehavior.FleeToPoint ||
                        __instance.DefaultBehavior == AiBehavior.FleeToPoint)
                    {
                        Mod.Log(__instance.ShortTermBehavior, LogLevel.Debug);
                        Mod.Log(__instance.DefaultBehavior, LogLevel.Debug);
                        return;
                    }

                    // you shouldn't get here, something is spawning wrong
                    if (targetParty.LeaderHero != null && targetParty.MemberRoster.TotalManCount == 1)
                    {
                        Mod.Log("BUG you shouldn't get here, something is spawning wrong, stacktrace:", LogLevel.Error);
                        FileLog.Log(new StackTrace().ToString());
                        targetParty.LeaderHero.KillHero();
                        return;
                    }

                    // conditions check
                    var militiaStrength = targetParty.TotalStrength + __instance.Party.TotalStrength;
                    var militiaCavalryCount = NumMountedTroops(__instance.MemberRoster) + NumMountedTroops(targetParty.MemberRoster);
                    var militiaTotalCount = __instance.MemberRoster.TotalManCount + targetParty.MemberRoster.TotalManCount;
                    if (militiaStrength < MaxPartyStrength &&
                        militiaTotalCount < MaxPartySize &&
                        militiaCavalryCount < militiaTotalCount / 2)
                    {
                        var distance = Campaign.Current.Models.MapDistanceModel.GetDistance(targetParty.MobileParty, __instance);
                        var closeHideOuts = Settlement.FindAll(
                            x => x.IsHideout() &&
                                 Campaign.Current.Models.MapDistanceModel.GetDistance(targetParty.MobileParty, x) < MinDistanceFromHideout).ToList();

                        // avoid using bandits near hideouts
                        if (closeHideOuts.Any())
                        {
                            return;
                        }

                        if (distance <= MergeDistance &&
                            targetParty.MobileParty.IsAlone() &&
                            __instance.IsAlone())
                        {
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
                        else if (targetParty.MobileParty.IsAlone())
                        {
                            Mod.Log($"{__instance} Seeking target {targetParty.MobileParty}", LogLevel.Debug);
                            __instance.SetMoveGoToPoint(targetParty.Position2D);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Mod.Log(ex, LogLevel.Error);
                }
            }
        }
    }
}
