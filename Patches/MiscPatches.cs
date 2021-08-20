using System;
using System.Collections.Generic;
using System.Linq;
using Bandit_Militias.Helpers;
using HarmonyLib;
using SandBox;
using SandBox.View.Map;
using SandBox.ViewModelCollection.MobilePartyTracker;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors.AiBehaviors;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static Bandit_Militias.Helpers.Helper;
using static Bandit_Militias.Globals;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Local
// ReSharper disable RedundantAssignment
// ReSharper disable InconsistentNaming

namespace Bandit_Militias.Patches
{
    public static class MiscPatches
    {
        [HarmonyPatch(typeof(MapScreen), "OnInitialize")]
        public static class MapScreenOnInitializePatch
        {
            private static void Postfix()
            {
                Mod.Log("MapScreen.OnInitialize");
                MinSplitSize = Globals.Settings.MinPartySize * 2;
                EquipmentItems.Clear();
                PopulateItems();

                var filter = new List<string>
                {
                    "regular_fighter",
                    "veteran_borrowed_troop",
                };

                Recruits = CharacterObject.All.Where(c =>
                    c.Level == 11
                    && c.Occupation == Occupation.Soldier
                    && !filter.Contains(c.StringId)
                    && !c.StringId.EndsWith("_tier_1"));

                // used for armour
                foreach (ItemObject.ItemTypeEnum value in Enum.GetValues(typeof(ItemObject.ItemTypeEnum)))
                {
                    ItemTypes[value] = Items.All.Where(x =>
                        x.Type == value && x.Value >= 1000 && x.Value <= Globals.Settings.MaxItemValue * Variance).ToList();
                }

                // front-load
                BanditEquipment.Clear();
                for (var i = 0; i < 1000; i++)
                {
                    BanditEquipment.Add(BuildViableEquipmentSet());
                }

                PartyMilitiaMap.Clear();
                Hideouts = Settlement.FindAll(x => x.IsHideout()).ToList();

                // considers leaderless militias
                var militias = MobileParty.All.Where(m =>
                    m.LeaderHero is not null && m.StringId.StartsWith("Bandit_Militia")).ToList();

                for (var i = 0; i < militias.Count; i++)
                {
                    var militia = militias[i];
                    var recreatedMilitia = new Militia(militia);
                    SetMilitiaPatrol(recreatedMilitia.MobileParty);
                    PartyMilitiaMap.Add(recreatedMilitia.MobileParty, recreatedMilitia);
                }

                FlushMilitiaCharacterObjects();
                Mod.Log($"Militias: {militias.Count} (registered {PartyMilitiaMap.Count})");
                // 1.6 is dropping the militia settlements at some point, I haven't figured out where
                ReHome();
                DoPowerCalculations(true);
                RunLateManualPatches();
            }
        }

        // workaround for mobileParty.MapFaction.Leader is null, still needed in 1.6 
        //public void AiHourlyTick(MobileParty mobileParty, PartyThinkParams p)
        //{
        //    if (mobileParty.IsMilitia || mobileParty.IsCaravan || (mobileParty.IsVillager || mobileParty.IsBandit) || !mobileParty.MapFaction.IsMinorFaction && !mobileParty.MapFaction.IsKingdomFaction && !mobileParty.MapFaction.Leader.IsNoble || (mobileParty.IsDeserterParty || mobileParty.CurrentSettlement is not null && mobileParty.CurrentSettlement.SiegeEvent is not null))
        [HarmonyPatch(typeof(AiPatrollingBehavior), "AiHourlyTick")]
        public static class AiPatrollingBehaviorAiHourlyTickPatch
        {
            public static void Prefix(MobileParty mobileParty)
            {
                if (mobileParty.ActualClan is not null
                    && mobileParty.ActualClan.Leader is null)
                {
                    var hero = HeroCreatorCopy.CreateBanditHero(mobileParty.ActualClan, null);
                    hero.SetName(new TextObject($"{mobileParty.ActualClan.Culture} Leader"));
                    hero.StringId += "Bandit_Militia";
                    hero.CharacterObject.StringId += "Bandit_Militia";
                    mobileParty.StringId += "Bandit_Militia";
                    mobileParty.ActualClan?.SetLeader(hero);
                    Mod.Log($"Added new Leader for {mobileParty.ActualClan.Culture}: {hero.CharacterObject?.StringId}");
                }
            }
        }

        // just disperse loser militias
        [HarmonyPatch(typeof(MapEventSide), "HandleMapEventEndForPartyInternal", typeof(PartyBase))]
        public static class MapEventSideHandleMapEventEndForPartyPatch
        {
            private static void Prefix(MapEventSide __instance, PartyBase party, ref bool __state)
            {
                __state = Traverse.Create(__instance.MapEvent).Method("IsWinnerSide", party.Side).GetValue<bool>();
            }

            private static void Postfix(MapEventSide __instance, PartyBase party, ref bool __state)
            {
                if (party?.MobileParty is null
                    || !party.MobileParty.IsBM()
                    || party.PrisonRoster is not null
                    && party.PrisonRoster.Contains(Hero.MainHero.CharacterObject))
                {
                    return;
                }

                // has yet to fire, needs adjustment if in fact any exist
                if (party.MobileParty.LeaderHero is null)
                {
                    party.MobileParty.SetCustomName(new TextObject("Leaderless Bandit Militia"));
                }

                var wonBattle = __state;
                if (__instance.MapEvent.HasWinner
                    && !wonBattle
                    && party.MobileParty.IsBM()
                    && party.MemberRoster.TotalManCount <= Globals.Settings.MinPartySize)
                {
                    Mod.Log($">>> Dispersing {party.Name} of {party.MemberRoster.TotalHealthyCount}+{party.MemberRoster.TotalWounded}w+{party.PrisonRoster?.Count}p");
                    Trash(party.MobileParty);
                }
            }
        }

        // not firing in 1.5.10
        [HarmonyPatch(typeof(HeroCreator), "CreateRelativeNotableHero")]
        public static class HeroCreatorCreateRelativeNotableHeroPatch
        {
            private static bool Prefix(Hero relative)
            {
                if (PartyMilitiaMap.Values.Any(x => x.Hero == relative))
                {
                    Mod.Log("Not creating relative of Bandit Militia hero");
                    return false;
                }

                return true;
            }
        }

        // 1.5.9 throws a vanilla stack, ignoring it seems to be fine
        public static Exception wait_menu_prisoner_wait_on_tickFinalizer(Exception __exception)
        {
            if (__exception is not null)
            {
                Mod.Log($"HACK Squelching exception at HeroCreator.CreateRelativeNotableHero");
            }

            return null;
        }

        // possibly related to Separatism and new kingdoms, ignoring it seems fine...
        [HarmonyPatch(typeof(BanditPartyComponent), "Name", MethodType.Getter)]
        public static class BanditPartyComponentGetNamePatch
        {
            public static Exception Finalizer(BanditPartyComponent __instance, Exception __exception)
            {
                if (__exception is not null)
                {
                    Mod.Log($"HACK Squelching exception at BanditPartyComponent.get_Name");
                    if (__instance.Hideout is null)
                    {
                        Mod.Log("Hideout is null.");
                    }

                    if (__instance.Hideout?.MapFaction is null)
                    {
                        Mod.Log("MapFaction is null.");
                    }

                    if (__instance.Hideout?.MapFaction?.Name is null)
                    {
                        Mod.Log("Name is null.");
                    }

                    Mod.Log($"MapFaction {__instance.Hideout?.MapFaction}.");
                    Mod.Log($"MapFaction.Name {__instance.Hideout?.MapFaction?.Name}.");
                }

                return null;
            }
        }

        // vanilla fix for 1.5.9?
        // maybe actually the chickens et al?
        // not happening on 3.0.3 for 1.5.9/10
        [HarmonyPatch(typeof(SPInventoryVM), "InitializeInventory")]
        public static class SPInventoryVMInitializeInventoryVMPatch
        {
            private static Exception Finalizer(Exception __exception)
            {
                if (__exception is not null)
                {
                    Mod.Log($"HACK Squelching exception at SPInventoryVM.InitializeInventory");
                }

                return null;
            }
        }

        [HarmonyPatch(typeof(MobilePartyTrackerVM), MethodType.Constructor, typeof(Camera), typeof(Action<Vec2>))]
        public static class MobilePartyTrackerVMCtorPatch
        {
            private static void Postfix(MobilePartyTrackerVM __instance)
            {
                Globals.MobilePartyTrackerVM = __instance;
            }
        }

        // the 2nd one at least
        // seems to make the skulls red in combat, eg kill
        [HarmonyPatch(typeof(BattleAgentLogic), "OnAgentRemoved")]
        public static class BattleAgentLogicOnAgentRemovedPatch
        {
            private static void Prefix(Agent affectedAgent, ref AgentState agentState)
            {
                if (affectedAgent.Character is not null
                    && affectedAgent.Character.StringId.EndsWith("Bandit_Militia"))
                {
                    agentState = AgentState.Killed;
                }
            }
        }

        [HarmonyPatch(typeof(Mission), "OnAgentRemoved")]
        public static class MissionOnAgentRemovedPatch
        {
            private static void Prefix(Agent affectedAgent, ref AgentState agentState)
            {
                if (affectedAgent.Character is not null
                    && affectedAgent.Character.StringId.EndsWith("Bandit_Militia"))
                {
                    agentState = AgentState.Killed;
                }
            }
        }
    }
}
