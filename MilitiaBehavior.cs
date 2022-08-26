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
        private const int settlementFindRange = 200;


        private static readonly AccessTools.FieldRef<BasicCharacterObject, TextObject> basicName =
            AccessTools.FieldRefAccess<BasicCharacterObject, TextObject>("_basicName");

        private static readonly AccessTools.FieldRef<BasicCharacterObject, MBBodyProperty> bodyPropertyRange =
            AccessTools.FieldRefAccess<BasicCharacterObject, MBBodyProperty>("<BodyPropertyRange>k__BackingField");

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
            CampaignEvents.RaidCompletedEvent.AddNonSerializedListener(this, (b, m) =>
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
            //CampaignEvents.MapEventEnded.AddNonSerializedListener(this, MapEventEnded);
        }

        //public static void MapEventEnded(MapEvent mapEvent)
        //{
        //    MapEventSide Loser()
        //    {
        //        return mapEvent.BattleState == BattleState.DefenderVictory
        //            ? mapEvent.AttackerSide
        //            : mapEvent.DefenderSide;
        //    }
        //
        //
        //    if (!mapEvent.HasWinner) return;
        //    Globals.LootRecord.Remove(Loser());
        //}

        private static void MobilePartyDestroyed(MobileParty mobileParty, PartyBase destroyer)
        {
            // Avoidance-bomb all BMs in the area
            int AvoidanceIncrease() => Rng.Next(15, 35);
            if (!mobileParty.IsBM() || destroyer?.LeaderHero is null)
            {
                return;
            }

            destroyer.MobileParty.GetBM()?.Avoidance.Remove(mobileParty.LeaderHero);
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
                return;

            if (mobileParty.MapEvent != null || mobileParty.IsCurrentlyUsedByAQuest)
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
                && m.MapEvent == null
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
                    if (!calculatedAvoidance.TryGetValue(entry.Key, out _))
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
                // can throw if Clan is null (doesn't happen in 3.9 apparently)
                Trash(mobileParty);
                Trash(mergeTarget);
            }
            catch (Exception ex)
            {
                Log(ex);
            }

            DoPowerCalculations();
        }

        public static void BMThink(MobileParty mobileParty)
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
                            Log($"||| {mobileParty.Name} avoided pillaging {target}");
                            break;
                        }

                        if (target.OwnerClan == Hero.MainHero.Clan)
                        {
                            InformationManager.DisplayMessage(new InformationMessage($"{mobileParty.Name} is raiding your village {target.Name} near {target.Town?.Name}!"));
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
                //if (mobileParty.MapEvent?.State == MapEventState.WaitingRemoval && mobileParty.MemberRoster.Count == 0) Meow();
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
            //Log($"{mobileParty.Name} finished Avoidance {mobileParty.BM().Avoidance}");
        }

        public static void TryGrowing(MobileParty mobileParty)
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
                        && !mobileParty.IsVisible
                        && !BanditMilitiaCharacters.Contains(rosterElement.Character))
                    .ToListQ();
                if (eligibleToGrow.Any())
                {
                    var growthAmount = mobileParty.MemberRoster.TotalManCount * Globals.Settings.GrowthPercent / 100f;
                    // bump up growth to reach GlobalPowerPercent (synthetic but it helps warm up militia population)
                    // thanks Erythion!
                    var boost = CalculatedGlobalPowerLimit / GlobalMilitiaPower;
                    growthAmount += Globals.Settings.GlobalPowerPercent / 100f * boost;
                    growthAmount = Mathf.Clamp(growthAmount, 1, 50);
                    Log($"+++ Growing {mobileParty.Name}, total: {mobileParty.MemberRoster.TotalManCount}");
                    for (var i = 0; i < growthAmount && mobileParty.MemberRoster.TotalManCount + 1 < CalculatedMaxPartySize; i++)
                    {
                        var troop = eligibleToGrow.GetRandomElement().Character;
                        if (GlobalMilitiaPower + troop.GetPower() < CalculatedGlobalPowerLimit)
                        {
                            mobileParty.MemberRoster.AddToCounts(troop, 1);
                        }
                    }

                    AdjustCavalryCount(mobileParty.MemberRoster);
                    //var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
                    //var strengthString = $"{Math.Round(mobileParty.Party.TotalStrength)} strength";
                    //Log($"{$"Grown to",-70} | {troopString,10} | {strengthString,12} |");
                    DoPowerCalculations();
                    // Log($"Grown to: {mobileParty.MemberRoster.TotalManCount}");
                }
            }
        }

        public static void SpawnBM()
        {
            if (!Globals.Settings.MilitiaSpawn)
            {
                return;
            }

            var settlement = Settlement.All.Where(s => !s.IsVisible && s.GetTrackDistanceToMainAgent() > 100).GetRandomElementInefficiently()
                             ?? Settlement.All.Where(s => s.GetTrackDistanceToMainAgent() > 200).GetRandomElementInefficiently(); // for cheats that make all IsVisible
            for (var i = 0;
                 MilitiaPowerPercent + 1 <= Globals.Settings.GlobalPowerPercent
                 && i < (Globals.Settings.GlobalPowerPercent - MilitiaPowerPercent) / 24f;
                 i++)
            {
                if (Rng.Next(0, 101) > Globals.Settings.SpawnChance)
                {
                    continue;
                }

                var clan = settlement.OwnerClan;
                var min = Convert.ToInt32(Globals.Settings.MinPartySize);
                var max = Convert.ToInt32(CalculatedMaxPartySize);
                var roster = TroopRoster.CreateDummyTroopRoster();
                var size = Convert.ToInt32(Rng.Next(min, max + 1) / 2f);
                roster.AddToCounts(clan.BasicTroop, size);
                roster.AddToCounts(Looters.BasicTroop, size);
                AdjustCavalryCount(roster);
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

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("BanditMilitiaHeroes", ref Globals.BanditMilitiaHeroes);
            dataStore.SyncData("BanditMilitiaCharacters", ref Globals.BanditMilitiaCharacters);
            dataStore.SyncData("BanditMilitiaTroops", ref Globals.BanditMilitiaTroops);
            if (!Globals.Settings.UpgradeTroops)
                return;
            dataStore.SyncData("EquipmentMap", ref Globals.EquipmentMap);
            if (dataStore.IsLoading)
            {
                foreach (var troop in BanditMilitiaTroops)
                {
                    MBObjectManager.Instance.RegisterPresumedObject(troop);
                    BanditMilitiaTroops.Remove(troop);
                    // TODO not sure if rehydrate is needed in 3.9 with change to SyncData
                    //RehydrateCharacterObject(new TroopRosterElement(troop));
                }
            }
        }

        //private static void RehydrateCharacterObject(TroopRosterElement troop)
        //{
        //    troop.Character.Culture = troop.Character.OriginalCharacter.Culture;
        //    basicName(troop.Character) = new TextObject("Upgraded " + troop.Character.OriginalCharacter.Name);
        //    Traverse.Create(troop.Character).Field<MBCharacterSkills>("CharacterSkills").Value =
        //        Traverse.Create(troop.Character.OriginalCharacter).Field<MBCharacterSkills>("CharacterSkills").Value;
        //    var bodyProps = new MBBodyProperty();
        //    bodyProps.Init(troop.Character.OriginalCharacter.GetBodyPropertiesMin(), troop.Character.OriginalCharacter.GetBodyPropertiesMax());
        //    bodyPropertyRange(troop.Character) = bodyProps;
        //    var mbEquipmentRoster = new MBEquipmentRoster();
        //    Equipments(mbEquipmentRoster) = new List<Equipment> { EquipmentMap[troop.Character.StringId] };
        //    EquipmentRoster(troop.Character) = mbEquipmentRoster;
        //    Traverse.Create(troop.Character).Property<CharacterObject[]>("UpgradeTargets").Value = troop.Character.OriginalCharacter.UpgradeTargets;
        //    MBObjectManager.Instance.RegisterObject(troop.Character);
        //    BanditMilitiaTroops.Add(troop.Character);
        //}
    }
}
