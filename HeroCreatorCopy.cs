using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helpers;
using MountAndBlade.CampaignBehaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace Bandit_Militias
{
    public static class HeroCreatorCopy
    {
        public static Hero CreateUnregisteredHero(
            Occupation neededOccupation,
            Settlement forcedHomeSettlement = null)
        {
            var settlement = forcedHomeSettlement ?? SettlementHelper.GetRandomTown();
            var source = new List<CharacterObject>();
            var culture = settlement.Culture;
            foreach (var characterObject in CharacterObject.Templates.Where(x => x.Occupation == neededOccupation))
            {
                if (characterObject.Culture == culture)
                {
                    source.Add(characterObject);
                }
            }

            var num1 = 0;
            foreach (var characterObject in source)
            {
                var num2 = characterObject.GetTraitLevel(DefaultTraits.Frequency) * 10;
                num1 += num2 > 0 ? num2 : 100;
            }

            if (!source.Any())
            {
                return null;
            }

            CharacterObject characterObject1 = null;
            var num3 = 1 + (int) (settlement.Random.GetValueNormalized(settlement.Notables.Count) * (double) (num1 - 1));
            foreach (var characterObject2 in source)
            {
                var num2 = characterObject2.GetTraitLevel(DefaultTraits.Frequency) * 10;
                num3 -= num2 > 0 ? num2 : 100;
                if (num3 < 0)
                {
                    characterObject1 = characterObject2;
                    break;
                }
            }

            var specialHero = HeroCreator.CreateSpecialHero(characterObject1, settlement);

            if (neededOccupation != Occupation.Wanderer)
            {
                specialHero.ChangeState(Hero.CharacterStates.Active);
            }

            Campaign.Current.GetCampaignBehavior<IHeroCreationCampaignBehavior>().DeriveSkillsFromTraits(specialHero, characterObject1);
            if (TextObject.IsNullOrEmpty(specialHero.FirstName))
            {
                specialHero.FirstName = new TextObject("{=!}ERROR");
            }

            if (neededOccupation != Occupation.Wanderer)
            {
                EnterSettlementAction.ApplyForCharacterOnly(specialHero, settlement);
            }

            if (neededOccupation != Occupation.Wanderer)
            {
                MBRandom.RandomInt(20, 50);
                var num2 = specialHero.IsMerchant ? 10000f : 5000f;
                GiveGoldAction.ApplyBetweenCharacters(null, specialHero, (int) (MBRandom.RandomFloat * (double) num2 + num2), true);
            }

            var heroObject = specialHero.Template?.HeroObject;
            specialHero.SupporterOf = heroObject == null || specialHero.Template.HeroObject.Clan == null || !specialHero.Template.HeroObject.Clan.IsMinorFaction
                ? HeroHelper.GetRandomClanForNotable(specialHero)
                : specialHero.Template.HeroObject.Clan;
            if (neededOccupation != Occupation.Wanderer)
            {
                Traverse.Create(typeof(HeroCreator)).Method("AddRandomVarianceToTraits", specialHero).GetValue();
            }

            return specialHero;
        }
    }
}
