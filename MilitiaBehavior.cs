using System;
using System.Diagnostics;
using System.Linq;
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
using TaleWorlds.LinQuick;
using TaleWorlds.MountAndBlade.GauntletUI.Widgets;
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
                            const double smallChance = 0.00001;
                            const int hardCap = 5;
                            if (Globals.Rng.NextDouble() < smallChance
                                && GetAllBMs().CountQ(BM => BM.ShortTermBehavior is AiBehavior.RaidSettlement) <= hardCap)
                            {
                                target = SettlementHelper.FindNearestVillage(null, mobileParty);
                                SetPartyAiAction.GetActionForRaidingSettlement(mobileParty, target);
                                mobileParty.Ai.SetAIState(AIState.Raiding);
                            }

                            break;
                        case AIState.InfestingVillage:


                            Debugger.Break();
                            SetPartyAiAction.GetActionForRaidingSettlement(mobileParty, target);
                            break;
                        case AIState.Raiding:
                            Log($"{new string('*', 50)} {mobileParty.Name} Pillage! {mobileParty.ItemRoster.TotalWeight} weight, {mobileParty.LeaderHero.Gold} GOLD!");


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
