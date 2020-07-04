using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using static Bandit_Militias.Helper.Globals;
using static Bandit_Militias.Helper;

namespace Bandit_Militias
{
    public class Militia
    {
        private static readonly TextObject Name = new TextObject("Bandit Militia");
        public static readonly HashSet<Militia> All = new HashSet<Militia>();
        public readonly MobileParty MobileParty;
        private Hero hero;

        public Militia(Vec2 position, TroopRoster party, TroopRoster prisoners)
        {
            MobileParty = MBObjectManager.Instance.CreateObject<MobileParty>("Bandit_Militia");
            Spawn(position, party, prisoners);
            Configure();
            All.Add(this);
            LogMilitiaFormed(MobileParty);
        }

        private void Spawn(Vec2 position, TroopRoster party, TroopRoster prisoners)
        {
            MobileParty.InitializeMobileParty(
                Name,
                party,
                prisoners,
                position,
                MergeDistance + 0.75f,
                MergeDistance + 0.5f); // does this have to be larger than MergeDistance?
        }

        internal void Configure()
        {
            try
            {
                if (MobileParty.MemberRoster.Count == 0)
                {
                    Mod.Log("Trying to configure militia with no troops, trashing", LogLevel.Debug);
                    Trash(MobileParty);
                    return;
                }

                BuildHeroWithBattleEquipment();
                MurderLordsForEquipment();
                var mostPrevalent = MostPrevalentFaction(MobileParty) ?? Clan.BanditFactions.First().MapFaction;
                SetupHero(mostPrevalent);
                var hideout = Settlement.FindAll(
                        x => x.IsHideout() &&
                             x.MapFaction != CampaignData.NeutralFaction)
                    .GetRandomElement();
                // home has to be set to a hideout to make party aggressive (see PartyBase.MapFaction)
                Traverse.Create(hero).Property("HomeSettlement").SetValue(hideout);

                // todo refactor for posse
                var index = Rng.Next(1, MobileParty.MemberRoster.Count);
                MobileParty.MemberRoster.AddXpToTroopAtIndex(300, index);
                PartyUpgraderCopy.UpgradeReadyTroopsCopy(MobileParty.Party);
            }
            catch (Exception ex)
            {
                Mod.Log(ex, LogLevel.Error);
            }
        }

        private void SetupHero(IFaction mostPrevalent)
        {
            MobileParty.Party.Owner = hero;
            hero.Clan = Clan.BanditFactions.FirstOrDefault(x => x == mostPrevalent);
            MobileParty.MemberRoster.AddToCounts(hero.CharacterObject, 1, false, 0, 0, true, 0);
            var leadership = SkillObject.All.First(x => x.Name.ToString() == "Leadership");
            hero.SetSkillValue(leadership, 125);
            var disciplinarian = PerkObject.All.First(x => x.Name.ToString() == "Disciplinarian");
            hero.SetPerkValue(disciplinarian, true);
            hero.Name = Name;
            hero.Gold = Convert.ToInt32(MobileParty.Party.CalculateStrength() * 500);
        }

        private void MurderLordsForEquipment()
        {
            int i = default;
            var equipment = new Equipment[3];
            while (i < 3)
            {
                var sacrificialLamb = HeroCreator.CreateHeroAtOccupation(Occupation.Lord);
                if (sacrificialLamb?.BattleEquipment?.Horse != null)
                {
                    equipment[i++] = sacrificialLamb.BattleEquipment;
                }

                sacrificialLamb.KillHero();
            }

            var gear = new Equipment();
            for (var j = 0; j < 12; j++)
            {
                gear[j] = equipment[Rng.Next(0, 3)][j];
            }

            // get rid of any mount
            gear[10] = new EquipmentElement();
            EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, gear);
        }

        private void BuildHeroWithBattleEquipment()
        {
            while (true)
            {
                hero = HeroCreator.CreateHeroAtOccupation(Occupation.Outlaw);
                if (hero.BattleEquipment != null)
                {
                    break;
                }

                Mod.Log("*", LogLevel.Debug);
                hero.KillHero();
            }
        }

        private IFaction MostPrevalentFaction(MobileParty mobileParty)
        {
            var map = new Dictionary<CultureObject, int>();
            var troopTypes =
                mobileParty.MemberRoster.Select(x => x.Character).ToList();
            foreach (var clan in Clan.BanditFactions)
            {
                for (var i = 0; i < troopTypes.Count && troopTypes[i].Culture == clan.Culture; i++)
                {
                    var troop = mobileParty.MemberRoster.GetElementCopyAtIndex(i);
                    var count = troop.Number;
                    if (map.ContainsKey(troop.Character.Culture))
                    {
                        map[troop.Character.Culture] += count;
                    }
                    else
                    {
                        map.Add(troop.Character.Culture, count);
                    }
                }
            }

            var faction = Clan.BanditFactions.FirstOrDefault(
                x => x.Culture == map.OrderByDescending(y => y.Value).FirstOrDefault().Key);
            return faction;
        }

        public static Militia FindMilitiaByParty(MobileParty mobileParty)
        {
            return All.FirstOrDefault(x => x.MobileParty == mobileParty);
        }

        public static Militia FindMilitiaByHero(Hero hero)
        {
            return All.FirstOrDefault(x => x.hero == hero);
        }

        public void Remove()
        {
            All.Remove(this);
        }
    }
}
