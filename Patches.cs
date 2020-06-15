using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Helpers;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors.AiBehaviors;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using static Bandit_Militias.Mod;
using static Bandit_Militias.Helper;
using static Bandit_Militias.Helper.Globals;
using Debug = TaleWorlds.Library.Debug;
using Module = TaleWorlds.MountAndBlade.Module;

// ReSharper disable UnusedMember.Local   
// ReSharper disable RedundantAssignment  
// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    public class Patches
    {
        internal static void SkipIntroPatch()
        {
            // thank you CommunityPatch
            typeof(Module)
                .GetField("_splashScreenPlayed", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(Module.CurrentModule, true);
        }

        // keeps updating the list of quest parties and they're omitted from mergers
        internal static void HoursTickPartyPatch(object __instance)
        {
            try
            {
                QuestParties = Traverse.Create(__instance).Field("_validPartiesList").GetValue<List<MobileParty>>();
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }

        internal static void MobilePartyDestroyedPostfix(MobileParty mobileParty)
        {
            try
            {
                if (QuestParties.Contains(mobileParty))
                {
                    QuestParties.Remove(mobileParty);
                }
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }

        // init section
        [HarmonyPatch(typeof(MapScreen), "OnInitialize")]
        public static class MapScreenOnInitializePatch
        {
            private static void Postfix()
            {
                try
                {
                    Looters = Clan.BanditFactions.First();
                    CalcMergeCriteria();
                    Trace("Setting Militia leaders");
                    foreach (var party in MobileParty.All.Where(x => IsValidParty(x) &&
                                                                     x.Name.ToString() == "Bandit Militia"))
                    {
                        var aTopTierTroop = party.Party.MemberRoster.Troops
                            .OrderByDescending(y => y.Tier).FirstOrDefault();
                        if (aTopTierTroop != null)
                        {
                            party.ChangePartyLeader(aTopTierTroop);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }
        }

        // only way I ever found to make new parties "red"
        //[HarmonyPatch(typeof(PartyBase), "MapFaction", MethodType.Getter)]
        //public static class PartyBaseMapFactionGetterPatch
        //{
        //    private static TextObject banditMilitiaTextObject = new TextObject("Bandit Militia");
        //
        //    private static void Postfix(PartyBase __instance, ref IFaction __result)
        //    {
        //        if (__instance.LeaderHero == null &&
        //            __instance.Name.Equals(banditMilitiaTextObject))
        //        {
        //            __result = looters;
        //        }
        //    }
        //}

        // some parties were throwing when exiting post-battle loot menu
        [HarmonyPatch(typeof(MBObjectManager), "UnregisterObject")]
        public static class MBObjectManagerUnregisterObjectPatch
        {
            private static Exception Finalizer(Exception __exception)
            {
                if (__exception is ArgumentNullException)
                {
                    Log("Bandit Militias suppressing exception in Patches.cs MBObjectManagerUnregisterObjectPatch");
                    Debug.Print("Bandit Militias suppressing exception in Patches.cs MBObjectManagerUnregisterObjectPatch");
                    Debug.Print(__exception.ToString());
                    return null;
                }

                return __exception;
            }
        }

        // haven't seen any bad field battles in 1.4.2b
        [HarmonyPatch(typeof(MapEventManager), "OnAfterLoad")]
        public static class MapEventManagerOnAfterLoadPatch
        {
            private static void Postfix(List<MapEvent> ___mapEvents)
            {
                try
                {
                    foreach (var mapEvent in ___mapEvents.Where(x => x.EventType == MapEvent.BattleTypes.FieldBattle))
                    {
                        if (mapEvent.AttackerSide.TroopCount == 0 ||
                            mapEvent.DefenderSide.TroopCount == 0)
                        {
                            Trace($"Removing bad field battle with {mapEvent.AttackerSide.LeaderParty.Name}, {mapEvent.DefenderSide.LeaderParty.Name}");
                            mapEvent.FinalizeEvent();
                        }
                        else
                        {
                            Trace($"Leaving valid field battle with {mapEvent.AttackerSide.LeaderParty.Name}, {mapEvent.DefenderSide.LeaderParty.Name}");
                        }
                    }
                }
                catch (Exception e)
                {
                    Log(e);
                }
            }
        }

        // prevents vanilla NRE from parties without Owner trying to pay for upgrades... 
        [HarmonyPatch(typeof(PartyUpgrader), "UpgradeReadyTroops", typeof(PartyBase))]
        public static class PartyUpgraderUpgradeReadyTroopsPatch
        {
            private static bool Prefix(PartyBase party)
            {
                if (party.MobileParty == null)
                {
                    Trace("party.MobileParty == null");
                    return false;
                }

                if (party.Owner == null)
                {
                    Trace("party.Owner == null, that throws vanilla in 1.4.2b, Prefix false");
                    return false;
                }

                if (QuestParties.Contains(party.MobileParty))
                {
                    Trace("Quest party");
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Campaign), "Tick")]
        public static class CampaignTickPatch
        {
            private static void Prefix()
            {
                try
                {
                    tempList.Clear();
                    MobileParty.All.Where(IsValidParty)
                        .Where(x => x.MemberRoster.TotalManCount == 0)
                        .Do(x => tempList.Add(x));
                    if (tempList.Count > 0)
                    {
                        Log($"Campaign.Tick() Clearing {tempList.Count} empty parties");
                        tempList.Do(DisbandPartyAction.ApplyDisband);
                    }
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }
        }

        // pretty sure this doesn't work.  WIP, have seen bandits fleeing and fighting other bandits
        // not even militia, just looters fighting looters.  v1.4.2b
        [HarmonyPatch(typeof(MobileParty), "GetFleeBehavior")]
        public static class MobilePartyGetFleeBehaviorPatch
        {
            private static bool Prefix(MobileParty __instance, MobileParty partyToFleeFrom)
            {
                return !(partyToFleeFrom.IsBandit && __instance.IsBandit);
            }
        }

        [HarmonyPatch(typeof(Campaign), "HourlyTick")]
        public static class CampaignHourlyTickPatch
        {
            private static void Postfix()
            {
                HoursPassed++;
                if (HoursPassed == 23)
                {
                    CalcMergeCriteria();
                    // HoursPassed set back to 0 in an other patch
                }
            }
        }

        // are these only Bandit Militias?
        [HarmonyPatch(typeof(AiBanditPatrollingBehavior), "AiHourlyTick")]
        public static class AiBanditPatrollingBehaviorAiHourlyTickPatch
        {
            private static void Prefix(ref MobileParty mobileParty)
            {
                if (mobileParty.HomeSettlement == null)
                {
                    Log($"mobileParty.MemberRoster.Reset(); {mobileParty} has no HomeSettlement");
                    {
                        if (mobileParty.Name.Equals("Bandit Militia"))
                        {
                            Log("Removing Bandit Militia");
                            mobileParty.MemberRoster.Reset();
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(MobileParty), "HourlyTick")]
        public static class MobilePartyHourlyTickPatch
        {
            private static void Postfix(MobileParty __instance)
            {
                try
                {
                    if (__instance.HomeSettlement == null)
                    {
                        Log($"HourlyTick {__instance} has no HomeSettlement, adding random town");
                        __instance.HomeSettlement = SettlementHelper.GetRandomTown();
                    }

                    // once daily per unit but the hours are counted at CampaignHourlyTickPatch
                    if (HoursPassed == 23)
                    {
                        HoursPassed = 0;
                        if (!IsValidParty(__instance))
                        {
                            return;
                        }

                        // splitting section disabled, can't get hostile parties created
                        // check daily each bandit party against the size factor and a random chance to split up
                        //if (CheckAndSplitParty(__instance))
                        //{
                        //    return;
                        //}
                    }

                    if (!IsValidParty(__instance))
                    {
                        return;
                    }

                    // location and targeting
                    var pos = __instance.Position2D;
                    var targetParty = MobileParty.FindPartiesAroundPosition(pos, SearchRadius)
                        .FirstOrDefault(x => x != __instance &&
                                             IsValidParty(x))?.Party;

                    // nobody else around SearchRadius
                    if (targetParty == null)
                    {
                        return;
                    }

                    if (__instance.ShortTermBehavior == AiBehavior.FleeToPoint ||
                        __instance.DefaultBehavior == AiBehavior.FleeToPoint)
                    {
                        Trace(__instance.ShortTermBehavior);
                        return;
                    }

                    // first clause prevents it from running for the 'other' party in a merge
                    // also prevent a militia compromised of more than 50% calvary because ouch
                    Traverse.Create(__instance).Property("TargetParty").SetValue(targetParty.MobileParty);
                    if (__instance.TargetParty?.TargetParty == __instance)
                    {
                        return;
                    }

                    var militiaStrength = targetParty.TotalStrength + __instance.Party.TotalStrength;
                    var militiaCavalryCount = GetMountedTroopHeadcount(__instance.MemberRoster) +
                                              GetMountedTroopHeadcount(targetParty.MemberRoster);
                    var militiaTotalCount = __instance.MemberRoster.TotalManCount + targetParty.MemberRoster.TotalManCount;
                    if (militiaStrength < MaxPartyStrength &&
                        militiaTotalCount < AvgHeroPartyMaxSize &&
                        militiaCavalryCount < militiaTotalCount / 2)
                    {
                        Trace($"{__instance} is suitable for merge");
                        var distance = targetParty.Position2D.Distance(__instance.Position2D);
                        // the FindAll is returning pretty fast, small sample average was 600 ticks
                        //var timer = new Stopwatch();
                        //timer.Restart();
                        var closeHideOuts = Settlement.FindAll(x => x.IsHideout())
                            .Where(x => targetParty.Position2D.Distance(x.Position2D) < MinDistanceFromHideout).ToList();
                        //Log(timer.ElapsedTicks);
                        if (closeHideOuts.Any())
                        {
                            //Trace($"Within {minDistanceFromHideout} distance of hideout - skipping");
                            return;
                        }

                        if (distance > 0f && distance < MergeDistance)
                        {
                            var aTopTierTroop = __instance.Party.MemberRoster.Troops
                                .OrderByDescending(x => x.Tier).First();

                            __instance.Party.AddMembers(targetParty.MemberRoster.ToFlattenedRoster());
                            __instance.Party.AddPrisoners(targetParty.PrisonRoster.ToFlattenedRoster());
                            __instance.ItemRoster.Add(targetParty.ItemRoster);
                            __instance.Name = new TextObject("Bandit Militia");
                            __instance.ChangePartyLeader(aTopTierTroop);
                            targetParty.MobileParty.RemoveParty();
                            Log($"{__instance.Name} forms. {__instance.Party.NumberOfAllMembers} troops ({GetMountedTroopHeadcount(__instance.MemberRoster)} cavalry), strength {Math.Round(__instance.Party.TotalStrength)}");

                            // this just won't work... Neutral parties
                            // create a new party merged from the two
                            //var mobileParty = MBObjectManager.Instance.CreateObject<MobileParty>("Bandit Militia");
                            //
                            //mobileParty.Party.AddMembers(targetParty.MemberRoster.ToFlattenedRoster());
                            //mobileParty.Party.AddPrisoners(targetParty.PrisonRoster.ToFlattenedRoster());
                            //mobileParty.Party.AddMembers(__instance.MemberRoster.ToFlattenedRoster());
                            //mobileParty.Party.AddPrisoners(__instance.PrisonRoster.ToFlattenedRoster());
                            //mobileParty.InitializeMobileParty(
                            //    new TextObject("Bandit Militia"),
                            //    mobileParty.MemberRoster,
                            //    mobileParty.PrisonRoster,
                            //    __instance.Position2D,
                            //    2f);
                            //
                            //var hideouts = Settlement.All.Where(x => x.IsHideout()).ToList();
                            //
                            //mobileParty.Ai.EnableAi();
                            //mobileParty.Ai.SetAIState(AIState.PatrollingAroundLocation);
                            //mobileParty.Ai.SetDoNotMakeNewDecisions(false);
                            //mobileParty.HomeSettlement = hideouts[Rng.Next(0, hideouts.Count)];
                            //mobileParty.Party.Visuals.SetMapIconAsDirty();
                            //mobileParty.Position2D = MobileParty.MainParty.Position2D;
                            //
                            //__instance.RemoveParty();
                        }
                        else
                        {
                            __instance.SetMoveEngageParty(targetParty.MobileParty);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }
        }
    }
}
