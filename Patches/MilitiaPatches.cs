using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Bandit_Militias.Helpers;
using HarmonyLib;
using SandBox.View.Map;
using SandBox.ViewModelCollection;
using SandBox.ViewModelCollection.MobilePartyTracker;
using SandBox.ViewModelCollection.Nameplate;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
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

                if (Campaign.CurrentTime - lastChecked < 1f)
                {
                    return;
                }

                lastChecked = Campaign.CurrentTime;
                var hideouts = Settlement.All.Where(s => s.IsHideout).ToList();
                var parties = MobileParty.All.Where(m =>
                        m.Party.IsMobile
                        && m.CurrentSettlement is null
                        && !m.IsUsedByAQuest()
                        && m.IsBandit // as of BM 3.1.1 they are classified as Bandits
                        && m.MemberRoster.TotalManCount >= Globals.Settings.MinPartySizeToConsiderMerge)
                    .ToListQ();
                for (var index = 0; index < parties.Count; index++)
                {
                    //T.Restart();
                    var mobileParty = parties[index];

                    if (hideouts.AnyQ(s => s.Position2D.Distance(mobileParty.Position2D) < MinDistanceFromHideout))
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

                    if (mobileParty.ToString().Contains("manhunter")) // Calradia Expanded Kingdoms
                    {
                        continue;
                    }

                    CampaignTime? lastChangeDate = null;
                    if (mobileParty.IsBM())
                    {
                        lastChangeDate = PartyMilitiaMap[mobileParty].LastMergedOrSplitDate;
                    }

                    if (CampaignTime.Now < lastChangeDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
                    {
                        continue;
                    }

                    var targetParty = nearbyParties.Where(m =>
                            IsAvailableBanditParty(m)
                            && m.MemberRoster.TotalManCount + mobileParty.MemberRoster.TotalManCount >= Globals.Settings.MinPartySize)
                        .ToListQ().GetRandomElement()?.Party;

                    //Mod.Log($">T targetParty {T.ElapsedTicks / 10000F:F3}ms.");
                    // "nobody" is a valid answer
                    if (targetParty is null)
                    {
                        continue;
                    }

                    CampaignTime? targetLastChangeDate = null;
                    if (targetParty.MobileParty.IsBM())
                    {
                        targetLastChangeDate = PartyMilitiaMap[targetParty.MobileParty].LastMergedOrSplitDate;
                    }

                    if (CampaignTime.Now < targetLastChangeDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
                    {
                        continue;
                    }


                    var militiaTotalCount = mobileParty.MemberRoster.TotalManCount + targetParty.MemberRoster.TotalManCount;
                    if (MilitiaPowerPercent > Globals.Settings.GlobalPowerPercent
                        || militiaTotalCount < Globals.Settings.MinPartySize
                        || militiaTotalCount > CalculatedMaxPartySize
                        || mobileParty.Party.TotalStrength > CalculatedMaxPartyStrength
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
                    // BUG... so many hours.  parties (rarely?) don't actually get trashed, BM hero character leaks
                    // MilitiaBehavior.cs registers a daily tick event to remove them
                    mobileParty.IsDisbanding = true;
                    targetParty.MobileParty.IsDisbanding = true;
                    try
                    {
                        // can throw if Clan is null
                        Trash(mobileParty);
                        Trash(targetParty.MobileParty);
                    }
                    catch (NullReferenceException)
                    {
                        // ignore
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

        internal static IEnumerable<CodeInstruction> CalculateBasePartySpeedPatch(IEnumerable<CodeInstruction> ins)
        {
            // ReSharper disable once EntityNameCapturedOnly.Local
            float SlowBM(MobileParty mobileParty, float input)
            {
                if (PartyMilitiaMap.ContainsKey(mobileParty))
                {
                    return input * 0.025f;
                }

                return input;
            }

            var codes = ins.ToListQ();
            for (var index = 0; index < codes.Count; index++)
            {
                if (codes[index].opcode == OpCodes.Call
                    && codes[index + 1].opcode == OpCodes.Stloc_S
                    && codes[index + 2].opcode == OpCodes.Ldloca_S
                    && codes[index + 3].opcode == OpCodes.Ldloc_S)
                {
                    codes.InsertRange(index + 1, new List<CodeInstruction>
                    {
                        new(OpCodes.Dup),
                        new(OpCodes.Ldarg_1),
                        new(OpCodes.Call, AccessTools.Method(typeof(MilitiaPatches), nameof(SlowBM)))
                    });
                }
            }

            return codes.AsEnumerable();
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

        [HarmonyPatch(typeof(EnterSettlementAction), "ApplyInternal")]
        public static class EnterSettlementActionApplyInternalPatch
        {
            private static bool Prefix(MobileParty mobileParty, Settlement settlement)
            {
                if (mobileParty.IsBM())
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
                if (__result && __instance.IsBM())
                {
                    var party1Strength = __instance.GetTotalStrengthWithFollowers();
                    var party2Strength = targetParty.GetTotalStrengthWithFollowers();
                    var delta = (party1Strength - party2Strength) / party1Strength * 100;
                    __result = delta <= Globals.Settings.MaxStrengthDeltaPercent;
                }
            }
        }

        // force Heroes to appear to die in combat
        // it's not IDEAL because a hero with 20hp (Wounded) will be killed
        // I tried many other approaches that didn't come close
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
                }

                return null;
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

        [HarmonyPatch(typeof(MobileParty), "IsBandit", MethodType.Getter)]
        public static class MobilePartyIsBanditPatch
        {
            public static void Postfix(MobileParty __instance, ref bool __result)
            {
                if (__instance.PartyComponent is MilitiaPartyComponent)
                {
                    __result = true;
                }
            }
        }

        [HarmonyPatch(typeof(MobileParty), "IsBanditBossParty", MethodType.Getter)]
        public static class MobilePartyIsBanditBossPatch
        {
            public static void Postfix(MobileParty __instance, ref bool __result)
            {
                if (__instance.PartyComponent is MilitiaPartyComponent)
                {
                    __result = false;
                }
            }
        }
    }
}
