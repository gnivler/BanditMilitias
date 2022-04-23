using Bandit_Militias.Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using static Bandit_Militias.Globals;

// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    public class MilitiaBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, Helper.TryGrowing);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, Helper.SynthesizeBM);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, DailyTickParty);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, Helper.FlushMilitiaCharacterObjects);
            CampaignEvents.OnPartyRemovedEvent.AddNonSerializedListener(this, party => PartyMilitiaMap.Remove(party.MobileParty));
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

        private static void DailyTickParty(MobileParty mobileParty)
        {
            if (mobileParty.IsBM())
            {
                //var nearestVillage = Settlement.FindSettlementsAroundPosition(mobileParty.Position2D, 100, s => s.IsVillage).GetRandomElementInefficiently();
                //mobileParty.SetMoveRaidSettlement(nearestVillage);
                //mobileParty.SetPartyObjective(MobileParty.PartyObjective.Aggressive);
                if (!Helper.TrySplitParty(mobileParty))
                {
                    if (mobileParty.ShortTermBehavior is AiBehavior.None or AiBehavior.Hold)
                    {
                        Helper.SetMilitiaPatrol(mobileParty);
                    }
                }
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
        }
    }
}
