using System;
using System.Linq;
using Bandit_Militias.Helpers;
using TaleWorlds.CampaignSystem;
using static Bandit_Militias.Helpers.Globals;

// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    public class MilitiaBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, TryGrowing);
            CampaignEvents.OnPartyRemovedEvent.AddNonSerializedListener(this, x => MergeMap.Remove(x.MobileParty));
        }

        private static void TryGrowing(MobileParty mobileParty)
        {
            try
            {
                if (Globals.Settings.Growth &&
                    mobileParty.StringId.StartsWith("Bandit_Militia") &&
                    Rng.NextDouble() >= Globals.Settings.GrowthChance)
                {
                    Mod.Log($"TryGrowing {mobileParty}, total: {mobileParty.MemberRoster.TotalManCount}");
                    foreach (var troopRosterElement in mobileParty.MemberRoster.Where(x => x.Character.HeroObject == null))
                    {
                        // calculate x percent of each unit type, need at least 0.5 to add a unit
                        var growth = troopRosterElement.Number * Globals.Settings.GrowthInPercent / 100;

                        var amount = Convert.ToInt32(Math.Max(1, growth));
                        mobileParty.MemberRoster.AddToCounts(troopRosterElement.Character, amount);
                    }

                    Mod.Log($"Grown to: {mobileParty.MemberRoster.TotalManCount}");
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
