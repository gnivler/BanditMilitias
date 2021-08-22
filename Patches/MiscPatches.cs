using System;
using System.Collections.Generic;
using System.Linq;
using Bandit_Militias.Helpers;
using HarmonyLib;
using SandBox;
using SandBox.View.Map;
using SandBox.ViewModelCollection.MobilePartyTracker;
using TaleWorlds.CampaignSystem;
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

                DoPowerCalculations(true);
                FlushMilitiaCharacterObjects();
                // 1.6 is dropping the militia settlements at some point, I haven't figured out where
                ReHome();
                Mod.Log($"Militias: {militias.Count} (registered {PartyMilitiaMap.Count})");
                RunLateManualPatches();
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
