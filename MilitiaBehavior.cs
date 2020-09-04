using System;
using Bandit_Militias.Helpers;
using TaleWorlds.CampaignSystem;
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
        }

        private static void OnMilitiaRemoved(PartyBase partyBase)
        {
            if (!partyBase.MobileParty.StringId.StartsWith("Bandit_Militia"))
            {
                return;
            }

            Mod.Log("OnMilitiaRemoved");
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
                    Rng.NextDouble() <= Globals.Settings.GrowthChance)
                {
                    Mod.Log($"TryGrowing {mobileParty.LeaderHero}, total: {mobileParty.MemberRoster.TotalManCount}");
                    var growthAmount = mobileParty.MemberRoster.TotalManCount * Globals.Settings.GrowthFactor;
                    growthAmount = Math.Max(1, growthAmount);
                    var rounded = Convert.ToInt32(growthAmount);
                    // last condition doesn't account for the size increase but who cares
                    if (mobileParty.MemberRoster.TotalManCount + rounded > CalculatedMaxPartySize ||
                        GlobalMilitiaPower + mobileParty.Party.TotalStrength > CalculatedGlobalPowerLimit)
                    {
                        return;
                    }

                    for (var i = 0; i < rounded; i++)
                    {
                        var index = Rng.Next(1, mobileParty.MemberRoster.Count);
                        mobileParty.MemberRoster.AddToCountsAtIndex(index, 1);
                    }

                    var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
                    var strengthString = $"{Math.Round(mobileParty.Party.TotalStrength)} strength";
                    Mod.Log($"{$"Grown to",-70} | {troopString,10} | {strengthString,12} |");
                    // Mod.Log($"Grown to: {mobileParty.MemberRoster.TotalManCount}");
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
