using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using Helpers;
using SandBox.View.Map;
using SandBox.ViewModelCollection.MobilePartyTracker;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
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

            if (!PartyImageMap.ContainsKey(MobileParty))
            {
                PartyImageMap.Add(MobileParty, new ImageIdentifierVM(Banner));
            }

            LogMilitiaFormed(MobileParty);
        }

        public Militia(Vec2 position, TroopRoster party, TroopRoster prisoners)
        {
            Banner = Banners.GetRandomElement();
            BannerKey = Banner.Serialize();
            Spawn(position, party, prisoners);
            if (!PartyImageMap.ContainsKey(MobileParty))
            {
                PartyImageMap.Add(MobileParty, new ImageIdentifierVM(Banner));
            }

            TrainMilitia();
            SetMilitiaPatrol(MobileParty);
            LogMilitiaFormed(MobileParty);
        }

        private void Spawn(Vec2 position, TroopRoster party, TroopRoster prisoners)
        {
            MobileParty = BanditPartyComponent.CreateBanditParty("Bandit_Militia", Clan.BanditFactions.First(), Hideouts.GetRandomElement().Hideout, false);
            MobileParty.InitializeMobileParty(
                party,
                prisoners,
                position,
                0);
            PartyMilitiaMap.Add(MobileParty, this);
            PartyImageMap.Add(MobileParty, new ImageIdentifierVM(Banner));
            var mostPrevalent = (Clan)GetMostPrevalentFactionInParty(MobileParty) ?? Clan.BanditFactions.First();
            MobileParty.ActualClan = mostPrevalent;
            CreateHero(mostPrevalent);
            var getLocalizedText = AccessTools.Method(typeof(MBTextManager), "GetLocalizedText");
            Name = (string)getLocalizedText.Invoke(null, new object[] { $"{Possess(Hero.FirstName.ToString())} Bandit Militia" });
            MobileParty.SetCustomName(new TextObject(Name));
            MobileParty.Party.SetCustomOwner(Hero);
            MobileParty.Leader.StringId += "Bandit_Militia";
            MobileParty.ShouldJoinPlayerBattles = true;
            var tracker = Globals.MobilePartyTrackerVM?.Trackers?.FirstOrDefault(t => t.TrackedParty == MobileParty);
            if (Globals.Settings.Trackers
                && tracker is null
                && MobileParty.MemberRoster.TotalManCount >= Globals.Settings.TrackedSizeMinimum)
            {
                tracker = new MobilePartyTrackItemVM(MobileParty, MapScreen.Instance.MapCamera, null);
                Globals.MobilePartyTrackerVM?.Trackers?.Add(tracker);
            }
            else if (tracker is not null)
            {
                Globals.MobilePartyTrackerVM.Trackers.Remove(tracker);
            }
        }

        private void CreateHero(Clan mostPrevalent)
        {
            //Hero = HeroCreatorCopy.CreateBanditHero(mostPrevalent, MobileParty);
            Hero = HeroCreator.CreateHeroAtOccupation(Occupation.Outlaw);
            Hero.Clan = mostPrevalent;
            var partyStrength = Traverse.Create(MobileParty.Party).Method("CalculateStrength").GetValue<float>();
            Hero.Gold = Convert.ToInt32(partyStrength * GoldMap[Globals.Settings.GoldReward.SelectedValue]);
            //Traverse.Create(specialHero).Field("_homeSettlement").SetValue(settlement);
            //Traverse.Create(specialHero.Clan).Field("_warParties").Method("Add", mobileParty).GetValue();
            MobileParty.MemberRoster.AddToCounts(Hero.CharacterObject, 1, false, 0, 0, true, 0);
            EquipmentHelper.AssignHeroEquipmentFromEquipment(Hero, BanditEquipment.GetRandomElement());
            if (Globals.Settings.CanTrain)
            {
                Traverse.Create(Hero).Method("SetSkillValueInternal", DefaultSkills.Leadership, 150).GetValue();
                Traverse.Create(Hero).Method("SetPerkValueInternal", DefaultPerks.Leadership.VeteransRespect, true).GetValue();
            }

            var faction = Clan.BanditFactions.FirstOrDefault(x => Hero.MapFaction.Name == x.Name);
            Hero.Culture = faction is null ? Clan.BanditFactions.FirstOrDefault()?.Culture : faction.Culture;
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
                if (Globals.Settings.LooterUpgradePercent > 0)
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
                            numberToUpgrade = Convert.ToInt32(number * Globals.Settings.LooterUpgradePercent / 100);
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

                    var minNumberToUpgrade = Convert.ToInt32(Globals.Settings.UpgradeUnitsPercent / 100 * number * Rng.NextDouble());
                    minNumberToUpgrade = Math.Max(1, minNumberToUpgrade);
                    numberToUpgrade = Convert.ToInt32(Rng.Next(minNumberToUpgrade, Convert.ToInt32((number + 1) / 2f)));
                    Mod.Log($"{MobileParty.LeaderHero.Name} is upgrading up to {numberToUpgrade} of {number} \"{troopToTrain.Character.Name}\".");
                    var xpGain = numberToUpgrade * DifficultyXpMap[Globals.Settings.XpGift.SelectedValue];
                    MobileParty.MemberRoster.AddXpToTroop(xpGain, troopToTrain.Character);
                    Campaign.Current._partyUpgrader.UpgradeReadyTroops(MobileParty.Party);
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
