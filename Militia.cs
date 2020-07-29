using System;
using System.Collections.Generic;
using System.Linq;
using Bandit_Militias.Helpers;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using static Bandit_Militias.Helpers.Helper.Globals;
using static Bandit_Militias.Helpers.Helper;

namespace Bandit_Militias
{
    internal class Militia
    {
        private static readonly TextObject Name = new TextObject("Bandit Militia");
        public MobileParty MobileParty;
        internal Banner Banner;
        internal Hero Hero;
        private static readonly PerkObject Disciplinarian = PerkObject.All.First(x => x.Name.ToString() == "Disciplinarian");
        private static readonly SkillObject Leadership = SkillObject.All.First(x => x.Name.ToString() == "Leadership");

        public Militia(MobileParty mobileParty)
        {
            //Mod.Log("Before create " + MobileParty.All.Count(x => x.MemberRoster.Count == 0 && x.HomeSettlement == null));
            Militias.Add(this);
            MobileParty = mobileParty;
            Banner = Banner.CreateRandomBanner();
            Hero = mobileParty.LeaderHero;
            //Mod.Log("After create " + MobileParty.All.Count(x => x.MemberRoster.Count == 0 && x.HomeSettlement == null));
            LogMilitiaFormed(MobileParty);
        }

        public Militia(Vec2 position, TroopRoster party, TroopRoster prisoners)
        {
            //Mod.Log("Before create " + MobileParty.All.Count(x => x.MemberRoster.Count == 0 && x.HomeSettlement == null));
            Militias.Add(this);
            Spawn(position, party, prisoners);
            Configure();
            //Mod.Log("After create " + MobileParty.All.Count(x => x.MemberRoster.Count == 0 && x.HomeSettlement == null));
            LogMilitiaFormed(MobileParty);
        }

        private void Spawn(Vec2 position, TroopRoster party, TroopRoster prisoners)
        {
            MobileParty = MBObjectManager.Instance.CreateObject<MobileParty>("Bandit_Militia");
            MobileParty.InitializeMobileParty(
                Name,
                party,
                prisoners,
                position,
                MergeDistance + 0.75f,
                MergeDistance + 0.5f);
        }

        private void Configure()
        {
            try
            {
                if (MobileParty.MemberRoster.Count == 0)
                {
                    Mod.Log("Trying to configure militia with no troops, trashing", LogLevel.Info);
                    Trash(MobileParty);
                    return;
                }

                Banner = Banner.CreateRandomBanner();
                Hero = HeroCreatorCopy.CreateUnregisteredOutlaw();
                MobileParty.Party.Owner = Hero;
                MBObjectManager.Instance.RegisterObject(Hero);
                EquipmentHelper.AssignHeroEquipmentFromEquipment(Hero, CreateEquipment(true));
                var mostPrevalent = (Clan) MostPrevalentFaction(MobileParty) ?? Clan.BanditFactions.First();
                SetupHero(mostPrevalent);
                var hideout = Hideouts.GetRandomElement() ?? Settlement.GetFirst;
                // home has to be set to a hideout to make party aggressive (see PartyBase.MapFaction)
                // 1.4.3 changed this now we also have to set ActualClan
                MobileParty.ActualClan = mostPrevalent;
                Traverse.Create(Hero).Field("_homeSettlement").SetValue(hideout);
                Traverse.Create(Hero.Clan).Field("_warParties").Method("Add", MobileParty).GetValue();
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
                Trash(MobileParty);
                Mod.Log(ex);
            }
        }

        private void SetupHero(Clan mostPrevalent)
        {
            Hero.Name = Name;
            Hero.Gold = Convert.ToInt32(MobileParty.Party.CalculateStrength() * GoldMap[Globals.Settings.GoldReward]);
            if (Globals.Settings.CanTrain)
            {
                Hero.Clan = mostPrevalent;
                MobileParty.MemberRoster.AddToCounts(Hero.CharacterObject, 1, false, 0, 0, true, 0);
                Hero.SetSkillValue(Leadership, 125);
                Hero.SetPerkValue(Disciplinarian, true);
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
            return Militias.FirstOrDefault(x => x.MobileParty == mobileParty);
        }

        private static void LogMilitiaFormed(MobileParty mobileParty)
        {
            var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
            var strengthString = $"{Math.Round(mobileParty.Party.TotalStrength)} strength";
            Mod.Log($"{"New Bandit Militia",-40} | {troopString,10} | {strengthString,10} |");
        }
    }
}
