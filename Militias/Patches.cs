using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SandBox.CampaignBehaviors.Towns;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.SandBox.Issues;
using TaleWorlds.ObjectSystem;
using static Bandit_Militias.Helper;
using static Bandit_Militias.Mod;
using static Bandit_Militias.Helper.Globals;

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
                    Log("Fixing bad settlement: " + settlement.Name);
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
                    Log(ex);
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

                    // find a nearby party that isn't already part of a merger
                    var pos = __instance.Position2D;
                    var targetParty = MobileParty.FindPartiesAroundPosition(pos, SearchRadius)
                        .FirstOrDefault(x => x != __instance && IsValidParty(x))?.Party;

                    // nobody else around SearchRadius
                    if (targetParty == null)
                    {
                        return;
                    }

                    // prevents snowballing
                    if (__instance.ShortTermBehavior == AiBehavior.FleeToPoint ||
                        __instance.DefaultBehavior == AiBehavior.FleeToPoint)
                    {
                        Trace(__instance.ShortTermBehavior);
                        Trace(__instance.DefaultBehavior);
                        return;
                    }

                    // BUG you shouldn't get here, something is spawning wrong
                    if (targetParty.LeaderHero != null && targetParty.MemberRoster.TotalManCount == 1)
                    {
                        Log(new string('*', 100));
                        Traverse.Create(typeof(KillCharacterAction))
                            .Method("MakeDead", targetParty.LeaderHero).GetValue();
                        MBObjectManager.Instance.UnregisterObject(targetParty.LeaderHero);
                        return;
                    }

                    // check MoveTarget to save cycles?  TODO
                    
                    // conditions check
                    var militiaStrength = targetParty.TotalStrength + __instance.Party.TotalStrength;
                    var militiaCavalryCount = GetMountedTroopHeadcount(__instance.MemberRoster) +
                                              GetMountedTroopHeadcount(targetParty.MemberRoster);
                    var militiaTotalCount = __instance.MemberRoster.TotalManCount + targetParty.MemberRoster.TotalManCount;
                    if (militiaStrength < MaxPartyStrength &&
                        militiaTotalCount < AvgHeroPartyMaxSize &&
                        militiaCavalryCount < militiaTotalCount / 2)
                    {
                        Trace($"{targetParty} is suitable for merge");
                        var distance = targetParty.Position2D.Distance(__instance.Position2D);
                        // the FindAll is returning pretty fast, small sample average was 600 ticks
                        // cache the result for performance over accuracy
                        // BUG possibly if the hideout disappeared in the instants between checks?
                        // does it matter much if they are moving around?
                        var closeHideOuts = new List<Settlement>();
                        closeHideOuts = closeHideOuts.Count == 0
                            ? Settlement.FindAll(x => x.IsHideout())
                                .Where(x => targetParty.Position2D.Distance(x.Position2D) < MinDistanceFromHideout).ToList()
                            : closeHideOuts;
                        // avoid using bandits near hideouts
                        if (closeHideOuts.Any())
                        {
                            //Trace($"Within {minDistanceFromHideout} distance of hideout - skipping");
                            return;
                        }

                        Trace($"Found a target for {__instance}, {__instance.MemberRoster.Count} troops: {targetParty}, troops {targetParty.MemberRoster.Count}");
                        if (distance <= MergeDistance)
                        {
                            // create a new party merged from the two
                            var rosters = MergeRosters(__instance, targetParty);
                            var militia = new Militia(__instance.Position2D, rosters[0], rosters[1]);
                            LogMilitiaFormed(militia.MobileParty);
                            // testing mode
                            if (testingMode)
                            {
                                militia.MobileParty.Position2D = Hero.MainHero.PartyBelongedTo.Position2D;
                            }
                            militia.MobileParty.Party.Visuals.SetMapIconAsDirty();
                            Trash(__instance);
                            Trash(targetParty.MobileParty);
                        }
                        else
                        {
                            __instance.SetMoveGoToPoint(targetParty.Position2D);
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
