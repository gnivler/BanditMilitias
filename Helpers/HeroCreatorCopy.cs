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
        private static readonly List<CharacterObject> Source = CharacterObject.Templates.Where(x =>
            x.Occupation == Occupation.Outlaw).ToList();

        // modified from 1.5.8 copy
        public static Hero CreateBanditHero(Clan mostPrevalent, MobileParty mobileParty)
        {
            var settlement = Hideouts.GetRandomElement();
            var num1 = 0;
            foreach (var characterObject in Source)
            {
                var num2 = characterObject.GetTraitLevel(DefaultTraits.Frequency) * 10;
                num1 += num2 > 0 ? num2 : 100;
            }

            if (!Source.Any())
                return null;
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
            specialHero.SupporterOf = Clan.BanditFactions.ToList().GetRandomElement();
            Traverse.Create(typeof(HeroCreator)).Method("AddRandomVarianceToTraits", specialHero).GetValue();
            // 1.4.3b doesn't have these wired up really, but I patched prisoners with it... kinda
            specialHero.NeverBecomePrisoner = true;
            specialHero.AlwaysDie = true;
            var partyStrength = Traverse.Create(mobileParty.Party).Method("CalculateStrength").GetValue<float>();
            specialHero.Gold = Convert.ToInt32(partyStrength * GoldMap[Globals.Settings.GoldReward.SelectedValue]);
            //var hideout = Hideouts.Where(x => x.MapFaction != CampaignData.NeutralFaction).GetRandomElement();
            // home has to be set to a hideout to make party aggressive (see PartyBase.MapFaction)
            // 1.4.3b changed this now we also have to set ActualClan
            specialHero.Clan = mostPrevalent;
            // why is this not accessed?
            // ReSharper disable once NotAccessedVariable
            var lastSeenPlace = Traverse.Create(specialHero).Field<Hero.HeroLastSeenInformation>("_lastSeenInformationKnownToPlayer").Value;
            lastSeenPlace.LastSeenPlace = settlement;
            EquipmentHelper.AssignHeroEquipmentFromEquipment(specialHero, BanditEquipment.GetRandomElement());
            Traverse.Create(specialHero).Field("_homeSettlement").SetValue(settlement);
            Traverse.Create(specialHero.Clan).Field("_warParties").Method("Add", mobileParty).GetValue();
            mobileParty.MemberRoster.AddToCounts(specialHero.CharacterObject, 1, false, 0, 0, true, 0);
            if (Globals.Settings.CanTrain)
            {
                specialHero.SetSkillValue(DefaultSkills.Leadership, 150);
                specialHero.SetPerkValue(DefaultPerks.Leadership.VeteransRespect, true);
            }

            MBObjectManager.Instance.RegisterObject(specialHero);
            return specialHero;
        }
    }
}
