using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BanditMilitias.Helpers;
using HarmonyLib;
using Helpers;
using SandBox.View.Map;
using SandBox.ViewModelCollection.MobilePartyTracker;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
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
        private static float lastChecked;
        private const double smallChance = 0.001;
        private static int cap;
        private const int CheckInterval = 1;
        private const float increment = 5;
        private const float effectRadius = 100;
        private const int AdjustRadius = 50;
        private static Clan looters;
        internal static Clan Looters => looters ??= Clan.BanditFactions.First(c => c.StringId == "looters");
        private static IEnumerable<Clan> synthClans;
        private static IEnumerable<Clan> SynthClans => synthClans ??= Clan.BanditFactions.Except(new[] { Looters });
        internal static List<MobileParty> Parties = new();

        public override void RegisterEvents()
        {
            CampaignEvents.VillageBeingRaided.AddNonSerializedListener(this, v =>
            {
                if (v.Settlement.Party?.MapEvent is not null
                    && v.Settlement.Party.MapEvent.PartiesOnSide(BattleSideEnum.Attacker)
                        .AnyQ(m => m.Party.MobileParty is not null && m.Party.MobileParty.IsBM()))
                {
                    InformationManager.AddQuickInformation(new TextObject($"{v.Name} is being raided by {v.Settlement.Party.MapEvent.PartiesOnSide(BattleSideEnum.Attacker).First().Party.Name}!"));
                }
            });
            CampaignEvents.RaidCompletedEvent.AddNonSerializedListener(this, (b, m) =>
            {
                {
                    InformationManager.AddQuickInformation(new TextObject($"{m.MapEventSettlement?.Name} successfully raided!"));
                }
            });
            CampaignEvents.TickEvent.AddNonSerializedListener(this, MergingTick);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, DailyTickPartyEvent);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, FlushMilitiaCharacterObjects);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTickEvent);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, SynthesizeBM);
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, AiTickPartyEvent);
            CampaignEvents.OnPartyRemovedEvent.AddNonSerializedListener(this, MobilePartyRemoved);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, MobilePartyDestroyed);
            CampaignEvents.MobilePartyCreated.AddNonSerializedListener(this, m =>
            {
                // this fires before IsBandit is set, so BM are not added here
                if (m.IsBandit)
                {
                    Parties.Add(m);
                }
            });
        }

        private static void MobilePartyRemoved(PartyBase party)
        {
            Parties.Remove(party.MobileParty);
        }

        private static void MobilePartyDestroyed(MobileParty mobileParty, PartyBase destroyer)
        {
            int AvoidanceIncrease() => Rng.Next(20, 51);
            Parties.Remove(mobileParty);
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

        private static void MergingTick(float dT)
        {
            if (Campaign.Current.TimeControlMode == CampaignTimeControlMode.Stop
                || Campaign.Current.TimeControlMode == CampaignTimeControlMode.UnstoppableFastForwardForPartyWaitTime
                || Campaign.Current.TimeControlMode == CampaignTimeControlMode.FastForwardStop
                || Campaign.Current.TimeControlMode == CampaignTimeControlMode.StoppableFastForward)
            {
                return;
            }

            if (lastChecked == 0)
            {
                lastChecked = Campaign.CurrentTime;
            }

            // don't run this if paused and unless 3% off power limit
            if (Campaign.CurrentTime - lastChecked < CheckInterval
                || MilitiaPowerPercent + MilitiaPowerPercent / 100 * 0.03 > Globals.Settings.GlobalPowerPercent)
            {
                return;
            }

            lastChecked = Campaign.CurrentTime;
            var parties = new List<MobileParty>(MobileParty.All.WhereQ(m => m.IsBandit));
            foreach (var party in Parties)
            {
                if (party.CurrentSettlement is null
                    && !party.IsUsedByAQuest()
                    && party.MemberRoster.TotalManCount >= Globals.Settings.MergeableSize)
                {
                    parties.Add(party);
                }
            }

            for (var index = 0; index < parties.Count; index++)
            {
                //T.Restart();
                var mobileParty = parties[index];
                if (Hideouts.AnyQ(s => s.Position2D.Distance(mobileParty.Position2D) < MinDistanceFromHideout))
                {
                    continue;
                }

                if (mobileParty.IsTooBusyToMerge())
                {
                    continue;
                }

                var nearbyParties = MobileParty.FindPartiesAroundPosition(mobileParty.Position2D, FindRadius)
                    .Intersect(parties)
                    .ToListQ();
                //var nearbyParties = parties.WhereQ(m => m.Position2D.Distance(mobileParty.Position2D) <= FindRadius).ToListQ();
                nearbyParties.Remove(mobileParty);
                if (!nearbyParties.Any())
                {
                    continue;
                }

                if (mobileParty.StringId.Contains("manhunter")) // Calradia Expanded Kingdoms
                {
                    continue;
                }

                if (mobileParty.IsBM())
                {
                    if (CampaignTime.Now < mobileParty.BM()?.LastMergedOrSplitDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
                    {
                        continue;
                    }
                }

                var targetParties = nearbyParties.Where(m =>
                    m.MemberRoster.TotalManCount + mobileParty.MemberRoster.TotalManCount >= Globals.Settings.MinPartySize
                    && IsAvailableBanditParty(m)).ToListQ();
                var targetParty = targetParties.GetRandomElement()?.Party;
                //SubModule.Log($">T targetParty {T.ElapsedTicks / 10000F:F3}ms.");
                // "nobody" is a valid answer
                if (targetParty is null)
                {
                    continue;
                }

                if (targetParty.MobileParty.IsBM())
                {
                    CampaignTime? targetLastChangeDate = targetParty.MobileParty.BM()?.LastMergedOrSplitDate;
                    if (CampaignTime.Now < targetLastChangeDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
                    {
                        continue;
                    }
                }

                var militiaTotalCount = mobileParty.MemberRoster.TotalManCount + targetParty.MemberRoster.TotalManCount;
                if (Globals.MilitiaPowerPercent > Globals.Settings.GlobalPowerPercent
                    || militiaTotalCount > Globals.CalculatedMaxPartySize
                    || militiaTotalCount < Globals.Settings.MinPartySize
                    || NumMountedTroops(mobileParty.MemberRoster) + NumMountedTroops(targetParty.MemberRoster) > militiaTotalCount / 2)
                {
                    continue;
                }

                //SubModule.Log($"==> counted {T.ElapsedTicks / 10000F:F3}ms.");
                if (mobileParty != targetParty.MobileParty.MoveTargetParty
                    && Campaign.Current.Models.MapDistanceModel.GetDistance(targetParty.MobileParty, mobileParty) > 5)
                {
                    //SubModule.Log($"{mobileParty} seeking > {targetParty.MobileParty}");
                    mobileParty.SetMoveEscortParty(mobileParty);
                    mobileParty.Ai.SetDoNotMakeNewDecisions(true);
                    //SubModule.Log($"SetNavigationModeParty ==> {T.ElapsedTicks / 10000F:F3}ms");
                    if (targetParty.MobileParty.MoveTargetParty != mobileParty)
                    {
                        //SubModule.Log($"{targetParty.MobileParty} seeking back > {mobileParty}");
                        targetParty.MobileParty.SetMoveEscortParty(mobileParty);
                        targetParty.MobileParty.Ai.SetDoNotMakeNewDecisions(true);
                        //SubModule.Log($"SetNavigationModeTargetParty ==> {T.ElapsedTicks / 10000F:F3}ms");
                    }

                    continue;
                }

                //SubModule.Log($"==> found settlement {T.ElapsedTicks / 10000F:F3}ms."); 
                // create a new party merged from the two
                var rosters = MergeRosters(mobileParty, targetParty);
                var clan = mobileParty.ActualClan ?? targetParty.MobileParty.ActualClan ?? Clan.BanditFactions.GetRandomElementInefficiently();
                var bm = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(clan), m => m.ActualClan = clan);
                InitMilitia(bm, rosters, mobileParty.Position2D);
                var calculatedAvoidance = new Dictionary<Hero, float>();
                if (mobileParty.PartyComponent is ModBanditMilitiaPartyComponent BM1)
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

                    if (targetParty.MobileParty.PartyComponent is ModBanditMilitiaPartyComponent BM2)
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
                    Trash(mobileParty);
                    Trash(targetParty.MobileParty);
                }
                catch (Exception ex)
                {
                    Log(ex);
                }

                DoPowerCalculations();
                //SubModule.Log($"==> Finished all work: {T.ElapsedTicks / 10000F:F3}ms.");
            }
            //SubModule.Log($"Looped ==> {T.ElapsedTicks / 10000F:F3}ms");
        }

        private static void OnDailyTickEvent()
        {
            RemoveHeroesWithoutParty();
            FlushPrisoners();
        }

        public static void AiTickPartyEvent(MobileParty mobileParty)
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
                            if (mobileParty.LeaderHero is not null
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
                                        Log($"{new string('-', 100)} {mobileParty.Name} Avoiding {s}");
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
                if ((int)CampaignTime.Now.ToWeeks % CampaignTime.DaysInWeek == 0)
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
            foreach (var party in Parties.WhereQ(m => m.IsBM()
                                                      && m.LeaderHero is not null
                                                      && m.Position2D.Distance(mobileParty.Position2D) < AdjustRadius))
            {
                var BM = party.BM();
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
                 MilitiaPowerPercent <= Globals.Settings.GlobalPowerPercent
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

                //var militia = ModBanditMilitiaPartyComponent.CreateBanditParty(settlement.GatePosition, roster, TroopRoster.CreateDummyTroopRoster());
                var bm = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(clan), m => m.ActualClan = clan);
                InitMilitia(bm, new[] { roster, TroopRoster.CreateDummyTroopRoster() }, settlement.GatePosition);

                // teleport new militias near the player
                if (Globals.Settings.TestingMode)
                {
                    // in case a prisoner
                    var party = Hero.MainHero.PartyBelongedTo ?? Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;
                    bm.Position2D = party.Position2D;
                }

                DoPowerCalculations();
                return;
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
            Parties = Parties.Distinct().ToListQ();
            dataStore.SyncData("Parties", ref Parties);
        }
    }
}
