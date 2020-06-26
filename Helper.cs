using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;
using static Bandit_Militias.Mod;
using static Bandit_Militias.Helper.Globals;

// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    public static class Helper
    {
        public static class Globals
        {
            // dev
            internal static bool testingMode = false;

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
            internal const float minPartyStrengthFactor = 0.6f;
            internal const float maxPartyStrengthFactor = 0.8f;
            internal const float maxPartySizeFactor = 0.8f;

            // holders for criteria
            internal static float HeroPartyStrength;
            internal static float MaxPartyStrength;
            internal static double AvgHeroPartyMaxSize;

            // misc
            internal static readonly Random Rng = new Random();
            internal static List<MapEvent> MapEvents;

            // collections
            internal static List<MobileParty> TempList = new List<MobileParty>();
        }

        internal static int GetMountedTroopHeadcount(TroopRoster troopRoster)
        {
            return troopRoster.Troops.Where(x => x.IsMounted)
                .Sum(troopRoster.GetTroopCount);
        }

        internal static void CalcMergeCriteria()
        {
            HeroPartyStrength = MobileParty.All.Where(x => x.LeaderHero != null && !x.Name.Equals("Bandit Militia"))
                .Select(x => x.Party.TotalStrength).Average();
            // reduce strength
            MaxPartyStrength = HeroPartyStrength * MBRandom.RandomFloatRanged(minPartyStrengthFactor, maxPartyStrengthFactor);
            // maximum size grows over time as clans level up
            AvgHeroPartyMaxSize = Math.Round(MobileParty.All
                .Where(x => x.LeaderHero != null && !x.IsBandit).Select(x => x.Party.PartySizeLimit).Average());
            AvgHeroPartyMaxSize *= maxPartySizeFactor;
            Trace($"Daily calculations => size: {AvgHeroPartyMaxSize:0} strength: {MaxPartyStrength:0}");
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
                __instance.Party.TotalStrength <= MaxPartyStrength * StrengthSplitFactor ||
                __instance.Party.MemberRoster.TotalManCount <= AvgHeroPartyMaxSize * SizeSplitFactor)
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
                Trace($"Processing troops: {original.MemberRoster.Count} types, {original.MemberRoster.TotalManCount} in total");
                foreach (var rosterElement in original.MemberRoster)
                {
                    SplitRosters(troops1, troops2, rosterElement);
                }

                Trace("party1.TotalManCount " + troops1.TotalManCount);
                Trace("party2.TotalManCount " + troops2.TotalManCount);

                if (original.PrisonRoster.TotalManCount > 0)
                {
                    // gross copy paste
                    Trace($"Processing prisoners: {original.PrisonRoster.Count} types, {original.PrisonRoster.TotalManCount} in total");
                    foreach (var rosterElement in original.PrisonRoster)
                    {
                        SplitRosters(prisoners1, prisoners2, rosterElement);
                    }
                }

                Trace("prisoners1.TotalManCount " + prisoners1.TotalManCount);
                Trace("prisoners2.TotalManCount " + prisoners2.TotalManCount);

                foreach (var item in original.ItemRoster)
                {
                    var half = Math.Max(1, item.Amount / 2);
                    inventory1.AddToCounts(item.EquipmentElement, half);
                    var remainder = item.Amount % 2;
                    if (half > 2)
                    {
                        inventory2.AddToCounts(item.EquipmentElement, Math.Min(1, item.Amount / 2 + remainder));
                    }
                }
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }

        private static void SplitRosters(TroopRoster troops1, TroopRoster troops2, TroopRosterElement rosterElement)
        {
            //var hero = militia.MobileParty.MemberRoster.GetCharacterAtIndex(0);
            //militia.MobileParty.MemberRoster.RemoveTroop(hero);


            var half = Math.Max(1, rosterElement.Number / 2);
            troops1.AddToCounts(rosterElement.Character, half);
            var remainder = rosterElement.Number % 2;
            // 1-3 will have a half of 1.  4 would be 2, and worth adding to second roster
            if (half > 2)
            {
                troops2.AddToCounts(rosterElement.Character, Math.Max(1, rosterElement.Number / 2 + remainder));
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
                Log($"{militia1.MobileParty.MapFaction.Name} <<< Split >>> {militia2.MobileParty.MapFaction.Name}");
                LogMilitiaFormed(militia1.MobileParty.Party.MobileParty);
                LogMilitiaFormed(militia2.MobileParty.Party.MobileParty);
                militia1.MobileParty.Party.Visuals.SetMapIconAsDirty();
                militia2.MobileParty.Party.Visuals.SetMapIconAsDirty();
                Trash(original);
            }
            catch (Exception e)
            {
                Log(e);
            }
        }

        internal static bool IsValidParty(MobileParty __instance)
        {
            if (!__instance.Party.IsMobile ||
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
                    Log($"Clearing {heroes.Count} null-party heroes.");
                }

                Traverse.Create(typeof(KillCharacterAction))
                    .Method("MakeDead", hero).GetValue();
                MBObjectManager.Instance.UnregisterObject(hero);
            }
        }


        internal static void FinalizeBadMapEvents()
        {
            if (MapEvents.Count == 0)
            {
                Trace("No map events to finalize");
                return;
            }

            foreach (var mapEvent in MapEvents.Where(x => x.EventType == MapEvent.BattleTypes.FieldBattle))
            {
                if (mapEvent.AttackerSide.TroopCount == 0 ||
                    mapEvent.DefenderSide.TroopCount == 0)
                {
                    Log($"Removing bad field battle with {mapEvent.AttackerSide.LeaderParty.Name}, {mapEvent.DefenderSide.LeaderParty.Name}");
                    mapEvent.FinalizeEvent();
                }
                else
                {
                    Trace($"Leaving valid field battle with {mapEvent.AttackerSide.LeaderParty.Name}, {mapEvent.DefenderSide.LeaderParty.Name}");
                }
            }
        }

        internal static void PurgeList(string message)
        {
            if (TempList.Count > 0)
            {
                Log(message);
                foreach (var mobileParty in TempList)
                {
                    mobileParty.RemoveParty();
                    mobileParty.Party.Visuals.SetMapIconAsDirty();
                    Traverse.Create(typeof(KillCharacterAction))
                        .Method("MakeDead", mobileParty.LeaderHero).GetValue();
                    MBObjectManager.Instance.UnregisterObject(mobileParty.LeaderHero);
                }

                TempList.Clear();
            }
        }

        internal static void LogMilitiaFormed(MobileParty mobileParty)
        {
            var banditString = $"{mobileParty.Name} forms led by {mobileParty.LeaderHero}";
            var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
            var strengthString = $"{Math.Round(mobileParty.Party.TotalStrength)} strength";
            Log($"{banditString,-55} | {troopString,12} | {strengthString,12} |");
        }

        internal static bool IsBetween(this int original, int min, int max)
        {
            return original <= max && original >= min;
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
                Traverse.Create(typeof(KillCharacterAction)).Method("MakeDead", mobileParty.LeaderHero).GetValue();
                MBObjectManager.Instance.UnregisterObject(mobileParty.LeaderHero);
            }

            mobileParty.RemoveParty();
            MBObjectManager.Instance.UnregisterObject(mobileParty);
        }
    }
}
