using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BanditMilitias.Helpers;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using TaleWorlds.TwoDimension;
using static BanditMilitias.Helpers.Helper;
using static BanditMilitias.Globals;

// ReSharper disable InconsistentNaming

namespace BanditMilitias
{
    public class MilitiaBehavior : CampaignBehaviorBase
    {
        private const double SmallChance = 0.0005;
        public const float Increment = 5;
        private const float EffectRadius = 100;
        private const int AdjustRadius = 50;
        private static Clan looters;
        public static Clan Looters => looters ??= Clan.BanditFactions.First(c => c.StringId == "looters");
        private static IEnumerable<Clan> synthClans;
        private static IEnumerable<Clan> SynthClans => synthClans ??= Clan.BanditFactions.Except(new[] { Looters });

        public override void RegisterEvents()
        {
            CampaignEvents.VillageBeingRaided.AddNonSerializedListener(this, v =>
            {
                if (Globals.Settings.ShowRaids
                    && v.Owner?.LeaderHero == Hero.MainHero
                    && v.Settlement.Party?.MapEvent is not null
                    && v.Settlement.Party.MapEvent.PartiesOnSide(BattleSideEnum.Attacker)
                        .AnyQ(m => m.Party.MobileParty is not null && m.Party.MobileParty.IsBM()))
                {
                    InformationManager.AddQuickInformation(new TextObject($"{v.Name} is being raided by {v.Settlement.Party.MapEvent.PartiesOnSide(BattleSideEnum.Attacker).First().Party.Name}!"));
                }
            });
            CampaignEvents.RaidCompletedEvent.AddNonSerializedListener(this, (b, m) =>
            {
                if (Globals.Settings.ShowRaids
                    && m.PartiesOnSide(BattleSideEnum.Attacker)
                        .AnyQ(mep => mep.Party.MobileParty is not null && mep.Party.MobileParty.IsBM()))
                {
                    InformationManager.AddQuickInformation(new TextObject($"{m.MapEventSettlement?.Name} raided!  {m.PartiesOnSide(BattleSideEnum.Attacker).First().Party.Name} is fat with loot near {SettlementHelper.FindNearestTown().Name}!"));
                }
            });
            CampaignEvents.TickPartialHourlyAiEvent.AddNonSerializedListener(this, TickPartialHourlyAiEvent);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, DailyTickPartyEvent);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTickEvent);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, SynthesizeBM);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, MobilePartyDestroyed);
        }

        private static void MobilePartyDestroyed(MobileParty mobileParty, PartyBase destroyer)
        {
            // Avoidance-bomb all BMs in the area
            int AvoidanceIncrease() => Rng.Next(15, 35);
            if (!mobileParty.IsBM() || destroyer?.LeaderHero is null)
            {
                return;
            }

            if (mobileParty.GetBM().Avoidance.TryGetValue(destroyer.LeaderHero, out _))
            {
                mobileParty.GetBM().Avoidance.Remove(destroyer.LeaderHero);
            }

            foreach (var BM in GetCachedBMs().WhereQ(bm =>
                         bm.MobileParty.Position2D.Distance(mobileParty.Position2D) < EffectRadius))
            {
                if (BM.Avoidance.TryGetValue(destroyer.LeaderHero, out _))
                {
                    BM.Avoidance[destroyer.LeaderHero] += AvoidanceIncrease();
                }
                else
                {
                    BM.Avoidance.Add(destroyer.LeaderHero, AvoidanceIncrease());
                }
            }
        }

        private static void TickPartialHourlyAiEvent(MobileParty mobileParty)
        {
            if (mobileParty.PartyComponent is not (BanditPartyComponent or ModBanditMilitiaPartyComponent))
            {
                return;
            }

            // near any Hideouts?
            if (mobileParty.PartyComponent is ModBanditMilitiaPartyComponent
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
                if (militiaTotalCount < Globals.Settings.MinPartySize
                    || militiaTotalCount > Globals.CalculatedMaxPartySize)
                {
                    continue;
                }

                if (target.IsBM())
                {
                    CampaignTime? targetLastChangeDate = target.GetBM().LastMergedOrSplitDate;
                    if (CampaignTime.Now < targetLastChangeDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
                    {
                        continue;
                    }
                }

                if (NumMountedTroops(mobileParty.MemberRoster) + NumMountedTroops(target.MemberRoster) > militiaTotalCount / 2)
                {
                    continue;
                }

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
                //Log($"{new string('>', 100)} MOVING {mobileParty.StringId,20} {mergeTarget.StringId,20}");
                mobileParty.SetMoveEscortParty(mergeTarget);
                mergeTarget.SetMoveEscortParty(mobileParty);
                return;
            }

            //Log($"{new string('=', 100)} MERGING {mobileParty.StringId,20} {mergeTarget.StringId,20}");
            // create a new party merged from the two
            var rosters = MergeRosters(mobileParty, mergeTarget.Party);
            var clan = mobileParty.ActualClan ?? mergeTarget.ActualClan ?? Clan.BanditFactions.GetRandomElementInefficiently();
            var bm = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(clan), m => m.ActualClan = clan);
            InitMilitia(bm, rosters, mobileParty.Position2D);
            // each BM gets the average of Avoidance values
            var calculatedAvoidance = new Dictionary<Hero, float>();

            void CalcAverageAvoidance(ModBanditMilitiaPartyComponent BM)
            {
                foreach (var entry in BM.Avoidance)
                {
                    if (!calculatedAvoidance.ContainsKey(entry.Key))
                    {
                        calculatedAvoidance.Add(entry.Key, entry.Value);
                    }
                    else
                    {
                        calculatedAvoidance[entry.Key] += entry.Value;
                        calculatedAvoidance[entry.Key] /= 2;
                    }
                }
            }

            if (mobileParty.PartyComponent is ModBanditMilitiaPartyComponent BM1)
            {
                CalcAverageAvoidance(BM1);
            }

            if (mergeTarget.PartyComponent is ModBanditMilitiaPartyComponent BM2)
            {
                CalcAverageAvoidance(BM2);
            }

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
                // can throw if Clan is null
                Trash(mobileParty);
                Trash(mergeTarget);
            }
            catch (Exception ex)
            {
                Log(ex);
            }

            DoPowerCalculations();
        }

        private static void OnDailyTickEvent()
        {
            FlushMilitiaCharacterObjects();
            RemoveHeroesWithoutParty();
            FlushPrisoners();
        }

        public static void BMThink(MobileParty mobileParty)
        {
            var target = mobileParty.TargetSettlement;
            if (mobileParty.ShortTermBehavior == AiBehavior.FleeToPoint && mobileParty.ShortTermTargetParty is { IsBandit: true }) Meow();
            switch (mobileParty.Ai.AiState)
            {
                case AIState.Undefined:
                case AIState.PatrollingAroundLocation when mobileParty.DefaultBehavior is AiBehavior.Hold or AiBehavior.None:
                    if (mobileParty.TargetSettlement is null)
                    {
                        target = SettlementHelper.GetRandomTown();
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

                        if (target is null)
                        {
                            Meow();
                            return;
                        }

                        var BM = mobileParty.GetBM();
                        if (BM is null)
                        {
                            return;
                        }

                        if (BM.Avoidance.ContainsKey(target.Owner)
                            && Rng.NextDouble() * 100 <= BM.Avoidance[target.Owner])
                        {
                            Log($"{new string('-', 100)} {mobileParty.Name} avoided pillaging {target}");
                            break;
                        }

                        if (target.OwnerClan == Hero.MainHero.Clan)
                        {
                            InformationManager.AddQuickInformation(new TextObject($"{mobileParty.Name} is raiding your village {target.Name} near {target.Town?.Name}!"));
                        }

                        //Log($"{new string('=', 100)} {target.Village.VillageState}");
                        mobileParty.SetMoveRaidSettlement(target);
                    }

                    break;
            }
        }


        public static void DailyTickPartyEvent(MobileParty mobileParty)
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

                //SetMilitiaPatrol(mobileParty);
                TrySplitParty(mobileParty);
            }
        }

        public static void AdjustAvoidance(MobileParty mobileParty)
        {
            //Log($"{mobileParty.Name} starting Avoidance {mobileParty.BM().Avoidance}");
            foreach (var BM in GetCachedBMs(true)
                         .WhereQ(bm => bm.Leader is not null && bm.MobileParty.Position2D.Distance(mobileParty.Position2D) < AdjustRadius))
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
            //Log($"{mobileParty.Name} finished Avoidance {mobileParty.BM().Avoidance}");
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
                    Log($"Growing {mobileParty.Name}, total: {mobileParty.MemberRoster.TotalManCount}");
                    for (var i = 0; i < growthAmount && mobileParty.MemberRoster.TotalManCount + 1 < CalculatedMaxPartySize; i++)
                    {
                        var troop = eligibleToGrow.GetRandomElement().Character;
                        if (GlobalMilitiaPower + troop.GetPower() < CalculatedGlobalPowerLimit)
                        {
                            mobileParty.MemberRoster.AddToCounts(troop, 1);
                        }
                    }

                    MurderMounts(mobileParty.MemberRoster);
                    //var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
                    //var strengthString = $"{Math.Round(mobileParty.Party.TotalStrength)} strength";
                    //Log($"{$"Grown to",-70} | {troopString,10} | {strengthString,12} |");
                    DoPowerCalculations();
                    // Log($"Grown to: {mobileParty.MemberRoster.TotalManCount}");
                }
            }
        }

        public static void SynthesizeBM()
        {
            if (!Globals.Settings.MilitiaSpawn)
            {
                return;
            }

            for (var i = 0;
                 MilitiaPowerPercent + 5 <= Globals.Settings.GlobalPowerPercent
                 && i < (Globals.Settings.GlobalPowerPercent - MilitiaPowerPercent) / 24f;
                 i++)
            {
                if (Rng.Next(0, 101) > Globals.Settings.SpawnChance)
                {
                    continue;
                }

                var settlement = Settlement.All.Where(s => !s.IsVisible).GetRandomElementInefficiently();
                var nearbyBandits = MobileParty.FindPartiesAroundPosition(settlement.Position2D, 100).WhereQ(m => m.IsBandit).ToListQ();
                Clan clan;
                if (!nearbyBandits.Any())
                {
                    clan = Looters;
                }
                else
                {
                    var cultureMap = new Dictionary<Clan, int>();
                    {
                        foreach (var party in nearbyBandits)
                        {
                            if (party.LeaderHero is null)
                            {
                                continue;
                            }

                            if (cultureMap.ContainsKey(party.ActualClan))
                            {
                                cultureMap[party.ActualClan]++;
                            }
                            else
                            {
                                cultureMap.Add(party.ActualClan, 1);
                            }
                        }
                    }
                    clan = cultureMap.Count == 0 || cultureMap.OrderByDescending(x => x.Value).First().Key == Looters
                        ? Looters
                        : SynthClans.First(c => c == cultureMap.OrderByDescending(x => x.Value).First().Key);
                }

                var min = Convert.ToInt32(Globals.Settings.MinPartySize);
                var max = Convert.ToInt32(CalculatedMaxPartySize);
                var roster = TroopRoster.CreateDummyTroopRoster();
                var size = Convert.ToInt32(Rng.Next(min, max + 1) / 2f);
                roster.AddToCounts(clan.BasicTroop, size);
                roster.AddToCounts(Looters.BasicTroop, size);
                MurderMounts(roster);
                var bm = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(clan), m => m.ActualClan = clan);
                InitMilitia(bm, new[] { roster, TroopRoster.CreateDummyTroopRoster() }, settlement.GatePosition);

                // teleport new militias near the player
                if (Globals.Settings.TestingMode)
                {
                    // in case a prisoner
                    var party = Hero.MainHero.PartyBelongedTo ?? Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;
                    bm.Position2D = party.Position2D;
                }
            }

            DoPowerCalculations();
        }

        // TODO verify if needed post-1.7.2
        public static void FlushMilitiaCharacterObjects()
        {
            var COs = MBObjectManager.Instance.GetObjectTypeList<CharacterObject>();
            var BMs = COs.WhereQ(c => c.HeroObject?.PartyBelongedTo is null
                                      && c.StringId.EndsWith("Bandit_Militia")
                                      && c.HeroObject is not null
                                      && !c.HeroObject.IsFactionLeader).ToList();
            if (BMs.Any())
            {
                // nothing so far with 3.7.4 on 1.7.2
                Debugger.Break();
                Log($">>> FLUSH {BMs.Count} BM CharacterObjects");
                Log(new StackTrace());
                BMs.Do(c => MBObjectManager.Instance.UnregisterObject(c));
                var charactersField = Traverse.Create(Campaign.Current).Field<MBReadOnlyList<CharacterObject>>("_characters");
                var tempCharacterObjectList = new List<CharacterObject>(charactersField.Value);
                tempCharacterObjectList = tempCharacterObjectList.Except(BMs).ToListQ();
                charactersField.Value = new MBReadOnlyList<CharacterObject>(tempCharacterObjectList);
            }

            //Log("");
            //Log($"{new string('=', 80)}\nBMs: {PartyMilitiaMap.Count,-4} Power: {GlobalMilitiaPower} / Power Limit: {CalculatedGlobalPowerLimit} = {GlobalMilitiaPower / CalculatedGlobalPowerLimit * 100:f2}% (limit {Globals.Settings.GlobalPowerPercent}%)");
            //Log("");
        }

        public override void SyncData(IDataStore dataStore)
        {
        }
    }
}
