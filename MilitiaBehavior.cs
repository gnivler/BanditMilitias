using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using static Bandit_Militias.Globals;
using static Bandit_Militias.Helpers.Helper;

// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    public class MilitiaBehavior : CampaignBehaviorBase
    {
        private static readonly bool Growth = Globals.Settings.GrowthPercent > 0;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, TryGrowing);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, DailyTickParty);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, FlushMilitiaCharacterObjects);
        }

        // todo un-kludge to unstick stuck militias  
        private static void DailyTickParty(MobileParty mobileParty)
        {
            if (mobileParty.IsBM() &&
                (mobileParty.ShortTermBehavior == AiBehavior.Hold ||
                 mobileParty.ShortTermBehavior == AiBehavior.None ||
                 mobileParty.DefaultBehavior == AiBehavior.Hold ||
                 mobileParty.DefaultBehavior == AiBehavior.None))
            {
                SetMilitiaPatrol(mobileParty);
                //var nearbySettlement = Settlement.FindSettlementsAroundPosition(mobileParty.Position2D, 30)?.ToList().GetRandomElement();
                //mobileParty.SetMovePatrolAroundSettlement(nearbySettlement ?? Settlement.All.GetRandomElement());
            }

            if (!IsValidParty(mobileParty))
            {
                return;
            }

            TrySplitParty(mobileParty);
        }

        private static void TryGrowing(MobileParty mobileParty)
        {
            try
            {
                if (Growth
                    && mobileParty.IsBM()
                    && mobileParty.ShortTermBehavior != AiBehavior.FleeToPoint
                    && IsValidParty(mobileParty)
                    && Rng.NextDouble() <= Globals.Settings.GrowthChance)
                {
                    var eligibleToGrow = mobileParty.MemberRoster.GetTroopRoster().Where(rosterElement =>
                        rosterElement.Character.Tier < Globals.Settings.MaxTrainingTier
                        && !rosterElement.Character.IsHero
                        && mobileParty.ShortTermBehavior != AiBehavior.FleeToPoint
                        && !mobileParty.IsVisible).ToList();
                    if (eligibleToGrow.Any())
                    {
                        Mod.Log($"TryGrowing {mobileParty.LeaderHero}, total: {mobileParty.MemberRoster.TotalManCount}");
                        var growthAmount = mobileParty.MemberRoster.TotalManCount * Globals.Settings.GrowthPercent;
                        // bump up growth to reach GlobalPowerFactor (synthetic but it helps warm up militia population)
                        // (Growth cap % - current %) / 2 = additional
                        growthAmount += (Globals.Settings.GlobalPowerFactor * 100 - GlobalMilitiaPower / CalculatedGlobalPowerLimit * 100) / 2;
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
                        DoPowerCalculations();
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
