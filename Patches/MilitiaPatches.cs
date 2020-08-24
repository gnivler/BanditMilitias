using System;
using System.Linq;
using HarmonyLib;
using SandBox.View.Map;
using SandBox.ViewModelCollection.Nameplate;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors;
using TaleWorlds.CampaignSystem.SandBox.GameComponents.Map;
using TaleWorlds.Core;
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
                        Campaign.Current.TimeControlMode == CampaignTimeControlMode.FastForwardStop)
                    {
                        return;
                    }

                    var parties = MobileParty.All.Where(x => x.IsBandit && x.CurrentSettlement == null).ToList();
                    T.Restart();
                    for (var i = 0; i < parties.Count; i++)
                    {
                        var __instance = parties[i];
                        if (!IsValidParty(__instance))
                        {
                            continue;
                        }

                        var lastMergedOrSplitDate = Militia.FindMilitiaByParty(__instance)?.LastMergedOrSplitDate;
                        if (CampaignTime.Now < lastMergedOrSplitDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
                        {
                            continue;
                        }

                        var targetParty = MobileParty.FindPartiesAroundPosition(__instance.Position2D, FindRadius,
                            x => x != __instance && x.IsBandit && IsValidParty(x)).GetRandomElement()?.Party;

                        // "nobody" is a valid answer
                        if (targetParty == null)
                        {
                            continue;
                        }

                        var targetLastMergedOrSplitDate = Militia.FindMilitiaByParty(targetParty.MobileParty)?.LastMergedOrSplitDate;
                        if ( //targetLastMergedOrSplitDate != null &&
                            CampaignTime.Now < targetLastMergedOrSplitDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
                        {
                            continue;
                        }

                        var militiaTotalCount = __instance.MemberRoster.TotalManCount + targetParty.MemberRoster.TotalManCount;
                        if (militiaTotalCount > Globals.Settings.MaxPartySize ||
                            militiaTotalCount > CalculatedMaxPartySize ||
                            __instance.Party.TotalStrength > CalculatedMaxPartyStrength ||
                            NumMountedTroops(__instance.MemberRoster) + NumMountedTroops(targetParty.MemberRoster) > militiaTotalCount / 2)
                        {
                            continue;
                        }

                        if (Campaign.Current.Models.MapDistanceModel.GetDistance(targetParty.MobileParty, __instance) > MergeDistance)
                        {
                            if (!MergeMap.ContainsKey(__instance))
                            {
                                MergeMap.Add(__instance, CampaignTime.Now);
                            }

                            if (!IsMovingToBandit(__instance, targetParty.MobileParty) &&
                                CampaignTime.Now > MergeMap[__instance] + CampaignTime.Hours(Globals.Settings.CooldownHours))
                            {
                                //var midPoint = GetMidPoint(__instance.Position2D, targetParty.Position2D);
                                MergeMap.Remove(__instance);
                                Mod.Log($"{__instance} seeking >> target {targetParty.MobileParty}");
                                __instance.SetMoveEscortParty(targetParty.MobileParty);
                                Mod.Log($"SetNavigationModeParty ==> {T.ElapsedTicks / 10000F:F3}ms");


                                if (targetParty.MobileParty.MoveTargetParty != __instance)
                                {
                                    MergeMap.Remove(targetParty.MobileParty);
                                    Mod.Log($"{targetParty.MobileParty} target << seeking {__instance}");
                                    targetParty.MobileParty.SetMoveEscortParty(__instance);
                                    Mod.Log($"SetNavigationModeTargetParty ==> {T.ElapsedTicks / 10000F:F3}ms");
                                }
                            }

                            return;
                        }

                        if (Settlement.FindSettlementsAroundPosition(__instance.Position2D, MinDistanceFromHideout, x => x.IsHideout()).Any())
                        {
                            continue;
                        }

                        MergeMap.Remove(__instance);
                        MergeMap.Remove(targetParty.MobileParty);

                        // create a new party merged from the two
                        var rosters = MergeRosters(__instance, targetParty);
                        var militia = new Militia(__instance, rosters[0], rosters[1]);
                        // teleport new militias near the player
                        if (testingMode)
                        {
                            // in case a prisoner
                            var party = Hero.MainHero.PartyBelongedTo ?? Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;
                            militia.MobileParty.Position2D = party.Position2D;
                        }

                        militia.MobileParty.Party.Visuals.SetMapIconAsDirty();
                        Trash(__instance);
                        Trash(targetParty.MobileParty);

                        parties = MobileParty.All.Where(x => x.IsBandit && x.CurrentSettlement == null).ToList();
                        Mod.Log($"finished ==> {T.ElapsedTicks / 10000F:F3}ms");
                        Mod.Log($"MergeMap.Count ==> {MergeMap.Count}");
                    }

                    Mod.Log($"Looped ==> {T.ElapsedTicks / 10000F:F3}ms");
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
            private const float SpeedFactor = 0.88f;

            private static void Postfix(MobileParty mobileParty, ref float __result)
            {
                if (mobileParty.StringId.StartsWith("Bandit_Militia"))
                {
                    __result *= SpeedFactor;
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
                    characterObject.HeroObject.PartyBelongedTo.StringId.StartsWith("Bandit_Militia"))
                {
                    bannerKey = Militia.FindMilitiaByParty(characterObject.HeroObject.PartyBelongedTo).Banner.Serialize();
                }
            }
        }

        // changes the little shield icon under the party
        [HarmonyPatch(typeof(PartyBase), "Banner", MethodType.Getter)]
        public class PartyBaseBannerPatch
        {
            private static void Postfix(PartyBase __instance, ref Banner __result)
            {
                if (Globals.Settings.RandomBanners &&
                    __instance.MobileParty != null &&
                    __instance.MobileParty.StringId.StartsWith("Bandit_Militia"))
                {
                    __result = Militia.FindMilitiaByParty(__instance.MobileParty)?.Banner;
                }
            }
        }

        // changes the shields in combat
        [HarmonyPatch(typeof(PartyGroupAgentOrigin), "Banner", MethodType.Getter)]
        public class PartyGroupAgentOriginBannerGetterPatch
        {
            private static void Postfix(IAgentOriginBase __instance, ref Banner __result)
            {
                var party = (PartyBase) __instance.BattleCombatant;
                if (Globals.Settings.RandomBanners &&
                    party.MobileParty != null &&
                    party.MobileParty.StringId.StartsWith("Bandit_Militia"))
                {
                    __result = Militia.FindMilitiaByParty(party.MobileParty)?.Banner;
                }
            }
        }

        [HarmonyPatch(typeof(MobileParty), "DailyTick")]
        public static class MobilePartyDailyTickPatch
        {
            private static void Postfix(MobileParty __instance)
            {
                if (!IsValidParty(__instance))
                {
                    return;
                }

                // check daily each bandit party against the size factor and a random chance to split up
                TrySplitParty(__instance);
            }
        }

#if OneFourTwo
        [HarmonyPatch(typeof(EnterSettlementAction), "ApplyForParty")]
        public class EnterSettlementActionApplyForPartyPatch
        {
            private static bool Prefix(MobileParty owner, Settlement settlement)
            {
                if (owner.StringId.StartsWith("Bandit_Militia"))
                {
                    Mod.Log($"Preventing {owner} from entering {settlement}");
                    owner.SetMovePatrolAroundSettlement(settlement);
                    return false;
                }

                return true;
            }
        }
    }  
}
#else
        private static bool Prefix(MobileParty mobileParty, Settlement settlement)
        {
            if (mobileParty.StringId.StartsWith("Bandit_Militia"))
            {
                Mod.Log($"Preventing {mobileParty} from entering {settlement}");
                mobileParty.SetMovePatrolAroundSettlement(settlement);
                return false;
            }

            return true;
        }

#endif

        // changes the name on the campaign map (hot path)
        [HarmonyPatch(typeof(PartyNameplateVM), "RefreshDynamicProperties")]
        public class PartyNameplateVMRefreshDynamicPropertiesPatch
        {
            private static void Postfix(PartyNameplateVM __instance, ref string ____fullNameBind)
            {
                // Leader is null after a battle, crashes after-action
                if (__instance.Party.StringId.StartsWith("Bandit_Militia") &&
                    __instance.Party.Leader != null)
                {
                    var heroName = __instance.Party.LeaderHero?.FirstName.ToString();
                    ____fullNameBind = $"{Possess(heroName)} Bandit Militia";
                }
            }
        }

        // blocks conversations with militias
        [HarmonyPatch(typeof(PlayerEncounter), "DoMeetingInternal")]
        public class MissionConversationVMCtorPatch
        {
            private static bool Prefix(PartyBase ____encounteredParty)
            {
                if (____encounteredParty.MobileParty.StringId.StartsWith("Bandit_Militia"))
                {
                    GameMenu.SwitchToMenu("encounter");
                    return false;
                }

                return true;
            }
        }

        // 1.4.3b vanilla issue?  have to replace the WeaponComponentData in some cases
        // this causes naked militias when 'fixed' in this manner
        [HarmonyPatch(typeof(PartyVisual), "WieldMeleeWeapon")]
        public class PartyVisualWieldMeleeWeaponPatch
        {
            private static void Prefix(PartyBase party)
            {
                // todo remove after a version or two... maybe solved it by changing/fixing CreateEquipment()
                for (var i = 0; i < 5; ++i)
                {
                    if (party?.Leader?.Equipment[i].Item != null && party.Leader.Equipment[i].Item.PrimaryWeapon == null)
                    {
                        party.Leader.Equipment[i] = new EquipmentElement(ItemObject.All.First(x =>
                            x.StringId == party.Leader.Equipment[i].Item.StringId));
                    }
                }
            }
        }

        // prevents militias from being added to DynamicBodyCampaignBehavior._heroBehaviorsDictionary 
        [HarmonyPatch(typeof(DynamicBodyCampaignBehavior), "CanBeEffectedByProperties")]
        public class DynamicBodyCampaignBehaviorCanBeEffectedByPropertiesPatch
        {
            private static void Postfix(Hero hero, ref bool __result)
            {
                if (hero.PartyBelongedTo != null &&
                    hero.PartyBelongedTo.StringId.StartsWith("Bandit_Militia"))
                {
                    __result = false;
                }
            }
        }
    }
}
