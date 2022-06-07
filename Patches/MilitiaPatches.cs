using System;
using System.Collections.Generic;
using BanditMilitias.Helpers;
using HarmonyLib;
using Helpers;
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
using static BanditMilitias.Helpers.Helper;
using static BanditMilitias.Globals;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Local
// ReSharper disable RedundantAssignment
// ReSharper disable InconsistentNaming

namespace BanditMilitias.Patches
{
    public static class MilitiaPatches
    {
        [HarmonyPatch(typeof(MobileParty), "ComputeSpeed")]
        public static class MobilePartyComputeSpeed
        {
            public static void Postfix(MobileParty __instance, ref float __result)
            {
                if (__instance is null) Log("PING");
                if (__instance.IsBandit
                    && __instance.TargetParty is not null
                    && __instance.TargetParty.IsBandit)
                {
                    __result *= 1.15f;
                }
                else if (__instance.IsBM())
                {
                    __result = Math.Max(1, __result * 0.85f);
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
                    var component = (ModBanditMilitiaPartyComponent)characterObject.HeroObject.PartyBelongedTo.PartyComponent;
                    bannerKey = component.BannerKey;
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
                    __instance.IsMobile &&
                    __instance.MobileParty.IsBM())
                {
                    __result = __instance.MobileParty.GetBM().Banner;
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
                    party.IsMobile &&
                    party.MobileParty.IsBM())
                {
                    __result = party.MobileParty?.GetBM().Banner;
                }
            }
        }

        [HarmonyPatch(typeof(EnterSettlementAction), "ApplyForParty")]
        public static class EnterSettlementActionApplyForPartyPatch
        {
            private static bool Prefix(MobileParty mobileParty, Settlement settlement)
            {
                if (mobileParty.PartyComponent is ModBanditMilitiaPartyComponent)
                {
                    Log($"Preventing {mobileParty} from entering {settlement.Name}");
                    MilitiaBehavior.BMThink(mobileParty);
                    return false;
                }

                return true;
            }
        }

        // changes the name on the campaign map (hot path)
        [HarmonyPatch(typeof(PartyNameplateVM), "RefreshDynamicProperties")]
        public static class PartyNameplateVMRefreshDynamicPropertiesPatch
        {
            private static readonly Dictionary<MobileParty, string> Map = new();

            public static void Postfix(PartyNameplateVM __instance, ref string ____fullNameBind)
            {
                //T.Restart();
                // Leader is null after a battle, crashes after-action
                // this staged approach feels awkward but it's fast
                if (__instance.Party?.LeaderHero is null)
                {
                    return;
                }

                if (Map.TryGetValue(__instance.Party, out var name))
                {
                    ____fullNameBind = name;
                    //SubModule.Log(T.ElapsedTicks);
                    return;
                }

                if (!__instance.Party.IsBM())
                {
                    return;
                }

                Map.Add(__instance.Party, __instance.Party.GetBM().Name.ToString());
                ____fullNameBind = Map[__instance.Party];
                //SubModule.Log(T.ElapsedTicks);
            }
        }

        // blocks conversations with militias
        [HarmonyPatch(typeof(PlayerEncounter), "DoMeetingInternal")]
        public static class PlayerEncounterDoMeetingInternalPatch
        {
            public static bool Prefix(PartyBase ____encounteredParty)
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
            public static void Postfix(MobileParty __instance, MobileParty targetParty, ref bool __result)
            {
                if (__result
                    && !targetParty.IsGarrison
                    && __instance.IsBM())
                {
                    if (Globals.Settings.IgnoreVillagersCaravans
                        && targetParty.IsCaravan || targetParty.IsVillager)
                    {
                        __result = false;
                        return;
                    }

                    if (targetParty.LeaderHero is not null
                        && __instance.GetBM().Avoidance.TryGetValue(targetParty.LeaderHero, out var heroAvoidance)
                        && Rng.NextDouble() * 100 < heroAvoidance)
                    {
                        //Log($"||| {__instance.Name} avoided attacking {targetParty.Name}");
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
            public static void Prefix(BasicCharacterObject character, ref int numberDead, ref int numberWounded)
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

        [HarmonyPatch(typeof(TroopRoster), "AddToCountsAtIndex")]
        public static class TroopRosterAddToCountsAtIndexPatch
        {
            public static Exception Finalizer(TroopRoster __instance, Exception __exception)
            {
                // throws with Heroes Must Die (old)
                if (__exception is IndexOutOfRangeException)
                {
                    Log("HACK Squelching IndexOutOfRangeException at TroopRoster.AddToCountsAtIndex");
                    return null;
                }

                // throws during nuke of poor state
                if (__exception is NullReferenceException)
                {
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
                if (__instance.TrackedParty is null) return;
                if (PartyImageMap.TryGetValue(__instance.TrackedParty, out var image))
                {
                    ____factionVisualBind = image;
                }
            }
        }

        // skip the regular bandit AI stuff, looks at moving into hideouts
        // and other stuff I don't really want happening
        [HarmonyPatch(typeof(AiBanditPatrollingBehavior), "AiHourlyTick")]
        public static class AiBanditPatrollingBehaviorAiHourlyTickPatch
        {
            public static bool Prefix(MobileParty mobileParty) => false;
        }

        [HarmonyPatch(typeof(DefaultMobilePartyFoodConsumptionModel), "DoesPartyConsumeFood")]
        public static class DefaultMobilePartyFoodConsumptionModelDoesPartyConsumeFoodPatch
        {
            public static void Postfix(MobileParty mobileParty, ref bool __result)
            {
                if (mobileParty.IsBM())
                {
                    __result = false;
                }
            }
        }
    }
}
