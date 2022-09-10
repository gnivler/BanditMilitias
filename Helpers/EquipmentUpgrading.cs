using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using static BanditMilitias.Globals;
using static BanditMilitias.Helpers.Helper;

namespace BanditMilitias.Helpers
{
    internal static class EquipmentUpgrading
    {
        private static readonly string[] BadLoot = { "throwing_stone" };

        private static readonly AccessTools.FieldRef<BasicCharacterObject, bool> IsSoldier =
            AccessTools.FieldRefAccess<BasicCharacterObject, bool>("<IsSoldier>k__BackingField");

        private static MethodInfo setName;
        private const int ItemValueThreshold = 1000;

        internal static void UpgradeEquipment(PartyBase party, ItemRoster loot)
        {
            try
            {
                var lootedItems = loot.OrderByDescending(i => i.EquipmentElement.ItemValue).ToListQ();
                var usableEquipment = lootedItems.WhereQ(i =>
                        i.EquipmentElement.Item.ItemType
                            is ItemObject.ItemTypeEnum.Horse
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
                        && i.EquipmentElement.ItemValue >= ItemValueThreshold)
                    .OrderByDescending(i => i.EquipmentElement.ItemValue).ToListQ();

                usableEquipment.RemoveAll(e => BadLoot.Contains(e.EquipmentElement.Item.StringId));
                if (!usableEquipment.Any())
                    return;

                // short-circuit to prevent over-stuffing cavalry
                if (usableEquipment.AllQ(i => i.EquipmentElement.Item.HasHorseComponent)
                    && party.MobileParty.MemberRoster.CountMounted() > party.MobileParty.MemberRoster.TotalManCount / 2)
                    return;

                // TODO total value of items instead of weight
                var troops = party.MemberRoster.ToFlattenedRoster().Troops.OrderByDescending(e => e.Level)
                    .ThenByDescending(e => e.Equipment.GetTotalWeightOfArmor(true) + e.Equipment.GetTotalWeightOfWeapons()).ToListQ();
                if (usableEquipment.Count == 0)
                    return;
                for (var i = 0; i < troops.Count; i++)
                {
                    var troop = troops[i];
                    if (!usableEquipment.Any())
                        break;
                    //Log.Debug?.Log($"{troop.Name} is up for upgrades.  Current equipment:");
                    //for (var index = 0; index < Equipment.EquipmentSlotLength; index++)
                    //    Log.Debug?.Log($"{index}: {troop.Equipment[index].Item?.Name} {(troop.Equipment[index].Item?.Value is not null ? "$" : "")}{troop.Equipment[index].Item?.Value}");

                    for (var index = 0; index < usableEquipment.Count; index++)
                    {
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

                        //Log.Debug?.Log($"{troop.HeroObject?.Name.ToString() ?? troop.Name.ToString()} considering... {possibleUpgrade.EquipmentElement.Item?.Name}, worth {possibleUpgrade.EquipmentElement.ItemValue}");
                        // TODO sanity check equipment - bows swapping in for melee weapons, others?
                        var rangedSlot = -1;
                        // assume that sane builds are coming in (no double bows, missing ammo)
                        if (possibleUpgrade.EquipmentElement.Item.HasWeaponComponent)
                        {
                            if (possibleUpgrade.EquipmentElement.Item?.ItemType
                                    is ItemObject.ItemTypeEnum.Bow
                                    or ItemObject.ItemTypeEnum.Crossbow
                                    or ItemObject.ItemTypeEnum.Pistol
                                    or ItemObject.ItemTypeEnum.Musket
                                && possibleUpgrade.EquipmentElement.Item.PrimaryWeapon.WeaponClass
                                    is not (WeaponClass.Javelin or WeaponClass.Stone))
                            {
                                // make sure the troop is already ranged or move onto next item
                                for (var slot = 0; slot < 4; slot++)
                                    if (troop.Equipment[slot].Item?.PrimaryWeapon != null && troop.Equipment[slot].Item.PrimaryWeapon.IsRangedWeapon)
                                    {
                                        rangedSlot = slot;
                                        break;
                                    }

                                if (rangedSlot < 0)
                                    continue;
                                // bow is an upgrade so take it and take the ammo
                                if (DoPossibleUpgrade(possibleUpgrade, ref troop, ref usableEquipment, rangedSlot))
                                {
                                    MapUpgrade(party, troop, ref troops);
                                    var ammo = GetAmmo(possibleUpgrade, usableEquipment);
                                    if (ammo.IsEmpty)
                                        continue;
                                    var ammoSlot = -1;
                                    for (var slot = 0; slot < 4; slot++)
                                        if (troop.Equipment[slot].Item?.PrimaryWeapon is not null
                                            && troop.Equipment[slot].Item.PrimaryWeapon.IsAmmo)
                                            ammoSlot = slot;

                                    possibleUpgrade = new ItemRosterElement(ammo.EquipmentElement.Item, 1);
                                    DoPossibleUpgrade(possibleUpgrade, ref troop, ref usableEquipment, ammoSlot);
                                    MapUpgrade(party, troop, ref troops);
                                    continue;
                                }
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
                                if (DoPossibleUpgrade(possibleUpgrade, ref troop, ref usableEquipment))
                                {
                                    MapUpgrade(party, troop, ref troops);
                                    break;
                                }

                                break;
                            }
                        }

                        //Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
                        //MobileParty.MainParty.Position2D = troop.FindParty().Position2D;
                        //MapScreen.Instance.TeleportCameraToMainParty();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug?.Log(ex);
                Meow();
            }
        }

        private static void MapUpgrade(PartyBase party, CharacterObject troop, ref List<CharacterObject> troops)
        {
            // Heroes keep their equipment without special tracking
            if (troop.IsHero)
                return;
            if (!EquipmentMap.TryGetValue(troop.StringId, out _))
            {
                if (party.MemberRoster.GetTroopRoster().AnyQ(e => e.Character.StringId == troop.StringId))
                    Debugger.Break();
                Troops.Add(troop);
                Log.Debug?.Log($">>> added {troop.Name} {troop.StringId}");
                EquipmentMap.Add(troop.StringId, troop.Equipment);
                party.MemberRoster.Add(new TroopRosterElement(troop) { Number = 1 });
                party.MemberRoster.RemoveTroop(troop.OriginalCharacter);
                // TODO total value of items instead of weight
                // collection is modified so re-sort
                troops = party.MemberRoster.ToFlattenedRoster().Troops.OrderByDescending(e => e.Level)
                    .ThenByDescending(e => e.Equipment.GetTotalWeightOfArmor(true) + e.Equipment.GetTotalWeightOfWeapons()).ToListQ();
            }
            else
            {
                Log.Debug?.Log($">>> update {troop.Name} {troop.StringId}");
                EquipmentMap[troop.StringId] = troop.Equipment;
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

        private static bool DoPossibleUpgrade(ItemRosterElement possibleUpgrade,
            ref CharacterObject troop,
            ref List<ItemRosterElement> usableEquipment,
            int slotOverride = -1)
        {
            if (!IsRegistered(troop) || !troop.IsReady)
            {
                // something is wrong
            }

            // current item where it's the right kind
            // TODO break to save time if the most valuable loot is less than the least valuable slot that isn't ammo
            var targetSlot = slotOverride < 0 ? GetLowestValueSlotThatFits(troop.Equipment, possibleUpgrade) : slotOverride;
            var replacedItem = troop.Equipment[targetSlot];
            // every slot is better or the equipment isn't an upgrade
            if (targetSlot < 0 || troop.Equipment.Contains(possibleUpgrade.EquipmentElement) || replacedItem.ItemValue >= possibleUpgrade.EquipmentElement.ItemValue)
                return false;
            //Log.Debug?.Log($"{troop.Name} {troop.OriginalCharacter?.Name}");
            if (troop.OriginalCharacter is null)
                CreateCustomCharacter(ref troop);

            if (!IsRegistered(troop) || !troop.IsReady)
            {
                // something is wrong
            }

            Log.Debug?.Log($"### Upgrading {troop.HeroObject?.Name ?? troop.Name} ({troop.StringId}): {replacedItem.Item?.Name.ToString() ?? "empty slot"} with {possibleUpgrade.EquipmentElement.Item.Name}");
            // assign the upgrade
            troop.Equipment[targetSlot] = possibleUpgrade.EquipmentElement;
            // decrement and remove ItemRosterElements
            if (--possibleUpgrade.Amount == 0)
            {
                usableEquipment.Remove(possibleUpgrade);
            }

            // put anything replaced back into the pile
            if (!replacedItem.IsEmpty && replacedItem.ItemValue >= ItemValueThreshold)
            {
                Log.Debug?.Log($"### Returning {replacedItem.Item?.Name} to the bag");
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

        private static void CreateCustomCharacter(ref CharacterObject troop)
        {
            var tempCharacter = CharacterObject.CreateFrom(troop);
            tempCharacter.InitializeHeroCharacterOnAfterLoad();
            setName ??= AccessTools.Method(typeof(CharacterObject), "SetName");
            // localization not included
            setName.Invoke(tempCharacter, new object[] { new TextObject(@"{=BMTroops}Upgraded " + tempCharacter.Name) });
            IsSoldier(tempCharacter) = true;
            HiddenInEncyclopedia(tempCharacter) = true;
            troop = tempCharacter;
            Log.Debug?.Log($">>> create {troop.Name} {troop.StringId}");
        }

        private static int GetLowestValueSlotThatFits(Equipment equipment, ItemRosterElement possibleUpgrade)
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

        // troops with missing data causing lots of NREs elsewhere
        internal static void PurgeUpgradedTroops()
        {
            Log.Debug?.Log("Starting iteration of all troops in all parties and settlements, this might take a minute...");
            foreach (var mobileParty in MobileParty.All)
            {
                var rosters = new[] { mobileParty.MemberRoster, mobileParty.PrisonRoster };
                foreach (var roster in rosters)
                    while (roster.GetTroopRoster().AnyQ(t => t.Character.Name == null)
                           )//|| roster.GetTroopRoster().AnyQ(t => t.Character.Name.Contains("Upgraded")))
                        foreach (var troop in roster.GetTroopRoster())
                            if (troop.Character.Name == null)// || troop.Character.Name.Contains("Upgraded"))
                            {
                                Log.Debug?.Log($"!!!!! Purge upgraded troop {troop.Character.StringId} from {mobileParty.Name}.  Prisoner? {roster.IsPrisonRoster}");
                                roster.AddToCounts(troop.Character, -troop.Number);
                                MBObjectManager.Instance.UnregisterObject(troop.Character);
                            }
            }

            foreach (var settlement in Settlement.All)
            {
                var rosters = new[] { settlement.Party.MemberRoster, settlement.Party.PrisonRoster };
                foreach (var roster in rosters)
                    while (roster.GetTroopRoster().AnyQ(t => t.Character.Name == null)
                           )//|| roster.GetTroopRoster().AnyQ(t => t.Character.Name.Contains("Upgraded")))
                        foreach (var troop in roster.GetTroopRoster())
                            if (troop.Character.Name == null)// || troop.Character.Name.Contains("Upgraded"))
                            {
                                Log.Debug?.Log($"!!!!! Purge upgraded troop {troop.Character.StringId} from {settlement.Name}.  Prisoner? {roster.IsPrisonRoster}");
                                roster.AddToCounts(troop.Character, -troop.Number);
                                MBObjectManager.Instance.UnregisterObject(troop.Character);
                            }
            }
        }
    }
}
