using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SandBox.View.Map;
using SandBox.ViewModelCollection.Nameplate;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
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

                    var parties = MobileParty.All.Where(x =>
                        x.Party.IsMobile &&
                        x.CurrentSettlement == null &&
                        !x.IsCurrentlyUsedByAQuest &&
                        x.IsBandit).ToList();
                    //T.Restart();
                    for (var index = 0; index < parties.Count; index++)
                    {
                        var mobileParty = parties[index];
                        if (mobileParty.MoveTargetParty != null &&
                            mobileParty.MoveTargetParty.IsBandit ||
                            // Calradia Expanded Kingdoms
                            mobileParty.ToString().Contains("manhunter") ||
                            mobileParty.IsTooBusyToMerge())
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

                        var targetParty = MobileParty.FindPartiesAroundPosition(mobileParty.Position2D, FindRadius,
                                x => x != mobileParty && IsValidParty(x) &&
                                     x.MemberRoster.TotalManCount + mobileParty.MemberRoster.TotalManCount >= Globals.Settings.MinPartySize)
                            .ToList().GetRandomElement()?.Party;

                        // "nobody" is a valid answer
                        if (targetParty == null)
                        {
                            continue;
                        }

                        CampaignTime? targetLastChangeDate = null;
                        if (PartyMilitiaMap.ContainsKey(targetParty.MobileParty))
                        {
                            targetLastChangeDate = PartyMilitiaMap[mobileParty].LastMergedOrSplitDate;
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

                        if (Campaign.Current.Models.MapDistanceModel.GetDistance(targetParty.MobileParty, mobileParty) > MergeDistance)
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
                        militia.MobileParty.SetMovePatrolAroundPoint(militia.MobileParty.Position2D);

                        // teleport new militias near the player
                        if (TestingMode)
                        {
                            // in case a prisoner
                            var party = Hero.MainHero.PartyBelongedTo ?? Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;?
                            militia.MobileParty.Position2D = party.Position2D;
                        }

                        militia.MobileParty.Party.Visuals.SetMapIconAsDirty();
                        Trash(mobileParty);
                        Trash(targetParty.MobileParty);
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
                    characterObject.HeroObject?.PartyBelongedTo != null &&
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
                    __instance.MobileParty != null &&
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
                    party.MobileParty != null &&
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

        // changes the name on the campaign map (hot path)
        [HarmonyPatch(typeof(PartyNameplateVM), "RefreshDynamicProperties")]
        public class PartyNameplateVMRefreshDynamicPropertiesPatch
        {
            private static readonly Dictionary<MobileParty, string> Map = new Dictionary<MobileParty, string>();

            private static void Postfix(PartyNameplateVM __instance, ref string ____fullNameBind)
            {
                //T.Restart();
                // Leader is null after a battle, crashes after-action
                // this staged approach feels awkward but it's fast; FindMilitiaByParty on every frame is wasteful
                if (__instance.Party?.Leader == null)
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
        public class MissionConversationVMCtorPatch
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

        // 1.4.3b vanilla issue?  have to replace the WeaponComponentData in some cases
        // this causes naked militias when 'fixed' in this manner
        // todo remove after a version or two... maybe solved it by changing/fixing CreateEquipment()
        // commented out at 1.5.8
        //[HarmonyPatch(typeof(PartyVisual), "WieldMeleeWeapon")]
        //public class PartyVisualWieldMeleeWeaponPatch
        //{
        //    private static void Prefix(PartyBase party)
        //    {
        //        for (var i = 0; i < 5; ++i)
        //        {
        //            if (party?.Leader?.Equipment[i].Item != null && party.Leader.Equipment[i].Item.PrimaryWeapon == null)
        //            {
        //                party.Leader.Equipment[i] = new EquipmentElement(ItemObject.All.First(x =>
        //                    x.StringId == party.Leader.Equipment[i].Item.StringId));
        //            }
        //        }
        //    }
        //}

        // prevents militias from being added to DynamicBodyCampaignBehavior._heroBehaviorsDictionary 
        //[HarmonyPatch(typeof(DynamicBodyCampaignBehavior), "CanBeEffectedByProperties")]
        //public class DynamicBodyCampaignBehaviorCanBeEffectedByPropertiesPatch
        //{
        //    private static void Postfix(Hero hero, ref bool __result)
        //    {
        //        if (hero.PartyBelongedTo != null &&
        //            hero.PartyBelongedTo.StringId.StartsWith("Bandit_Militia"))
        //        {
        //            __result = false;
        //        }
        //    }
        //}

        // prevent militias from attacking parties they can destroy easily
        [HarmonyPatch(typeof(MobileParty), "CanAttack")]
        public class MobilePartyCanAttackPatch
        {
            private static void Postfix(MobileParty __instance, MobileParty targetParty, ref bool __result)
            {
                if (__result &&
                    !targetParty.IsGarrison &&
                    !targetParty.IsMilitia &&
                    PartyMilitiaMap.ContainsKey(__instance))
                    //Militias.Any(x => x.MobileParty == __instance))
                {
                    if (targetParty == MobileParty.MainParty)
                    {
                        __result = true;
                    }
                    else
                    {
                        var party1Strength = __instance.GetTotalStrengthWithFollowers();
                        var party2Strength = targetParty.GetTotalStrengthWithFollowers();
                        var delta = (party1Strength - party2Strength) / party1Strength * 100;
                        __result = delta <= Globals.Settings.PartyStrengthDeltaPercent;
                    }
                }
            }
        }
    }
}
