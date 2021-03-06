using System;
using System.Collections.Generic;
using System.Linq;
using Bandit_Militias.Helpers;
using HarmonyLib;
using SandBox;
using SandBox.View.Map;
using SandBox.ViewModelCollection.MobilePartyTracker;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors.Towns;
using TaleWorlds.CampaignSystem.SandBox.GameComponents.Map;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
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
                    ItemTypes[value] = ItemObject.All.Where(x =>
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

                var militias = MobileParty.All.Where(x =>
                    x is not null && x.StringId.StartsWith("Bandit_Militia")).ToList();
                
                // vestigial, remove at BL v1.6
                for (var i = 0; i < militias.Count; i++)
                {
                    var militia = militias[i];
                    // this could be removed after 2.8.7 ish
                    if (militia.CurrentSettlement is not null)
                    {
                        Mod.Log("Militia in hideout found and removed.");
                        Trash(militia);
                        continue;
                    }

                    if (militia.LeaderHero is null)
                    {
                        Mod.Log("Leaderless militia found and removed.");
                        Trash(militia);
                    }
                    else
                    {
                        var recreatedMilitia = new Militia(militia);
                        PartyMilitiaMap.Add(recreatedMilitia.MobileParty, recreatedMilitia);
                    }
                }

                PartyMilitiaMap.Keys.Do(mobileParty =>
                {
                    var settlement = Settlement.FindSettlementsAroundPosition(mobileParty.Position2D, 30).GetRandomElementInefficiently() ?? Settlement.All.GetRandomElement();
                    mobileParty.SetMovePatrolAroundSettlement(settlement);
                });

                Mod.Log($"Militias: {militias.Count} (registered {PartyMilitiaMap.Count})");
                // 1.5.10 is dropping the militia settlements at some point, I haven't figured out where
                ReHome();
                DoPowerCalculations(true);

                // have to patch late because of static constructors (type initialization exception)
                Mod.harmony.Patch(
                    AccessTools.Method(typeof(EncounterGameMenuBehavior), "game_menu_encounter_on_init"),
                    new HarmonyMethod(AccessTools.Method(typeof(Helper), nameof(FixMapEventFuckery))));

                Mod.harmony.Patch(AccessTools.Method(typeof(PlayerTownVisitCampaignBehavior), "wait_menu_prisoner_wait_on_tick")
                    , null, null, null,
                    new HarmonyMethod(AccessTools.Method(typeof(MiscPatches), nameof(wait_menu_prisoner_wait_on_tickFinalizer))));

                var original = AccessTools.Method(typeof(DefaultPartySpeedCalculatingModel), "CalculateFinalSpeed");
                var postfix = AccessTools.Method(typeof(MilitiaPatches), nameof(MilitiaPatches.DefaultPartySpeedCalculatingModelCalculateFinalSpeedPatch));
                Mod.harmony.Patch(original, null, new HarmonyMethod(postfix));
            }
        }

        // just disperse small militias
        // todo prevent this unless the militia has lost or retreated from combat
        [HarmonyPatch(typeof(MapEventSide), "HandleMapEventEndForParty")]
        public static class MapEventSideHandleMapEventEndForPartyPatch
        {
            private static void Postfix(MapEventSide __instance, PartyBase party, Hero __state)
            {
                if (__state is null ||
                    party?.MobileParty is null ||
                    !party.MobileParty.IsBM() ||
                    party.PrisonRoster is not null &&
                    party.PrisonRoster.Contains(Hero.MainHero.CharacterObject))
                {
                    return;
                }

                if (party.MemberRoster?.TotalHealthyCount == 0 ||
                    party.MemberRoster?.TotalHealthyCount < Globals.Settings.MinPartySize &&
                    party.PrisonRoster?.Count < Globals.Settings.MinPartySize &&
                    __instance.Casualties > party.MemberRoster?.TotalHealthyCount * 2)
                {
                    Mod.Log($">>> Dispersing {party.Name} of {party.MemberRoster.TotalHealthyCount}+{party.MemberRoster.TotalWounded}w+{party.PrisonRoster?.Count}p");
                    //party.MobileParty.LeaderHero.RemoveCharacterObject();
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
                Globals.MobilePartyTrackerVM ??= __instance;
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
