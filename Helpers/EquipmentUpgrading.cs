using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using static BanditMilitias.Globals;
using static BanditMilitias.Helpers.Helper;

namespace BanditMilitias.Helpers
{
    public static class EquipmentUpgrading
    {
        private static readonly string[] BadLoot = { "throwing_stone" };

        private static readonly AccessTools.FieldRef<BasicCharacterObject, bool> IsSoldier =
            AccessTools.FieldRefAccess<BasicCharacterObject, bool>("<IsSoldier>k__BackingField");

        private static MethodInfo setName;

        public static void UpgradeEquipment(PartyBase party, ItemRoster loot)
        {
            try
            {
                var lootedItems = loot.OrderByDescending(i => i.EquipmentElement.ItemValue).ToListQ();
                var usableEquipment = lootedItems.WhereQ(i =>
                        i.EquipmentElement.Item.ItemType is ItemObject.ItemTypeEnum.Horse
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
                            or ItemObject.ItemTypeEnum.HorseHarness
                        && i.EquipmentElement.ItemValue >= 1000)
                    .OrderByDescending(i => i.EquipmentElement.ItemValue).ToListQ();

                usableEquipment.RemoveAll(e => BadLoot.Contains(e.EquipmentElement.Item.StringId));
                if (!usableEquipment.Any())
                    return;

                // short-circuit to prevent over-stuffing cavalry
                if (usableEquipment.AllQ(i => i.EquipmentElement.Item.HasHorseComponent)
                    && party.MobileParty.MemberRoster.CountMounted() > party.MobileParty.MemberRoster.TotalManCount / 2)
                    return;

                var troops = party.MemberRoster.ToFlattenedRoster().Troops.OrderByDescending(e => e.Level)
                    .ThenByDescending(e => e.Equipment.GetTotalWeightOfArmor(true) + e.Equipment.GetTotalWeightOfWeapons()).ToListQ();
                if (usableEquipment.Count == 0)
                    return;
                for (var i = 0; i < troops.Count; i++)
                {
                    var troop = troops[i];
                    if (!IsRegistered(troop))
                    {
                    }

                    if (!usableEquipment.Any())
                        break;
                    bool wasUpgraded = default;
                    //DeferringLogger.Instance.Debug?.Log($"{troop.Name} is up for upgrades.  Current equipment:");
                    //for (var index = 0; index < Equipment.EquipmentSlotLength; index++)
                    //    DeferringLogger.Instance.Debug?.Log($"{index}: {troop.Equipment[index].Item?.Name} {(troop.Equipment[index].Item?.Value is not null ? "$" : "")}{troop.Equipment[index].Item?.Value}");

                    for (var index = 0; index < usableEquipment.Count; index++)
                    {
                        var itemReturned = false;
                        var possibleUpgrade = usableEquipment[index];
                        var upgradeValue = possibleUpgrade.EquipmentElement.ItemValue;
                        if (upgradeValue <= LeastValuableItem(troop))
                            break;
                        if (possibleUpgrade.EquipmentElement.Item.ItemType
                            is ItemObject.ItemTypeEnum.Arrows
                            or ItemObject.ItemTypeEnum.Bolts
                            or ItemObject.ItemTypeEnum.Bullets)
                            continue;
                        // prevent them from getting a bunch of the same item
                        if (troop.Equipment.Contains(possibleUpgrade.EquipmentElement))
                            continue;

                        //DeferringLogger.Instance.Debug?.Log($"{troop.HeroObject?.Name.ToString() ?? troop.Name.ToString()} considering... {possibleUpgrade.EquipmentElement.Item?.Name}, worth {possibleUpgrade.EquipmentElement.ItemValue}");
                        // TODO shields
                        var rangedSlot = -1;
                        // assume that sane builds are coming in (no double bows, missing ammo)
                        if (possibleUpgrade.EquipmentElement.Item.HasWeaponComponent)
                        {
                            if (possibleUpgrade.EquipmentElement.Item?.ItemType is
                                    ItemObject.ItemTypeEnum.Bow
                                    or ItemObject.ItemTypeEnum.Crossbow
                                    or ItemObject.ItemTypeEnum.Pistol
                                    or ItemObject.ItemTypeEnum.Musket
                                && possibleUpgrade.EquipmentElement.Item.PrimaryWeapon.WeaponClass is not
                                    (WeaponClass.Javelin or WeaponClass.Stone))
                            {
                                // make sure the troop is already ranged or move onto next item
                                for (var slot = 0; slot < 4; slot++)
                                {
                                    if (troop.Equipment[slot].Item?.PrimaryWeapon != null && troop.Equipment[slot].Item.PrimaryWeapon.IsRangedWeapon)
                                    {
                                        rangedSlot = slot;
                                        break;
                                    }
                                }

                                if (rangedSlot < 0)
                                    continue;
                                // bow is an upgrade so take it and take the ammo
                                if (DoPossibleUpgrade(possibleUpgrade, ref troop, ref usableEquipment, ref wasUpgraded, rangedSlot))
                                {
                                    var ammo = GetAmmo(possibleUpgrade, usableEquipment);
                                    if (ammo.IsEmpty)
                                        continue;
                                    var ammoSlot = -1;
                                    for (var slot = 0; slot < 4; slot++)
                                    {
                                        if (troop.Equipment[slot].Item?.PrimaryWeapon is not null
                                            && troop.Equipment[slot].Item.PrimaryWeapon.IsAmmo)
                                        {
                                            ammoSlot = slot;
                                        }
                                    }

                                    possibleUpgrade = new ItemRosterElement(ammo.EquipmentElement.Item, 1);
                                    if (DoPossibleUpgrade(possibleUpgrade, ref troop, ref usableEquipment, ref itemReturned, ammoSlot))
                                        usableEquipment.Remove(ammo);
                                }

                                if (itemReturned)
                                    index = -1;
                                continue;
                            }
                        }

                        // if it's a horse slot but we already have enough, skip to next upgrade EquipmentElement
                        if (possibleUpgrade.EquipmentElement.Item.HasHorseComponent && party.MemberRoster.CountMounted() > party.MemberRoster.TotalManCount / 2)
                            continue;

                        // simple record of slots yet to try
                        var slots = new List<int>();
                        for (var s = 0; s < Equipment.EquipmentSlotLength; s++)
                            slots.Add(s);
                        slots.Shuffle();
                        for (; slots.Count > 0; slots.RemoveAt(0))
                        {
                            var slot = slots[0];
                            if (Equipment.IsItemFitsToSlot((EquipmentIndex)slot, possibleUpgrade.EquipmentElement.Item))
                            {
                                if (DoPossibleUpgrade(possibleUpgrade, ref troop, ref usableEquipment, ref wasUpgraded))
                                {
                                    if (!troop.IsHero)
                                    {
                                        if (!Globals.EquipmentMap.TryGetValue(troop.StringId, out _))
                                        {
                                            // TODO does UpgradeReadyTroops fuck with data?
                                            // BUG why do we reach this point if the roster already has the custom CO?
                                            if (party.MemberRoster.GetTroopRoster().AnyQ(e => e.Character.StringId == troop.StringId))
                                                Debugger.Break();
                                            Globals.EquipmentMap.Add(troop.StringId, troop.Equipment);
                                            //if (party.MemberRoster.GetTroopRoster().WhereQ(e => !e.Character.IsHero).AnyQ(e => e.Number <= 1))
                                            //    Debugger.Break();
                                            party.MemberRoster.RemoveTroop(troop.OriginalCharacter);
                                            // collection is modified
                                            troops = party.MemberRoster.ToFlattenedRoster().Troops.OrderByDescending(e => e.Level)
                                                .ThenByDescending(e => e.Equipment.GetTotalWeightOfArmor(true) + e.Equipment.GetTotalWeightOfWeapons()).ToListQ();
                                            party.MemberRoster.Add(new TroopRosterElement(troop) { Number = 1 });
                                            Troops.Add(troop);
                                        }
                                        else
                                            Globals.EquipmentMap[troop.StringId] = troop.Equipment;

                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DeferringLogger.Instance.Debug?.Log(ex);
                Meow();
            }
        }

        private static int LeastValuableItem(CharacterObject tempCharacter)
        {
            var leastValuable = int.MaxValue;
            for (var slot = 0; slot < Equipment.EquipmentSlotLength; slot++)
            {
                if (tempCharacter.Equipment[slot].ItemValue < leastValuable)
                    leastValuable = tempCharacter.Equipment[slot].ItemValue;
            }

            return leastValuable;
        }

        private static ItemRosterElement GetAmmo(ItemRosterElement possibleUpgrade, List<ItemRosterElement> usableEquipment)
        {
            var ammo = possibleUpgrade.EquipmentElement.Item.ItemType switch
            {
                ItemObject.ItemTypeEnum.Bow => usableEquipment.FirstOrDefaultQ(e => e.EquipmentElement.Item.ItemType is ItemObject.ItemTypeEnum.Arrows),
                ItemObject.ItemTypeEnum.Crossbow => usableEquipment.FirstOrDefaultQ(e => e.EquipmentElement.Item.ItemType is ItemObject.ItemTypeEnum.Bolts),
                ItemObject.ItemTypeEnum.Musket or ItemObject.ItemTypeEnum.Pistol =>
                    usableEquipment.FirstOrDefaultQ(e => e.EquipmentElement.Item.ItemType is ItemObject.ItemTypeEnum.Bullets),
                _ => default
            };
            return ammo;
        }

        public static bool DoPossibleUpgrade(ItemRosterElement possibleUpgrade,
            ref CharacterObject troop,
            ref List<ItemRosterElement> usableEquipment,
            ref bool wasUpgraded, int slotOverride = -1)
        {
            // current item where it's the right kind
            // TODO break to save time if the most valuable loot is less than the least valuable slot that isn't ammo
            var targetSlot = slotOverride < 0 ? GetLowestValueSlotThatFits(troop.Equipment, possibleUpgrade) : slotOverride;
            // every slot is better
            if (targetSlot < 0)
                return false;
            var replacedItem = troop.Equipment[targetSlot];
            if (troop.Equipment.Contains(possibleUpgrade.EquipmentElement) || replacedItem.ItemValue >= possibleUpgrade.EquipmentElement.ItemValue)
                return false;
            if (!Troops.Contains(troop) && troop.OriginalCharacter is null)
            {
                troop = CreateCustomCharacter(troop);
                if (!IsRegistered(troop))
                    Debugger.Break();
            }

            DeferringLogger.Instance.Debug?.Log($"### Upgrading {troop.HeroObject?.Name ?? troop.Name} ({troop.StringId}): {replacedItem.Item?.Name.ToString() ?? "empty slot"} with {possibleUpgrade.EquipmentElement.Item.Name}");
            // assign the upgrade
            troop.Equipment[targetSlot] = possibleUpgrade.EquipmentElement;
            wasUpgraded = true;
            // decrement and remove ItemRosterElements
            if (--possibleUpgrade.Amount == 0)
            {
                usableEquipment.Remove(possibleUpgrade);
            }

            // put anything replaced back into the pile
            if (!replacedItem.IsEmpty && replacedItem.ItemValue >= 1000)
            {
                DeferringLogger.Instance.Debug?.Log($"### Returning {replacedItem.Item?.Name} to the bag");
                var index = usableEquipment.SelectQ(e => e.EquipmentElement.Item).ToListQ().FindIndexQ(replacedItem.Item);
                if (index > -1)
                {
                    var item = usableEquipment[index];
                    item.Amount++;
                    usableEquipment[index] = item;
                }
                else
                {
                    usableEquipment.Add(new ItemRosterElement(replacedItem.Item, 1));
                    usableEquipment = usableEquipment.OrderByDescending(e => e.EquipmentElement.ItemValue).ToListQ();
                }
            }

            return true;
        }

        private static CharacterObject CreateCustomCharacter(CharacterObject troop)
        {
            //DeferringLogger.Instance.Debug?.Log("### Creating custom character for " + troop.Name);
            if (troop.Name.Contains("Hero") || troop.StringId.StartsWith("lord_"))
                Debugger.Break();
            // goal here is only generate one custom CharacterObject, if receiving an already customized one it can be further customized as-is
            var tempCharacter = CharacterObject.CreateFrom(troop);
            // throws TypeLoadException if assigned at declaration
            setName ??= AccessTools.Method(typeof(CharacterObject), "SetName");
            setName.Invoke(tempCharacter, new object[] { new TextObject($"Upgraded {tempCharacter.Name}") });
            IsSoldier(tempCharacter) = true;
            HiddenInEncyclopedia(tempCharacter) = true;
            var mbEquipmentRoster = new MBEquipmentRoster();
            Equipments(mbEquipmentRoster) = new List<Equipment> { new(troop.Equipment) };
            EquipmentRoster(tempCharacter) = mbEquipmentRoster;
            return tempCharacter;
        }

        public static int GetLowestValueSlotThatFits(Equipment equipment, ItemRosterElement possibleUpgrade)
        {
            var lowestValue = int.MaxValue;
            var targetSlot = -1;
            for (var slot = 0; slot < Equipment.EquipmentSlotLength; slot++)
            {
                if (!Equipment.IsItemFitsToSlot((EquipmentIndex)slot, possibleUpgrade.EquipmentElement.Item))
                    continue;
                if (equipment[slot].IsEmpty)
                {
                    targetSlot = slot;
                    break;
                }

                if (equipment[slot].Item.ItemType is
                    ItemObject.ItemTypeEnum.Arrows
                    or ItemObject.ItemTypeEnum.Bolts
                    or ItemObject.ItemTypeEnum.Bullets) continue;

                if (equipment[slot].ItemValue < lowestValue)
                {
                    lowestValue = equipment[slot].ItemValue;
                    targetSlot = slot;
                }
            }

            return targetSlot;
        }
    }
}
