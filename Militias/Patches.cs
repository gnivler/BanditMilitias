using System;
using System.Linq;
using HarmonyLib;
using SandBox.CampaignBehaviors.Towns;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.SandBox.Issues;
using TaleWorlds.Core;
using TaleWorlds.Localization;
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
                    settlement.OwnerClan = Clan.BanditFactions.ToList()[Rng.Next(1, Clan.BanditFactions.Count())];
                }
            }
        }

        // fixing vanilla?
        [HarmonyPatch(typeof(NearbyBanditBaseIssueBehavior), "FindSuitableHideout")]
        public static class NearbyBanditBaseIssueBehaviorFindSuitableHideoutPatch
        {
            private const float num = float.MaxValue;

            // taken from CapturedByBountyHuntersIssue because this class' version throws
            private static bool Prefix(Hero issueOwner, Settlement __result)
            {
                foreach (var settlement in Settlement.FindAll(x => x.Hideout != null))
                {
                    if (Campaign.Current.Models.MapDistanceModel.GetDistance(
                        issueOwner.GetMapPoint(), settlement, (55f < num) ? 55f : num, out var num2) && num2 < num)
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

                    // first clause prevents it from running for the 'other' party in a merge
                    // also prevent a militia compromised of more than 50% calvary because ouch
                    Traverse.Create(__instance).Property("TargetParty").SetValue(targetParty.MobileParty);
                    if (__instance.TargetParty?.MoveTargetParty == __instance)
                    {
                        return;
                    }

                    // don't rejoin with a split hero BUG BUG BUG
                    if (targetParty.LeaderHero != null && targetParty.MemberRoster.TotalManCount == 1)
                    {
                        return;
                    }

                    // conditions check
                    var militiaStrength = targetParty.TotalStrength + __instance.Party.TotalStrength;
                    var militiaCavalryCount = GetMountedTroopHeadcount(__instance.MemberRoster) +
                                              GetMountedTroopHeadcount(targetParty.MemberRoster);
                    var militiaTotalCount = __instance.MemberRoster.TotalManCount + targetParty.MemberRoster.TotalManCount;
                    if (militiaStrength < MaxPartyStrength &&
                        militiaTotalCount < AvgHeroPartyMaxSize &&
                        militiaCavalryCount < militiaTotalCount / 2)
                    {
                        Trace($"{__instance} is suitable for merge");
                        var distance = targetParty.Position2D.Distance(__instance.Position2D);
                        // the FindAll is returning pretty fast, small sample average was 600 ticks
                        var closeHideOuts = Settlement.FindAll(x => x.IsHideout())
                            .Where(x => targetParty.Position2D.Distance(x.Position2D) < MinDistanceFromHideout).ToList();
                        // avoid using bandits near hideouts
                        if (closeHideOuts.Any())
                        {
                            //Trace($"Within {minDistanceFromHideout} distance of hideout - skipping");
                            return;
                        }

                        if (distance <= MergeDistance)
                        {
                            // create a new party merged from the two
                            var mobileParty = MBObjectManager.Instance.CreateObject<MobileParty>("Bandit_Militia");
                            mobileParty.HomeSettlement = Settlement.FindFirst(x => x.IsHideout());
                            MergeParties(__instance, targetParty, mobileParty);

                            // figure out whether to replace hero with target party's higher level hero
                            var banditHero = __instance.LeaderHero ??
                                             targetParty.LeaderHero ??
                                             HeroCreator.CreateHeroAtOccupation(Occupation.Outlaw);
                            Clan existingClan = null;
                            if (__instance.LeaderHero != null &&
                                targetParty.LeaderHero != null)
                            {
                                banditHero = SelectBanditHero(__instance, targetParty, mobileParty, out existingClan);
                            }

                            __instance.RemoveParty();
                            targetParty.MobileParty.RemoveParty();

                            // add some reward money, it doesn't all go to loot
                            banditHero.ChangeHeroGold(Rng.Next(minGoldGift, maxGoldGift + 1));
                            // TODO add a few free level-ups for troops?
                            // skip 0 - Looters.  They fuck stuff up;  infighting, bandits taking each other prisoner and ransoming   
                            mobileParty.MemberRoster.AddToCounts(banditHero.CharacterObject, 1, true);
                            // setting the owner affiliates the party with a kingdom for some reason, need to then set HomeSettlement
                            mobileParty.Party.Owner = banditHero;
                            mobileParty.ChangePartyLeader(banditHero.CharacterObject);
                            Traverse.Create(mobileParty.LeaderHero.Clan).Method("RemoveHero", mobileParty.LeaderHero);
                            // set the faction BACK to a bandit faction and remove it from the clan's false registry of it
                            banditHero.Clan = existingClan ?? Clan.BanditFactions.ToList()[Rng.Next(1, 5)];
                            Traverse.Create(banditHero.Clan).Method("RemoveHero", banditHero);
                            // home has to be set to a hideout to make party aggressive (see PartyBase.MapFaction)
                            var hideout = Settlement.FindAll(x => x.IsHideout() &&
                                                                  x.MapFaction != CampaignData.NeutralFaction).GetRandomElement();
                            Traverse.Create(banditHero).Property("HomeSettlement").SetValue(hideout);

                            LogMilitiaFormed(mobileParty);
                            banditHero.Name = new TextObject("Bandit Militia");
                            mobileParty.Party.Visuals.SetMapIconAsDirty();
                        }
                        // don't move to the other party if they are already moving to us
                        else if (targetParty.MobileParty?.TargetParty?.MoveTargetParty != __instance)
                        {
                            Traverse.Create(__instance).Property("MoveTargetParty").SetValue(targetParty.MobileParty);
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
