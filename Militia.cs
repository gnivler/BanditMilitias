using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bandit_Militias.Helpers;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using static Bandit_Militias.Helpers.Globals;
using static Bandit_Militias.Helpers.Helper;
using Debug = TaleWorlds.Library.Debug;

namespace Bandit_Militias
{
    internal class Militia
    {
        public MobileParty MobileParty;
        internal readonly Banner Banner;
        internal Hero Hero;
        private static readonly PerkObject Disciplinarian = PerkObject.All.First(x => x.Name.ToString() == "Disciplinarian");
        private static readonly SkillObject Leadership = SkillObject.All.First(x => x.Name.ToString() == "Leadership");
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
            Configure();
            LogMilitiaFormed(MobileParty);
        }

        private void Spawn(IMapPoint mobileParty, TroopRoster party, TroopRoster prisoners)
        {
            Hero = HeroCreatorCopy.CreateUnregisteredOutlaw();
            MobileParty = MBObjectManager.Instance.CreateObject<MobileParty>("Bandit_Militia");
            MobileParty.InitializeMobileParty(
                null,
                party,
                prisoners,
                mobileParty.Position2D,
                0);
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

                MobileParty.Name = new TextObject($"{Possess(Hero.FirstName.ToString())} Bandit Militia");
                MBObjectManager.Instance.RegisterObject(Hero);
                EquipmentHelper.AssignHeroEquipmentFromEquipment(Hero, BanditEquipment.GetRandomElement());
                var mostPrevalent = (Clan) MostPrevalentFaction(MobileParty) ?? Clan.BanditFactions.First();
                SetupHero(mostPrevalent);
                MobileParty.Party.Owner = Hero;

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
                Debug.PrintError("Bandit Militias is failing to configure parties!  Exception:");
                var stackTrace = new StackTrace(ex, true);
                Mod.Log(stackTrace.GetFrame(0).GetFileLineNumber());
            }
        }

        private void SetupHero(Clan mostPrevalent)
        {
            // 1.4.3b doesn't have these wired up really, but I patched prisoners with it
            Hero.NeverBecomePrisoner = true;
            Hero.AlwaysDie = true;
            Hero.Gold = Convert.ToInt32(MobileParty.Party.CalculateStrength() * GoldMap[Globals.Settings.GoldReward]);
            var hideout = Hideouts.Where(x => x.MapFaction != CampaignData.NeutralFaction).GetRandomElement();
            // home has to be set to a hideout to make party aggressive (see PartyBase.MapFaction)
            // 1.4.3b changed this now we also have to set ActualClan
            Hero.Clan = mostPrevalent;
#if !OneFourTwo
            Traverse.Create(Hero).Field("_homeSettlement").SetValue(hideout);
            MobileParty.ActualClan = mostPrevalent;
            Traverse.Create(Hero.Clan).Field("_warParties").Method("Add", MobileParty).GetValue();
#else
            Traverse.Create(Hero).Property("HomeSettlement").SetValue(hideout);
#endif
            if (Globals.Settings.CanTrain)
            {
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
            try
            {
                var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
                var strengthString = $"{Math.Round(mobileParty.Party.TotalStrength)} strength";
                Mod.Log($"{$"New Bandit Militia led by {mobileParty.LeaderHero.Name}",-70} | {troopString,10} | {strengthString,10} |");
            }
            catch (Exception ex)
            {
                Mod.Log(ex);
            }
        }
    }
}
