using System;
using System.Linq;
using Bandit_Militias.Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors;
using TaleWorlds.Core;
using TaleWorlds.TwoDimension;
using static Bandit_Militias.Globals;
using static Bandit_Militias.Helpers.Helper;

// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    public class MilitiaBehavior : CampaignBehaviorBase
    {
        private static readonly bool Growth = Globals.Settings.GrowthPercent > 0;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, TryGrowing);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, DailyTickParty);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, FlushMilitiaCharacterObjects);
            CampaignEvents.OnPartyRemovedEvent.AddNonSerializedListener(this, OnPartyRemoved);
        }

        private static void OnPartyRemoved(PartyBase party)
        {
            PartyMilitiaMap.Remove(party.MobileParty);
        }

        // todo un-kludge to unstick stuck militias  
        private static void DailyTickParty(MobileParty mobileParty)
        {
            if (mobileParty.IsBM() &&
                (mobileParty.ShortTermBehavior == AiBehavior.Hold ||
                 mobileParty.ShortTermBehavior == AiBehavior.None ||
                 mobileParty.DefaultBehavior == AiBehavior.Hold ||
                 mobileParty.DefaultBehavior == AiBehavior.None))
            {
                SetMilitiaPatrol(mobileParty);
            }

            if (!IsAvailableBanditParty(mobileParty))
            {
                return;
            }

            TrySplitParty(mobileParty);
        }

        private static void TryGrowing(MobileParty mobileParty)
        {
            try
            {
                if (Growth
                    && MilitiaPowerPercent < Globals.Settings.GlobalPowerPercent
                    && mobileParty.IsBM()
                    && mobileParty.ShortTermBehavior != AiBehavior.FleeToPoint
                    && IsAvailableBanditParty(mobileParty)
                    && Rng.NextDouble() <= Globals.Settings.GrowthChance / 100f)
                {
                    var behavior = Campaign.Current.GetCampaignBehavior<BanditsCampaignBehavior>();
                    if (MilitiaPowerPercent < Globals.Settings.GlobalPowerPercent / 2f)
                    {
                        Traverse.Create(behavior).Method("SpawnBanditOrLooterPartiesAroundAHideoutOrSettlement", 3).GetValue();
                    }
                    var eligibleToGrow = mobileParty.MemberRoster.GetTroopRoster().Where(rosterElement =>
                        rosterElement.Character.Tier < Globals.Settings.MaxTrainingTier
                        && !rosterElement.Character.IsHero
                        && mobileParty.ShortTermBehavior != AiBehavior.FleeToPoint
                        && !mobileParty.IsVisible).ToList();
                    if (eligibleToGrow.Any())
                    {
                        var growthAmount = mobileParty.MemberRoster.TotalManCount * Globals.Settings.GrowthPercent / 100f;
                        // bump up growth to reach GlobalPowerPercent (synthetic but it helps warm up militia population)
                        // (Growth cap % - current %) / 2 = additional
                        // thanks Erythion!
                        var boost = CalculatedGlobalPowerLimit / GlobalMilitiaPower;
                        growthAmount += Globals.Settings.GlobalPowerPercent / 100f * boost;
                        growthAmount = Mathf.Clamp(growthAmount, 1, 50);
                        var growthRounded = Convert.ToInt32(growthAmount);
                        // last condition doesn't account for the size increase but who cares
                        if (mobileParty.MemberRoster.TotalManCount + growthRounded > CalculatedMaxPartySize)
                        {
                            return;
                        }

                        Mod.Log($"Growing {mobileParty.Name}, total: {mobileParty.MemberRoster.TotalManCount}");
                        for (var i = 0; i < eligibleToGrow.Count; i++)
                        {
                            var troop = eligibleToGrow.GetRandomElement().Character;
                            if (GlobalMilitiaPower + troop.GetPower() < CalculatedGlobalPowerLimit)
                            {
                                mobileParty.MemberRoster.AddToCounts(troop, 1);
                            }
                        }

                        var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
                        var strengthString = $"{Math.Round(mobileParty.Party.TotalStrength)} strength";
                        Mod.Log($"{$"Grown to",-70} | {troopString,10} | {strengthString,12} |");
                        DoPowerCalculations();
                        // Mod.Log($"Grown to: {mobileParty.MemberRoster.TotalManCount}");
                    }
                }
            }
            catch (Exception ex)
            {
                Mod.Log(ex);
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
        }
    }
}
