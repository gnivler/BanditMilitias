using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;
using static Bandit_Militias.Helper.Globals;

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
            HeroPartyStrength = MobileParty.All.Where(x => x.LeaderHero != null && !x.Name.Equals("Bandit Militia"))
                .Select(x => x.Party.TotalStrength).Average();
            // reduce strength
            MaxPartyStrength = HeroPartyStrength * PartyStrengthFactor * Variance;
            // maximum size grows over time as clans level up
            MaxPartySize = Math.Round(MobileParty.All
                .Where(x => x.LeaderHero != null && !x.IsBandit).Select(x => x.Party.PartySizeLimit).Average());
            MaxPartySize *= MaxPartySizeFactor * Variance;
            Mod.Log($"Daily calculations => size: {MaxPartySize:0} strength: {MaxPartyStrength:0}", LogLevel.Debug);
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

        // TODO hopefully figure out where this shit is coming from
        // I think now it might be because defeated parties
        // seems to occur more often with lots of adjacent merges like in testing mode
        internal static void KillNullPartyHeroes()
        {
            var heroes = Hero.All.Where(x => Clan.BanditFactions.Contains(x.MapFaction) &&
                                             x.PartyBelongedTo == null).ToList();
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

            var homelessHeroes = Hero.All.Where(x => x.HomeSettlement == null);
            foreach (var hero in homelessHeroes)
            {
                Traverse.Create(hero).Property("HomeSettlement").SetValue(hero.BornSettlement);
            }
        }


        internal static void FinalizeBadMapEvents()
        {
            if (MapEvents.Count == 0)
            {
                return;
            }

            foreach (var mapEvent in MapEvents.Where(x => x.EventType == MapEvent.BattleTypes.FieldBattle))
            {
                if (mapEvent.AttackerSide.TroopCount == 0 ||
                    mapEvent.DefenderSide.TroopCount == 0)
                {
                    Mod.Log($"Removing bad field battle with {mapEvent.AttackerSide.LeaderParty.Name}, {mapEvent.DefenderSide.LeaderParty.Name}", LogLevel.Info);
                    mapEvent.FinalizeEvent();
                }
            }
        }

        internal static void PurgeNullRefDescriptionIssues(bool logOnlyMode = false)
        {
            var purgeList = new List<Hero>();
            foreach (var issue in Campaign.Current.IssueManager.Issues)
            {
                try
                {
                    var _ = issue.Value.Description;
                }
                catch (Exception ex)
                {
                    if (ex is NullReferenceException)
                    {
                        purgeList.Add(issue.Key);
                    }
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
                    var issues = Traverse.Create(Campaign.Current.IssueManager)
                        .Field("_issues").GetValue<Dictionary<Hero, IssueBase>>();
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
                    mobileParty.Party.Visuals.SetMapIconAsDirty();
                    mobileParty.LeaderHero.KillHero();
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
            MBObjectManager.Instance.UnregisterObject(mobileParty);
        }

        internal static Exception SuppressingFinalizer() => null;

        internal static void KillHero(this Hero hero)
        {
            Traverse.Create(typeof(KillCharacterAction))
                .Method("MakeDead", hero).GetValue();
            MBObjectManager.Instance.UnregisterObject(hero);
        }

        internal static bool IsAlone(this MobileParty mobileParty)
        {
            return MobileParty.FindPartiesAroundPosition(mobileParty.Position2D, MergeDistance, x=> x.IsBandit).Count(IsValidParty) == 2;
        }
    }
}
