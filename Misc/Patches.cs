using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using static Bandit_Militias.Helper.Globals;
using static Bandit_Militias.Mod;
using Debug = TaleWorlds.Library.Debug;

// ReSharper disable UnusedMember.Local   
// ReSharper disable RedundantAssignment  
// ReSharper disable InconsistentNaming

namespace Bandit_Militias.Misc
{
    public static class Patches
    {
        [HarmonyPatch(typeof(Campaign), "OnInitialize")]
        public static class CampaignOnInitializePatch
        {
            private static void Postfix()
            {
                try
                {
                    Log("Campaign.OnInitialize");
                    var militias = MobileParty.All.Where(x => x != null && x.Name.Equals("Bandit Militia")).ToList();
                    Log($"Militias: {militias.Count()}");
                    Log($"Homeless: {militias.Count(x => x.HomeSettlement == null)}");
                    militias.Where(x => x.HomeSettlement == null)
                        .Do(x =>
                        {
                            Log("Fixing null HomeSettlement (destroyed hideout)");
                            x.HomeSettlement = Game.Current.ObjectManager.GetObjectTypeList<Settlement>().Where(y => y.IsHideout()).GetRandomElement();
                        });
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }
        }

        // init section.  NRE without setting leaders again here for whatever reason
        [HarmonyPatch(typeof(MapScreen), "OnInitialize")]
        public static class MapScreenOnInitializePatch
        {
            private static void Postfix()
            {
                try
                {
                    Log("MapScreen.OnInitialize");
                    Helper.CalcMergeCriteria();
                    Trace("Setting Militia leaders");
                    foreach (var party in MobileParty.All.Where(x => Helper.IsValidParty(x) &&
                                                                     x.Name.ToString() == "Bandit Militia"))
                    {
                        var hero = party.MemberRoster.GetCharacterAtIndex(0);
                        party.MemberRoster.AddToCounts(hero, 1, true);
                        party.ChangePartyLeader(party.MemberRoster.GetCharacterAtIndex(0));
                        if (party.LeaderHero.Clan == null)
                        {
                            Log("Fixing clan");
                            party.LeaderHero.Clan = Clan.BanditFactions.ToList()[Rng.Next(1, Clan.BanditFactions.Count())];
                        }

                        //var aTopTierTroop = party.Party.MemberRoster.Troops
                        //    .OrderByDescending(y => y.Tier).FirstOrDefault();
                        //if (aTopTierTroop != null)
                        //{
                        //    party.ChangePartyLeader(aTopTierTroop);
                        //}
                    }
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }
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

        // BUG some parties were throwing when exiting post-battle loot menu 1.4.2b
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

        // BUG more vanilla patching
        [HarmonyPatch(typeof(BanditsCampaignBehavior), "OnSettlementEntered")]
        public static class BanditsCampaignBehaviorOnSettlementEnteredPatch
        {
            private static bool Prefix(ref MobileParty mobileParty, Hero hero)
            {
                if (mobileParty == null)
                {
                    Trace("Fixing vanilla call with null MobileParty at BanditsCampaignBehavior.OnSettlementEntered");
                    if (hero == null)
                    {
                        Trace("Hero is also null");
                        return false;
                    }

                    var parties = hero.OwnedParties?.ToList();
                    if (parties == null ||
                        parties.Count == 0)
                    {
                        Trace("No parties available...");
                        return false;
                    }

                    mobileParty = parties[0]?.MobileParty;
                    Trace($"New party: {mobileParty}");
                    if (mobileParty == null)
                    {
                        // some shit still falls through this far
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(MapEventManager), MethodType.Constructor, typeof(Game))]
        public static class MapEventManagerCtorPatch
        {
            private static void Postfix(MapEventManager __instance)
            {
                try
                {
                    Helper.Globals.MapEventManager = __instance;
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }
        }

        // BUG (seeing leftovers being cleaned up, in 1.4.2b)
        [HarmonyPatch(typeof(MapEventManager), "OnAfterLoad")]
        public static class MapEventManagerOnAfterLoadPatch
        {
            private static void Postfix(List<MapEvent> ___mapEvents)
            {
                try
                {
                    Helper.FinalizeBadMapEvents(___mapEvents);
                }
                catch (Exception e)
                {
                    Log(e);
                }
            }
        }

        // pretty sure this doesn't work.  WIP, have seen bandits fleeing
        // not even militia, just looters fighting looters.  v1.4.2b
        //[HarmonyPatch(typeof(MobileParty), "GetFleeBehavior")]
        //public static class MobilePartyGetFleeBehaviorPatch
        //{
        //    private static bool Prefix(MobileParty __instance, MobileParty partyToFleeFrom)
        //    {
        //        if (__instance.CurrentSettlement != null ||
        //            partyToFleeFrom.CurrentSettlement != null)
        //        {
        //            return false;
        //        }
        //
        //        if (__instance.IsBandit && partyToFleeFrom.IsBandit)
        //        {
        //            Log($"Not fleeing from {partyToFleeFrom.Name} for {__instance.Name}");
        //            Log($"this fucked up party {__instance.Name} claims to be a bandit, its clan is: {__instance.LeaderHero.Clan}");
        //            return false;
        //        }
        //
        //        return true;
        //    }
        //}

        [HarmonyPatch(typeof(Campaign), "HourlyTick")]
        public static class CampaignHourlyTickPatch
        {
            private static int hoursPassed;

            private static void Postfix()
            {
                tempList.Clear();
                foreach (var mobileParty in MobileParty.All
                    .Where(x => x.MemberRoster.TotalManCount == 0))
                {
                    tempList.Add(mobileParty);
                }

                PurgeList($"CampaignHourlyTickPatch Clearing {tempList.Count} empty parties");
                try
                {
                    // BUG weird neutral bandits??  - caused by merging bandits in combat?
                    foreach (var mobileParty in MobileParty.All
                        .Where(x => x.Name.Equals("Bandit Militia") &&
                                    x.MapFaction == CampaignData.NeutralFaction))
                    {
                        Log("This bandit shouldn't exist " + mobileParty + " size " + mobileParty.MemberRoster.TotalManCount);
                        tempList.Add(mobileParty);
                    }
                }
                catch (Exception ex)
                {
                    Log(ex);
                }

                PurgeList($"CampaignHourlyTickPatch Clearing {tempList.Count} weird neutral parties");
                var mapEvents = Traverse.Create(Helper.Globals.MapEventManager).Field("mapEvents").GetValue<List<MapEvent>>();
                Helper.FinalizeBadMapEvents(mapEvents);
                PurgeList($"CampaignHourlyTickPatch Clearing {tempList.Count} bad map events");

                if (hoursPassed == 23)
                {
                    Helper.CalcMergeCriteria();
                    hoursPassed = 0;
                }

                hoursPassed++;

                void PurgeList(string message)
                {
                    if (tempList.Count > 0)
                    {
                        Log(message);
                        foreach (var mobileParty in tempList)
                        {
                            mobileParty.RemoveParty();
                            mobileParty.Party.Visuals.SetMapIconAsDirty();
                        }

                        tempList.Clear();
                    }
                }
            }
        }

        // makes the hero's name appear in conversation
        [HarmonyPatch(typeof(ConversationManager), "AddConversationAgent", typeof(IAgent))]
        public static class ConversationManagerAddConversationAgentPatch
        {
            private static void Prefix(ref IAgent agent)
            {
                try
                {
                    if (agent.Character.Name.Equals("Bandit Militia"))
                    {
                        var character = (CharacterObject) agent.Character;
                        character.HeroObject.RenameHeroBackIfNeeded();
                        LastConversationName = character.Name.ToString();
                        var rename = new TextObject($"{character.HeroObject.FirstName} - Bandit Militia");
                        agent.Character.Name = rename;
                        Log("Renamed to " + rename);
                    }
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }
        }

        [HarmonyPatch(typeof(ConversationManager), "EndConversation")]
        public static class ConversationManagerEndConversationPatch
        {
            private static void Prefix(List<IAgent> ____conversationAgents)
            {
                try
                {
                    Trace($"____conversationAgents {____conversationAgents}");
                    foreach (var agent in ____conversationAgents)
                    {
                        if (agent.Character.Name.ToString().EndsWith("- Bandit Militia"))
                        {
                            var character = (CharacterObject) agent.Character;
                            character.HeroObject.RenameHeroBackIfNeeded();
                            agent.Character.Name = new TextObject(LastConversationName);
                            character.HeroObject.PartyBelongedTo.Name = new TextObject("Bandit Militia");
                            Log("Renamed back to " + LastConversationName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }
        }

        // just disperse small militias
        [HarmonyPatch(typeof(MapEventSide), "HandleMapEventEndForParty")]
        public static class MapEventSideHandleMapEventEndForPartyPatch
        {
            private static void Postfix(MapEventSide __instance)
            {
                try
                {
                    foreach (var partyBase in __instance.Parties.Where(x => x.LeaderHero != null))
                    {
                        if (partyBase.LeaderHero.Name.Equals("Bandit Militia") &&
                            partyBase.MemberRoster.TotalManCount.IsBetween(0, 10))
                        {
                            Trace("Dispersing militia of " + partyBase.MemberRoster.TotalManCount);
                            partyBase.MobileParty.RemoveParty();
                            partyBase.Visuals.SetMapIconAsDirty();
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
