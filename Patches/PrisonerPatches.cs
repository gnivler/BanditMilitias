using System.Collections.Generic;
using System.Linq;
using BanditMilitias.Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using static BanditMilitias.Helpers.Helper;

// ReSharper disable UnusedType.Global
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Local

namespace BanditMilitias.Patches
{
    public static class PrisonerPatches
    {
        // prevents BM hero prisoners being taken after battle
        [HarmonyPatch(typeof(MapEvent), "FinishBattle")]
        public static class MapEventFinishBattlePatch
        {
            public static void Prefix(MapEvent __instance)
            {
                if (__instance.DefeatedSide is BattleSideEnum.None)
                {
                    return;
                }

                var loserBMs = __instance.PartiesOnSide(__instance.DefeatedSide)
                    .Where(p => p.Party?.MobileParty?.PartyComponent is ModBanditMilitiaPartyComponent);
                foreach (var party in loserBMs)
                {
                    var heroes = party.Party.MemberRoster.RemoveIf(t => t.Character.IsHero).ToListQ();
                    for (var i = 0; i < heroes.Count; i++)
                    {
                        Log($"<<< Killing {heroes[i].Character.Name} ({heroes[i].Character.StringId}) at FinishBattle.");
                        heroes[i].Character.HeroObject.RemoveMilitiaHero();
                    }

                    if (party.Party.MobileParty.LeaderHero is null)
                    {
                        party.Party.MobileParty.SetCustomName(new TextObject(Globals.Settings.LeaderlessBanditMilitiaString));
                    }

                    RemoveUndersizedTracker(party.Party);
                }

                DoPowerCalculations();
            }
        }

        // prevents BM hero prisoners being taken after battle
        // upgrades all troops with any looted equipment in Postfix
        // drops Avoidance scores when BMs win
        [HarmonyPatch(typeof(MapEvent), "LootDefeatedParties")]
        public static class MapEventLootDefeatedPartiesPatch
        {
            public static void Prefix(MapEvent __instance, object lootCollector)
            {
                if (__instance.DefeatedSide is BattleSideEnum.None)
                {
                    return;
                }

                var loserBMs = __instance.PartiesOnSide(__instance.DefeatedSide)
                    .Where(p => p.Party?.MobileParty?.PartyComponent is ModBanditMilitiaPartyComponent);

                foreach (var party in loserBMs)
                {
                    // disperse small militias
                    if (party.Party.MobileParty.MemberRoster.TotalManCount < Globals.Settings.DisperseSize)
                    {
                        Trash(party.Party.MobileParty);
                        continue;
                    }

                    var heroes = party.Party.MemberRoster.RemoveIf(t => t.Character.IsHero).ToListQ();
                    for (var i = 0; i < heroes.Count; i++)
                    {
                        Log($"<<< Killing {heroes[i].Character.Name} at LootDefeatedParties.");
                        heroes[i].Character.HeroObject.RemoveMilitiaHero();
                    }

                    RemoveUndersizedTracker(party.Party);
                }


                DoPowerCalculations();
            }

            public static void Postfix(MapEvent __instance, object lootCollector)
            {
                var winnerBMs = __instance.PartiesOnSide(__instance.WinningSide)
                    .Where(p => p.Party?.MobileParty?.PartyComponent is ModBanditMilitiaPartyComponent).ToListQ();
                if (!winnerBMs.Any())
                {
                    return;
                }

                var loserHeroes = __instance.PartiesOnSide(__instance.DefeatedSide)
                    .SelectQ(mep => mep.Party.Owner).Where(h => h is not null).ToListQ();

                //    winnerBMs.Select(mep => mep.Party.MemberRoster.GetCharacterAtIndex(
                var lootedItems = Traverse.Create(lootCollector).Property<ItemRoster>("LootedItems")
                    .Value.OrderByDescending(i => i.EquipmentElement.ItemValue).ToListQ();
                var usableEquipment = lootedItems.WhereQ(i => i.EquipmentElement.Item.ItemType is
                        ItemObject.ItemTypeEnum.Horse
                        or ItemObject.ItemTypeEnum.OneHandedWeapon
                        or ItemObject.ItemTypeEnum.TwoHandedWeapon
                        or ItemObject.ItemTypeEnum.Polearm
                        or ItemObject.ItemTypeEnum.Arrows
                        or ItemObject.ItemTypeEnum.Bolts
                        or ItemObject.ItemTypeEnum.Shield
                        or ItemObject.ItemTypeEnum.Bow
                        or ItemObject.ItemTypeEnum.Crossbow
                        or ItemObject.ItemTypeEnum.Thrown
                        or ItemObject.ItemTypeEnum.HeadArmor
                        or ItemObject.ItemTypeEnum.BodyArmor
                        or ItemObject.ItemTypeEnum.LegArmor
                        or ItemObject.ItemTypeEnum.HandArmor
                        or ItemObject.ItemTypeEnum.Pistol
                        or ItemObject.ItemTypeEnum.Musket
                        or ItemObject.ItemTypeEnum.Bullets
                        or ItemObject.ItemTypeEnum.ChestArmor
                        or ItemObject.ItemTypeEnum.Cape
                        or ItemObject.ItemTypeEnum.HorseHarness)
                    .OrderByDescending(i => i.EquipmentElement.ItemValue).ToListQ();

                usableEquipment.RemoveAll(e => e.EquipmentElement.Item.StringId == "mule");
                if (!usableEquipment.Any())
                {
                    return;
                }

                //  individuated troops for gear upgrades
                List<CharacterObject> bigBagOfTroops = new();
                foreach (var BM in winnerBMs)
                {
                    bigBagOfTroops.AddRange(BM.Party.MemberRoster.ToFlattenedRoster().Troops);
                    DecreaseAvoidance(loserHeroes, BM);
                }

                // perf short-circuit prevent over-stuffing cavalry
                if (usableEquipment.AllQ(i => i.EquipmentElement.Item.HasHorseComponent)
                    && winnerBMs.AllQ(BM => BM.Party.MobileParty.MemberRoster.MountedCount() > BM.Party.MobileParty.MemberRoster.TotalManCount / 2))
                {
                    return;
                }

                bigBagOfTroops.Shuffle();
                // find upgrades (seems like only horses with vanilla 1.7.2)
                for (var item = 0; item < usableEquipment.Count; item++)
                {
                    var possibleUpgrade = usableEquipment[item];
                    bool superBreak = default;
                    foreach (var troop in bigBagOfTroops)
                    {
                        if (possibleUpgrade.Amount == 0)
                        {
                            break;
                        }

                        // simple record of slots yet to try
                        var slots = new List<int>();
                        for (var s = 0; s < Equipment.EquipmentSlotLength; s++)
                        {
                            slots.Add(s);
                        }

                        // go through each inventory slot in random order
                        slots.Shuffle();
                        for (var slot = slots[0]; slots.Count > 0; slots.RemoveAt(0))
                        {
                            if (slot == 10 && troop.FindParty().MemberRoster.MountedCount() > troop.FindParty().MemberRoster.TotalManCount / 2)
                            {
                                superBreak = true;
                                break;
                            }

                            if (Equipment.IsItemFitsToSlot((EquipmentIndex)slot, possibleUpgrade.EquipmentElement.Item))
                            {
                                var currentItem = troop.FirstBattleEquipment[slot];
                                if (currentItem.ItemValue < possibleUpgrade.EquipmentElement.ItemValue)
                                {
                                    Log($"Upgrading {troop} with {possibleUpgrade.EquipmentElement.Item.StringId}.");
                                    troop.FirstBattleEquipment[slot] = possibleUpgrade.EquipmentElement;
                                    if (--possibleUpgrade.Amount == 0)
                                    {
                                        // put anything replaced, back into the loot pile for others
                                        if (!currentItem.IsEmpty)
                                        {
                                            Log($"Returning {currentItem.Item.Name} to loot pile.");
                                            lootedItems.Add(new ItemRosterElement(currentItem.Item, 1));
                                        }

                                        lootedItems.Remove(possibleUpgrade);
                                    }
                                }

                                // regardless of whether it was an upgrade, it matched the slot so move on
                                // therefore it's not going to currently check if This Weapon is better than All Weapons.
                                // just a slot comparison
                                break;
                            }
                        }

                        if (superBreak)
                        {
                            break;
                        }
                    }

                    if (superBreak)
                    {
                        break;
                    }
                }
            }
        }
    }
}
