using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using Helpers;
using MountAndBlade.CampaignBehaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;
using static Bandit_Militias.Globals;

namespace Bandit_Militias.Helpers
{
    public static class HeroCreatorCopy
    {
        private static readonly PerkObject Disciplinarian = PerkObject.All.First(x => x.Name.ToString() == "Disciplinarian");
        private static readonly SkillObject Leadership = SkillObject.All.First(x => x.Name.ToString() == "Leadership");

        private static readonly List<CharacterObject> Source = CharacterObject.Templates.Where(x =>
            x.Occupation == Occupation.Outlaw).ToList();

        // modified copy from the assembly e1.4.2
        public static Hero CreateBanditHero(Clan mostPrevalent, MobileParty mobileParty)
        {
            Hero specialHero = default;
            try
            {
                // todo make it the closest hideout
                var settlement = Hideouts.GetRandomElement();
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

                specialHero = HeroCreator.CreateSpecialHero(characterObject1, settlement);
                specialHero.ChangeState(Hero.CharacterStates.Active);
                Campaign.Current.GetCampaignBehavior<IHeroCreationCampaignBehavior>().DeriveSkillsFromTraits(specialHero, characterObject1);
                specialHero.SupporterOf = Clan.All.Where(x => x.IsMafia || x.IsNomad || x.IsSect).GetRandomElement();
                Traverse.Create(typeof(HeroCreator)).Method("AddRandomVarianceToTraits", specialHero).GetValue();
                // 1.4.3b doesn't have these wired up really, but I patched prisoners with it
                specialHero.NeverBecomePrisoner = true;
                specialHero.AlwaysDie = true;
                specialHero.Gold = Convert.ToInt32(mobileParty.Party.CalculateStrength() * GoldMap[Globals.Settings.GoldReward]);
                //var hideout = Hideouts.Where(x => x.MapFaction != CampaignData.NeutralFaction).GetRandomElement();
                // home has to be set to a hideout to make party aggressive (see PartyBase.MapFaction)
                // 1.4.3b changed this now we also have to set ActualClan
                specialHero.Clan = mostPrevalent;
                // ReSharper disable once NotAccessedVariable
                var lastSeenPlace = Traverse.Create(specialHero).Field<Hero.HeroLastSeenInformation>("_lastSeenInformationKnownToPlayer").Value;
                lastSeenPlace.LastSeenPlace = settlement;
                EquipmentHelper.AssignHeroEquipmentFromEquipment(specialHero, BanditEquipment.GetRandomElement());
#if !OneFourTwo
                Traverse.Create(specialHero).Field("_homeSettlement").SetValue(settlement);
                Traverse.Create(specialHero.Clan).Field("_warParties").Method("Add", mobileParty).GetValue();
#else
                Traverse.Create(Hero).Property("HomeSettlement").SetValue(hideout);
#endif
                if (Globals.Settings.CanTrain)
                {
                    mobileParty.MemberRoster.AddToCounts(specialHero.CharacterObject, 1, false, 0, 0, true, 0);
                    specialHero.SetSkillValue(Leadership, 125);
                    specialHero.SetPerkValue(Disciplinarian, true);
                }
            }
            catch (Exception ex)
            {
                var stackTrace = new StackTrace(ex, true);
                Mod.Log(stackTrace);
                Mod.Log(stackTrace.GetFrame(0).GetFileLineNumber());
            }

            MBObjectManager.Instance.RegisterObject(specialHero);
            return specialHero;
        }
    }
}
