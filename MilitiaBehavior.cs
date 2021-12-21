using System;
using System.Linq;
using Bandit_Militias.Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.TwoDimension;
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
            CampaignEvents.OnPartyRemovedEvent.AddNonSerializedListener(this, OnPartyRemoved);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTickEvent);
        }

        private static void OnDailyTickEvent()
        {
            if (!Globals.Settings.MilitiaSpawn)
            {
                return;
            }

            // ReSharper disable once PossibleLossOfFraction
            for (var i = 0; i < Globals.Settings.GlobalPowerPercent - MilitiaPowerPercent; i++)
            {
                SynthesizeBM();
            }
        }

        private static void SynthesizeBM()
        {
            var settlement = Settlement.All.Where(s => !s.IsVisible).GetRandomElementInefficiently();
            var clan = Clan.BanditFactions.ToList()[Rng.Next(0, Clan.BanditFactions.Count())];
            var mobileParty = settlement.IsHideout
                ? BanditPartyComponent.CreateBanditParty("Bandit_Militia", clan, settlement.Hideout, false)
                : BanditPartyComponent.CreateLooterParty("Bandit_Militia", clan, settlement, false);
            mobileParty.InitializeMobileParty(clan.DefaultPartyTemplate, settlement.GatePosition, 0);
            var simulatedMergedRoster = TroopRoster.CreateDummyTroopRoster();
            while (mobileParty.MemberRoster.TotalManCount != 0
                   && simulatedMergedRoster.TotalManCount < Globals.Settings.MinPartySize * Rng.Next(1, Globals.Settings.SpawnSizeMultiplier + 1))
            {
                simulatedMergedRoster.Add(mobileParty.MemberRoster);
            }

            var _ = new Militia(mobileParty.Position2D, simulatedMergedRoster, TroopRoster.CreateDummyTroopRoster());
            mobileParty.RemoveParty();
            DoPowerCalculations();
        }

        private static void OnPartyRemoved(PartyBase party)
        {
            PartyMilitiaMap.Remove(party.MobileParty);
        }

        private static void DailyTickParty(MobileParty mobileParty)
        {
            if (mobileParty.IsBM())
            {
                SetMilitiaPatrol(mobileParty);
            }

            if (!IsAvailableBanditParty(mobileParty))
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
                    && MilitiaPowerPercent < Globals.Settings.GlobalPowerPercent
                    && mobileParty.IsBM()
                    && mobileParty.ShortTermBehavior != AiBehavior.FleeToPoint
                    && IsAvailableBanditParty(mobileParty)
                    && Rng.NextDouble() <= Globals.Settings.GrowthChance / 100f)
                {
                    var eligibleToGrow = mobileParty.MemberRoster.GetTroopRoster().Where(rosterElement =>
                        rosterElement.Character.Tier < Globals.Settings.MaxTrainingTier
                        && !rosterElement.Character.IsHero
                        && mobileParty.ShortTermBehavior != AiBehavior.FleeToPoint
                        && !mobileParty.IsVisible).ToList();
                    if (eligibleToGrow.Any())
                    {
                        var growthAmount = mobileParty.MemberRoster.TotalManCount * Globals.Settings.GrowthPercent / 100f;
                        // bump up growth to reach GlobalPowerPercent (synthetic but it helps warm up militia population)
                        // (Growth cap % - current %) / 2 = additional
                        // thanks Erythion!
                        var boost = CalculatedGlobalPowerLimit / GlobalMilitiaPower;
                        growthAmount += Globals.Settings.GlobalPowerPercent / 100f * boost;
                        growthAmount = Mathf.Clamp(growthAmount, 1, 50);
                        var growthRounded = Convert.ToInt32(growthAmount);
                        // last condition doesn't account for the size increase but who cares
                        if (mobileParty.MemberRoster.TotalManCount + growthRounded > CalculatedMaxPartySize)
                        {
                            return;
                        }

                        Mod.Log($"Growing {mobileParty.Name}, total: {mobileParty.MemberRoster.TotalManCount}");
                        for (var i = 0; i < eligibleToGrow.Count; i++)
                        {
                            var troop = eligibleToGrow.GetRandomElement().Character;
                            if (GlobalMilitiaPower + troop.GetPower() < CalculatedGlobalPowerLimit)
                            {
                                mobileParty.MemberRoster.AddToCounts(troop, 1);
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
