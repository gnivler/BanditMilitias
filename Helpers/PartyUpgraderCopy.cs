using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment.Managers;
using TaleWorlds.Core;

// 1.5.8
namespace Bandit_Militias
{
    public static class PartyUpgraderCopy
    {
        private static float CalculateUpgradeChance(CharacterObject upgradeTarget, float netChance, float cavalryRatioAtParty)
        {
            if (upgradeTarget.IsMounted)
            {
                return netChance;
            }

            if (upgradeTarget.UpgradeTargets is null)
            {
                return netChance * (float) (cavalryRatioAtParty * 2.0 + 0.100000001490116);
            }

            var num = 0.0f;
            var length = upgradeTarget.UpgradeTargets.Length;
            foreach (var upgradeTarget1 in upgradeTarget.UpgradeTargets)
            {
                num += 1f / length * CalculateUpgradeChance(upgradeTarget1, netChance * 0.9f, cavalryRatioAtParty);
            }

            return num;
        }

        public static void UpgradeReadyTroops(PartyBase party)
        {
            var memberRoster = party.MemberRoster;
            var troopUpgradeModel = Campaign.Current.Models.PartyTroopUpgradeModel;
            var cavalryRatioAtParty = party.MobileParty is null || party.MobileParty.IsGarrison || party.MobileParty.IsMilitia || party.MobileParty.IsVillager ? 1f : party.NumberOfMenWithHorse / (party.NumberOfAllMembers + 0.1f);
            for (var index1 = 0; index1 < memberRoster.Count; ++index1)
            {
                var elementCopyAtIndex = memberRoster.GetElementCopyAtIndex(index1);
                if (troopUpgradeModel.IsTroopUpgradeable(party, elementCopyAtIndex.Character))
                {
                    var characterObjects = new List<Tuple<CharacterObject, int, int>>();
                    var numberReadyToUpgrade = elementCopyAtIndex.NumberReadyToUpgrade;
                    var upgradeXpCost = elementCopyAtIndex.Character.UpgradeXpCost;
                    if (numberReadyToUpgrade > elementCopyAtIndex.Number - elementCopyAtIndex.WoundedNumber)
                    {
                        numberReadyToUpgrade = elementCopyAtIndex.Number - elementCopyAtIndex.WoundedNumber;
                    }

                    if (numberReadyToUpgrade > 0)
                    {
                        for (var index2 = 0; index2 < elementCopyAtIndex.Character.UpgradeTargets.Length; ++index2)
                        {
                            var upgradeTarget = elementCopyAtIndex.Character.UpgradeTargets[index2];
                            var upgradeGoldCost = elementCopyAtIndex.Character.UpgradeCost(party, index2);
                            if (party.LeaderHero is not null && upgradeGoldCost != 0 && numberReadyToUpgrade * upgradeGoldCost > party.LeaderHero.Gold)
                            {
                                numberReadyToUpgrade = party.LeaderHero.Gold / upgradeGoldCost;
                            }

                            if (party.Owner is not null && elementCopyAtIndex.Character.UpgradeTargets[index2].UpgradeRequiresItemFromCategory is not null)
                            {
                                var flag = false;
                                var itemCount = 0;
                                foreach (var itemRosterElement in party.ItemRoster)
                                {
                                    if (itemRosterElement.EquipmentElement.Item.ItemCategory == upgradeTarget.UpgradeRequiresItemFromCategory)
                                    {
                                        itemCount += itemRosterElement.Amount;
                                        flag = true;
                                        if (itemCount >= numberReadyToUpgrade)
                                        {
                                            break;
                                        }
                                    }
                                }

                                if (flag)
                                {
                                    numberReadyToUpgrade = Math.Min(itemCount, numberReadyToUpgrade);
                                }
                            }

                            //if (party.Culture.IsBandit)
                            //  flag = elementCopyAtIndex.Character.UpgradeTargets[index2].Culture.IsBandit;
                            //if (elementCopyAtIndex.Character.Occupation == Occupation.Bandit)
                            //  flag = troopUpgradeModel.CanPartyUpgradeTroopToTarget(party, elementCopyAtIndex.Character, elementCopyAtIndex.Character.UpgradeTargets[index2]);
                            if (numberReadyToUpgrade > 0)
                            {
                                characterObjects.Add(new Tuple<CharacterObject, int, int>(elementCopyAtIndex.Character.UpgradeTargets[index2], numberReadyToUpgrade, upgradeGoldCost));
                            }
                        }

                        if (characterObjects.Count > 0)
                        {
                            var character = characterObjects.GetRandomElement();
                            if (party.IsMobile && party.LeaderHero is not null && cavalryRatioAtParty < 0.360000014305115)
                            {
                                var num2 = 0f;
                                foreach (var tuple2 in characterObjects)
                                {
                                    num2 += CalculateUpgradeChance(tuple2.Item1, 1f, cavalryRatioAtParty);
                                }

                                var num3 = num2 * MBRandom.RandomFloat;
                                foreach (var tuple2 in characterObjects)
                                {
                                    num3 -= CalculateUpgradeChance(tuple2.Item1, 1f, cavalryRatioAtParty);
                                    if (num3 < 0.0)
                                    {
                                        character = tuple2;
                                        break;
                                    }
                                }
                            }

                            var characterObject = character.Item1;
                            var numberToUpgrade = character.Item2;
                            var upgradeGoldCost = character.Item3;
                            var totalXpCost = upgradeXpCost * numberToUpgrade;
                            memberRoster.SetElementXp(index1, memberRoster.GetElementXp(index1) - totalXpCost);
                            memberRoster.AddToCounts(elementCopyAtIndex.Character, -numberToUpgrade);
                            memberRoster.AddToCounts(characterObject, numberToUpgrade);
                            var gold = party.Owner?.Gold;
                            var totalGoldCost = upgradeGoldCost * numberToUpgrade;
                            if ((gold.GetValueOrDefault() < totalGoldCost) & gold.HasValue)
                            {
                                numberToUpgrade = party.Owner.Gold / upgradeGoldCost;
                            }

                            if (numberToUpgrade > 0)
                            {
                                if (party.Owner is not null)
                                {
                                    SkillLevelingManager.OnUpgradeTroops(party, characterObject, numberToUpgrade);
                                    GiveGoldAction.ApplyBetweenCharacters(party.Owner, null, upgradeGoldCost * numberToUpgrade, true);
                                }
                                else if (party.LeaderHero is not null)
                                {
                                    SkillLevelingManager.OnUpgradeTroops(party, characterObject, numberToUpgrade);
                                    GiveGoldAction.ApplyBetweenCharacters(party.LeaderHero, null, upgradeGoldCost * numberToUpgrade, true);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
