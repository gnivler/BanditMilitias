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
            //RemoveHeroesWithoutParty();
            //FlushPrisoners();
        }

        private static void SynthesizeBM()
        {
            if (!Globals.Settings.MilitiaSpawn)
            {
                return;
            }

            for (var i = 0;
                 MilitiaPowerPercent <= Globals.Settings.GlobalPowerPercent
                 && i < (Globals.Settings.GlobalPowerPercent - MilitiaPowerPercent) / 24f;
                 i++)
            {
                if (Rng.Next(0, 101) > Globals.Settings.SpawnChance)
                {
                    continue;
                }

                var settlement = Settlement.All.Where(s => !s.IsVisible).GetRandomElementInefficiently();
                var clan = Clan.BanditFactions.ToList()[Rng.Next(0, Clan.BanditFactions.Count())];
                var min = Convert.ToInt32(Globals.Settings.MinPartySize);
                var max = Convert.ToInt32(CalculatedMaxPartySize);
                var roster = TroopRoster.CreateDummyTroopRoster();
                roster.AddToCounts(clan.BasicTroop, Rng.Next(min, max + 1));
                var numMounted = NumMountedTroops(roster);
                var mountedTroops = roster.ToFlattenedRoster().Troops.WhereQ(t => t.IsMounted);
                // remove horses past 50% of the BM
                if (numMounted > roster.TotalManCount / 2)
                {
                    foreach (var troop in mountedTroops)
                    {
                        if (NumMountedTroops(roster) > roster.TotalManCount / 2)
                        {
                            troop.Equipment[10] = new EquipmentElement();
                            troop.Equipment[11] = new EquipmentElement();
                        }
                        else
                        {
                            break;
                        }
                    }
                }


                var militia = new Militia(settlement.GatePosition, roster, TroopRoster.CreateDummyTroopRoster());
                // teleport new militias near the player
                if (Globals.Settings.TestingMode)
                {
                    // in case a prisoner
                    var party = Hero.MainHero.PartyBelongedTo ?? Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;
                    militia.MobileParty.Position2D = party.Position2D;
                }

                //Trash(mobileParty);
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
                        && !mobileParty.IsVisible)
                    .ToListQ();
                if (eligibleToGrow.Any())
                {
                    var growthAmount = mobileParty.MemberRoster.TotalManCount * Globals.Settings.GrowthPercent / 100f;
                    // bump up growth to reach GlobalPowerPercent (synthetic but it helps warm up militia population)
                    // thanks Erythion!
                    var boost = CalculatedGlobalPowerLimit / GlobalMilitiaPower;
                    growthAmount += Globals.Settings.GlobalPowerPercent / 100f * boost;
                    growthAmount = Mathf.Clamp(growthAmount, 1, 50);
                    Mod.Log($"Growing {mobileParty.Name}, total: {mobileParty.MemberRoster.TotalManCount}");
                    for (var i = 0; i < growthAmount && mobileParty.MemberRoster.TotalManCount + 1 < CalculatedMaxPartySize; i++)
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
