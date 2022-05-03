using Bandit_Militias.Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using static Bandit_Militias.Globals;

// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    public class MilitiaBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, Helper.TryGrowing);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, DailyTickParty);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, Helper.FlushMilitiaCharacterObjects);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTickEvent);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, Helper.SynthesizeBM);
            //CampaignEvents.AiHourlyTickEvent.AddNonSerializedListener(this, AiHourlyTick);
            CampaignEvents.OnPartyRemovedEvent.AddNonSerializedListener(this, party => PartyMilitiaMap.Remove(party.MobileParty));
        }

        private static void OnDailyTickEvent()
        {
            //RemoveHeroesWithoutParty();
            //FlushPrisoners();
        }

        private static void AiHourlyTick(MobileParty mobileParty, PartyThinkParams p)
        {
            if (!mobileParty.IsBM())
            {
                return;
            }

            var settlement = Settlement.All.GetRandomElement();
            switch (mobileParty.Ai.AiState)
            {
                case AIState.Undefined:
                    p.AIBehaviorScores.Add(new AIBehaviorTuple(settlement, AiBehavior.PatrolAroundPoint), 1f);
                    break;
                case AIState.BesiegingCenter:
                    break;
                case AIState.PatrollingAroundCenter:
                    var t = mobileParty.IsMoving;
                    break;
                case AIState.PatrollingAroundLocation:
                    var x = mobileParty.IsMoving;
                    break;
                case AIState.VisitingHideout:
                    break;
                case AIState.InfestingVillage:
                    break;
                case AIState.Raiding:
                    break;
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
