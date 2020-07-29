using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment.Managers;
using TaleWorlds.Core;

// ReSharper disable ClassNeverInstantiated.Global

namespace Bandit_Militias.Helpers
{
    // copied due to serious fuckery with MapFaction
    public class PartyUpgraderCopy
    {
        public static void UpgradeReadyTroopsCopy(PartyBase party)
        {
            if (!party.Name.Equals("Bandit Militia"))
            {
                return;
            }

            var memberRoster = party.MemberRoster;
            for (var i = 0; i < memberRoster.Count; ++i)
            {
                var troop = memberRoster.GetElementCopyAtIndex(i);
                if (troop.Character.IsHero || troop.Character.UpgradeTargets == null || troop.Character.UpgradeTargets.Length == 0)
                {
                    continue;
                }

                var upgrades = new List<Tuple<CharacterObject, int, int>>();
                var numberReadyToUpgrade = troop.NumberReadyToUpgrade;
                var upgradeXpCost = troop.Character.UpgradeXpCost;
                if (numberReadyToUpgrade > troop.Number - troop.WoundedNumber)
                {
                    numberReadyToUpgrade = troop.Number - troop.WoundedNumber;
                }

                if (numberReadyToUpgrade <= 0)
                {
                    continue;
                }

                for (var j = 0; j < troop.Character.UpgradeTargets.Length; ++j)
                {
                    var upgradeTarget = troop.Character.UpgradeTargets[j];
                    var upgradePrice = troop.Character.UpgradeCost(party, j);
                    bool flag = default;
                    if (party.Owner != null && troop.Character.UpgradeTargets[j].UpgradeRequiresItemFromCategory != null)
                    {
                        var numRequiredItems = 0;
                        foreach (var itemRosterElement in party.ItemRoster)
                        {
                            if (itemRosterElement.EquipmentElement.Item.ItemCategory == upgradeTarget.UpgradeRequiresItemFromCategory)
                            {
                                numRequiredItems += itemRosterElement.Amount;
                                flag = true;
                                if (numRequiredItems >= numberReadyToUpgrade)
                                {
                                    break;
                                }
                            }
                        }

                        if (flag)
                        {
                            numberReadyToUpgrade = Math.Min(numRequiredItems, numberReadyToUpgrade);
                        }
                    }

                    if (numberReadyToUpgrade > 0)
                    {
                        upgrades.Add(new Tuple<CharacterObject, int, int>(troop.Character.UpgradeTargets[j], numberReadyToUpgrade, upgradePrice));
                    }
                }

                if (upgrades.Count > 0)
                {
                    var randomElement = upgrades.GetRandomElement();
                    var characterObject = randomElement.Item1;
                    var numReadyToUpgrade = randomElement.Item2;
                    var unitGoldPrice = randomElement.Item3;
                    var totalXpCost = upgradeXpCost * numReadyToUpgrade;
                    memberRoster.SetElementXp(i, memberRoster.GetElementXp(i) - totalXpCost);
                    memberRoster.AddToCounts(troop.Character, -numReadyToUpgrade);
                    memberRoster.AddToCounts(characterObject, numReadyToUpgrade);
                    // check if upgrade requirement items are available
                    if (party.Owner != null && characterObject.UpgradeRequiresItemFromCategory != null)
                    {
                        foreach (var itemRosterElement in party.ItemRoster)
                        {
                            if (itemRosterElement.EquipmentElement.Item.ItemCategory == characterObject.UpgradeRequiresItemFromCategory)
                            {
                                var itemCount = Math.Min(numReadyToUpgrade, itemRosterElement.Amount);
                                party.ItemRoster.AddToCounts(itemRosterElement.EquipmentElement.Item, -itemCount);
                                numReadyToUpgrade -= itemCount;
                                if (numReadyToUpgrade == 0)
                                {
                                    break;
                                }
                            }
                        }
                    }

                    if (party.Owner.Gold < unitGoldPrice * numReadyToUpgrade)
                    {
                        numReadyToUpgrade = party.Owner.Gold / unitGoldPrice;
                    }

                    if (numReadyToUpgrade > 0)
                    {
                        if (party.Owner != null)
                        {
                            SkillLevelingManager.OnUpgradeTroops(party, characterObject, numReadyToUpgrade);
                            GiveGoldAction.ApplyBetweenCharacters(party.Owner, null, unitGoldPrice * numReadyToUpgrade, true);
                        }
                        else if (party.LeaderHero != null)
                        {
                            SkillLevelingManager.OnUpgradeTroops(party, characterObject, numReadyToUpgrade);
                            GiveGoldAction.ApplyBetweenCharacters(party.LeaderHero, null, unitGoldPrice * numReadyToUpgrade, true);
                        }
                    }
                }
            }
        }
    }
}
