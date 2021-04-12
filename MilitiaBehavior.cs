using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using static Bandit_Militias.Globals;
using static Bandit_Militias.Helpers.Helper;

// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    public class MilitiaBehavior : CampaignBehaviorBase
    {
        private static readonly bool Growth = Globals.Settings.GrowthFactor > 0;

        public override void RegisterEvents()
        {
            CampaignEvents.AfterDailyTickEvent.AddNonSerializedListener(this, DailyCalculations);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, TryGrowing);
            CampaignEvents.OnPartyRemovedEvent.AddNonSerializedListener(this, OnMilitiaRemoved);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, StopHoldingBehavior);
        }

        // kludge to unstick stuck militias
        private static void StopHoldingBehavior(MobileParty mobileParty)
        {
            if (IsBM(mobileParty) &&
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
            if (!IsBM(partyBase.MobileParty))
            {
                return;
            }

            Mod.Log($">>> OnMilitiaRemoved - {partyBase.Name}.");
            if (partyBase.MobileParty.LeaderHero?.CurrentSettlement != null)
            {
                Traverse.Create(HeroesWithoutParty(partyBase.MobileParty.LeaderHero?.CurrentSettlement)).Field<List<Hero>>("_list").Value.Remove(partyBase.MobileParty.LeaderHero);
                Mod.Log($">>> FLUSH OnMilitiaRemoved bandit hero without party - {partyBase.MobileParty.LeaderHero.Name} at {partyBase.MobileParty.LeaderHero?.CurrentSettlement}.");
            }

            PartyMilitiaMap.Remove(partyBase.MobileParty);
        }

        private static void TryGrowing(MobileParty mobileParty)
        {
            try
            {
                if (Growth &&
                    mobileParty.ShortTermBehavior != AiBehavior.FleeToPoint &&
                    IsValidParty(mobileParty) &&
                    IsBM(mobileParty) &&
                    ((float) GlobalMilitiaPower / CalculatedGlobalPowerLimit < Globals.Settings.GrowthFactor ||
                     Rng.NextDouble() <= Globals.Settings.GrowthChance))
                {
                    var eligibleToGrow = mobileParty.MemberRoster.GetTroopRoster().Where(x =>
                        x.Character.Tier < Globals.Settings.MaxTrainingTier &&
                        !x.Character.IsHero &&
                        mobileParty.ShortTermBehavior != AiBehavior.FleeToPoint).ToList();
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
