using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;
using static Bandit_Militias.Globals;

namespace Bandit_Militias.Helpers
{
    public static class HeroCreatorCopy
    {
        private static readonly List<CharacterObject> Outlaws = CharacterObject.Templates.Where(x =>
            x.Occupation == Occupation.Outlaw).ToList();

        // modified from 1.5.8 copy
        public static Hero CreateBanditHero(Clan mostPrevalent, MobileParty mobileParty)
        {
            if (!Outlaws.Any())
            {
                return null;
            }

            var settlement = Hideouts.GetRandomElement();
            var num1 = 0;
            foreach (var outlaw in Outlaws)
            {
                var num2 = outlaw.GetTraitLevel(DefaultTraits.Frequency) * 10;
                num1 += num2 > 0 ? num2 : 100;
            }

            CharacterObject characterObject1 = null;
            var num3 = 1 + (int)(settlement.Random.GetValueNormalized(settlement.Notables.Count) * (double)(num1 - 1));
            foreach (var characterObject2 in Outlaws)
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
            specialHero.SupporterOf = Clan.BanditFactions.ToList().GetRandomElement();
            Traverse.Create(typeof(HeroCreator)).Method("AddRandomVarianceToTraits", specialHero).GetValue();
            if (mobileParty is not null)
            {
                var partyStrength = Traverse.Create(mobileParty.Party).Method("CalculateStrength").GetValue<float>();
                specialHero.Gold = Convert.ToInt32(partyStrength * GoldMap[Globals.Settings.GoldReward.SelectedValue]);
                Traverse.Create(specialHero).Field("_homeSettlement").SetValue(settlement);
                Traverse.Create(specialHero.Clan).Field("_warParties").Method("Add", mobileParty).GetValue();
                mobileParty.MemberRoster.AddToCounts(specialHero.CharacterObject, 1, false, 0, 0, true, 0);
            }

            //var hideout = Hideouts.Where(x => x.MapFaction != CampaignData.NeutralFaction).GetRandomElement();
            // home has to be set to a hideout to make party aggressive (see PartyBase.MapFaction)
            // 1.4.3b changed this now we also have to set ActualClan
            specialHero.Clan = mostPrevalent;
            var heroLastSeenInformation = Traverse.Create(specialHero).Field<Hero.HeroLastSeenInformation>("_lastSeenInformationKnownToPlayer").Value;
            heroLastSeenInformation.LastSeenPlace = settlement;
            Traverse.Create(specialHero).Field<Hero.HeroLastSeenInformation>("_lastSeenInformationKnownToPlayer").Value = heroLastSeenInformation;
            EquipmentHelper.AssignHeroEquipmentFromEquipment(specialHero, BanditEquipment.GetRandomElement());


            if (Globals.Settings.CanTrain)
            {
                Traverse.Create(specialHero).Method("SetSkillValueInternal", DefaultSkills.Leadership, 150).GetValue();
                Traverse.Create(specialHero).Method("SetPerkValueInternal", DefaultPerks.Leadership.VeteransRespect, true).GetValue();
            }

            MBObjectManager.Instance.RegisterObject(specialHero);
            return specialHero;
        }
    }
}
