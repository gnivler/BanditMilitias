using System;
using System.Collections.Generic;
using System.Linq;
using BanditMilitias.Helpers;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.TwoDimension;
using static BanditMilitias.Helpers.Helper;
using static BanditMilitias.Globals;

// ReSharper disable InconsistentNaming

namespace BanditMilitias
{
    public class MilitiaBehavior : CampaignBehaviorBase
    {
        private const double SmallChance = 0.0005;
        internal const float Increment = 5;
        private const float EffectRadius = 100;
        private const int AdjustRadius = 50;
        private const int settlementFindRange = 200;

        public override void RegisterEvents()
        {
            CampaignEvents.VillageBeingRaided.AddNonSerializedListener(this, v =>
            {
                if (Globals.Settings.ShowRaids
                    && v.Owner?.LeaderHero == Hero.MainHero
                    && v.Settlement.Party?.MapEvent is not null
                    && v.Settlement.Party.MapEvent.PartiesOnSide(BattleSideEnum.Attacker)
                        .AnyQ(m => m.Party.IsMobile && m.Party.MobileParty.IsBM()))
                {
                    InformationManager.DisplayMessage(
                        new InformationMessage($"{v.Name} is being raided by {v.Settlement.Party.MapEvent.PartiesOnSide(BattleSideEnum.Attacker).First().Party.Name}!"));
                }
            });
            CampaignEvents.RaidCompletedEvent.AddNonSerializedListener(this, (_, m) =>
            {
                if (Globals.Settings.ShowRaids
                    && m.PartiesOnSide(BattleSideEnum.Attacker)
                        .AnyQ(mep => mep.Party.IsMobile && mep.Party.MobileParty.IsBM()))
                {
                    InformationManager.DisplayMessage(
                        new InformationMessage($"{m.MapEventSettlement?.Name} raided!  " +
                                               $"{m.PartiesOnSide(BattleSideEnum.Attacker).First().Party.Name} is fat with loot near {SettlementHelper.FindNearestTown().Name}!"));
                }
            });

            CampaignEvents.TickPartialHourlyAiEvent.AddNonSerializedListener(this, TickPartialHourlyAiEvent);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, DailyTickPartyEvent);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, SpawnBM);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, MobilePartyDestroyed);
        }

        private static void MobilePartyDestroyed(MobileParty mobileParty, PartyBase destroyer)
        {
            // Avoidance-bomb all BMs in the area
            int AvoidanceIncrease() => Rng.Next(15, 35);
            if (!mobileParty.IsBM() || destroyer?.LeaderHero is null)
                return;

            destroyer.MobileParty.GetBM()?.Avoidance.Remove(mobileParty.LeaderHero);
            foreach (var BM in GetCachedBMs().WhereQ(bm =>
                         bm.MobileParty.Position2D.Distance(mobileParty.Position2D) < EffectRadius))
            {
                if (BM.Avoidance.TryGetValue(destroyer.LeaderHero, out _))
                    BM.Avoidance[destroyer.LeaderHero] += AvoidanceIncrease();
                else
                    BM.Avoidance.Add(destroyer.LeaderHero, AvoidanceIncrease());
            }
        }

        private static void TickPartialHourlyAiEvent(MobileParty mobileParty)
        {
            if (mobileParty.PartyComponent is not (BanditPartyComponent or ModBanditMilitiaPartyComponent))
                return;

            if (mobileParty.MemberRoster.TotalManCount < Globals.Settings.MergeableSize)
                return;

            // they will evacuate hideouts and not chase caravans
            if (mobileParty.PartyComponent is BanditPartyComponent)
            {
                if ((mobileParty.CurrentSettlement is not null
                     && mobileParty.AiBehaviorMapEntity is Settlement { IsHideout: true })
                    || mobileParty.AiBehaviorMapEntity is MobileParty { IsCaravan: true })
                    return;
            }

            if (mobileParty.MapEvent is not null)
                return;

            // near any Hideouts?
            if (mobileParty.IsBM()
                && Settlement.FindSettlementsAroundPosition(mobileParty.Position2D, MinDistanceFromHideout, s => s.IsHideout).Any())
            {
                BMThink(mobileParty);
                return;
            }

            // BM changed too recently?
            if (mobileParty.IsBM()
                && CampaignTime.Now < mobileParty.GetBM().LastMergedOrSplitDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
            {
                BMThink(mobileParty);
                return;
            }

            var nearbyBandits = MobileParty.FindPartiesAroundPosition(mobileParty.Position2D, FindRadius).WhereQ(m =>
                m.IsBandit
                && m.MapEvent is null
                && m.MemberRoster.TotalManCount > Globals.Settings.MergeableSize
                && m.MemberRoster.TotalManCount + mobileParty.MemberRoster.TotalManCount >= Globals.Settings.MinPartySize
                && IsAvailableBanditParty(m)).ToListQ();
            nearbyBandits.Remove(mobileParty);
            if (!nearbyBandits.Any())
            {
                BMThink(mobileParty);
                return;
            }

            MobileParty mergeTarget = default;
            foreach (var target in nearbyBandits.OrderByQ(m => m.Position2D.Distance(mobileParty.Position2D)))
            {
                var militiaTotalCount = mobileParty.MemberRoster.TotalManCount + target.MemberRoster.TotalManCount;
                if (militiaTotalCount < Globals.Settings.MinPartySize || militiaTotalCount > CalculatedMaxPartySize)
                    continue;

                if (target.IsBM())
                {
                    CampaignTime? targetLastChangeDate = target.GetBM().LastMergedOrSplitDate;
                    if (CampaignTime.Now < targetLastChangeDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
                        continue;
                }

                if (NumMountedTroops(mobileParty.MemberRoster) + NumMountedTroops(target.MemberRoster) > militiaTotalCount / 2)
                    continue;

                mergeTarget = target;
                break;
            }

            if (mergeTarget is null)
            {
                BMThink(mobileParty);
                return;
            }

            if (Campaign.Current.Models.MapDistanceModel.GetDistance(mergeTarget, mobileParty) > MergeDistance
                && mobileParty.TargetParty != mergeTarget)
            {
                //Log.Debug?.Log($"{new string('>', 100mobileParty.SetMoveEscortParty(mergeTarget);)} MOVING {mobileParty.StringId,20} {mergeTarget.StringId,20}");
                mobileParty.SetMoveEscortParty(mergeTarget);
                mergeTarget.SetMoveEscortParty(mobileParty);
                return;
            }

            //Log.Debug?.Log($"{new string('=', 100)} MERGING {mobileParty.StringId,20} {mergeTarget.StringId,20}");
            // create a new party merged from the two
            var rosters = MergeRosters(mobileParty, mergeTarget);
            var clan = mobileParty.ActualClan ?? mergeTarget.ActualClan ?? Clan.BanditFactions.GetRandomElementInefficiently();
            var bm = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(clan), m => m.ActualClan = clan);
            InitMilitia(bm, rosters, mobileParty.Position2D);
            // each BM gets the average of Avoidance values
            var calculatedAvoidance = new Dictionary<Hero, float>();

            void CalcAverageAvoidance(ModBanditMilitiaPartyComponent BM)
            {
                foreach (var entry in BM.Avoidance)
                    if (!calculatedAvoidance.TryGetValue(entry.Key, out _))
                        calculatedAvoidance.Add(entry.Key, entry.Value);
                    else
                    {
                        calculatedAvoidance[entry.Key] += entry.Value;
                        calculatedAvoidance[entry.Key] /= 2;
                    }
            }

            if (mobileParty.PartyComponent is ModBanditMilitiaPartyComponent BM1)
                CalcAverageAvoidance(BM1);

            if (mergeTarget.PartyComponent is ModBanditMilitiaPartyComponent BM2)
                CalcAverageAvoidance(BM2);

            bm.GetBM().Avoidance = calculatedAvoidance;
            // teleport new militias near the player
            if (Globals.Settings.TestingMode)
            {
                // in case a prisoner
                var party = Hero.MainHero.PartyBelongedTo ?? Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;
                bm.Position2D = party.Position2D;
            }

            bm.Party.Visuals.SetMapIconAsDirty();
            try
            {
                // can throw if Clan is null (doesn't happen in 3.9 apparently)
                Trash(mobileParty);
                Trash(mergeTarget);
            }
            catch (Exception ex)
            {
                Log.Debug?.Log(ex);
            }

            DoPowerCalculations();
        }

        internal static void BMThink(MobileParty mobileParty)
        {
            var target = mobileParty.TargetSettlement;
            switch (mobileParty.Ai.AiState)
            {
                case AIState.Undefined:
                case AIState.PatrollingAroundLocation when mobileParty.DefaultBehavior is AiBehavior.Hold or AiBehavior.None:
                    if (mobileParty.TargetSettlement is null)
                    {
                        while (target is null)
                        {
                            target = Settlement.All.GetRandomElementInefficiently();
                            if (target.Position2D.Distance(mobileParty.Position2D) < settlementFindRange)
                                break;
                        }
                    }

                    mobileParty.SetMovePatrolAroundSettlement(target);
                    break;
                case AIState.PatrollingAroundLocation:
                    // PILLAGE!
                    if (Globals.Settings.AllowPillaging
                        && mobileParty.LeaderHero is not null
                        && mobileParty.Party.TotalStrength > MilitiaPartyAveragePower
                        && Rng.NextDouble() < SmallChance
                        && GetCachedBMs().CountQ(m => m.MobileParty.ShortTermBehavior is AiBehavior.RaidSettlement) < RaidCap)
                    {
                        target = SettlementHelper.FindNearestVillage(s =>
                        {
                            // JetBrains Rider suggested this insanity
                            if (s.Village is { VillageState: Village.VillageStates.BeingRaided or Village.VillageStates.Looted }
                                || s.Owner is null
                                || s.GetValue() <= 0)
                            {
                                return false;
                            }

                            return true;
                        }, mobileParty);

                        var BM = mobileParty.GetBM();
                        if (BM is null)
                            return;

                        if (BM.Avoidance.ContainsKey(target.Owner)
                            && Rng.NextDouble() * 100 <= BM.Avoidance[target.Owner])
                        {
                            Log.Debug?.Log($"||| {mobileParty.Name} avoided pillaging {target}");
                            break;
                        }

                        if (target.OwnerClan == Hero.MainHero.Clan)
                            InformationManager.DisplayMessage(new InformationMessage($"{mobileParty.Name} is raiding your village {target.Name} near {target.Town?.Name}!"));

                        //Log.Debug?.Log($"{new string('=', 100)} {target.Village.VillageState}");
                        mobileParty.SetMoveRaidSettlement(target);
                    }

                    break;
            }
        }

        private static void DailyTickPartyEvent(MobileParty mobileParty)
        {
            if (mobileParty.IsBM())
            {
                if ((int)CampaignTime.Now.ToWeeks % CampaignTime.DaysInWeek == 0
                    && Globals.Settings.AllowPillaging)
                {
                    AdjustAvoidance(mobileParty);
                }

                TryGrowing(mobileParty);
                if (Rng.NextDouble() <= Globals.Settings.TrainingChance)
                {
                    TrainMilitia(mobileParty);
                }

                TrySplitParty(mobileParty);
            }
        }

        private static void AdjustAvoidance(MobileParty mobileParty)
        {
            foreach (var BM in GetCachedBMs(true)
                         .WhereQ(bm => bm.Leader is not null
                                       && bm.MobileParty.Position2D.Distance(mobileParty.Position2D) < AdjustRadius))
            {
                foreach (var kvp in BM.Avoidance)
                {
                    if (BM.Avoidance.ContainsKey(kvp.Key))
                    {
                        if (kvp.Value > BM.Avoidance[kvp.Key])
                        {
                            BM.Avoidance[kvp.Key] -= Increment;
                        }
                        else if (kvp.Value < BM.Avoidance[kvp.Key])
                        {
                            BM.Avoidance[kvp.Key] += Increment;
                        }
                    }
                }
            }
        }

        private static void TryGrowing(MobileParty mobileParty)
        {
            if (Globals.Settings.GrowthPercent > 0
                && MilitiaPowerPercent <= Globals.Settings.GlobalPowerPercent
                && mobileParty.ShortTermBehavior != AiBehavior.FleeToPoint
                && mobileParty.MapEvent is null
                && IsAvailableBanditParty(mobileParty)
                && Rng.NextDouble() <= Globals.Settings.GrowthChance / 100f)
            {
                var eligibleToGrow = mobileParty.MemberRoster.GetTroopRoster().WhereQ(rosterElement =>
                        rosterElement.Character.Tier < Globals.Settings.MaxTrainingTier
                        && !rosterElement.Character.IsHero
                        && mobileParty.ShortTermBehavior != AiBehavior.FleeToPoint
                        && !mobileParty.IsVisible
                        && !Heroes.Contains(rosterElement.Character.HeroObject))
                    .ToListQ();
                if (eligibleToGrow.Any())
                {
                    var growthAmount = mobileParty.MemberRoster.TotalManCount * Globals.Settings.GrowthPercent / 100f;
                    // bump up growth to reach GlobalPowerPercent (synthetic but it helps warm up militia population)
                    // thanks Erythion!
                    var boost = CalculatedGlobalPowerLimit / GlobalMilitiaPower;
                    growthAmount += Globals.Settings.GlobalPowerPercent / 100f * boost;
                    growthAmount = Mathf.Clamp(growthAmount, 1, 50);
                    //Log.Debug?.Log($"+++ Growing {mobileParty.Name}, total: {mobileParty.MemberRoster.TotalManCount}");
                    for (var i = 0; i < growthAmount && mobileParty.MemberRoster.TotalManCount + 1 < CalculatedMaxPartySize; i++)
                    {
                        var troop = eligibleToGrow.GetRandomElement().Character;
                        if (GlobalMilitiaPower + troop.GetPower() < CalculatedGlobalPowerLimit)
                        {
                            mobileParty.MemberRoster.AddToCounts(troop.OriginalCharacter ?? troop, 1);
                        }
                    }

                    AdjustCavalryCount(mobileParty.MemberRoster);
                    //var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
                    //var strengthString = $"{Math.Round(mobileParty.Party.TotalStrength)} strength";
                    //Log.Debug?.Log($"{$"Grown to",-70} | {troopString,10} | {strengthString,12} |");
                    DoPowerCalculations();
                    // Log.Debug?.Log($"Grown to: {mobileParty.MemberRoster.TotalManCount}");
                }
            }
        }

        private static void SpawnBM()
        {
            if (!Globals.Settings.MilitiaSpawn)
            {
                return;
            }

            try
            {
                var settlement = Settlement.All.WhereQ(s => !s.IsVisible && s.GetTrackDistanceToMainAgent() > 100).GetRandomElementInefficiently()
                                 ?? Settlement.All.WhereQ(s => s.GetTrackDistanceToMainAgent() > 200).GetRandomElementInefficiently(); // for cheats that make all IsVisible
                for (var i = 0;
                     MilitiaPowerPercent + 1 <= Globals.Settings.GlobalPowerPercent
                     && i < (Globals.Settings.GlobalPowerPercent - MilitiaPowerPercent) / 24f;
                     i++)
                {
                    if (Rng.Next(0, 101) > Globals.Settings.SpawnChance)
                    {
                        continue;
                    }


                    Clan clan;
                    // ROT
                    if (settlement.OwnerClan == Wights)
                        clan = Clan.BanditFactions.Except(new[] { Wights }).GetRandomElementInefficiently();
                    else
                        clan = settlement.OwnerClan;
                    var min = Convert.ToInt32(Globals.Settings.MinPartySize);
                    var max = Convert.ToInt32(CalculatedMaxPartySize);
                    // if the MinPartySize is cranked it will throw ArgumentOutOfRangeException
                    if (max < min)
                        max = min;
                    var roster = TroopRoster.CreateDummyTroopRoster();
                    var size = Convert.ToInt32(Rng.Next(min, max + 1) / 2f);
                    var foot = Rng.Next(40, 61);
                    var range = Rng.Next(20, Rng.Next(35, 100 - foot) + 1);
                    var horse = 100 - foot - range;
                    // DRM has no cavalry
                    if (Globals.BasicCavalry.Count == 0)
                    {
                        foot += horse % 2 == 0
                            ? horse / 2
                            : horse / 2 + 1;
                        range += horse / 2;
                        horse = 0;
                    }

                    var formation = new List<int>
                    {
                        foot, range, horse
                    };
                    for (var index = 0; index < formation.Count; index++)
                    {
                        for (var c = 0; c < formation[index] * size / 100f; c++)
                        {
                            switch (index)
                            {
                                case 0:
                                    roster.AddToCounts(Globals.BasicInfantry.GetRandomElement(), 1);
                                    break;
                                case 1:
                                    roster.AddToCounts(Globals.BasicRanged.GetRandomElement(), 1);
                                    break;
                                case 2:
                                    roster.AddToCounts(Globals.BasicCavalry.GetRandomElement(), 1);
                                    break;
                            }
                        }
                    }

                    var bm = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(clan), m => m.ActualClan = clan);
                    InitMilitia(bm, new[] { roster, TroopRoster.CreateDummyTroopRoster() }, settlement.GatePosition);
                    DoPowerCalculations();

                    // teleport new militias near the player
                    if (Globals.Settings.TestingMode)
                    {
                        // in case a prisoner
                        var party = Hero.MainHero.PartyBelongedTo ?? Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;
                        bm.Position2D = party.Position2D;
                    }
                }
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage("Problem spawning BM, please open a bug report with the log.txt file (Debug setting must be on)."));
                InformationManager.DisplayMessage(new InformationMessage($"{ex.Message}"));
                Log.Debug?.Log(ex);
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("Heroes", ref Heroes);
        }
    }
}
