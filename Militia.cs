using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bandit_Militias.Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using static Bandit_Militias.Globals;
using static Bandit_Militias.Helpers.Helper;
using Debug = TaleWorlds.Library.Debug;

namespace Bandit_Militias
{
    internal class Militia
    {
        public MobileParty MobileParty;
        internal readonly Banner Banner;
        internal Hero Hero;
        internal CampaignTime LastMergedOrSplitDate = CampaignTime.Now;

        public Militia(MobileParty mobileParty)
        {
            Militias.Add(this);
            MobileParty = mobileParty;
            Banner = Banner.CreateRandomBanner();
            Hero = mobileParty.LeaderHero;
            LogMilitiaFormed(MobileParty);
        }

        public Militia(MobileParty mobileParty, TroopRoster party, TroopRoster prisoners)
        {
            Militias.Add(this);
            Banner = Banner.CreateRandomBanner();
            Spawn(mobileParty, party, prisoners);
            TrainMilitia();
            LogMilitiaFormed(MobileParty);
        }

        private void Spawn(IMapPoint mobileParty, TroopRoster party, TroopRoster prisoners)
        {
            MobileParty = MBObjectManager.Instance.CreateObject<MobileParty>("Bandit_Militia");
            MobileParty.InitializeMobileParty(
                null,
                party,
                prisoners,
                mobileParty.Position2D,
                0);
            var mostPrevalent = (Clan) MostPrevalentFaction(MobileParty) ?? Clan.BanditFactions.First();
            MobileParty.ActualClan = mostPrevalent;
            Hero = HeroCreatorCopy.CreateBanditHero(mostPrevalent, MobileParty);
        }

        private void TrainMilitia()
        {
            try
            {
                if (MobileParty.MemberRoster.Count == 0)
                {
                    Mod.Log("Trying to configure militia with no troops, trashing", LogLevel.Info);
                    Trash(MobileParty);
                    return;
                }

                var partyName = (string) Traverse.Create(typeof(MBTextManager))
                    .Method("GetLocalizedText", $"{Possess(Hero.FirstName.ToString())} Bandit Militia").GetValue();
                MobileParty.Name = new TextObject(partyName);
                MobileParty.Party.Owner = Hero;

                if (!Globals.Settings.CanTrain ||
                    DifficultyXpMap[Globals.Settings.XpGift] == 0)
                {
                    return;
                }

                int iterations = default;
                switch (Globals.Settings.XpGift)
                {
                    case "OFF":
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

                int number, numberToUpgrade;
                if (Globals.Settings.LooterUpgradeFactor > float.Epsilon)
                {
                    try
                    {
                        // upgrade any looters first, then go back over and iterate further upgrades
                        var looters = MobileParty.MemberRoster.Troops.Where(x =>
                            x.Name.Contains("Looter")).ToList();
                        var culture = FindMostPrevalentFaction(MobileParty.Position2D);
                        if (looters.Any())
                        {
                            foreach (var looter in looters)
                            {
                                number = MobileParty.MemberRoster.GetElementCopyAtIndex(MobileParty.MemberRoster.FindIndexOfTroop(looter)).Number;
                                numberToUpgrade = Convert.ToInt32(number * Globals.Settings.LooterUpgradeFactor);
                                if (numberToUpgrade == 0)
                                {
                                    continue;
                                }

                                var roster = MobileParty.MemberRoster;
                                roster.RemoveTroop(looter, numberToUpgrade);
                                ConvertLootersToKingdomCultureRecruits(ref roster, culture, numberToUpgrade);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Mod.Log(ex);
                    }
                }

                for (var i = 0; i < iterations; i++)
                {
                    // start at index 1 to avoid hero - could be more robust...
                    var randomIndex = Rng.Next(1, MobileParty.MemberRoster.Troops.Count());
                    number = MobileParty.MemberRoster.GetElementCopyAtIndex(randomIndex).Number;
                    var minNumberToUpgrade = Convert.ToInt32(Globals.Settings.UpgradeUnitsFactor * number * Rng.NextDouble());
                    if (minNumberToUpgrade == 0)
                    {
                        minNumberToUpgrade = 1;
                    }

                    numberToUpgrade = Convert.ToInt32(Rng.Next(minNumberToUpgrade, number + 1));
                    Mod.Log($"Adding {numberToUpgrade,-3} from {number}");
                    MobileParty.MemberRoster.AddXpToTroopAtIndex(numberToUpgrade * DifficultyXpMap[Globals.Settings.XpGift], randomIndex);
                    PartyUpgraderCopy.UpgradeReadyTroopsCopy(MobileParty.Party);
                }
            }
            catch (Exception ex)

            {
                Trash(MobileParty);
                Mod.Log("Bandit Militias is failing to configure parties!  Exception: " + ex);
                Debug.PrintError("Bandit Militias is failing to configure parties!  Exception: " + ex);
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
            try
            {
                var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
                var strengthString = $"{Math.Round(mobileParty.Party.TotalStrength)} strength";
                Mod.Log($"{$"New Bandit Militia led by {mobileParty.LeaderHero.Name}",-70} | {troopString,10} | {strengthString,12} |");
            }
            catch (Exception ex)
            {
                Mod.Log(new StackTrace());
                Mod.Log(ex);
            }
        }
    }
}
