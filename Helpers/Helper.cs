using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using static Bandit_Militias.Helpers.Globals;

// ReSharper disable InconsistentNaming

namespace Bandit_Militias.Helpers
{
    public static class Globals
    {
        // dev
        internal static bool testingMode;

        // how close before merging
        internal const float MergeDistance = 2f;
        internal const float FindRadius = 10f;
        internal const float MinDistanceFromHideout = 15;

        // holders for criteria
        internal static float CalculatedHeroPartyStrength;
        internal static float CalculatedMaxPartyStrength;
        internal static double CalculatedMaxPartySize;

        // misc
        internal static readonly Random Rng = new Random();
        internal static List<Equipment> LordEquipment = new List<Equipment>();

        internal static Settings Settings;
        internal static readonly HashSet<Militia> Militias = new HashSet<Militia>();
        internal static List<Settlement> Hideouts = new List<Settlement>();

        internal static readonly Dictionary<string, int> DifficultyXpMap = new Dictionary<string, int>
        {
            {"LOW", 0},
            {"NORMAL", 300},
            {"HARD", 600},
            {"HARDER", 900},
        };

        internal static readonly Dictionary<string, int> GoldMap = new Dictionary<string, int>
        {
            {"LOW", 250},
            {"NORMAL", 500},
            {"RICH", 900},
            {"RICHER", 2000},
        };
    }

    public static class Helper
    {
        internal static int NumMountedTroops(TroopRoster troopRoster) => troopRoster.Troops
            .Where(x => x.IsMounted).Sum(troopRoster.GetTroopCount);

        private static float Variance => MBRandom.RandomFloatRanged(0.5f, 1.5f);

        internal static void CalcMergeCriteria()
        {
            // first campaign init hasn't populated this apparently
            var parties = MobileParty.All.Where(
                x => x.LeaderHero != null && !x.StringId.StartsWith("Bandit_Militia")).ToList();
            if (parties.Any())
            {
                CalculatedHeroPartyStrength = parties.Select(x => x.Party.TotalStrength).Average();
                // reduce strength
                CalculatedMaxPartyStrength = CalculatedHeroPartyStrength * Globals.Settings.PartyStrengthFactor * Variance;
                // maximum size grows over time as clans level up
                CalculatedMaxPartySize = Math.Round(MobileParty.All
                    .Where(x => x.LeaderHero != null && !x.IsBandit).Select(x => x.Party.PartySizeLimit).Average());
                CalculatedMaxPartySize *= Globals.Settings.MaxPartySizeFactor * Variance;
                Mod.Log($"Daily calculations => size: {CalculatedMaxPartySize:0} strength: {CalculatedMaxPartyStrength:0}");
            }
        }

        internal static void TrySplitParty(MobileParty __instance)
        {
            if (!__instance.StringId.StartsWith("Bandit_Militia") ||
                __instance.Party.MemberRoster.TotalManCount < 50 ||
                __instance.IsTooBusyToMerge())
            {
                return;
            }

            var roll = Rng.NextDouble();
            if (__instance.MemberRoster.TotalManCount == 0 ||
                roll > Globals.Settings.RandomSplitChance ||
                !__instance.StringId.StartsWith("Bandit_Militia") ||
                __instance.Party.TotalStrength <= CalculatedMaxPartyStrength * Globals.Settings.StrengthSplitFactor * Variance ||
                __instance.Party.MemberRoster.TotalManCount <= CalculatedMaxPartySize * Globals.Settings.SizeSplitFactor * Variance)
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
            Mod.Log($"Processing troops: {original.MemberRoster.Count} types, {original.MemberRoster.TotalManCount} in total");
            foreach (var rosterElement in original.MemberRoster.Where(x => x.Character.HeroObject == null))
            {
                SplitRosters(troops1, troops2, rosterElement);
            }

            if (original.PrisonRoster.TotalManCount > 0)
            {
                Mod.Log($"Processing prisoners: {original.PrisonRoster.Count} types, {original.PrisonRoster.TotalManCount} in total");
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
                var militia1 = new Militia(original, party1, prisoners1);
                var militia2 = new Militia(original, party2, prisoners2);
                Mod.Log($"{militia1.MobileParty.MapFaction.Name} <<< Split >>> {militia2.MobileParty.MapFaction.Name}");
                Traverse.Create(militia1.MobileParty.Party).Property("ItemRoster").SetValue(inventory1);
                Traverse.Create(militia2.MobileParty.Party).Property("ItemRoster").SetValue(inventory2);
                militia1.MobileParty.Party.Visuals.SetMapIconAsDirty();
                militia2.MobileParty.Party.Visuals.SetMapIconAsDirty();
                var warParties = Traverse.Create(original.ActualClan).Field("_warParties").GetValue<List<MobileParty>>();
                while (warParties.Contains(original))
                {
                    // it's been added twice... at least.  for Reasons?
                    warParties.Remove(original);
                }

                Trash(original);
                //Mod.Log("After split " + MobileParty.All.Count(x => x.MemberRoster.Count == 0 && x.HomeSettlement == null));
            }
            catch (Exception ex)
            {
                Mod.Log(ex);
            }
        }

        internal static bool IsValidParty(MobileParty __instance)
        {
            if (!__instance.IsBandit ||
                !__instance.Party.IsMobile ||
                __instance.CurrentSettlement != null ||
                __instance.Party.MemberRoster.TotalManCount == 0 ||
                __instance.IsCurrentlyUsedByAQuest ||
                __instance.IsTooBusyToMerge())
            {
                return false;
            }

            return __instance.IsBandit && !__instance.IsBanditBossParty;
        }

        internal static TroopRoster[] MergeRosters(MobileParty sourceParty, PartyBase targetParty)
        {
            var troopRoster = new TroopRoster();
            var prisonerRoster = new TroopRoster();
            var rosters = new List<TroopRoster>
            {
                sourceParty.MemberRoster,
                targetParty.MemberRoster
            };

            var prisoners = new List<TroopRoster>
            {
                sourceParty.PrisonRoster,
                targetParty.PrisonRoster
            };

            // dumps all bandit heroes (shouldn't be more than 2 though...)
            foreach (var roster in rosters)
            {
                foreach (var troopRosterElement in roster.Where(x => x.Character?.HeroObject == null))
                {
                    troopRoster.AddToCounts(troopRosterElement.Character, troopRosterElement.Number, woundedCount: troopRosterElement.WoundedNumber, xp: troopRosterElement.Xp);
                }
            }

            foreach (var roster in prisoners)
            {
                foreach (var troopRosterElement in roster.Where(x => x.Character?.HeroObject == null))
                {
                    prisonerRoster.AddToCounts(troopRosterElement.Character, troopRosterElement.Number, woundedCount: troopRosterElement.WoundedNumber, xp: troopRosterElement.Xp);
                }
            }

            return new[]
            {
                troopRoster,
                prisonerRoster
            };
        }

        private static void PurgeList(string logMessage, List<MobileParty> mobileParties)
        {
            if (mobileParties.Count > 0)
            {
                Mod.Log(logMessage);
                foreach (var mobileParty in mobileParties)
                {
                    Trash(mobileParty);
                }
            }
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
            Militias.Remove(Militia.FindMilitiaByParty(mobileParty));
            mobileParty.LeaderHero?.KillHero();
            mobileParty.RemoveParty();
        }

        internal static void KillHero(this Hero hero)
        {
            try
            {
                hero.ChangeState(Hero.CharacterStates.NotSpawned);
                hero.HeroDeveloper.ClearUnspentPoints();
                AccessTools.Method(typeof(CampaignEventDispatcher), "OnHeroKilled")
                    .Invoke(CampaignEventDispatcher.Instance, new object[] {hero, hero, KillCharacterAction.KillCharacterActionDetail.None, false});
                // no longer needed without registered heroes but leaving in for a few extra versions...
                Traverse.Create(hero.CurrentSettlement).Field("_heroesWithoutParty").Method("Remove", hero).GetValue();
                MBObjectManager.Instance.UnregisterObject(hero);
                MBObjectManager.Instance.UnregisterObject(hero.CharacterObject);
            }
            catch (Exception ex)
            {
                Mod.Log(ex, LogLevel.Error);
            }
        }

        internal static void Nuke()
        {
            Mod.Log("Clearing mod data.", LogLevel.Info);
            FlushBanditMilitias();
            Flush();
            InformationManager.AddQuickInformation(new TextObject("BANDIT MILITIAS CLEARED"));
        }

        private static void FlushBanditMilitias()
        {
            Militias.Clear();
            var tempList = MobileParty.All.Where(x => x.StringId.StartsWith("Bandit_Militia")).ToList();
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

        internal static void ReHome()
        {
            var tempList = Militias.Where(x => x?.Hero?.HomeSettlement == null).Select(x => x.Hero).ToList();
            Mod.Log($"Fixing {tempList.Count} null HomeSettlement heroes");
            tempList.Do(x => Traverse.Create(x).Field("_homeSettlement").SetValue(Hideouts.GetRandomElement()));
        }

        internal static void Flush()
        {
            FlushHideoutsOfMilitias();
            FlushNullPartyHeroes();
            FlushEmptyMilitiaParties();
            FlushNeutralBanditParties();
            FlushBadCharacterObjects();
            FlushBadBehaviors();
            FlushMapEvents();
        }

        private static void FlushMapEvents()
        {
            var mapEvents = Traverse.Create(Campaign.Current.MapEventManager).Field("mapEvents").GetValue<List<MapEvent>>();
            for (var index = 0; index < mapEvents.Count; index++)
            {
                var mapEvent = mapEvents[index];
                if (mapEvent.InvolvedParties.Any(x =>
                    x.MobileParty != null &&
                    x.MobileParty.StringId.StartsWith("Bandit_Militia")))
                {
                    mapEvent.FinalizeEvent();
                }
            }
        }

        private static void FlushHideoutsOfMilitias()
        {
            foreach (var hideout in Settlement.All.Where(x => x.IsHideout()).ToList())
            {
                for (var index = 0; index < hideout.Parties.Count; index++)
                {
                    var party = hideout.Parties[index];
                    if (party.StringId.StartsWith("Bandit_Militia"))
                    {
                        LeaveSettlementAction.ApplyForParty(party);
                        party.SetMovePatrolAroundSettlement(hideout);
                    }
                }
            }
        }

        private static void FlushBadBehaviors()
        {
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

            var hasLogged = false;
            foreach (var hero in heroes)
            {
                if (!hasLogged)
                {
                    hasLogged = true;
                    Mod.Log($"Clearing {heroes.Count} hero behaviors without heroes.", LogLevel.Info);
                }

                behaviors.Remove(hero);
            }
        }

        // todo Patch Hero.Encyclopedia link et al to not have data
        private static void FlushNullPartyHeroes()
        {
            var heroes = Hero.All.Where(x =>
                x.Name.ToString() == "Bandit Militia" && x.PartyBelongedTo == null).ToList();
            var hasLogged = false;
            foreach (var hero in heroes)
            {
                if (!hasLogged)
                {
                    hasLogged = true;
                    Mod.Log($"Killing {heroes.Count} null-party heroes.", LogLevel.Info);
                }

                Mod.Log("Killing " + hero);
                hero.KillHero();
            }
        }

        private static void FlushBadCharacterObjects()
        {
            var badChars = CharacterObject.All.Where(x => x.HeroObject == null)
                .Where(x =>
                    x.Name == null ||
                    x.Occupation == Occupation.NotAssigned ||
                    x.Occupation == Occupation.Outlaw &&
                    x.HeroObject?.CurrentSettlement != null)
                .Where(x =>
                    !x.StringId.Contains("template") &&
                    !x.StringId.Contains("char_creation") &&
                    !x.StringId.Contains("equipment") &&
                    !x.StringId.Contains("for_perf") &&
                    !x.StringId.Contains("dummy") &&
                    !x.StringId.Contains("npc_") &&
                    !x.StringId.Contains("unarmed_ai"))
                .ToList();

            var hasLogged = false;
            foreach (var badChar in badChars)
            {
                if (!hasLogged)
                {
                    hasLogged = true;
                    Mod.Log($"Unregistering {badChars.Count} bad characters.", LogLevel.Info);
                }

                Mod.Log(badChar, LogLevel.Info);
                Traverse.Create(badChar?.HeroObject?.CurrentSettlement)
                    .Field("_heroesWithoutParty").Method("Remove", badChar?.HeroObject).GetValue();
                MBObjectManager.Instance.UnregisterObject(badChar);
                CharacterObject.All.Contains(badChar);
            }
        }

        private static void FlushNeutralBanditParties()
        {
            var tempList = new List<MobileParty>();
            foreach (var mobileParty in MobileParty.All.Where(x =>
                x.StringId.StartsWith("Bandit_Militia") &&
                x.MapFaction == CampaignData.NeutralFaction))
            {
                Mod.Log("This bandit shouldn't exist " + mobileParty + " size " + mobileParty.MemberRoster.TotalManCount, LogLevel.Info);
                tempList.Add(mobileParty);
            }

            PurgeList($"CampaignHourlyTickPatch Clearing {tempList.Count} weird neutral parties", tempList);
        }

        private static void FlushEmptyMilitiaParties()
        {
            var tempList = new List<MobileParty>();
            foreach (var mobileParty in MobileParty.All
                .Where(x => x.MemberRoster.TotalManCount == 0 && x.StringId.StartsWith("Bandit_Militia")))
            {
                tempList.Add(mobileParty);
            }

            PurgeList($"CampaignHourlyTickPatch Clearing {tempList.Count} empty parties", tempList);
        }

        internal static Equipment CreateEquipment()
        {
            var gear = new Equipment();
            for (var j = 0; j < 10; j++)
            {
                var piece = LordEquipment.GetRandomElement()[j];
                while (j < 4 && piece.Item != null && piece.Item?.PrimaryWeapon == null)
                {
                    // this is never reached luckily.. troubleshooting code leaving in just case
                    piece = LordEquipment.GetRandomElement()[j];
                }

                gear[j] = new EquipmentElement(piece);
            }

            // get rid of any mount
            gear[10] = new EquipmentElement();
            gear[11] = new EquipmentElement();
            return gear;
        }

        internal static bool IsMovingToBandit(MobileParty mobileParty, MobileParty other)
        {
            return mobileParty.MoveTargetParty != null &&
                   mobileParty.MoveTargetParty == other;
        }

        internal static string Possess(string input)
        {
            // game tries to redraw the PartyNamePlateVM after combat with multiple militias
            // and crashes because __instance.Party.LeaderHero?.FirstName.ToString() is null
            if (input == null)
            {
                return null;
            }

            var lastChar = input[input.Length - 1];
            return $"{input}{(lastChar == 's' ? "'" : "'s")}";
        }
    }
}
