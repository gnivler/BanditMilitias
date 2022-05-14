using System;
using System.Diagnostics;
using System.Security.Cryptography;
using BanditMilitias.Helpers;
using HarmonyLib;
using Helpers;
using StoryMode.GameComponents;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
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
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, TryImproving);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, FlushMilitiaCharacterObjects);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTickEvent);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, SynthesizeBM);
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, HourlyTickPartyEvent);
            //CampaignEvents.TickPartialHourlyAiEvent.AddNonSerializedListener(this, TickPartialHourlyAiEvent);
        }

        private static void OnDailyTickEvent()
        {
            RemoveHeroesWithoutParty();
            FlushPrisoners();
        }

        public static void HourlyTickPartyEvent(MobileParty mobileParty)
        {
            try
            {
                if (mobileParty.IsBM())
                {
                    var target = mobileParty.TargetSettlement;
                    switch (mobileParty.Ai.AiState)
                    {
                        case AIState.Undefined:
                            SetPartyAiAction.GetActionForPatrollingAroundSettlement(mobileParty, target);
                            mobileParty.Ai.SetAIState(AIState.PatrollingAroundLocation);
                            break;
                        case AIState.PatrollingAroundLocation:
                            if (Globals.Rng.NextDouble() < 0.1)
                            {
                                target = SettlementHelper.FindNearestVillage(null, mobileParty);
                                SetPartyAiAction.GetActionForRaidingSettlement(mobileParty, target);
                                mobileParty.Ai.SetAIState(AIState.Raiding);
                            }

                            break;
                        case AIState.InfestingVillage:
                            break;
                        case AIState.Raiding:
                            //Log($"{new string('*', 50)} {mobileParty.Name} {mobileParty.Position2D.Distance(target.Position2D)}");
                            //if (target.Position2D.Distance(mobileParty.Position2D) < 3)
                            //{
                            //
                            //}

                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }


        public override void SyncData(IDataStore dataStore)
        {
        }
    }
}
