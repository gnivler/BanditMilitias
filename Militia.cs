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
    internal class Militia

    {
        private static readonly TextObject Name = new TextObject("Bandit Militia");
        public static readonly HashSet<Militia> All = new HashSet<Militia>();
        public readonly MobileParty MobileParty;
        private Hero hero;
        internal List<Settlement> NearbyHideouts = new List<Settlement>();

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
                    Mod.Log("Trying to configure militia with no troops, trashing", LogLevel.Warning);
                    Trash(MobileParty);
                    return;
                }

                hero = HeroCreatorCopy.CreateUnregisteredHero(Occupation.Outlaw);
                MBObjectManager.Instance.RegisterObject(hero);
                EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, CreateEquipment(true));
                var mostPrevalent = MostPrevalentFaction(MobileParty) ?? Clan.BanditFactions.First().MapFaction;
                SetupHero(mostPrevalent);
                var hideout = Settlement.FindAll(
                        x => x.IsHideout() &&
                             x.MapFaction != CampaignData.NeutralFaction)
                    .GetRandomElement() ?? Settlement.GetFirst;
                // home has to be set to a hideout to make party aggressive (see PartyBase.MapFaction) 
                Traverse.Create(hero).Property("HomeSettlement").SetValue(hideout);

                // todo refactor for posse
                var index = Rng.Next(1, MobileParty.MemberRoster.Count);
                int iterations = default;
                switch (Globals.Settings.XpGift)
                {
                    case "LOW":
                        break;
                    case "NORMAL":
                        iterations = 1;
                        break;
                    case "HARD":
                        iterations = 2;
                        break;
                    case "HARDEST":
                        iterations = 4;
                        break;
                }

                for (var i = 0; i < iterations; i++)
                {
                    MobileParty.MemberRoster.AddXpToTroopAtIndex(DifficultyXpMap[Globals.Settings.XpGift], index);
                }

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
            hero.Name = Name;
            hero.Gold = Convert.ToInt32(MobileParty.Party.CalculateStrength() * GoldMap[Globals.Settings.GoldReward]);
            if (Globals.Settings.CanTrain)
            {
                hero.Clan = Clan.BanditFactions.FirstOrDefault(x => x == mostPrevalent);
                MobileParty.MemberRoster.AddToCounts(hero.CharacterObject, 1, false, 0, 0, true, 0);
                var leadership = SkillObject.All.First(x => x.Name.ToString() == "Leadership");
                hero.SetSkillValue(leadership, 125);
                var disciplinarian = PerkObject.All.First(x => x.Name.ToString() == "Disciplinarian");
                hero.SetPerkValue(disciplinarian, true);
            }
        }

        private static IFaction MostPrevalentFaction(MobileParty mobileParty)
        {
            var map = new Dictionary<CultureObject, int>();
            var troopTypes = mobileParty.MemberRoster.Select(x => x.Character).ToList();
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

        public void Remove()
        {
            All.Remove(this);
        }

        private static void LogMilitiaFormed(MobileParty mobileParty)
        {
            var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
            var strengthString = $"{Math.Round(mobileParty.Party.TotalStrength)} strength";
            Mod.Log($"{"New Bandit Militia",-40} | {troopString,10} | {strengthString,10} |", LogLevel.Debug);
        }
    }
}
