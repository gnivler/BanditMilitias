using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SandBox.View.Map;
using SandBox.ViewModelCollection.MobilePartyTracker;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using static Bandit_Militias.Globals;
using static Bandit_Militias.Helpers.Helper;

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
            if (!Hero.StringId.EndsWith("Bandit_Militia")
                || !Hero.CharacterObject.StringId.EndsWith("Bandit_Militia"))
            {
                Hero.StringId += "Bandit_Militia";
                Hero.CharacterObject.StringId += "Bandit_Militia";
            }

            if (!PartyImageMap.ContainsKey(MobileParty))
            {
                PartyImageMap.Add(MobileParty, new ImageIdentifierVM(Banner));
            }

            //LogMilitiaFormed(MobileParty);
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
            //LogMilitiaFormed(MobileParty);
        }

        private void Spawn(Vec2 position, TroopRoster party, TroopRoster prisoners)
        {
            var partyClan = GetMostPrevalent(party) ?? Clan.BanditFactions.First();
            MobileParty = ModBanditMilitiaPartyComponent.CreateBanditParty(partyClan);
            MobileParty.InitializeMobilePartyAroundPosition(party, prisoners, position, 0);
            PartyMilitiaMap.Add(MobileParty, this);
            PartyImageMap.Add(MobileParty, new ImageIdentifierVM(Banner));
            var leaderHero = MobileParty.MemberRoster.GetTroopRoster().ToListQ()[0].Character.HeroObject;
            MobileParty.PartyComponent.ChangePartyLeader(leaderHero);
            Hero = MobileParty.LeaderHero;
            Hero.Gold = Convert.ToInt32(MobileParty.Party.TotalStrength * Globals.GoldMap[Globals.Settings.GoldReward.SelectedValue]);
            if (MobileParty.ActualClan.Leader is null)
            {
                MobileParty.ActualClan.SetLeader(Hero);
            }

            if (MobileParty.MemberRoster.GetTroopRoster().Any(t => t.Character.IsMounted))
            {
                var mount = Mounts.GetRandomElement();
                Hero.BattleEquipment[10] = new EquipmentElement(mount);
                if (mount.HorseComponent.Monster.MonsterUsage == "camel")
                {
                    Hero.BattleEquipment[11] = new EquipmentElement(Saddles.Where(saddle =>
                        saddle.Name.ToString().ToLower().Contains("camel")).ToList().GetRandomElement());
                }
                else
                {
                    Hero.BattleEquipment[11] = new EquipmentElement(Saddles.Where(saddle =>
                        !saddle.Name.ToString().ToLower().Contains("camel")).ToList().GetRandomElement());
                }
            }

            var getLocalizedText = AccessTools.Method(typeof(MBTextManager), "GetLocalizedText");
            Name = (string)getLocalizedText.Invoke(null, new object[] { $"{Possess(Hero.FirstName.ToString())} Bandit Militia" });
            MobileParty.SetCustomName(new TextObject(Name));
            MobileParty.LeaderHero.StringId += "Bandit_Militia";
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
                    GlobalMilitiaPower > Globals.Settings.GlobalPowerPercent
                    || DifficultyXpMap[Globals.Settings.XpGift.SelectedValue] == 0)
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
                for (var i = 0; i < iterations && GlobalMilitiaPower <= Globals.Settings.GlobalPowerPercent; i++)
                {
                    var validTroops = MobileParty.MemberRoster.GetTroopRoster().Where(x =>
                        x.Character.Tier < Globals.Settings.MaxTrainingTier
                        && !x.Character.IsHero
                        && troopUpgradeModel.IsTroopUpgradeable(MobileParty.Party, x.Character));
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
                Trash(MobileParty);
            }
        }

        private static Clan GetMostPrevalent(TroopRoster troopRoster)
        {
            var map = new Dictionary<CultureObject, int>();
            var troopTypes = troopRoster.GetTroopRoster().Select(t => t.Character).ToList();
            foreach (var clan in Clan.BanditFactions)
            {
                for (var i = 0; i < troopTypes.Count && troopTypes[i].Culture == clan.Culture; i++)
                {
                    var troop = troopRoster.GetElementCopyAtIndex(i);
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

            var faction = Clan.BanditFactions.FirstOrDefault(c => c.Culture == map.OrderByDescending(y => y.Value).FirstOrDefault().Key);
            return faction;
        }

        // too slow
        //private static void LogMilitiaFormed(MobileParty mobileParty)
        //{
        //    try
        //    {
        //        var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
        //        var strengthString = $"{Math.Round(mobileParty.Party.TotalStrength)} strength";
        //        Mod.Log($"{$"New Bandit Militia led by {mobileParty.LeaderHero.Name}",-70} | {troopString,10} | {strengthString,12} | >>> {GlobalMilitiaPower / CalculatedGlobalPowerLimit * 100}%");
        //    }
        //    catch (Exception ex)
        //    {
        //        Mod.Log(ex);
        //    }
        //}
    }
}
