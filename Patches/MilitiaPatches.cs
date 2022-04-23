using System;
using System.Collections.Generic;
using System.Linq;
using Bandit_Militias.Helpers;
using HarmonyLib;
using SandBox.View.Map;
using SandBox.ViewModelCollection;
using SandBox.ViewModelCollection.MobilePartyTracker;
using SandBox.ViewModelCollection.Nameplate;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.LinQuick;
using static Bandit_Militias.Helpers.Helper;
using static Bandit_Militias.Globals;

// ReSharper disable UnusedMember.Global 
// ReSharper disable UnusedType.Global   
// ReSharper disable UnusedMember.Local   
// ReSharper disable RedundantAssignment  
// ReSharper disable InconsistentNaming

namespace Bandit_Militias.Patches
{
    public static class MilitiaPatches
    {
        private static float lastChecked;
        private static List<Settlement> Hideouts;

        [HarmonyPatch(typeof(Campaign), "Tick")]
        public static class CampaignTickPatch
        {
            // main merge method
            private static void Postfix()
            {
                if (Campaign.Current.TimeControlMode == CampaignTimeControlMode.Stop
                    || Campaign.Current.TimeControlMode == CampaignTimeControlMode.UnstoppableFastForwardForPartyWaitTime
                    || Campaign.Current.TimeControlMode == CampaignTimeControlMode.FastForwardStop
                    || Campaign.Current.TimeControlMode == CampaignTimeControlMode.StoppableFastForward)
                {
                    return;
                }

                // don't run this if paused and unless 3% off power limit
                if (Campaign.CurrentTime - lastChecked < 1f
                    || MilitiaPowerPercent + MilitiaPowerPercent / 100 * 0.03 > Globals.Settings.GlobalPowerPercent)
                {
                    return;
                }

                lastChecked = Campaign.CurrentTime;
                foreach (var militia in PartyMilitiaMap.Values.WhereQ(m =>
                             m.MobileParty.MemberRoster.TotalManCount < Globals.Settings.MinPartySize).OrderByDescending(x => x.MobileParty.MemberRoster.TotalManCount))
                {
                    if (militia.MobileParty.MapEvent is null)
                    {
                        Mod.Log($"{new string('*', 40)} {militia.MobileParty.Name}: {militia.MobileParty.MemberRoster.TotalManCount}");
                    }
                }

                Hideouts ??= Settlement.All.Where(s => s.IsHideout).ToListQ();
                var parties = MobileParty.All.Where(m =>
                        m.Party.IsMobile
                        && m.CurrentSettlement is null
                        && !m.IsUsedByAQuest()
                        && m.IsBandit
                        && m.MemberRoster.TotalManCount >= Globals.Settings.MergeableSize)
                    .ToListQ();
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
                        .Except(new[] { mobileParty })
                        .ToListQ();

                    if (!nearbyParties.Any())
                    {
                        continue;
                    }

                    // TODO improve
                    if (mobileParty.ToString().Contains("manhunter")) // Calradia Expanded Kingdoms
                    {
                        continue;
                    }

                    if (mobileParty.IsBM())
                    {
                        //AdjustRelations(mobileParty, nearbyParties.WhereQ(m => PartyMilitiaMap.ContainsKey(m)));
                        CampaignTime? lastChangeDate = PartyMilitiaMap[mobileParty].LastMergedOrSplitDate;
                        if (CampaignTime.Now < lastChangeDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
                        {
                            continue;
                        }
                    }

                    var targetParties = nearbyParties.Where(m =>
                        m.MemberRoster.TotalManCount + mobileParty.MemberRoster.TotalManCount >= Globals.Settings.MinPartySize
                        && IsAvailableBanditParty(m)).ToListQ();

                    var targetParty = targetParties?.GetRandomElement()?.Party;

                    //Mod.Log($">T targetParty {T.ElapsedTicks / 10000F:F3}ms.");
                    // "nobody" is a valid answer
                    if (targetParty is null)
                    {
                        continue;
                    }

                    if (targetParty.MobileParty.IsBM())
                    {
                        CampaignTime? targetLastChangeDate = PartyMilitiaMap[targetParty.MobileParty].LastMergedOrSplitDate;
                        if (CampaignTime.Now < targetLastChangeDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
                        {
                            continue;
                        }
                    }

                    var militiaTotalCount = mobileParty.MemberRoster.TotalManCount + targetParty.MemberRoster.TotalManCount;
                    if (MilitiaPowerPercent > Globals.Settings.GlobalPowerPercent
                        || militiaTotalCount > CalculatedMaxPartySize
                        || militiaTotalCount < Globals.Settings.MinPartySize
                        || NumMountedTroops(mobileParty.MemberRoster) + NumMountedTroops(targetParty.MemberRoster) > militiaTotalCount / 2)
                    {
                        continue;
                    }

                    //Mod.Log($"==> counted {T.ElapsedTicks / 10000F:F3}ms.");
                    if (mobileParty != targetParty.MobileParty.MoveTargetParty &&
                        Campaign.Current.Models.MapDistanceModel.GetDistance(targetParty.MobileParty, mobileParty) > MergeDistance)
                    {
                        //Mod.Log($"{mobileParty} seeking > {targetParty.MobileParty}");
                        mobileParty.SetMoveEscortParty(targetParty.MobileParty);
                        //Mod.Log($"SetNavigationModeParty ==> {T.ElapsedTicks / 10000F:F3}ms");

                        if (targetParty.MobileParty.MoveTargetParty != mobileParty)
                        {
                            //Mod.Log($"{targetParty.MobileParty} seeking back > {mobileParty}");
                            targetParty.MobileParty.SetMoveEscortParty(mobileParty);
                            //Mod.Log($"SetNavigationModeTargetParty ==> {T.ElapsedTicks / 10000F:F3}ms");
                        }

                        continue;
                    }

                    //Mod.Log($"==> found settlement {T.ElapsedTicks / 10000F:F3}ms."); 
                    // create a new party merged from the two
                    if (mobileParty.MemberRoster.TotalManCount + targetParty.MemberRoster.TotalManCount < 20)
                    {
                        Mod.Log("fml");
                    }
                    var rosters = MergeRosters(mobileParty, targetParty);
                    var militia = new Militia(mobileParty.Position2D, rosters[0], rosters[1]);
                    // teleport new militias near the player
                    if (Globals.Settings.TestingMode)
                    {
                        // in case a prisoner
                        var party = Hero.MainHero.PartyBelongedTo ?? Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;
                        militia.MobileParty.Position2D = party.Position2D;
                    }

                    militia.MobileParty.Party.Visuals.SetMapIconAsDirty();

                    try
                    {
                        // can throw if Clan is null
                        Trash(mobileParty);
                        Trash(targetParty.MobileParty);
                    }
                    catch (Exception ex)
                    {
                        Mod.Log(ex);
                    }

                    DoPowerCalculations();
                    //Mod.Log($"==> Finished all work: {T.ElapsedTicks / 10000F:F3}ms.");
                }

                //Mod.Log($"Looped ==> {T.ElapsedTicks / 10000F:F3}ms");
            }
        }

        public static class DefaultPartySpeedCalculatingModelCalculatePureSpeedPatch
        {
            public static void Postfix(MobileParty mobileParty, ref ExplainedNumber __result)
            {
                if (PartyMilitiaMap.ContainsKey(mobileParty))
                {
                    __result.AddFactor(-0.15f);
                }
            }
        }

        // changes the flag
        [HarmonyPatch(typeof(PartyVisual), "AddCharacterToPartyIcon")]
        public static class PartyVisualAddCharacterToPartyIconPatch
        {
            private static void Prefix(CharacterObject characterObject, ref string bannerKey)
            {
                if (Globals.Settings.RandomBanners &&
                    characterObject.HeroObject?.PartyBelongedTo is not null &&
                    characterObject.HeroObject.PartyBelongedTo.IsBM())
                {
                    bannerKey = PartyMilitiaMap[characterObject.HeroObject.PartyBelongedTo].BannerKey;
                }
            }
        }

        // changes the little shield icon under the party
        [HarmonyPatch(typeof(PartyBase), "Banner", MethodType.Getter)]
        public static class PartyBaseBannerPatch
        {
            private static void Postfix(PartyBase __instance, ref Banner __result)
            {
                if (Globals.Settings.RandomBanners &&
                    __instance.MobileParty is not null &&
                    __instance.MobileParty.IsBM())
                {
                    __result = PartyMilitiaMap[__instance.MobileParty].Banner;
                }
            }
        }

        // changes the shields in combat
        [HarmonyPatch(typeof(PartyGroupAgentOrigin), "Banner", MethodType.Getter)]
        public static class PartyGroupAgentOriginBannerGetterPatch
        {
            private static void Postfix(IAgentOriginBase __instance, ref Banner __result)
            {
                var party = (PartyBase)__instance.BattleCombatant;
                if (Globals.Settings.RandomBanners &&
                    party.MobileParty is not null &&
                    party.MobileParty.IsBM())
                {
                    __result = PartyMilitiaMap[party.MobileParty]?.Banner;
                }
            }
        }

        [HarmonyPatch(typeof(EnterSettlementAction), "ApplyForParty")]
        public static class EnterSettlementActionApplyInternalPatch
        {
            private static bool Prefix(MobileParty mobileParty, Settlement settlement)
            {
                if (mobileParty.PartyComponent is ModBanditMilitiaPartyComponent)
                {
                    Mod.Log($"Preventing {mobileParty} from entering {settlement.Name}");
                    SetMilitiaPatrol(mobileParty);
                    return false;
                }

                return true;
            }
        }

        // changes the name on the campaign map (hot path)
        [HarmonyPatch(typeof(PartyNameplateVM), "RefreshDynamicProperties")]
        public static class PartyNameplateVMRefreshDynamicPropertiesPatch
        {
            private static readonly Dictionary<MobileParty, string> Map = new Dictionary<MobileParty, string>();

            private static void Postfix(PartyNameplateVM __instance, ref string ____fullNameBind)
            {
                //T.Restart();
                // Leader is null after a battle, crashes after-action
                // this staged approach feels awkward but it's fast
                if (__instance.Party?.LeaderHero is null)
                {
                    return;
                }

                if (Map.ContainsKey(__instance.Party))
                {
                    ____fullNameBind = Map[__instance.Party];
                    //Mod.Log(T.ElapsedTicks);
                    return;
                }

                if (!__instance.Party.IsBM())
                {
                    return;
                }

                Map.Add(__instance.Party, PartyMilitiaMap[__instance.Party].Name);
                ____fullNameBind = Map[__instance.Party];
                //Mod.Log(T.ElapsedTicks);
            }
        }

        // blocks conversations with militias
        [HarmonyPatch(typeof(PlayerEncounter), "DoMeetingInternal")]
        public static class PlayerEncounterDoMeetingInternalPatch
        {
            private static bool Prefix(PartyBase ____encounteredParty)
            {
                if (____encounteredParty.MobileParty.IsBM())
                {
                    GameMenu.SwitchToMenu("encounter");
                    return false;
                }

                return true;
            }
        }

        // prevent militias from attacking parties they can destroy easily
        [HarmonyPatch(typeof(MobileParty), "CanAttack")]
        public static class MobilePartyCanAttackPatch
        {
            private static void Postfix(MobileParty __instance, MobileParty targetParty, ref bool __result)
            {
                if (__result
                    && !targetParty.IsGarrison
                    && __instance.PartyComponent is ModBanditMilitiaPartyComponent)
                {
                    if (Globals.Settings.IgnoreVillagersCaravans
                        && targetParty.IsCaravan || targetParty.IsVillager)
                    {
                        __result = false;
                        return;
                    }

                    var party1Strength = __instance.GetTotalStrengthWithFollowers();
                    var party2Strength = targetParty.GetTotalStrengthWithFollowers();
                    float delta;
                    if (party1Strength > party2Strength)
                    {
                        delta = party1Strength - party2Strength;
                    }
                    else
                    {
                        delta = party2Strength - party1Strength;
                    }

                    var deltaPercent = delta / party1Strength * 100;
                    __result = deltaPercent <= Globals.Settings.MaxStrengthDeltaPercent;
                }
            }
        }

        // force Heroes to die in simulated combat
        [HarmonyPriority(Priority.High)]
        [HarmonyPatch(typeof(SPScoreboardVM), "TroopNumberChanged")]
        public static class SPScoreboardVMTroopNumberChangedPatch
        {
            private static void Prefix(BasicCharacterObject character, ref int numberDead, ref int numberWounded)
            {
                var c = (CharacterObject)character;
                if (numberWounded > 0
                    && c.HeroObject?.PartyBelongedTo is not null
                    && c.HeroObject.PartyBelongedTo.IsBM())
                {
                    numberDead = 1;
                    numberWounded = 0;
                }
            }
        }

        // throws with Heroes Must Die
        [HarmonyPatch(typeof(TroopRoster), "AddToCountsAtIndex")]
        public static class TroopRosterAddToCountsAtIndexPatch
        {
            private static Exception Finalizer(Exception __exception)
            {
                if (__exception is IndexOutOfRangeException)
                {
                    Mod.Log("HACK Squelching IndexOutOfRangeException at TroopRoster.AddToCountsAtIndex");
                    return null;
                }

                return __exception;
            }
        }

        // changes the optional Tracker icons to match banners
        [HarmonyPatch(typeof(MobilePartyTrackItemVM), "UpdateProperties")]
        public static class MobilePartyTrackItemVMUpdatePropertiesPatch
        {
            public static void Postfix(MobilePartyTrackItemVM __instance, ref ImageIdentifierVM ____factionVisualBind)
            {
                if (__instance.TrackedParty is not null
                    && PartyImageMap.ContainsKey(__instance.TrackedParty))
                {
                    ____factionVisualBind = PartyImageMap[__instance.TrackedParty];
                }
            }
        }

        //[HarmonyPatch(typeof(MobileParty), "IsBandit", MethodType.Getter)]
        //public static class MobilePartyIsBanditPatch
        //{
        //    public static void Postfix(MobileParty __instance, ref bool __result)
        //    {
        //        if (__instance.PartyComponent is ModBanditMilitiaPartyComponent)
        //        {
        //            __result = true;
        //        }
        //    }
        //}

        //[HarmonyPatch(typeof(MobileParty), "IsBanditBossParty", MethodType.Getter)]
        //public static class MobilePartyIsBanditBossPartyPatch
        //{
        //    public static void Postfix(MobileParty __instance, ref bool __result)
        //    {
        //        if (__instance.PartyComponent is ModBanditMilitiaPartyComponent)
        //        {
        //            __result = false;
        //        }
        //    }
        //}

        // skip the regular bandit AI stuff, looks at moving into hideouts
        // and other stuff I don't really want happening
        [HarmonyPatch(typeof(AiBanditPatrollingBehavior), "AiHourlyTick")]
        public static class AiBanditPatrollingBehaviorAiHourlyTickPatch
        {
            public static bool Prefix(MobileParty mobileParty)
            {
                return mobileParty.PartyComponent is not ModBanditMilitiaPartyComponent;
            }
        }

        [HarmonyPatch(typeof(DefaultMobilePartyFoodConsumptionModel), "DoesPartyConsumeFood")]
        public static class DefaultMobilePartyFoodConsumptionModelDoesPartyConsumeFoodPatch
        {
            public static void Postfix(MobileParty mobileParty, ref bool __result)
            {
                if (mobileParty.PartyComponent is ModBanditMilitiaPartyComponent)
                {
                    __result = false;
                }
            }
        }
    }
}
