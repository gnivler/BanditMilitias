using System;
using System.Linq;
using Bandit_Militias.Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.LinQuick;
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
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, SynthesizeBM);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, DailyTickParty);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, FlushMilitiaCharacterObjects);
            CampaignEvents.OnPartyRemovedEvent.AddNonSerializedListener(this, OnPartyRemoved);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTickEvent);
            //CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        //private void OnSessionLaunched(CampaignGameStarter campaignGameStarter)
        //{
        //    AddDialogs(campaignGameStarter);
        //}

        //private static void AddDialogs(CampaignGameStarter cgs)
        //{
        //    cgs.AddPlayerLine("BM_001", "Input_001", "Output_001", "{=DEnFOGhS}Something!", null, null);
        //}

        private static void OnDailyTickEvent()
        {
            // TODO remove this temporary fix
            RemoveHeroesWithoutParty();
            FlushPrisoners();
        }

        private static void SynthesizeBM()
        {
            if (!Globals.Settings.MilitiaSpawn
                || CalculatedMaxPartySize < Globals.Settings.MinPartySize)
            {
                return;
            }

            for (var i = 0;
                 MilitiaPowerPercent <= Globals.Settings.GlobalPowerPercent
                 && i < (Globals.Settings.GlobalPowerPercent - MilitiaPowerPercent) / 24f;
                 i++)
            {
                if (Rng.Next(0, 10) != 0)
                {
                    continue;
                }

                var settlement = Settlement.All.Where(s => !s.IsVisible).GetRandomElementInefficiently();
                var clan = Clan.BanditFactions.ToList()[Rng.Next(0, Clan.BanditFactions.Count())];
                var mobileParty = settlement.IsHideout
                    ? BanditPartyComponent.CreateBanditParty("Bandit_Militia", clan, settlement.Hideout, false)
                    : BanditPartyComponent.CreateLooterParty("Bandit_Militia", clan, settlement, false);
                mobileParty.InitializeMobilePartyAroundPosition(clan.DefaultPartyTemplate, settlement.GatePosition, 0);
                // create an empty roster and stuff it with template roster copies
                var simulatedMergedRoster = TroopRoster.CreateDummyTroopRoster();
                for (var count = 0;
                     count < CalculatedMaxPartySize / Globals.Settings.MinPartySize
                     && simulatedMergedRoster.TotalManCount < CalculatedMaxPartySize
                     && NumMountedTroops(simulatedMergedRoster) <= simulatedMergedRoster.TotalManCount / 2;
                     count++)
                {
                    simulatedMergedRoster.Add(mobileParty.MemberRoster);
                }

                var _ = new Militia(mobileParty.Position2D, simulatedMergedRoster, TroopRoster.CreateDummyTroopRoster());
                Trash(mobileParty);
                DoPowerCalculations();
            }
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
            if (Growth
                && MilitiaPowerPercent <= Globals.Settings.GlobalPowerPercent
                && mobileParty.ShortTermBehavior != AiBehavior.FleeToPoint
                && mobileParty.IsBM()
                && IsAvailableBanditParty(mobileParty)
                && Rng.NextDouble() <= Globals.Settings.GrowthChance / 100f)
            {
                var eligibleToGrow = mobileParty.MemberRoster.GetTroopRoster().Where(rosterElement =>
                    rosterElement.Character.Tier < Globals.Settings.MaxTrainingTier
                    && !rosterElement.Character.IsHero
                    && mobileParty.ShortTermBehavior != AiBehavior.FleeToPoint
                    && !mobileParty.IsVisible).ToListQ();
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

                    //var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
                    //var strengthString = $"{Math.Round(mobileParty.Party.TotalStrength)} strength";
                    //Mod.Log($"{$"Grown to",-70} | {troopString,10} | {strengthString,12} |");
                    DoPowerCalculations();
                    // Mod.Log($"Grown to: {mobileParty.MemberRoster.TotalManCount}");
                }
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
        }
    }
}
