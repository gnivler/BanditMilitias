using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bandit_Militias.Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
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
        public string Name;
        internal readonly Banner Banner;
        internal readonly string BannerKey;
        internal Hero Hero;
        internal CampaignTime LastMergedOrSplitDate = CampaignTime.Now;

        public Militia(MobileParty mobileParty)
        {
            MobileParty = mobileParty;
            Banner = Banners.GetRandomElement();
            BannerKey = Banner.Serialize();
            Hero = mobileParty.LeaderHero;
            if (!MobileParty.Leader.StringId.EndsWith("Bandit_Militia"))
            {
                MobileParty.Leader.StringId += "Bandit_Militia";
            }

            LogMilitiaFormed(MobileParty);
        }

        public Militia(Vec2 position, TroopRoster party, TroopRoster prisoners)
        {
            Banner = Banners.GetRandomElement();
            BannerKey = Banner.Serialize();
            Spawn(position, party, prisoners);
            TrainMilitia();
            PartyMilitiaMap.Add(MobileParty, this);
            SetMilitiaPatrol(MobileParty);
            LogMilitiaFormed(MobileParty);
        }

        private void Spawn(Vec2 position, TroopRoster party, TroopRoster prisoners)
        {
            MobileParty = MBObjectManager.Instance.CreateObject<MobileParty>("Bandit_Militia");
            MobileParty.InitializeMobileParty(
                party,
                prisoners,
                position,
                0);
            var mostPrevalent = (Clan) GetMostPrevalentFactionInParty(MobileParty) ?? Clan.BanditFactions.First();
            MobileParty.ActualClan = mostPrevalent;
            Hero = HeroCreatorCopy.CreateBanditHero(mostPrevalent, MobileParty);
            //Hero = HeroCreator.CreateHeroAtOccupation(Occupation.Outlaw);
            var faction = Clan.BanditFactions.FirstOrDefault(x => Hero.MapFaction.Name == x.Name);
            Hero.Culture = faction is null ? Clan.BanditFactions.FirstOrDefault()?.Culture : faction.Culture;

            var getLocalizedText = AccessTools.Method(typeof(MBTextManager), "GetLocalizedText");
            Name = /*MountAndWarcraftMod
                ? (string) getLocalizedText.Invoke(null, new object[] {"Scourge Horde"})              
                : */(string) getLocalizedText.Invoke(null, new object[] {$"{Possess(Hero.FirstName.ToString())} Bandit Militia"});
            MobileParty.SetCustomName(new TextObject(Name));
            MobileParty.Party.Owner = Hero;
            MobileParty.Leader.StringId += "Bandit_Militia";
            MobileParty.ShouldJoinPlayerBattles = true;
            var tracker = MobilePartyTrackerVM?.Trackers?.FirstOrDefault(t => t.TrackedParty == MobileParty);
            if (tracker is not null)
            {
                MobilePartyTrackerVM.Trackers.Remove(tracker);
            }
        }

        private void TrainMilitia()
        {
            try
            {
                if (MobileParty.MemberRoster.Count == 0)
                {
                    Mod.Log("Trying to configure militia with no troops, trashing");
                    Trash(MobileParty);
                    return;
                }

                if (!Globals.Settings.CanTrain ||
                    DifficultyXpMap[Globals.Settings.XpGift.SelectedValue] == 0)
                {
                    return;
                }

                int iterations = default;
                switch (Globals.Settings.XpGift.SelectedValue)
                {
                    case "Off":
                        break;
                    case "Normal":
                        iterations = 1;
                        break;
                    case "Hard":
                        iterations = 2;
                        break;
                    case "Hardest":
                        iterations = 4;
                        break;
                }

                int number, numberToUpgrade;
                if (Globals.Settings.LooterUpgradeFactor > float.Epsilon)
                {
                    // upgrade any looters first, then go back over and iterate further upgrades
                    var looters = MobileParty.MemberRoster.GetTroopRoster().Where(x =>
                        x.Character.Name.Contains("Looter")).ToList();
                    var culture = GetMostPrevalentFromNearbySettlements(MobileParty.Position2D);
                    if (looters.Any())
                    {
                        foreach (var looter in looters)
                        {
                            number = MobileParty.MemberRoster.GetElementCopyAtIndex(MobileParty.MemberRoster.FindIndexOfTroop(looter.Character)).Number;
                            numberToUpgrade = Convert.ToInt32(number * Globals.Settings.LooterUpgradeFactor);
                            if (numberToUpgrade == 0)
                            {
                                continue;
                            }

                            var roster = MobileParty.MemberRoster;
                            roster.RemoveTroop(looter.Character, numberToUpgrade);
                            ConvertLootersToKingdomCultureRecruits(ref roster, culture, numberToUpgrade);
                        }
                    }
                }

                var troopUpgradeModel = Campaign.Current.Models.PartyTroopUpgradeModel;
                for (var i = 0; i < iterations; i++)
                {
                    var validTroops = MobileParty.MemberRoster.GetTroopRoster().Where(x =>
                        x.Character.Tier < Globals.Settings.MaxTrainingTier &&
                        !x.Character.IsHero &&
                        troopUpgradeModel.IsTroopUpgradeable(MobileParty.Party, x.Character));
                    var troopToTrain = validTroops.ToList().GetRandomElement();
                    number = troopToTrain.Number;
                    if (number < 1)
                    {
                        continue;
                    }

                    var minNumberToUpgrade = Convert.ToInt32(Globals.Settings.UpgradeUnitsFactor * number * Rng.NextDouble());
                    minNumberToUpgrade = Math.Max(1, minNumberToUpgrade);
                    numberToUpgrade = Convert.ToInt32(Rng.Next(minNumberToUpgrade, Convert.ToInt32((number + 1) / 2f)));
                    Mod.Log($"{MobileParty.LeaderHero.Name} is upgrading up to {numberToUpgrade} of {number} \"{troopToTrain.Character.Name}\".");
                    var xpGain = numberToUpgrade * DifficultyXpMap[Globals.Settings.XpGift.SelectedValue];
                    MobileParty.MemberRoster.AddXpToTroop(xpGain, troopToTrain.Character);
                    PartyUpgraderCopy.UpgradeReadyTroops(MobileParty.Party);
                    // this is gross, not sure why it doesn't update itself, seems like the right way to call
                    Traverse.Create(MobileParty.MemberRoster).Field<List<TroopRosterElement>>("_troopRosterElements").Value
                        = MobileParty.MemberRoster.GetTroopRoster();
                    MobileParty.MemberRoster.UpdateVersion();
                    if (Globals.Settings.TestingMode)
                    {
                        var party = Hero.MainHero.PartyBelongedTo ?? Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;
                        MobileParty.Position2D = party.Position2D;
                    }
                }
            }
            catch (Exception ex)
            {
                Mod.Log("Bandit Militias is failing to configure parties!  Exception: " + ex);
                Debug.PrintError("Bandit Militias is failing to configure parties!  Exception: " + ex);
                Trash(MobileParty);
            }
        }

        private static IFaction GetMostPrevalentFactionInParty(MobileParty mobileParty)
        {
            var map = new Dictionary<CultureObject, int>();
            var troopTypes = mobileParty.MemberRoster.GetTroopRoster().Select(x => x.Character).ToList();
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

        private static void LogMilitiaFormed(MobileParty mobileParty)
        {
            try
            {
                var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
                var strengthString = $"{Math.Round(mobileParty.Party.TotalStrength)} strength";
                Mod.Log($"{$">>> New Bandit Militia led by {mobileParty.LeaderHero.Name}",-70} | {troopString,10} | {strengthString,12} |");
            }
            catch (Exception ex)
            {
                Mod.Log(new StackTrace());
                Mod.Log(ex);
            }
        }
    }
}
