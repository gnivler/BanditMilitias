using Bandit_Militias.Helpers;
using TaleWorlds.CampaignSystem;
using static Bandit_Militias.Globals;
using static Bandit_Militias.Helpers.Helper;

// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    public class MilitiaBehavior : CampaignBehaviorBase
    {
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


        public override void SyncData(IDataStore dataStore)
        {
        }
    }
}
