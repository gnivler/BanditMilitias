using System;
using System.Diagnostics;
using BanditMilitias.Helpers;
using HarmonyLib;
using Helpers;
using StoryMode.GameComponents;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using static BanditMilitias.Helpers.Helper;

// ReSharper disable InconsistentNaming

namespace BanditMilitias
{
    public class MilitiaBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, Helper.TryImproving);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, Helper.FlushMilitiaCharacterObjects);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTickEvent);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, Helper.SynthesizeBM);
            CampaignEvents.AiHourlyTickEvent.AddNonSerializedListener(this, AiHourlyTick);
        }

        private static void OnDailyTickEvent()
        {
            RemoveHeroesWithoutParty();
            FlushPrisoners();
        }

        private static void AiHourlyTick(MobileParty mobileParty, PartyThinkParams p)
        {
            if (mobileParty.IsBM())
            {
                SetMilitiaPatrol(mobileParty);
                return;
                
                //p.DoNotChangeBehavior = false;
                switch (mobileParty.ShortTermBehavior)
                {
                    case AiBehavior.Hold:
                    case AiBehavior.None:
                        mobileParty.SetMovePatrolAroundSettlement(Settlement.All.GetRandomElement());
                        mobileParty.RecalculateShortTermAi();
                        Log($"{mobileParty.Name} patrolling {mobileParty.TargetSettlement} distance {mobileParty.Position2D.Distance(mobileParty.TargetSettlement.Position2D)}");
                        //Helper.SetMilitiaPatrol(mobileParty);
                        break;
                    case AiBehavior.GoToSettlement:
                        Debugger.Break();
                        break;
                    case AiBehavior.AssaultSettlement:
                        Debugger.Break();
                        break;
                    case AiBehavior.RaidSettlement when mobileParty.TargetSettlement is null:
                        mobileParty.SetMovePatrolAroundSettlement(Settlement.All.GetRandomElement());
                        mobileParty.RecalculateShortTermAi();
                        Log($"{mobileParty.Name} patrolling {mobileParty.TargetSettlement} distance {mobileParty.Position2D.Distance(mobileParty.TargetSettlement.Position2D)}");
                        break;
                    case AiBehavior.RaidSettlement :
                        Log($"{mobileParty.Name} raiding {mobileParty.TargetSettlement} distance {mobileParty.Position2D.Distance(mobileParty.TargetSettlement.Position2D)}");
                        //Debugger.Break();
                        break;
                    case AiBehavior.BesiegeSettlement:
                        Debugger.Break();
                        break;
                    case AiBehavior.EngageParty:
                        Debugger.Break();
                        break;
                    case AiBehavior.JoinParty:
                        Debugger.Break();
                        break;
                    case AiBehavior.GoAroundParty:
                        Debugger.Break();
                        break;
                    case AiBehavior.PatrolAroundPoint when mobileParty.TargetSettlement is null:
                    case AiBehavior.GoToPoint when mobileParty.TargetSettlement is null:
                        if (Globals.Rng.NextDouble() < 0.01)
                        {
                            var settlement = SettlementHelper.FindNearestSettlementToPoint(mobileParty.Position2D, s => s.IsVillage);
                            //p.AIBehaviorScores.Add(new AIBehaviorTuple(settlement, AiBehavior.RaidSettlement), 1f);
                            mobileParty.SetMoveRaidSettlement(settlement);
                            mobileParty.RecalculateShortTermAi();
                            Log($"{mobileParty.Name} raiding {settlement} distance {mobileParty.Position2D.Distance(settlement.Position2D)}");
                            //mobileParty.Ai.RethinkAtNextHourlyTick = false;
                        }

                        break;
                    case AiBehavior.FleeToPoint:
                        //Debugger.Break();
                        break;
                    case AiBehavior.FleeToGate:
                        Debugger.Break();
                        break;
                    case AiBehavior.FleeToParty:
                        Debugger.Break();
                        break;
                    case AiBehavior.EscortParty:
                        Debugger.Break();
                        break;
                    case AiBehavior.DefendSettlement:
                        Debugger.Break();
                        break;
                    case AiBehavior.DoOperation:
                        Debugger.Break();
                        break;
                }
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
        }
    }
}
