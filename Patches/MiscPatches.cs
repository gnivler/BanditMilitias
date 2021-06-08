using System;
using System.Linq;
using Bandit_Militias.Helpers;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors.Towns;
using TaleWorlds.CampaignSystem.ViewModelCollection;
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
    public class MiscPatches
    {
        [HarmonyPatch(typeof(MapScreen), "OnInitialize")]
        public static class MapScreenOnInitializePatch
        {
            private static void Postfix()
            {
                Mod.Log("MapScreen.OnInitialize");
                EquipmentItems.Clear();
                PopulateItems();
                Recruits = CharacterObject.All.Where(x =>
                    x.Level == 11 &&
                    x.Occupation == Occupation.Soldier &&
                    !x.StringId.StartsWith("regular_fighter") &&
                    !x.StringId.StartsWith("veteran_borrowed_troop") &&
                    !x.StringId.EndsWith("_tier_1") &&
                    !x.StringId.Contains("_militia_") &&
                    !x.StringId.Equals("sturgian_warrior_son") &&
                    !x.StringId.Equals("khuzait_noble_son") &&
                    !x.StringId.Equals("imperial_vigla_recruit") &&
                    !x.StringId.Equals("battanian_highborn_youth") &&
                    !x.StringId.Equals("vlandian_squire") &&
                    !x.StringId.Equals("aserai_youth") &&
                    !x.StringId.Equals("poacher"));

                // used for armour
                foreach (ItemObject.ItemTypeEnum value in Enum.GetValues(typeof(ItemObject.ItemTypeEnum)))
                {
                    ItemTypes[value] = Items.FindAll(x =>
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
                // 1.5.8 is dropping the militia settlements at some point, I haven't figured out where
                ReHome();
                DoPowerCalculations(true);

                // have to patch late because of static constructors (type initialization exception)
                Mod.harmony.Patch(
                    AccessTools.Method(typeof(EncounterGameMenuBehavior), "game_menu_encounter_on_init"),
                    new HarmonyMethod(AccessTools.Method(typeof(Helper), nameof(FixMapEventFuckery))));

                Mod.harmony.Patch(AccessTools.Method(typeof(PlayerTownVisitCampaignBehavior), "wait_menu_prisoner_wait_on_tick")
                    , null, null, null,
                    new HarmonyMethod(AccessTools.Method(typeof(MiscPatches), nameof(wait_menu_prisoner_wait_on_tickFinalizer))));
            }
        }

        // just disperse small militias
        // todo prevent this unless the militia has lost or retreated from combat
        [HarmonyPatch(typeof(MapEventSide), "HandleMapEventEndForParty")]
        public class MapEventSideHandleMapEventEndForPartyPatch
        {
            // the method purges the party data so we capture the hero for use in Postfix
            private static void Prefix(PartyBase party, ref Hero __state)
            {
                __state = party.LeaderHero;
            }

            private static void Postfix(MapEventSide __instance, PartyBase party, Hero __state)
            {
                if (__state is null ||
                    party?.MobileParty is null ||
                    !IsBM(party.MobileParty) ||
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
                    __state.KillHero();
                    Trash(party.MobileParty);
                }
            }
        }

        [HarmonyPatch(typeof(HeroCreator), "CreateRelativeNotableHero")]
        public class HeroCreatorCreateRelativeNotableHeroPatch
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
                Mod.Log(__exception);
            }

            return null;
        }

        // possibly related to Separatism and new kingdoms, ignoring it seems fine...
        [HarmonyPatch(typeof(BanditPartyComponent), "Name", MethodType.Getter)]
        public class BanditPartyComponentGetNamePatch
        {
            public static Exception Finalizer(BanditPartyComponent __instance, Exception __exception)
            {
                if (__exception is not null)
                {
                    Mod.Log(new string('-', 50));
                    Mod.Log("PING");
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

                    Mod.Log($"Party {__instance.MobileParty.Name} is throwing.");
                    Mod.Log($"MapFaction {__instance.Hideout?.MapFaction}.");
                    Mod.Log($"MapFaction.Name {__instance.Hideout?.MapFaction?.Name}.");
                    Mod.Log(__exception);
                }

                return null;
            }
        }

        // vanilla fix for 1.5.9?
        [HarmonyPatch(typeof(SPInventoryVM), "InitializeInventory")]
        public class SPInventoryVMInitializeInventoryVMPatch
        {
            static Exception Finalizer(Exception __exception)
            {
                if (__exception is not null)
                {
                    Mod.Log(__exception);
                }

                return null;
            }
        }
    }
}
