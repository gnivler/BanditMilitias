using System;
using System.Linq;
using Bandit_Militias.Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using static Bandit_Militias.Globals;

// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    public class MilitiaBehavior : CampaignBehaviorBase
    {
        private static readonly bool Growth = Globals.Settings.GrowthFactor > 0;

        public override void RegisterEvents()
        {
            CampaignEvents.AfterDailyTickEvent.AddNonSerializedListener(this, Helper.DailyCalculations);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, TryGrowing);
            CampaignEvents.OnPartyRemovedEvent.AddNonSerializedListener(this, OnMilitiaRemoved);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, StopHoldingBehavior);
        }

        private static void StopHoldingBehavior(MobileParty mobileParty)
        {
            if (mobileParty.StringId.StartsWith("Bandit_Militia") &&
                (mobileParty.ShortTermBehavior == AiBehavior.Hold ||
                mobileParty.ShortTermBehavior == AiBehavior.None ||
                mobileParty.DefaultBehavior == AiBehavior.Hold ||
                mobileParty.DefaultBehavior == AiBehavior.None))
            {
                Traverse.Create(mobileParty).Property<AiBehavior>("ShortTermBehavior").Value = AiBehavior.PatrolAroundPoint;
                Traverse.Create(mobileParty).Property<AiBehavior>("DefaultBehavior").Value = AiBehavior.PatrolAroundPoint;
            }
        }

        private static void OnMilitiaRemoved(PartyBase partyBase)
        {
            if (!partyBase.MobileParty.StringId.StartsWith("Bandit_Militia"))
            {
                return;
            }

            Mod.Log($"OnMilitiaRemoved {partyBase.Name}");
            MergeMap.Remove(partyBase.MobileParty);
            Militias.Remove(Militia.FindMilitiaByParty(partyBase.MobileParty));
        }

        private static void TryGrowing(MobileParty mobileParty)
        {
            try
            {
                if (Growth &&
                    Helper.IsValidParty(mobileParty) &&
                    mobileParty.StringId.StartsWith("Bandit_Militia") &&
                    ((float) GlobalMilitiaPower / CalculatedGlobalPowerLimit < Globals.Settings.GrowthFactor ||
                     Rng.NextDouble() <= Globals.Settings.GrowthChance))
                {
                    var eligibleToGrow = mobileParty.MemberRoster.GetTroopRoster().Where(x => x.Character.Tier < Globals.Settings.MaxTrainingTier && !x.Character.IsHero).ToList();
                    if (eligibleToGrow.Any())
                    {
                        Mod.Log($"TryGrowing {mobileParty.LeaderHero}, total: {mobileParty.MemberRoster.TotalManCount}");
                        var growthAmount = mobileParty.MemberRoster.TotalManCount * Globals.Settings.GrowthFactor;
                        // bump up growth to reach GrowthFactor
                        // Growth cap % - current % = additional
                        growthAmount += Globals.Settings.GlobalPowerFactor * 100 - (float) GlobalMilitiaPower / CalculatedGlobalPowerLimit * 100;
                        growthAmount = Math.Max(1, growthAmount);
                        var growthRounded = Convert.ToInt32(growthAmount);
                        // last condition doesn't account for the size increase but who cares
                        if (mobileParty.MemberRoster.TotalManCount + growthRounded > CalculatedMaxPartySize ||
                            GlobalMilitiaPower + mobileParty.Party.TotalStrength > CalculatedGlobalPowerLimit)
                        {
                            return;
                        }

                        var iterations = Convert.ToInt32((float) growthRounded / eligibleToGrow.Count);
                        for (var i = 0; i < iterations; i++)
                        {
                            var amount = Convert.ToInt32((float) growthRounded / iterations);
                            if (iterations % 2 != 0 && i + 1 == iterations)
                            {
                                // final loop, add the leftover troop randomly
                                mobileParty.MemberRoster.AddToCounts(eligibleToGrow.GetRandomElement().Character, amount + 1);
                            }
                            else
                            {
                                mobileParty.MemberRoster.AddToCounts(eligibleToGrow.GetRandomElement().Character, amount);
                            }
                        }

                        var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
                        var strengthString = $"{Math.Round(mobileParty.Party.TotalStrength)} strength";
                        Mod.Log($"{$"Grown to",-70} | {troopString,10} | {strengthString,12} |");
                        // Mod.Log($"Grown to: {mobileParty.MemberRoster.TotalManCount}"); 
                    }
                }
            }
            catch (Exception ex)
            {
                Mod.Log(ex);
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
        }
    }
}
