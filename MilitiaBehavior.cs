using System;
using System.Linq;
using Bandit_Militias.Helpers;
using TaleWorlds.CampaignSystem;
using static Bandit_Militias.Globals;

// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    public class MilitiaBehavior : CampaignBehaviorBase
    {
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
                if (Globals.Settings.Growth &&
                    mobileParty.StringId.StartsWith("Bandit_Militia") &&
                    Rng.NextDouble() <= Globals.Settings.GrowthChance)
                {
                    Mod.Log($"TryGrowing {mobileParty}, total: {mobileParty.MemberRoster.TotalManCount}");
                    foreach (var troopRosterElement in mobileParty.MemberRoster.Where(x => x.Character.HeroObject == null))
                    {
                        // calculate x percent of each unit type, need at least 0.5 to add a unit
                        var growth = troopRosterElement.Number * Globals.Settings.GrowthInPercent / 100;
                        var amount = Convert.ToInt32(Math.Max(1, growth));
                        // deliberately not 'topping up' militias so there aren't a bunch of same-sizes around
                        if (mobileParty.MemberRoster.TotalManCount + amount <= Globals.Settings.MaxPartySize)
                        {
                            mobileParty.MemberRoster.AddToCounts(troopRosterElement.Character, amount);
                        }
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
