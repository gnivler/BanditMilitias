using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BanditMilitias.Helpers;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using TaleWorlds.TwoDimension;
using static BanditMilitias.Helpers.Helper;
using static BanditMilitias.Globals;

// ReSharper disable MemberCanBePrivate.Global

// ReSharper disable InconsistentNaming

namespace BanditMilitias
{
    public class MilitiaBehavior : CampaignBehaviorBase
    {
        private const double smallChance = 0.001;
        private static int cap;
        private const float increment = 5;
        private const float effectRadius = 100;
        private const int AdjustRadius = 50;
        private static Clan looters;
        internal static Clan Looters => looters ??= Clan.BanditFactions.First(c => c.StringId == "looters");
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
                if (m.PartiesOnSide(BattleSideEnum.Attacker)
                    .AnyQ(mep => mep.Party.MobileParty is not null && mep.Party.MobileParty.IsBM()))
                {
                    InformationManager.AddQuickInformation(new TextObject($"{m.MapEventSettlement?.Name} raided!  {m.PartiesOnSide(BattleSideEnum.Attacker).First().Party.Name} is fat with loot near {SettlementHelper.FindNearestTown().Name}!"));
                }
            });
            CampaignEvents.AiHourlyTickEvent.AddNonSerializedListener(this, AiHourlyTickEvent);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, DailyTickPartyEvent);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, FlushMilitiaCharacterObjects);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTickEvent);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, SynthesizeBM);
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, DetermineActivity);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, MobilePartyDestroyed);
        }


        private static void MobilePartyDestroyed(MobileParty mobileParty, PartyBase destroyer)
        {
            if (!Globals.Settings.AllowPillaging)
            {
                return;
            }

            int AvoidanceIncrease() => Rng.Next(20, 51);
            if (mobileParty.IsBM())
            {
                if (destroyer?.LeaderHero is null)
                {
                    return;
                }

                if (mobileParty.BM().Avoidance.ContainsKey(destroyer.LeaderHero))
                {
                    mobileParty.BM().Avoidance.Remove(destroyer.LeaderHero);
                }

                foreach (var BM in GetCachedBMs().WhereQ(bm =>
                             bm.MobileParty.Position2D.Distance(mobileParty.Position2D) < effectRadius))
                {
                    if (BM.Avoidance.ContainsKey(destroyer.LeaderHero))
                    {
                        BM.Avoidance[destroyer.LeaderHero] += AvoidanceIncrease();
                    }
                    else
                    {
                        BM.Avoidance.Add(destroyer.LeaderHero, AvoidanceIncrease());
                    }
                }
            }
        }

        private static void AiHourlyTickEvent(MobileParty bandit, PartyThinkParams partyThinkParams)
        {
            if (!bandit.IsBandit)
            {
                return;
            }

            if (Settlement.FindSettlementsAroundPosition(bandit.Position2D, MinDistanceFromHideout, s => s.IsHideout).Any())
            {
                DetermineActivity(bandit);
                return;
            }

            var nearbyBandits = MobileParty.FindPartiesAroundPosition(bandit.Position2D, FindRadius * 3).WhereQ(m => m.IsBandit).ToListQ();
            nearbyBandits.Remove(bandit);
            if (!nearbyBandits.Any())
            {
                DetermineActivity(bandit);
                return;
            }

            if (bandit.IsBM()
                && CampaignTime.Now < bandit.BM().LastMergedOrSplitDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
            {
                DetermineActivity(bandit);
                return;
            }

            var targetParties = nearbyBandits.Where(m =>
                m.MemberRoster.TotalManCount + bandit.MemberRoster.TotalManCount >= Globals.Settings.MinPartySize
                && IsAvailableBanditParty(m)).ToListQ();

            MobileParty mergeTarget = default;
            foreach (var target in targetParties.OrderByQ(m => m.Position2D.Distance(bandit.Position2D)))
            {
                var militiaTotalCount = bandit.MemberRoster.TotalManCount + target.MemberRoster.TotalManCount;
                if (militiaTotalCount < Globals.Settings.MinPartySize
                    || militiaTotalCount > Globals.CalculatedMaxPartySize
                    || militiaTotalCount < Globals.Settings.MinPartySize)

                {
                    continue;
                }

                if (target.IsBM())
                {
                    CampaignTime? targetLastChangeDate = target.BM().LastMergedOrSplitDate;
                    if (CampaignTime.Now < targetLastChangeDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
                    {
                        continue;
                    }
                }

                if (NumMountedTroops(bandit.MemberRoster) + NumMountedTroops(target.MemberRoster) > militiaTotalCount / 2)
                {
                    continue;
                }

                mergeTarget = target;
                break;
            }

            if (mergeTarget is null)
            {
                DetermineActivity(bandit);
                return;
            }

            //SubModule.Log($"==> counted {T.ElapsedTicks / 10000F:F3}ms.");
            if (Campaign.Current.Models.MapDistanceModel.GetDistance(mergeTarget, bandit) > MergeDistance)
            {
                //SubModule.Log($"{mobileParty} seeking > {targetParty.MobileParty}")
                AccessTools.Method(typeof(MobileParty), "SetAiBehavior")
                    .Invoke(bandit, new object[] { AiBehavior.EscortParty, mergeTarget.Party, mergeTarget.Position2D });
                AccessTools.Method(typeof(MobileParty), "SetAiBehavior")
                    .Invoke(mergeTarget, new object[] { AiBehavior.EscortParty, bandit.Party, bandit.Position2D });
                return;
            }

            //SubModule.Log($"==> found settlement {T.ElapsedTicks / 10000F:F3}ms."); 
            // create a new party merged from the two
            var rosters = MergeRosters(bandit, mergeTarget.Party);
            var clan = bandit.ActualClan ?? mergeTarget.ActualClan ?? Clan.BanditFactions.GetRandomElementInefficiently();
            var bm = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(clan), m => m.ActualClan = clan);
            InitMilitia(bm, rosters, bandit.Position2D);
            var calculatedAvoidance = new Dictionary<Hero, float>();
            if (bandit.PartyComponent is ModBanditMilitiaPartyComponent BM1)
            {
                foreach (var entry in BM1.Avoidance)
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

                if (mergeTarget.PartyComponent is ModBanditMilitiaPartyComponent BM2)
                {
                    foreach (var entry in BM2.Avoidance)
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
            }

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
                Trash(bandit);
                Trash(mergeTarget);
            }
            catch (Exception ex)
            {
                Log(ex);
            }

            DoPowerCalculations();
            //SubModule.Log($"==> Finished all work: {T.ElapsedTicks / 10000F:F3}ms.");
        }
        //SubModule.Log($"Looped ==> {T.ElapsedTicks / 10000F:F3}ms");


        private static void OnDailyTickEvent()
        {
            RemoveHeroesWithoutParty();
            FlushPrisoners();
        }

        public static void DetermineActivity(MobileParty mobileParty)
        {
            try
            {
                if (mobileParty.PartyComponent is ModBanditMilitiaPartyComponent BM)
                {
                    if (cap == 0)
                    {
                        cap = Convert.ToInt32(Village.All.CountQ() / 10f);
                    }

                    var target = mobileParty.TargetSettlement;
                    switch (mobileParty.Ai.AiState)
                    {
                        case AIState.Undefined:
                        case AIState.PatrollingAroundLocation when mobileParty.DefaultBehavior is AiBehavior.Hold or AiBehavior.None:
                        case AIState.Raiding when mobileParty.DefaultBehavior is not AiBehavior.RaidSettlement:
                            if (mobileParty.TargetSettlement is null)
                            {
                                target = SettlementHelper.GetRandomTown();
                            }

                            SetPartyAiAction.GetActionForPatrollingAroundSettlement(mobileParty, target);
                            mobileParty.Ai.SetAIState(AIState.PatrollingAroundLocation);
                            break;
                        case AIState.PatrollingAroundLocation:
                            // PILLAGE!
                            if (Globals.Settings.AllowPillaging
                                && mobileParty.LeaderHero is not null
                                && mobileParty.Party.TotalStrength > MilitiaPartyAveragePower
                                && Rng.NextDouble() < smallChance
                                && GetCachedBMs().CountQ(m => m.MobileParty.ShortTermBehavior is AiBehavior.RaidSettlement) <= cap)
                            {
                                target = SettlementHelper.FindNearestVillage(s =>
                                {
                                    if (s.IsRaided || s.IsUnderRaid || s.Owner is null || s.GetValue() <= 0)
                                    {
                                        return false;
                                    }

                                    if (BM.Avoidance.ContainsKey(s.Owner)
                                        && Rng.NextDouble() * 100 <= BM.Avoidance[s.Owner])
                                    {
                                        Log($"{new string('-', 100)} {mobileParty.Name} avoided pillaging {s}");
                                        return false;
                                    }

                                    return true;
                                }, mobileParty);
                                if (target?.OwnerClan == Hero.MainHero.Clan)
                                {
                                    InformationManager.AddQuickInformation(new TextObject($"{mobileParty.Name} is raiding your village {target?.Name} near {target?.Town?.Name}!"));
                                }

                                SetPartyAiAction.GetActionForRaidingSettlement(mobileParty, target);
                                mobileParty.Ai.SetAIState(AIState.Raiding);
                            }

                            break;
                        case AIState.InfestingVillage:
                            Debugger.Break();
                            break;
                        case AIState.Raiding:
                            //Log($"{new string('*', 50)} {mobileParty.Name + " Pillage!",-20} {mobileParty.ItemRoster.TotalWeight} weight, {mobileParty.LeaderHero?.Gold} GOLD!");
                            //MobileParty.MainParty.Position2D = mobileParty.Position2D;
                            //MapScreen.Instance.TeleportCameraToMainParty();
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log(ex);
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
                            BM.Avoidance[kvp.Key] -= increment;
                        }
                        else if (kvp.Value < BM.Avoidance[kvp.Key])
                        {
                            BM.Avoidance[kvp.Key] += increment;
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
                // nothing so far with 3.7.0 on 1.7.2
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
