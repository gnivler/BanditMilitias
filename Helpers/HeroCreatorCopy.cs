using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MountAndBlade.CampaignBehaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace Bandit_Militias.Helpers
{
    public static class HeroCreatorCopy
    {
        private static readonly List<CharacterObject> Source = CharacterObject.Templates.Where(x =>
            x.Occupation == Occupation.Outlaw).ToList();

        // copied from the assembly, notably removed is the RegisterObject call from the end
        public static Hero CreateUnregisteredOutlaw()
        {
            var settlement = Helper.Globals.Hideouts.GetRandomElement();

            var num1 = 0;
            foreach (var characterObject in Source)
            {
                var num2 = characterObject.GetTraitLevel(DefaultTraits.Frequency) * 10;
                num1 += num2 > 0 ? num2 : 100;
            }

            CharacterObject characterObject1 = null;
            var num3 = 1 + (int) (settlement.Random.GetValueNormalized(settlement.Notables.Count) * (double) (num1 - 1));
            foreach (var characterObject2 in Source)
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
            specialHero.ChangeState(Hero.CharacterStates.Active);
            Campaign.Current.GetCampaignBehavior<IHeroCreationCampaignBehavior>().DeriveSkillsFromTraits(specialHero, characterObject1);
            specialHero.SupporterOf = Clan.All.Where(x => x.IsMafia || x.IsNomad || x.IsSect).GetRandomElement();
            Traverse.Create(typeof(HeroCreator)).Method("AddRandomVarianceToTraits", specialHero).GetValue();
            // RegisterObject removed
            return specialHero;
        }
    }
}
