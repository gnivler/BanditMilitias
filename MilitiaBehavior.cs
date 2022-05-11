using System.Diagnostics;
using BanditMilitias.Helpers;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

// ReSharper disable InconsistentNaming

namespace BanditMilitias
{
    public class MilitiaBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, Helper.TryImproving);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, DailyTickParty);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, Helper.FlushMilitiaCharacterObjects);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTickEvent);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, Helper.SynthesizeBM);
            CampaignEvents.AiHourlyTickEvent.AddNonSerializedListener(this, AiHourlyTick);
        }

        private static void OnDailyTickEvent()
        {
            //RemoveHeroesWithoutParty();
            //FlushPrisoners();
        }

        private static void AiHourlyTick(MobileParty mobileParty, PartyThinkParams p)
        {
            if (mobileParty.IsBM())
            {
                p.DoNotChangeBehavior = true;
                switch (mobileParty.Ai.AiState)
                {
                    case AIState.Undefined:
                    case AIState.PatrollingAroundCenter:
                    case AIState.PatrollingAroundLocation when mobileParty.TargetSettlement is null:
                        var settlement = SettlementHelper.FindNearestSettlementToPoint(mobileParty.Position2D, s => s.IsVillage);
                        p.AIBehaviorScores.Add(new AIBehaviorTuple(settlement, AiBehavior.RaidSettlement), 1f);
                        settlement = Settlement.All.GetRandomElement();
                        p.AIBehaviorScores.Add(new AIBehaviorTuple(settlement, AiBehavior.PatrolAroundPoint), 0.1f);
                        break;
                    case AIState.InfestingVillage:
                    case AIState.Raiding:
                        Debugger.Break();


                        break;
                }
            }
        }


        private static void DailyTickParty(MobileParty mobileParty)
        {
            if (mobileParty.IsBM())
            {
                if (!Helper.TrySplitParty(mobileParty))
                {
                    Helper.SetMilitiaPatrol(mobileParty);
                }
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
        }
    }
}
