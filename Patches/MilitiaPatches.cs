using System;
using System.Collections.Generic;
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
        [HarmonyPatch(typeof(MobileParty), "CalculateSpeed")]
        public static class MobilePartyCalculateSpeed
        {
            public static void Postfix(MobileParty __instance, ref float __result)
            {
                if (__instance is null)
                {
                    Meow();
                    throw new NullReferenceException("MobileParty is null at CalculateSpeed");
                }

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
                if (mobileParty.IsBM())
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
        public static class TroopRosterRemoveTroop
        {
            public static void Prefix(UniqueTroopDescriptor troopSeed, FlattenedTroopRoster ____roster)
            {
                var troop = ____roster[troopSeed].Troop;
                if (troop.StringId.Contains("Bandit_Militia_Troop"))
                    MBObjectManager.Instance.UnregisterObject(troop);
            }
        }

        //[HarmonyPatch(typeof(PartyUpgraderCampaignBehavior), "UpgradeTroop")]
        //public static class PartyUpgraderCampaignBehaviorUpgradeTroop
        //{
        //    public static bool Prefix(PartyUpgraderCampaignBehavior __instance, PartyBase party, int rosterIndex, object upgradeArgs)
        //    {
        //        var troop = party.MemberRoster.GetCharacterAtIndex(rosterIndex);
        //        var memberRoster = party.MemberRoster;
        //        var upgradeTarget = Traverse.Create(upgradeArgs).Field<CharacterObject>("UpgradeTarget").Value;
        //        var possibleUpgradeCount = Traverse.Create(upgradeArgs).Field<int>("PossibleUpgradeCount").Value;
        //        var num = Traverse.Create(upgradeArgs).Field<int>("UpgradeXpCost").Value * possibleUpgradeCount;
        //        memberRoster.SetElementXp(rosterIndex, memberRoster.GetElementXp(rosterIndex) - num);
        //        memberRoster.AddToCounts(Traverse.Create(upgradeArgs).Field<CharacterObject>("Target").Value, -possibleUpgradeCount);
        //        memberRoster.AddToCounts(upgradeTarget, possibleUpgradeCount);
        //        if (possibleUpgradeCount > 0)
        //            Traverse.Create(__instance).Method("ApplyEffects", party, upgradeArgs);
        //
        //        if (troop.StringId.Contains("Bandit_Militia_Troop"))
        //            MBObjectManager.Instance.UnregisterObject(Traverse.Create(upgradeArgs).Field<CharacterObject>("Target").Value);
        //
        //        return false;
        //    }
        //}

        [HarmonyPatch(typeof(TroopRoster), "AddToCountsAtIndex")]
        public static class TroopRosterAddToCountsAtIndex
        {
            public static void Prefix(TroopRoster __instance, int index, int countChange)
            {
                var troop = __instance.GetCharacterAtIndex(index);
                if (countChange < 0 && troop.StringId.Contains("Bandit_Militia_Troop"))
                {
                    MBObjectManager.Instance.UnregisterObject(troop);
                }
            }

            public static void Postfix(TroopRoster __instance, int index)
            {
                //Log(__instance);
            }

            public static Exception Finalizer(TroopRoster __instance, int index, Exception __exception)
            {
                switch (__exception)
                {
                    case null:
                        return null;
                    // throws with Heroes Must Die (old)
                    case IndexOutOfRangeException:
                        Log("HACK Squelching IndexOutOfRangeException at TroopRoster.AddToCountsAtIndex");
                        return null;
                    // throws during nuke of poor state (old)
                    case NullReferenceException:
                        Log(__exception);
                        return null;
                    default:
                        Log(__exception);
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
            public static bool Prefix(MobileParty mobileParty) => false;
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
                    && hero.PartyBelongedTo is not null
                    && !hero.PartyBelongedTo.IsBM())
                    return;

                var textObject = heroFirstName;
                var index = (int)AccessTools.Method(typeof(NameGenerator), "SelectNameIndex")
                    .Invoke(NameGenerator.Current, new object[] { hero, GangLeaderNames(NameGenerator.Current), 0u, false });
                NameGenerator.Current.AddName(GangLeaderNames(NameGenerator.Current)[index]);
                textObject = GangLeaderNames(NameGenerator.Current)[index].CopyTextObject();
                textObject.SetTextVariable("FEMALE", hero.IsFemale ? 1 : 0);
                textObject.SetTextVariable("IMPERIAL", (hero.Culture.StringId == "empire") ? 1 : 0);
                textObject.SetTextVariable("COASTAL", (hero.Culture.StringId == "empire" || hero.Culture.StringId == "vlandia") ? 1 : 0);
                textObject.SetTextVariable("NORTHERN", (hero.Culture.StringId == "battania" || hero.Culture.StringId == "sturgia") ? 1 : 0);
                StringHelpers.SetCharacterProperties("HERO", hero.CharacterObject, textObject).SetTextVariable("FIRSTNAME", heroFirstName);
                __result = textObject;
            }
        }

        [HarmonyPatch(typeof(DefaultAgentDecideKilledOrUnconsciousModel), "GetAgentStateProbability")]
        public class DefaultAgentDecideKilledOrUnconsciousModelGetAgentStateProbability
        {
            public static void Postfix(Agent effectedAgent, ref float __result)
            {
                if (effectedAgent.Character.StringId.EndsWith("Bandit_Militia"))
                {
                    __result = 1;
                }
            }
        }

        [HarmonyPatch(typeof(StoryModeAgentDecideKilledOrUnconsciousModel), "GetAgentStateProbability")]
        public class StoryModeAgentDecideKilledOrUnconsciousModelGetAgentStateProbability
        {
            public static void Postfix(Agent effectedAgent, ref float __result)
            {
                if (effectedAgent.Character.StringId.EndsWith("Bandit_Militia"))
                {
                    __result = 1;
                }
            }
        }

        [HarmonyPatch(typeof(SandboxAgentDecideKilledOrUnconsciousModel), "GetAgentStateProbability")]
        public class SandboxAgentDecideKilledOrUnconsciousModelGetAgentStateProbability
        {
            public static void Postfix(Agent effectedAgent, ref float __result)
            {
                if (effectedAgent.Character.StringId.EndsWith("Bandit_Militia"))
                {
                    __result = 1;
                }
            }
        }

        // copied from assembly since there is no BanditPartyComponent in BMs
        [HarmonyPatch(typeof(MobileParty), "CalculateContinueChasingScore")]
        public class MobilePartyBanditPartyComponent
        {
            public static bool Prefix(MobileParty __instance, MobileParty enemyParty, ref float __result)
            {
                var num = __instance.Army != null && __instance.Army.LeaderParty == __instance ? __instance.Army.TotalStrength : __instance.Party.TotalStrength;
                var num2 = (enemyParty.Army != null && enemyParty.Army.LeaderParty == __instance ? enemyParty.Army.TotalStrength : enemyParty.Party.TotalStrength) / (num + 0.01f);
                var num3 = 1f + 0.01f * Traverse.Create(enemyParty).Field<int>("_numberOfRecentFleeingFromAParty").Value;
                var num4 = Math.Min(1f, (__instance.Position2D - enemyParty.Position2D).Length / 3f);
                Settlement settlement = null;
                if (__instance.IsBM())
                {
                    settlement = __instance.GetBM().HomeSettlement;
                }
                else
                {
                    settlement = __instance.IsBandit ? __instance.BanditPartyComponent.Hideout?.Settlement : !__instance.IsLordParty || __instance.LeaderHero == null || !__instance.LeaderHero.IsMinorFactionHero ? SettlementHelper.FindNearestFortification((Settlement x) => x.MapFaction == __instance.MapFaction) : __instance.MapFaction.FactionMidSettlement;
                }

                var num5 = Campaign.AverageDistanceBetweenTwoFortifications * 3f;
                if (settlement != null)
                {
                    num5 = Campaign.Current.Models.MapDistanceModel.GetDistance(__instance, settlement);
                }

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
    }
}
