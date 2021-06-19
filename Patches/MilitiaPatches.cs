using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SandBox.View.Map;
using SandBox.ViewModelCollection.Nameplate;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors.AiBehaviors;
using TaleWorlds.CampaignSystem.SandBox.GameComponents.Map;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using static Bandit_Militias.Helpers.Helper;
using static Bandit_Militias.Globals;

// ReSharper disable UnusedMember.Global 
// ReSharper disable UnusedType.Global   
// ReSharper disable UnusedMember.Local   
// ReSharper disable RedundantAssignment  
// ReSharper disable InconsistentNaming

namespace Bandit_Militias.Patches
{
    public class MilitiaPatches
    {
        [HarmonyPatch(typeof(Campaign), "Tick")]
        public class CampaignTickPatch
        {
            private static void Postfix()
            {
                try
                {
                    if (Campaign.Current.TimeControlMode == CampaignTimeControlMode.Stop ||
                        Campaign.Current.TimeControlMode == CampaignTimeControlMode.UnstoppableFastForwardForPartyWaitTime ||
                        Campaign.Current.TimeControlMode == CampaignTimeControlMode.FastForwardStop ||
                        Campaign.Current.TimeControlMode == CampaignTimeControlMode.StoppableFastForward ||
                        GlobalMilitiaPower > CalculatedGlobalPowerLimit)
                    {
                        return;
                    }

                    var parties = MobileParty.All.Where(m =>
                            m.Party.IsMobile &&
                            m.CurrentSettlement is null &&
                            !m.IsUsedByAQuest() &&
                            m.IsBandit &&
                            m.MemberRoster.TotalManCount > Globals.Settings.MinPartySizeToConsiderMerge)
                        .Concat(PartyMilitiaMap.Keys).ToList(); // might cause duplicates if IsBandit returns differently in the future
                    //T.Restart();
                    for (var index = 0; index < parties.Count; index++)
                    {
                        var mobileParty = parties[index];
                        if (mobileParty.IsTooBusyToMerge() ||
                            mobileParty.ToString().Contains("manhunter")) // Calradia Expanded Kingdoms
                        {
                            continue;
                        }

                        CampaignTime? lastChangeDate = null;
                        if (PartyMilitiaMap.ContainsKey(mobileParty))
                        {
                            lastChangeDate = PartyMilitiaMap[mobileParty].LastMergedOrSplitDate;
                        }

                        if (CampaignTime.Now < lastChangeDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
                        {
                            continue;
                        }

                        var nearbyParties = MobileParty.FindPartiesAroundPosition(mobileParty.Position2D, FindRadius);
                        var targetParty = nearbyParties.Where(x => x != mobileParty &&
                                                                   IsValidParty(x) &&
                                                                   x.MemberRoster.TotalManCount + mobileParty.MemberRoster.TotalManCount >= Globals.Settings.MinPartySize)
                            .ToList().GetRandomElement()?.Party;

                        // "nobody" is a valid answer
                        if (targetParty is null)
                        {
                            continue;
                        }

                        CampaignTime? targetLastChangeDate = null;
                        if (PartyMilitiaMap.ContainsKey(targetParty.MobileParty))
                        {
                            targetLastChangeDate = PartyMilitiaMap[targetParty.MobileParty].LastMergedOrSplitDate;
                        }

                        if (CampaignTime.Now < targetLastChangeDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
                        {
                            continue;
                        }

                        var militiaTotalCount = mobileParty.MemberRoster.TotalManCount + targetParty.MemberRoster.TotalManCount;
                        if (militiaTotalCount < Globals.Settings.MinPartySize ||
                            militiaTotalCount > CalculatedMaxPartySize ||
                            mobileParty.Party.TotalStrength > CalculatedMaxPartyStrength ||
                            NumMountedTroops(mobileParty.MemberRoster) + NumMountedTroops(targetParty.MemberRoster) > militiaTotalCount / 2)
                        {
                            continue;
                        }

                        if (mobileParty != targetParty.MobileParty.MoveTargetParty &&
                            Campaign.Current.Models.MapDistanceModel.GetDistance(targetParty.MobileParty, mobileParty) > MergeDistance)
                        {
                            Mod.Log($"{mobileParty} seeking > {targetParty.MobileParty}");
                            mobileParty.SetMoveEscortParty(targetParty.MobileParty);
                            //Mod.Log($"SetNavigationModeParty ==> {T.ElapsedTicks / 10000F:F3}ms");

                            if (targetParty.MobileParty.MoveTargetParty != mobileParty)
                            {
                                Mod.Log($"{targetParty.MobileParty} seeking back > {mobileParty}");
                                targetParty.MobileParty.SetMoveEscortParty(mobileParty);
                                //Mod.Log($"SetNavigationModeTargetParty ==> {T.ElapsedTicks / 10000F:F3}ms");
                            }

                            continue;
                        }

                        if (Settlement.FindSettlementsAroundPosition(mobileParty.Position2D, MinDistanceFromHideout, x => x.IsHideout()).Any())
                        {
                            continue;
                        }

                        // create a new party merged from the two
                        var rosters = MergeRosters(mobileParty, targetParty);
                        var militia = new Militia(mobileParty, rosters[0], rosters[1]);
                        // teleport new militias near the player
                        if (TestingMode)
                        {
                            // in case a prisoner
                            var party = Hero.MainHero.PartyBelongedTo ?? Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;
                            militia.MobileParty.Position2D = party.Position2D;
                        }

                        militia.MobileParty.Party.Visuals.SetMapIconAsDirty();
                        Trash(mobileParty);
                        Trash(targetParty.MobileParty);
                        DoPowerCalculations();
                        //Mod.Log($">>> Finished all work: {T.ElapsedTicks / 10000F:F3}ms.");
                    }
                    //Mod.Log($"Looped ==> {T.ElapsedTicks / 10000F:F3}ms");
                }
                catch (Exception ex)
                {
                    Mod.Log(ex);
                }
            }
        }

        [HarmonyPatch(typeof(DefaultPartySpeedCalculatingModel), "CalculateFinalSpeed")]
        public class DefaultPartySpeedCalculatingModelCalculateFinalSpeedPatch
        {
            private static float SpeedModifier = -0.15f;

            private static void Postfix(MobileParty mobileParty, ref ExplainedNumber __result)
            {
                if (PartyMilitiaMap.ContainsKey(mobileParty))
                {
                    __result.AddFactor(SpeedModifier, new TextObject("Bandit Militia"));
                }
            }
        }

        // changes the flag
        [HarmonyPatch(typeof(PartyVisual), "AddCharacterToPartyIcon")]
        public class PartyVisualAddCharacterToPartyIconPatch
        {
            private static void Prefix(CharacterObject characterObject, ref string bannerKey)
            {
                if (Globals.Settings.RandomBanners &&
                    characterObject.HeroObject?.PartyBelongedTo is not null &&
                    IsBM(characterObject.HeroObject.PartyBelongedTo))
                {
                    bannerKey = PartyMilitiaMap[characterObject.HeroObject.PartyBelongedTo].BannerKey;
                }
            }
        }

        //// changes the little shield icon under the party
        [HarmonyPatch(typeof(PartyBase), "Banner", MethodType.Getter)]
        public class PartyBaseBannerPatch
        {
            private static void Postfix(PartyBase __instance, ref Banner __result)
            {
                if (Globals.Settings.RandomBanners &&
                    __instance.MobileParty is not null &&
                    IsBM(__instance.MobileParty))
                {
                    __result = PartyMilitiaMap[__instance.MobileParty].Banner;
                }
            }
        }

        //// changes the shields in combat
        [HarmonyPatch(typeof(PartyGroupAgentOrigin), "Banner", MethodType.Getter)]
        public class PartyGroupAgentOriginBannerGetterPatch
        {
            private static void Postfix(IAgentOriginBase __instance, ref Banner __result)
            {
                var party = (PartyBase) __instance.BattleCombatant;
                if (Globals.Settings.RandomBanners &&
                    party.MobileParty is not null &&
                    IsBM(party.MobileParty))
                {
                    __result = PartyMilitiaMap[party.MobileParty]?.Banner;
                }
            }
        }

        // check daily each bandit party against the size factor and a random chance to split up
        [HarmonyPatch(typeof(MobileParty), "DailyTick")]
        public static class MobilePartyDailyTickPatch
        {
            private static void Postfix(MobileParty __instance)
            {
                if (!IsValidParty(__instance))
                {
                    return;
                }

                TrySplitParty(__instance);
            }
        }

        [HarmonyPatch(typeof(EnterSettlementAction), "ApplyInternal")]
        public class EnterSettlementActionApplyInternalPatch
        {
            private static bool Prefix(MobileParty mobileParty, Settlement settlement)
            {
                if (IsBM(mobileParty))
                {
                    Mod.Log($"Preventing {mobileParty} from entering {settlement}");
                    mobileParty.SetMovePatrolAroundSettlement(settlement);
                    return false;
                }

                return true;
            }
        }

        // changes the name on the campaign map (hot path)
        [HarmonyPatch(typeof(PartyNameplateVM), "RefreshDynamicProperties")]
        public class PartyNameplateVMRefreshDynamicPropertiesPatch
        {
            private static readonly Dictionary<MobileParty, string> Map = new Dictionary<MobileParty, string>();

            private static void Postfix(PartyNameplateVM __instance, ref string ____fullNameBind)
            {
                //T.Restart();
                // Leader is null after a battle, crashes after-action
                // this staged approach feels awkward but it's fast
                if (__instance.Party?.Leader is null)
                {
                    return;
                }

                if (Map.ContainsKey(__instance.Party))
                {
                    ____fullNameBind = Map[__instance.Party];
                    //Mod.Log(T.ElapsedTicks);
                    return;
                }

                if (!IsBM(__instance.Party))
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
        public class PlayerEncounterDoMeetingInternalPatch
        {
            private static bool Prefix(PartyBase ____encounteredParty)
            {
                if (IsBM(____encounteredParty.MobileParty))
                {
                    GameMenu.SwitchToMenu("encounter");
                    return false;
                }

                return true;
            }
        }

        // prevent militias from attacking parties they can destroy easily
        [HarmonyPatch(typeof(MobileParty), "CanAttack")]
        public class MobilePartyCanAttackPatch
        {
            private static void Postfix(MobileParty __instance, MobileParty targetParty, ref bool __result)
            {
                if (__result && PartyMilitiaMap.ContainsKey(__instance))
                {
                    var party1Strength = __instance.GetTotalStrengthWithFollowers();
                    var party2Strength = targetParty.GetTotalStrengthWithFollowers();
                    var delta = (party1Strength - party2Strength) / party1Strength * 100;
                    __result = delta <= Globals.Settings.MaxStrengthDeltaPercent;
                }
            }
        }

        // workaround for mobileParty.MapFaction.Leader is null
        /*
        public void AiHourlyTick(MobileParty mobileParty, PartyThinkParams p)
        {
            if (mobileParty.IsMilitia || mobileParty.IsCaravan || (mobileParty.IsVillager || mobileParty.IsBandit) || !mobileParty.MapFaction.IsMinorFaction && !mobileParty.MapFaction.IsKingdomFaction && !mobileParty.MapFaction.Leader.IsNoble || (mobileParty.IsDeserterParty || mobileParty.CurrentSettlement is not null && mobileParty.CurrentSettlement.SiegeEvent is not null))
        */
        [HarmonyPatch(typeof(AiPatrollingBehavior), "AiHourlyTick")]
        public class AiPatrollingBehaviorAiHourlyTickPatch
        {
            private static void Prefix(MobileParty mobileParty, PartyThinkParams p)
            {
                if (mobileParty is not null
                    && p is not null
                    && PartyMilitiaMap.ContainsKey(mobileParty)
                    && mobileParty.ActualClan?.Leader is null)
                {
                    Traverse.Create(mobileParty.ActualClan).Field<Hero>("_leader").Value = mobileParty.LeaderHero;
                }
            }
        }

        // force Heroes to die in simulated combat
        [HarmonyPatch(typeof(MapEventSide), "ApplySimulationDamageToSelectedTroop")]
        public static class MapEventSideApplySimulationDamageToSelectedTroopPatch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = instructions.ToList();

                // strategy: come in after AddHeroDamage.  Feed the helper `this`.  NOP out the original if {} completely.
                var insertPoint = codes.FindIndex(c => c?.operand is MethodInfo mi
                                                       && mi == AccessTools.Method(typeof(MapEventSide), "AddHeroDamage"));
                insertPoint++;
                var callStack = new List<CodeInstruction>
                {
                    new(OpCodes.Ldarg_0),
                    new(OpCodes.Call, AccessTools.Method(typeof(MapEventSideApplySimulationDamageToSelectedTroopPatch), nameof(HandleHeroWounding)))
                };

                for (var i = 0; i < 32; i++)
                {
                    codes[insertPoint + i].opcode = OpCodes.Nop;
                }

                codes.InsertRange(insertPoint, callStack);
                //codes.Do(c => FileLog.Log($"{c.opcode,-10}\t{c.operand}"));
                return codes.AsEnumerable();
            }

            private static void HandleHeroWounding(MapEventSide mapEventSide)
            {
                var BattleObserver = Traverse.Create(mapEventSide).Property<IBattleObserver>("BattleObserver").Value;
                var MissionSide = mapEventSide.MissionSide;
                var _selectedSimulationTroopDescriptor = Traverse.Create(mapEventSide).Field<UniqueTroopDescriptor>("_selectedSimulationTroopDescriptor").Value;
                var _selectedSimulationTroop = Traverse.Create(mapEventSide).Field<CharacterObject>("_selectedSimulationTroop").Value;
                if (_selectedSimulationTroop.HeroObject.HitPoints <= 0
                    && _selectedSimulationTroop.StringId.EndsWith("_Bandit_Militia"))
                {
                    Traverse.Create(_selectedSimulationTroop.HeroObject).Field("CharacterStates").SetValue(3);
                    BattleObserver?.TroopNumberChanged(MissionSide, mapEventSide.GetAllocatedTroopParty(_selectedSimulationTroopDescriptor), _selectedSimulationTroop, -1, 1, 0);
                }
                else if (!_selectedSimulationTroop.StringId.EndsWith("_Bandit_Militia"))
                {
                    Traverse.Create(_selectedSimulationTroop.HeroObject).Field("CharacterStates").SetValue(2);
                    BattleObserver?.TroopNumberChanged(MissionSide, mapEventSide.GetAllocatedTroopParty(_selectedSimulationTroopDescriptor), _selectedSimulationTroop, -1, 0, 1);
                }
            }
        }
    }
}
