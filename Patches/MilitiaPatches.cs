using System;
using System.Collections.Generic;
using System.Diagnostics;
using BanditMilitias.Helpers;
using HarmonyLib;
using Helpers;
using SandBox.GameComponents;
using SandBox.View.Map;
using SandBox.ViewModelCollection;
using SandBox.ViewModelCollection.Map;
using SandBox.ViewModelCollection.Nameplate;
using StoryMode.GameComponents;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
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
        private static readonly AccessTools.FieldRef<MobileParty, int> numberOfRecentFleeingFromAParty =
            AccessTools.FieldRefAccess<MobileParty, int>("_numberOfRecentFleeingFromAParty");

        [HarmonyPatch(typeof(MobileParty), "CalculateSpeed")]
        public static class MobilePartyCalculateSpeed
        {
            public static void Postfix(MobileParty __instance, ref float __result)
            {
                // make them move faster towards a merge, or slow them down generally
                if (__instance.IsBandit
                    && __instance.TargetParty is { IsBandit: true })
                    __result *= 1.15f;
                else if (__instance.IsBM())
                    __result = Math.Max(1, __result * 0.85f);
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
                if (mobileParty.IsBM())
                {
                    DeferringLogger.Instance.Debug?.Log($"Preventing {mobileParty} from entering {settlement.Name}");
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
                // Leader is null after a battle, crashes after-action
                // this staged approach feels awkward but it's fast
                if (__instance.Party?.LeaderHero is null)
                {
                    return;
                }

                if (Map.TryGetValue(__instance.Party, out var name))
                {
                    ____fullNameBind = name;
                    return;
                }

                if (!__instance.Party.IsBM())
                {
                    return;
                }

                Map.Add(__instance.Party, __instance.Party.GetBM().Name.ToString());
                ____fullNameBind = Map[__instance.Party];
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
                if (SubModule.MEOWMEOW && targetParty == MobileParty.MainParty)
                {
                    __result = false;
                    return;
                }

                if (__result && targetParty.Party.IsMobile && __instance.IsBM())
                {
                    if (Globals.Settings.IgnoreVillagersCaravans
                        && (targetParty.IsCaravan || targetParty.IsVillager))
                    {
                        __result = false;
                        return;
                    }

                    if (targetParty.LeaderHero is not null
                        && __instance.GetBM().Avoidance.TryGetValue(targetParty.LeaderHero, out var heroAvoidance)
                        && Rng.NextDouble() * 100 < heroAvoidance)
                    {
                        __result = false;
                        return;
                    }

                    var party1Strength = __instance.GetTotalStrengthWithFollowers();
                    var party2Strength = targetParty.GetTotalStrengthWithFollowers();
                    float delta;
                    if (party1Strength > party2Strength)
                        delta = party1Strength - party2Strength;
                    else
                        delta = party2Strength - party1Strength;
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
                if (character is CharacterObject c
                    && numberWounded > 0
                    && c.HeroObject?.PartyBelongedTo is not null
                    && c.HeroObject.PartyBelongedTo.IsBM())
                {
                    numberDead = 1;
                    numberWounded = 0;
                }
            }
        }

        [HarmonyPatch(typeof(MapEventParty), "OnTroopKilled")]
        public static class MapEventPartyOnTroopKilled
        {
            public static void Prefix(UniqueTroopDescriptor troopSeed, FlattenedTroopRoster ____roster, ref CharacterObject __state)
            {
                __state = ____roster[troopSeed].Troop;
            }

            public static void Postfix(UniqueTroopDescriptor troopSeed, FlattenedTroopRoster ____roster, CharacterObject __state)
            {
                if (Troops.Contains(__state))
                    MBObjectManager.Instance.UnregisterObject(__state);
            }
        }

        // TODO TroopRoster.Clear patch for more performance

        [HarmonyPatch(typeof(TroopRoster), "AddToCountsAtIndex")]
        public static class TroopRosterAddToCountsAtIndex
        {
            public static void Prefix(TroopRoster __instance, int index, int countChange, ref CharacterObject __state)
            {
                if (!SubModule.MEOWMEOW || !Globals.Settings.UpgradeTroops)
                    return;
                __state = __instance.GetCharacterAtIndex(index);
                switch (__instance.GetElementCopyAtIndex(index).Number)
                {
                    // the CO will be removed in the method and throw in the Postfix unless we remove it here
                    case 0:
                        Troops.Remove(__state);
                        EquipmentMap.Remove(__state.StringId);
                        break;
                    default:
                        __state = null;
                        break;
                }
            }


            public static void Postfix(TroopRoster __instance, int index, int countChange, CharacterObject __state)
            {
                // this correctly assumes there will never be Number > 1
                if (countChange < 0 && Troops.Contains(__state))
                {
                    if (!SubModule.MEOWMEOW || !Globals.Settings.UpgradeTroops)
                        return;
                    Troops.Remove(__state);
                    EquipmentMap.Remove(__state.StringId);
                    MBObjectManager.Instance.UnregisterObject(__state);
                }
            }

            public static Exception Finalizer(TroopRoster __instance, int index, Exception __exception)
            {
                switch (__exception)
                {
                    case null:
                        return null;
                    case IndexOutOfRangeException:
                        DeferringLogger.Instance.Debug?.Log("HACK Squelching IndexOutOfRangeException at TroopRoster.AddToCountsAtIndex");
                        return null;
                    default:
                        DeferringLogger.Instance.Debug?.Log(__exception);
                        return __exception;
                }
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
                    ____factionVisualBind = image;
            }
        }

        // skip the regular bandit AI stuff, looks at moving into hideouts
        // and other stuff I don't really want happening
        [HarmonyPatch(typeof(AiBanditPatrollingBehavior), "AiHourlyTick")]
        public static class AiBanditPatrollingBehaviorAiHourlyTickPatch
        {
            public static bool Prefix(MobileParty mobileParty) => !mobileParty.IsBM();
        }

        [HarmonyPatch(typeof(DefaultMobilePartyFoodConsumptionModel), "DoesPartyConsumeFood")]
        public static class DefaultMobilePartyFoodConsumptionModelDoesPartyConsumeFoodPatch
        {
            public static void Postfix(MobileParty mobileParty, ref bool __result)
            {
                if (mobileParty.IsBM())
                    __result = false;
            }
        }

        // copied out of assembly and modified to not check against occupation
        [HarmonyPatch(typeof(NameGenerator), "GenerateHeroFullName")]
        public static class NameGeneratorGenerateHeroName
        {
            public static void Postfix(Hero hero, TextObject heroFirstName, ref TextObject __result)
            {
                if (hero.CharacterObject.Occupation is not Occupation.Bandit
                    || (hero.PartyBelongedTo is not null
                        && !hero.PartyBelongedTo.IsBM()))
                    return;

                var textObject = heroFirstName;
                var index = (int)AccessTools.Method(typeof(NameGenerator), "SelectNameIndex")
                    .Invoke(NameGenerator.Current, new object[] { hero, GangLeaderNames(NameGenerator.Current), 0u, false });
                NameGenerator.Current.AddName(GangLeaderNames(NameGenerator.Current)[index]);
                textObject = GangLeaderNames(NameGenerator.Current)[index].CopyTextObject();
                textObject.SetTextVariable("FEMALE", hero.IsFemale ? 1 : 0);
                textObject.SetTextVariable("IMPERIAL", hero.Culture.StringId == "empire" ? 1 : 0);
                textObject.SetTextVariable("COASTAL", hero.Culture.StringId is "empire" or "vlandia" ? 1 : 0);
                textObject.SetTextVariable("NORTHERN", hero.Culture.StringId is "battania" or "sturgia" ? 1 : 0);
                StringHelpers.SetCharacterProperties("HERO", hero.CharacterObject, textObject).SetTextVariable("FIRSTNAME", heroFirstName);
                __result = textObject;
            }
        }

        [HarmonyPatch(typeof(StoryModeAgentDecideKilledOrUnconsciousModel), "GetAgentStateProbability")]
        public class StoryModeAgentDecideKilledOrUnconsciousModelGetAgentStateProbability
        {
            public static void Postfix(Agent effectedAgent, ref float __result)
            {
                if (effectedAgent.Character is CharacterObject co && Heroes.Contains(co.HeroObject))
                    __result = 1;
            }
        }

        [HarmonyPatch(typeof(SandboxAgentDecideKilledOrUnconsciousModel), "GetAgentStateProbability")]
        public class SandboxAgentDecideKilledOrUnconsciousModelGetAgentStateProbability
        {
            public static void Postfix(Agent effectedAgent, ref float __result)
            {
                if (effectedAgent.Character is CharacterObject co && Heroes.Contains(co.HeroObject))
                    __result = 1;
            }
        }

        // copied from assembly since there is no BanditPartyComponent in BMs
        [HarmonyPatch(typeof(MobileParty), "CalculateContinueChasingScore")]
        public class MobilePartyCalculateContinueChasingScore
        {
            public static bool Prefix(MobileParty __instance, MobileParty enemyParty, ref float __result)
            {
                if (!__instance.IsBM())
                    return true;
                var num = __instance.Army != null && __instance.Army.LeaderParty == __instance ? __instance.Army.TotalStrength : __instance.Party.TotalStrength;
                var num2 = (enemyParty.Army != null && enemyParty.Army.LeaderParty == __instance ? enemyParty.Army.TotalStrength : enemyParty.Party.TotalStrength) / (num + 0.01f);
                var num3 = 1f + 0.01f * numberOfRecentFleeingFromAParty(enemyParty);
                var num4 = Math.Min(1f, (__instance.Position2D - enemyParty.Position2D).Length / 3f);
                var settlement = __instance.GetBM().HomeSettlement;
                var num5 = Campaign.AverageDistanceBetweenTwoFortifications * 3f;
                num5 = Campaign.Current.Models.MapDistanceModel.GetDistance(__instance, settlement);
                var num6 = num5 / (Campaign.AverageDistanceBetweenTwoFortifications * 3f);
                var input = 1f + (float)Math.Pow(enemyParty.Speed / (__instance.Speed - 0.25f), 3.0);
                input = MBMath.Map(input, 0f, 5.2f, 0f, 2f);
                var num7 = 60000f;
                var num8 = 10000f;
                var num9 = (enemyParty.LeaderHero != null ? enemyParty.PartyTradeGold + enemyParty.LeaderHero.Gold : enemyParty.PartyTradeGold) / (enemyParty.IsCaravan ? num8 : num7);
                var num10 = enemyParty.LeaderHero == null ? 0.75f : enemyParty.LeaderHero.IsFactionLeader ? 1.5f : 1f;
                var num11 = num2 * num6 * input * num3 * num4;
                __result = MBMath.ClampFloat(num9 * num10 / (num11 + 0.001f), 0.005f, 3f);
                return false;
            }
        }

        // avoid stuffing the BM into PartiesWithoutPartyComponent at CampaignObjectManager.InitializeOnLoad
        [HarmonyPatch(typeof(MobileParty), "UpdatePartyComponentFlags")]
        public static class MobilePartyInitializeOnLoad
        {
            public static void Postfix(MobileParty __instance)
            {
                if (!__instance.IsBandit && __instance.IsBM())
                    IsBandit(__instance) = true;
            }
        }

        [HarmonyPatch(typeof(Clan), "AddWarPartyInternal")]
        public static class ClanAddWarPartyInternal
        {
            public static bool Prefix(WarPartyComponent warPartyComponent) => warPartyComponent is not ModBanditMilitiaPartyComponent;
        }

        // Trash() removes the party, which nulls out the clan, which throws harmlessly here
        [HarmonyPatch(typeof(WarPartyComponent), "OnFinalize")]
        public static class WarPartyComponentOnFinalize
        {
            public static Exception Finalizer(Exception __exception, WarPartyComponent __instance)
            {
                if (__exception is not null && __instance is ModBanditMilitiaPartyComponent)
                    return null;

                return __exception;
            }
        }
    }
}
