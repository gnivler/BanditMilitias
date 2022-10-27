using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Emit;
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
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static BanditMilitias.Helpers.Helper;
using static BanditMilitias.Globals;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Local
// ReSharper disable RedundantAssignment
// ReSharper disable InconsistentNaming

namespace BanditMilitias.Patches
{
    internal static class MilitiaPatches
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
                    Log.Debug?.Log($"Preventing {mobileParty} from entering {settlement.Name}");
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

        // changes the optional Tracker icons to match banners
        [HarmonyPatch(typeof(MobilePartyTrackItemVM), "UpdateProperties")]
        public static class MobilePartyTrackItemVMUpdatePropertiesPatch
        {
            public static void Postfix(MobilePartyTrackItemVM __instance, ref ImageIdentifierVM ____factionVisualBind)
            {
                if (__instance.TrackedParty is null)
                    return;
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

        // copied from 1.9 assembly since there is no BanditPartyComponent in BMs
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

        [HarmonyPatch(typeof(TroopRoster), "AddToCountsAtIndex")]
        public static class TroopRosterAddToCountsAtIndex
        {
            public static void Prefix(TroopRoster __instance, int index, ref int countChange, int woundedCountChange)
            {
                //var troop = __instance.GetElementCopyAtIndex(index);
                //if (!troop.Character.IsHero && troop.Character.OriginalCharacter != null)
                //{
                //    var sb = new StringBuilder();
                //    new StackTrace().GetFrames()?.Skip(2).Take(6).Do(s => sb.AppendLine(s.GetMethod().Name));
                //    Log.Debug?.Log($"Registered: {IsRegistered(troop.Character)} Match: {troop.Character.FindRoster()?.GetTroopRoster().FirstOrDefault(c => c.Character.StringId == troop.Character.StringId).Character?.StringId}");
                //    Log.Debug?.Log($"AddToCountsAtIndex: {troop.Character.Name} {troop.Character.StringId} {countChange} {woundedCountChange}");
                //    Log.Debug?.Log($"{sb}");
                //}
            }

            public static Exception Finalizer(TroopRoster __instance, int index, Exception __exception)
            {
                switch (__exception)
                {
                    case null:
                        return null;
                    case IndexOutOfRangeException:
                        //Log.Debug?.Log("HACK Squelching IndexOutOfRangeException at TroopRoster.AddToCountsAtIndex");
                        return null;
                    default:
                        Log.Debug?.Log(__exception);
                        return __exception;
                }
            }
        }

        // final gate rejects bandit troops from being upgraded to non-bandit troops
        // put an if-BM-jump at the start to bypass the vanilla blockage
        [HarmonyPatch(typeof(PartyUpgraderCampaignBehavior), "GetPossibleUpgradeTargets")]
        public static class PartyUpgraderCampaignBehaviorGetPossibleUpgradeTargets
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGenerator)
            {
                var codes = instructions.ToListQ();
                var insertion = 0;
                var jumpLabel = ilGenerator.DefineLabel();
                var method = AccessTools.Method(typeof(PartyUpgraderCampaignBehaviorGetPossibleUpgradeTargets), nameof(IsBM));
                for (var index = 0; index < codes.Count; index++)
                {
                    if (codes[index].opcode == OpCodes.Ldarg_1
                        && codes[index + 1].opcode == OpCodes.Callvirt
                        && codes[index + 2].opcode == OpCodes.Callvirt
                        && codes[index + 3].opcode == OpCodes.Brfalse_S)
                        insertion = index;
                    if (codes[index].opcode == OpCodes.Call
                        && codes[index + 1].opcode == OpCodes.Callvirt
                        && codes[index + 2].opcode == OpCodes.Callvirt
                        && codes[index + 3].opcode == OpCodes.Ldarg_1)
                        codes[index].labels.Add(jumpLabel);
                }

                var stack = new List<CodeInstruction>
                {
                    new(OpCodes.Ldarg_1),
                    new(OpCodes.Ldloc_2),
                    new(OpCodes.Ldloc_S, 6),
                    new(OpCodes.Call, method),
                    new(OpCodes.Brtrue_S, jumpLabel)
                };

                codes.InsertRange(insertion, stack);
                return codes.AsEnumerable();
            }

            private static bool IsBM(PartyBase party, CharacterObject character, CharacterObject target)
            {
                if (party.IsMobile && party.MobileParty.IsBM())
                {
                    return Campaign.Current.Models.PartyTroopUpgradeModel.CanPartyUpgradeTroopToTarget(party, character, target);
                }

                return false;
            }
        }

        // rewrite of broken original in 1.8.0
        [HarmonyPatch(typeof(Hideout), "MapFaction", MethodType.Getter)]
        public static class HideoutMapFactionGetter
        {
            // ReSharper disable once RedundantAssignment
            public static bool Prefix(Hideout __instance, ref IFaction __result)
            {
                __result = Clan.BanditFactions.First(c => c.Culture == __instance.Settlement.Culture);
                return false;
            }
        }

        // game seems to dislike me removing parties on tick 3.9
        [HarmonyPatch(typeof(MobileParty), "GetFollowBehavior")]
        public static class MobilePartyGetFollowBehavior
        {
            public static bool Prefix(MobileParty __instance, MobileParty followedParty)
            {
                if (__instance.Army == null &&
                    followedParty is null)
                {
                    __instance.Ai.DisableForHours(1);
                    __instance.Ai.RethinkAtNextHourlyTick = true;
                    return false;
                }

                return true;
            }
        }

        // game seems to dislike me removing parties on tick 3.9
        [HarmonyPatch(typeof(MobileParty), "GetTotalStrengthWithFollowers")]
        public static class MobilePartyGetTotalStrengthWithFollowers
        {
            public static bool Prefix(MobileParty __instance, ref float __result)
            {
                if (!__instance.IsBandit
                    && __instance.Party.MobileParty.TargetParty == null
                    && __instance.Party.MobileParty.ShortTermBehavior is AiBehavior.EngageParty)
                {
                    __result = __instance.Party.TotalStrength;
                    return false;
                }

                return true;
            }
        }

        // 1.9 broke this
        [HarmonyPatch(typeof(MobileParty), "IsBanditBossParty", MethodType.Getter)]
        public class MobilePartyIsBanditBossParty
        {
            public static bool Prefix(MobileParty __instance) => !__instance.IsBM();
        }

        public class CharacterRelationsManagerGetRelation
        {
            public static bool Prefix(Hero hero1, Hero hero2) => hero1 is not null && hero2 is not null;
        }

        [HarmonyPatch(typeof(ChangeRelationAction), "ApplyInternal")]
        public class ChangeRelationActionApplyInternal
        {
            public static Exception Finalizer() => null;
        }
    }
}
