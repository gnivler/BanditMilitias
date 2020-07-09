using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using static Bandit_Militias.Helper.Globals;
using Debug = TaleWorlds.Library.Debug;

// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    public static class Helper
    {
        public static class Globals
        {
            // dev
            internal static bool testingMode;
            internal static LogLevel logging = LogLevel.Disabled;

            // how far to look
            internal const float SearchRadius = 25;

            // how close before merging
            internal const float MergeDistance = 3.5f;
            internal const float MinDistanceFromHideout = 15;

            // thresholds for splitting
            internal const float StrengthSplitFactor = 0.8f;
            internal const float SizeSplitFactor = 0.8f;
            internal const float RandomSplitChance = 0.25f;

            // adjusts size and strengths
            internal const float PartyStrengthFactor = 0.8f;
            internal const float MaxPartySizeFactor = 0.8f;

            // holders for criteria
            internal static float HeroPartyStrength;
            internal static float MaxPartyStrength;
            internal static double MaxPartySize;

            // misc
            internal static readonly Random Rng = new Random();
            internal static List<MapEvent> MapEvents;
        }

        internal static int NumMountedTroops(TroopRoster troopRoster)
        {
            return troopRoster.Troops.Where(x => x.IsMounted)
                .Sum(troopRoster.GetTroopCount);
        }

        private static float Variance => MBRandom.RandomFloatRanged(0.5f, 1.5f);

        internal static void CalcMergeCriteria()
        {
            // first campaign init hasn't populated this apparently
            var parties = MobileParty.All.Where(
                x => x.LeaderHero != null && !x.Name.Equals("Bandit Militia")).ToList();
            if (parties.Any())
            {
                HeroPartyStrength = parties.Select(x => x.Party.TotalStrength).Average();
                // reduce strength
                MaxPartyStrength = HeroPartyStrength * PartyStrengthFactor * Variance;
                // maximum size grows over time as clans level up
                MaxPartySize = Math.Round(MobileParty.All
                    .Where(x => x.LeaderHero != null && !x.IsBandit).Select(x => x.Party.PartySizeLimit).Average());
                MaxPartySize *= MaxPartySizeFactor * Variance;
                Mod.Log($"Daily calculations => size: {MaxPartySize:0} strength: {MaxPartyStrength:0}", LogLevel.Debug);
            }
        }

        internal static void TrySplitUpParty(MobileParty __instance)
        {
            if (__instance.Party.MemberRoster.TotalManCount < 50)
            {
                return;
            }

            if (__instance.IsTooBusyToMerge())
            {
                return;
            }

            if (!__instance.Name.Equals("Bandit Militia"))
            {
                return;
            }

            var roll = Rng.NextDouble();
            if (__instance.MemberRoster.TotalManCount == 0 ||
                roll > RandomSplitChance ||
                !__instance.Name.Equals("Bandit Militia") ||
                __instance.Party.TotalStrength <= MaxPartyStrength * StrengthSplitFactor * Variance ||
                __instance.Party.MemberRoster.TotalManCount <= MaxPartySize * SizeSplitFactor * Variance)
            {
                return;
            }

            var party1 = new TroopRoster();
            var party2 = new TroopRoster();
            var prisoners1 = new TroopRoster();
            var prisoners2 = new TroopRoster();
            var inventory1 = new ItemRoster();
            var inventory2 = new ItemRoster();
            SplitRosters(__instance, party1, party2, prisoners1, prisoners2, inventory1, inventory2);
            CreateSplitMilitias(__instance, party1, party2, prisoners1, prisoners2, inventory1, inventory2);
        }

        private static void SplitRosters(MobileParty original, TroopRoster troops1, TroopRoster troops2,
            TroopRoster prisoners1, TroopRoster prisoners2, ItemRoster inventory1, ItemRoster inventory2)
        {
            try
            {
                Mod.Log($"Processing troops: {original.MemberRoster.Count} types, {original.MemberRoster.TotalManCount} in total", LogLevel.Debug);
                foreach (var rosterElement in original.MemberRoster)
                {
                    SplitRosters(troops1, troops2, rosterElement);
                }

                if (original.PrisonRoster.TotalManCount > 0)
                {
                    Mod.Log($"Processing prisoners: {original.PrisonRoster.Count} types, {original.PrisonRoster.TotalManCount} in total", LogLevel.Debug);
                    foreach (var rosterElement in original.PrisonRoster)
                    {
                        SplitRosters(prisoners1, prisoners2, rosterElement);
                    }
                }

                foreach (var item in original.ItemRoster)
                {
                    var half = Math.Max(1, item.Amount / 2);
                    inventory1.AddToCounts(item.EquipmentElement, half);
                    var remainder = item.Amount % 2;
                    if (half > 2)
                    {
                        inventory2.AddToCounts(item.EquipmentElement, half + remainder);
                    }
                }
            }
            catch (Exception ex)
            {
                Mod.Log(ex, LogLevel.Error);
            }
        }

        private static void SplitRosters(TroopRoster troops1, TroopRoster troops2, TroopRosterElement rosterElement)
        {
            var half = Math.Max(1, rosterElement.Number / 2);
            troops1.AddToCounts(rosterElement.Character, half);
            var remainder = rosterElement.Number % 2;
            // 1-3 will have a half of 1.  4 would be 2, and worth adding to second roster
            if (half > 2)
            {
                troops2.AddToCounts(rosterElement.Character, Math.Max(1, half + remainder));
            }
        }

        private static void CreateSplitMilitias(MobileParty original, TroopRoster party1, TroopRoster party2,
            TroopRoster prisoners1, TroopRoster prisoners2, ItemRoster inventory1, ItemRoster inventory2)
        {
            try
            {
                var militia1 = new Militia(original.Position2D, party1, prisoners1);
                var militia2 = new Militia(original.Position2D, party2, prisoners2);
                Traverse.Create(militia1.MobileParty.Party).Property("ItemRoster").SetValue(inventory1);
                Traverse.Create(militia2.MobileParty.Party).Property("ItemRoster").SetValue(inventory2);
                Mod.Log($"{militia1.MobileParty.MapFaction.Name} <<< Split >>> {militia2.MobileParty.MapFaction.Name}", LogLevel.Info);
                militia1.MobileParty.Party.Visuals.SetMapIconAsDirty();
                militia2.MobileParty.Party.Visuals.SetMapIconAsDirty();
                Trash(original);
            }
            catch (Exception ex)
            {
                Mod.Log(ex, LogLevel.Error);
            }
        }

        internal static bool IsValidParty(MobileParty __instance)
        {
            if (!__instance.Party.IsMobile ||
                __instance.CurrentSettlement != null ||
                __instance.Party.MemberRoster.TotalManCount == 0 ||
                __instance.IsCurrentlyUsedByAQuest ||
                __instance.IsTooBusyToMerge())
            {
                return false;
            }

            return __instance.IsBandit && !__instance.IsBanditBossParty;
        }

        // dumps all bandit heroes (shouldn't be more than 2 though...)
        internal static TroopRoster[] MergeRosters(MobileParty __instance, PartyBase targetParty)
        {
            var troopRoster = new TroopRoster();
            var faction = __instance.MapFaction;
            foreach (var troopRosterElement in __instance.MemberRoster
                .Where(x => x.Character?.HeroObject == null &&
                            Clan.BanditFactions.Contains(faction)))
            {
                Traverse.Create(troopRoster).Method("Add", troopRosterElement).GetValue();
            }

            foreach (var troopRosterElement in targetParty.MemberRoster
                .Where(x => x.Character?.HeroObject == null &&
                            Clan.BanditFactions.Contains(faction)))
            {
                Traverse.Create(troopRoster).Method("Add", troopRosterElement).GetValue();
            }

            var prisonerRoster = new TroopRoster();
            foreach (var troopRosterElement in __instance.PrisonRoster)
            {
                Traverse.Create(troopRoster).Method("Add", troopRosterElement).GetValue();
            }

            foreach (var troopRosterElement in targetParty.PrisonRoster)
            {
                Traverse.Create(troopRoster).Method("Add", troopRosterElement).GetValue();
            }

            return new[]
            {
                troopRoster,
                prisonerRoster
            };
        }

        internal static void FinalizeBadMapEvents()
        {
            if (MapEvents == null || MapEvents.Count == 0)
            {
                return;
            }

            foreach (var mapEvent in MapEvents.Where(x => x.EventType == MapEvent.BattleTypes.FieldBattle))
            {
                // bug added IsFinished, does it work?  I don't want it to clear parties that the game would be dealing with
                if (mapEvent.AttackerSide.TroopCount == 0 ||
                    mapEvent.DefenderSide.TroopCount == 0 &&
                    mapEvent.IsFinished)
                {
                    Mod.Log($"Removing bad field battle with {mapEvent.AttackerSide.LeaderParty.Name}, {mapEvent.DefenderSide.LeaderParty.Name}", LogLevel.Info);
                    mapEvent.FinalizeEvent();
                }
            }
        }

        public static AccessTools.FieldRef<IssueManager, Dictionary<Hero, IssueBase>> issuesRef =
            AccessTools.FieldRefAccess<IssueManager, Dictionary<Hero, IssueBase>>("_issues");

        internal static void PurgeNullRefDescriptionIssues(bool logOnlyMode = false)
        {
            var purgeList = new List<Hero>();
            foreach (var issue in Campaign.Current.IssueManager.Issues)
            {
                try
                {
                    var _ = issue.Value.Description;
                }
                catch (NullReferenceException)
                {
                    purgeList.Add(issue.Key);
                }
            }

            foreach (var heroKey in purgeList)
            {
                if (logOnlyMode)
                {
                    Mod.Log(heroKey.Issue, LogLevel.Debug);
                }
                else
                {
                    var issues = issuesRef(Campaign.Current.IssueManager);
                    Mod.Log($"Removing {heroKey} from IssueBase._issues as the Description is throwing NRE", LogLevel.Debug);
                    Debug.PrintError("Bandit Militias is removing {heroKey} from IssueBase._issues as the Description is throwing NRE");
                    issues.Remove(heroKey);
                }
            }
        }

        internal static void PurgeList(string logMessage, List<MobileParty> mobileParties)
        {
            if (mobileParties.Count > 0)
            {
                Mod.Log(logMessage, LogLevel.Debug);
                foreach (var mobileParty in mobileParties)
                {
                    mobileParty.RemoveParty();
                    mobileParty.LeaderHero?.KillHero();
                    mobileParty.Party.Visuals.SetMapIconAsDirty();
                }
            }

            mobileParties.Clear();
        }

        // todo move to Militia
        internal static void LogMilitiaFormed(MobileParty mobileParty)
        {
            var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
            var strengthString = $"{Math.Round(mobileParty.Party.TotalStrength)} strength";
            Mod.Log($"{"New Bandit Militia",-40} | {troopString,10} | {strengthString,10} |", LogLevel.Info);
        }

        private static bool IsTooBusyToMerge(this MobileParty mobileParty)
        {
            return mobileParty.TargetParty != null ||
                   mobileParty.ShortTermTargetParty != null ||
                   mobileParty.ShortTermBehavior == AiBehavior.EngageParty ||
                   mobileParty.DefaultBehavior == AiBehavior.EngageParty ||
                   mobileParty.ShortTermBehavior == AiBehavior.RaidSettlement ||
                   mobileParty.DefaultBehavior == AiBehavior.RaidSettlement ||
                   mobileParty.ShortTermBehavior == AiBehavior.BesiegeSettlement ||
                   mobileParty.DefaultBehavior == AiBehavior.BesiegeSettlement ||
                   mobileParty.ShortTermBehavior == AiBehavior.AssaultSettlement ||
                   mobileParty.DefaultBehavior == AiBehavior.AssaultSettlement;
        }

        internal static void Trash(MobileParty mobileParty)
        {
            if (mobileParty.LeaderHero != null)
            {
                Militia.FindMilitiaByParty(mobileParty)?.Remove();
                mobileParty.LeaderHero.KillHero();
            }

            mobileParty.RemoveParty();
        }

        internal static Stopwatch timer3 = new Stopwatch();

        // 0-2 milliseconds
        internal static void KillHero(this Hero hero)
        {
            try
            {
                hero.ChangeState(Hero.CharacterStates.Dead);
                hero.HeroDeveloper.ClearUnspentPoints();
                AccessTools.Method(typeof(CampaignEventDispatcher), "OnHeroKilled")
                    .Invoke(CampaignEventDispatcher.Instance, new object[] {hero, hero, KillCharacterAction.KillCharacterActionDetail.None, false});
                Traverse.Create(hero.CurrentSettlement).Field("_heroesWithoutParty").Method("Remove", hero).GetValue();
                Traverse.Create(hero.Clan).Field("_heroes").Method("Remove", hero).GetValue();
                MBObjectManager.Instance.UnregisterObject(hero.CharacterObject);
                MBObjectManager.Instance.UnregisterObject(hero);
            }
            catch (Exception ex)
            {
                Mod.Log(ex, LogLevel.Error);
            }
        }

        internal static bool IsAlone(this MobileParty mobileParty) => MobileParty.FindPartiesAroundPosition(
            mobileParty.Position2D, MergeDistance, x => x.IsBandit).Count(IsValidParty) == 2;

        internal static void Nuke()
        {
            Mod.Log("Clearing mod data.", LogLevel.Info);
            InformationManager.AddQuickInformation(new TextObject("BANDIT MILITIAS CLEARED"));
            FlushBanditMilitias();
            Flush();
        }

        private static void FlushBanditMilitias()
        {
            Militia.All.Clear();
            var tempList = MobileParty.All
                .Where(x => x.Name.Equals("Bandit Militia")).ToList();
            var hasLogged = false;
            foreach (var mobileParty in tempList)
            {
                if (!hasLogged)
                {
                    Mod.Log($"Clearing {tempList.Count} Bandit Militias", LogLevel.Info);
                    hasLogged = true;
                }

                Trash(mobileParty);
            }
        }

        internal static void Flush()
        {
            FixHomelessHeroes();
            FixBadSettlements();
            FlushNullPartyHeroes();
            FlushEmptyMilitiaParties();
            FlushNeutralBanditParties();
            FlushBadIssues();
            FlushBadHeroes();
            FlushBadCharacterObjects();
            FlushBadBehaviors();
            FinalizeBadMapEvents();
        }

        private static void FlushBadBehaviors()
        {
            bool hasLogged;
            var behaviors = (IDictionary) Traverse.Create(
                    Campaign.Current.CampaignBehaviorManager.GetBehavior<DynamicBodyCampaignBehavior>())
                .Field("_heroBehaviorsDictionary").GetValue();
            var heroes = new List<Hero>();
            foreach (var hero in behaviors.Keys)
            {
                if (!Hero.All.Contains(hero))
                {
                    heroes.Add((Hero) hero);
                }
            }

            hasLogged = false;
            foreach (var hero in heroes)
            {
                if (!hasLogged)
                {
                    hasLogged = true;
                    Mod.Log($"Clearing {heroes.Count} hero behaviors without heroes.", LogLevel.Debug);
                }

                behaviors.Remove(hero);
            }
        }

        private static void FlushNullPartyHeroes()
        {
            var heroes = Hero.All.Where(
                x => Clan.BanditFactions.Contains(x.MapFaction) && x.PartyBelongedTo == null).ToList();
            var hasLogged = false;
            foreach (var hero in heroes)
            {
                if (!hasLogged)
                {
                    hasLogged = true;
                    Mod.Log($"Clearing {heroes.Count} null-party heroes.", LogLevel.Debug);
                }

                hero.KillHero();
            }
        }

        private static void FixHomelessHeroes()
        {
            var homelessHeroes = Hero.All.Where(x => x.HomeSettlement == null);
            foreach (var hero in homelessHeroes)
            {
                Traverse.Create(hero).Property("HomeSettlement").SetValue(hero.BornSettlement);
            }
        }

        private static void FlushBadIssues()
        {
            bool hasLogged;
            var badIssues = Campaign.Current.IssueManager.Issues
                .Where(x => Clan.BanditFactions.Contains(x.Key.MapFaction)).ToList();
            hasLogged = false;
            foreach (var issue in badIssues)
            {
                if (!hasLogged)
                {
                    hasLogged = true;
                    Mod.Log($"Clearing {badIssues.Count} bad-issue heroes.", LogLevel.Info);
                }

                issue.Key.KillHero();
            }
        }

        private static void FixBadSettlements()
        {
            bool hasLogged;
            var badSettlements = Settlement.All
                .Where(x => x.IsHideout() && x.OwnerClan == null).ToList();

            hasLogged = false;
            foreach (var settlement in badSettlements)
            {
                if (!hasLogged)
                {
                    hasLogged = true;
                    Mod.Log($"Clearing {badSettlements.Count} bad settlements.", LogLevel.Info);
                }

                settlement.OwnerClan = Clan.BanditFactions.ToList()[Rng.Next(1, 5)];
            }
        }

        private static void FlushBadHeroes()
        {
            try
            {
                bool hasLogged;
                var badHeroes = Hero.All.Where(
                    x => !x.IsNotable && x.PartyBelongedTo == null && x.CurrentSettlement != null &&
                         x.MapFaction == CampaignData.NeutralFaction && x.EncyclopediaLink.Contains("CharacterObject")).ToList();
                hasLogged = false;
                foreach (var hero in badHeroes)
                {
                    if (!hasLogged)
                    {
                        hasLogged = true;
                        Mod.Log($"Clearing {badHeroes.Count()} bad heroes.", LogLevel.Debug);
                    }

                    if (Hero.All.Contains(hero))
                    {
                        hero.KillHero();
                    }
                }
            }
            catch (Exception ex)
            {
                Mod.Log(ex, LogLevel.Error);
            }
        }

        // todo FieldRef
        private static readonly AccessTools.FieldRef<Settlement, List<Hero>> heroesWithoutPartyRef =
            AccessTools.FieldRefAccess<Settlement, List<Hero>>("_heroesWithoutParty");

        private static void FlushBadCharacterObjects()
        {
            bool hasLogged;
            var badChars = CharacterObject.All.Where(
                x => x.Name == null || x.Occupation == Occupation.NotAssigned ||
                     x.Occupation == Occupation.Outlaw && x.HeroObject?.CurrentSettlement != null).ToList();

            hasLogged = false;
            foreach (var badChar in badChars)
            {
                if (!hasLogged)
                {
                    hasLogged = true;
                    Mod.Log($"Clearing {badChars.Count} bad characters.", LogLevel.Debug);
                }

                Traverse.Create(badChar?.HeroObject?.CurrentSettlement).Field("_heroesWithoutParty").Method("Remove", badChar?.HeroObject).GetValue();
                MBObjectManager.Instance.UnregisterObject(badChar);
            }
        }

        internal static void HourlyFlush()
        {
            FlushEmptyMilitiaParties();
            FlushNeutralBanditParties();
            FlushBadIssues();
            FlushBadHeroes();
            // todo may not be needed anymore
            FinalizeBadMapEvents();
        }

        private static void FlushNeutralBanditParties()
        {
            var tempList = new List<MobileParty>();
            foreach (var mobileParty in MobileParty.All
                .Where(x => x.Name.Equals("Bandit Militia") &&
                            x.MapFaction == CampaignData.NeutralFaction))
            {
                Mod.Log("This bandit shouldn't exist " + mobileParty + " size " + mobileParty.MemberRoster.TotalManCount, LogLevel.Debug);
                tempList.Add(mobileParty);
            }

            PurgeList($"CampaignHourlyTickPatch Clearing {tempList.Count} weird neutral parties", tempList);
        }

        private static List<MobileParty> FlushEmptyMilitiaParties()
        {
            var tempList = new List<MobileParty>();
            foreach (var mobileParty in MobileParty.All
                .Where(x => x.MemberRoster.TotalManCount == 0 && x.Name.Equals("Bandit Militia")))
            {
                tempList.Add(mobileParty);
            }

            PurgeList($"CampaignHourlyTickPatch Clearing {tempList.Count} empty parties", tempList);
            return tempList;
        }
        
        internal static Equipment MurderLordsForEquipment(Hero hero, bool randomizeWornEquipment)
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

            if (!randomizeWornEquipment)
            {
                return equipment[0];
            }
            
            var gear = new Equipment();
            for (var j = 0; j < 12; j++)
            {
                gear[j] = equipment[Rng.Next(0, 3)][j];
            }

            // get rid of any mount
            gear[10] = new EquipmentElement();
            EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, gear);
            return null;
        }
    }
}
