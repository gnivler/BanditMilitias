using System.Collections.Generic;
using System.Linq;
using BanditMilitias.Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.LinQuick;
using static BanditMilitias.Globals;
using static BanditMilitias.Helpers.Helper;

// ReSharper disable InconsistentNaming

namespace BanditMilitias.Patches
{
    public class Upgrades
    {
        [HarmonyPatch(typeof(MapEvent), "FinalizeEvent")]
        public static class MapEventFinalizeEvent
        {
            public static void Prefix(MapEvent __instance)
            {
                if (!Globals.Settings.UpgradeTroops) return;
                foreach (var casualty in __instance.PartiesOnSide(BattleSideEnum.Attacker)
                             .Concat(__instance.PartiesOnSide(BattleSideEnum.Defender))
                             .Select(p => Traverse.Create(p).Field<TroopRoster>("_diedInBattle").Value)
                             .SelectMany(t => t.GetTroopRoster()))
                {
                    // the TRE is always just 1 troop
                    Globals.EquipmentMap.Remove(casualty.Character.StringId);
                }
            }
        }

        // idea from True Battle Loot
        [HarmonyPatch(typeof(MapEventSide), "OnTroopKilled")]
        public static class MapEventSideOnTroopKilled
        {
            public static void Postfix(MapEventSide __instance, CharacterObject ____selectedSimulationTroop)
            {
                if (!Globals.Settings.UpgradeTroops && MapEvent.PlayerMapEvent is not null && ____selectedSimulationTroop is null)
                    return;

                // makes all loot drop in any BM-involved fight which isn't with the main party
                var BMs = __instance.Parties.WhereQ(p =>
                    p.Party.MobileParty?.PartyComponent is ModBanditMilitiaPartyComponent).SelectQ(p => p.Party);
                if (BMs.Any() && !__instance.IsMainPartyAmongParties())
                {
                    for (var index = 0; index < Equipment.EquipmentSlotLength; index++)
                    {
                        var item = ____selectedSimulationTroop.Equipment[index];
                        if (item.IsEmpty) continue;

                        if (Rng.Next(0, 101) < 66) continue;
                        if (LootRecord.TryGetValue(__instance, out _))
                        {
                            LootRecord[__instance].Add(new EquipmentElement(item));
                        }
                        else
                        {
                            LootRecord.Add(__instance, new List<EquipmentElement> { item });
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(BattleCampaignBehavior), "CollectLoots")]
        public static class BattleCampaignBehaviorCollectLoots
        {
            public static void Prefix(MapEvent mapEvent, PartyBase party, ref ItemRoster loot)
            {
                if (!Globals.Settings.UpgradeTroops || !mapEvent.HasWinner || !party.IsMobile || !party.MobileParty.IsBM())
                    return;
                if (LootRecord.TryGetValue(party.MapEventSide, out var equipment))
                {
                    foreach (var e in equipment)
                    {
                        loot.AddToCounts(e, 1);
                    }
                }

                if (loot.AnyQ(i => !i.IsEmpty))
                {
                    UpgradeEquipment(party, loot);
                }

                Globals.LootRecord.Remove(party.MobileParty.MapEventSide);
            }
        }
    }
}
