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
                        Log($">>> Killing {heroes[i].Character.Name} ({heroes[i].Character.StringId}) at FinishBattle.");
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
                        Log($">>> Killing {heroes[i].Character.Name} at LootDefeatedParties.");
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

                var loserHeroes = __instance.PartiesOnSide(__instance.WinningSide).SelectQ(mep => mep.Party.Owner).Where(h => h is not null).ToListQ();

                //    winnerBMs.Select(mep => mep.Party.MemberRoster.GetCharacterAtIndex(
                var lootedItems = Traverse.Create(lootCollector).Property<ItemRoster>("LootedItems")
                    .Value.OrderByDescending(i => i.EquipmentElement.ItemValue).ToListQ();
                var wearableGear = lootedItems.WhereQ(i => i.EquipmentElement.Item.ItemType is
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
                    or ItemObject.ItemTypeEnum.HorseHarness).ToListQ();

                if (!wearableGear.Any())
                {
                    return;
                }

                //  individuated troops for gear upgrades
                List<CharacterObject> bigBagOfTroops = new();
                foreach (var mep in winnerBMs)
                {
                    foreach (var loserHero in loserHeroes)
                    {
                        if (mep.Party.MobileParty.GetBM().Avoidance.TryGetValue(loserHero, out _))
                        {
                            mep.Party.MobileParty.GetBM().Avoidance[loserHero] -= MilitiaBehavior.Increment;
                        }
                        else
                        {
                            mep.Party.MobileParty.GetBM().Avoidance.Add(loserHero, Globals.Rng.Next(15, 35));
                        }
                    }

                    foreach (var rosterElement in mep.Party.MemberRoster.GetTroopRoster())
                    {
                        for (var count = 0; count < rosterElement.Number; count++)
                        {
                            bigBagOfTroops.Add(rosterElement.Character);
                        }
                    }
                }

                bigBagOfTroops.Shuffle();
                // find upgrades (seems like only horses with vanilla 1.7.2)
                foreach (var characterObject in bigBagOfTroops)
                {
                    for (var index = 0; index < Equipment.EquipmentSlotLength; index++)
                    {
                        var currentItem = characterObject.FirstBattleEquipment[index];
                        if (currentItem.Item is null)
                        {
                            continue;
                        }

                        ItemRosterElement bestOfType = default;
                        if (wearableGear.AnyQ(i => i.EquipmentElement.Item.ItemType == currentItem.Item.ItemType))
                        {
                            bestOfType = wearableGear.First(i => i.EquipmentElement.Item.ItemType == currentItem.Item.ItemType);
                        }

                        if (currentItem.ItemValue < bestOfType.EquipmentElement.ItemValue)
                        {
                            //Debugger.Break();
                            characterObject.FirstBattleEquipment[index] = bestOfType.EquipmentElement;
                            if (--bestOfType.Amount == 0)
                            {
                                if (!currentItem.IsEmpty)
                                {
                                    lootedItems.Add(new ItemRosterElement(currentItem.Item, 1));
                                }

                                lootedItems.Remove(bestOfType);
                            }
                        }
                    }
                }
            }
        }
    }
}
