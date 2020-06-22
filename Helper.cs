using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;
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
            // how far to look
            internal const float SearchRadius = 25;

            // how close before merging
            internal const float MergeDistance = 2;
            internal const float MinDistanceFromHideout = 15;

            // thresholds for splitting
            internal const float StrengthSplitFactor = 0.8f;
            internal const float SizeSplitFactor = 0.8f;
            internal const float RandomSplitChance = 0.1f;

            // adjusts size and strengths
            internal const float minPartyStrengthFactor = 0.6f;
            internal const float maxPartyStrengthFactor = 0.8f;
            internal const float maxPartySizeFactor = 0.8f;

            // gold for troop training and player reward - much less is received
            internal const int minGoldGift = 2000;
            internal const int maxGoldGift = 6000;

            // holders for criteria
            internal static float HeroPartyStrength;
            internal static float MaxPartyStrength;
            internal static double AvgHeroPartyMaxSize;

            // misc
            internal static readonly Random Rng = new Random();
            internal static string LastConversationName = "";
            internal static MapEventManager MapEventManager;

            // collections
            internal static List<MobileParty> QuestParties = new List<MobileParty>();
            internal static List<MobileParty> tempList = new List<MobileParty>();
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

            //var roll = Rng.NextDouble();
            //if (__instance.MemberRoster.TotalManCount == 0 ||
            //    !__instance.Name.Equals("Bandit Militia") ||
            //    roll > RandomSplitChance ||
            //    __instance.Party.TotalStrength <= MaxPartyStrength * StrengthSplitFactor ||
            //    __instance.Party.MemberRoster.TotalManCount <= AvgHeroPartyMaxSize * SizeSplitFactor)
            //{
            //    return;
            //}

            //Trace($"roll {roll} > RandomSplitChance {RandomSplitChance}");
            //Trace($"__instance.Party.TotalStrength {__instance.Party.TotalStrength} > MaxPartyStrength * StrengthSplitFactor {MaxPartyStrength * StrengthSplitFactor}");
            //Trace($"__instance.Party.MemberRoster.TotalManCount {__instance.Party.MemberRoster.TotalManCount} > AvgHeroPartyMaxSize * SizeSplitFactor {AvgHeroPartyMaxSize * SizeSplitFactor}");
            //Trace("Splitting " + __instance.Name);
            var party1 = new TroopRoster();
            var party2 = new TroopRoster();
            var prisoners1 = new TroopRoster();
            var prisoners2 = new TroopRoster();
            var inventory1 = new ItemRoster();
            var inventory2 = new ItemRoster();
            SplitRosters(__instance, party1, party2, prisoners1, prisoners2, inventory1, inventory2);
            CreateNewMilitias(__instance, party1, party2, prisoners1, prisoners2, inventory1, inventory2);
        }

        private static void SplitRosters(MobileParty __instance, TroopRoster party1, TroopRoster party2,
            TroopRoster prisoners1, TroopRoster prisoners2, ItemRoster inventory1, ItemRoster inventory2)
        {
            try
            {
                Trace($"Processing troops: {__instance.MemberRoster.Count} types, {__instance.MemberRoster.TotalManCount} in total");
                foreach (var rosterElement in __instance.MemberRoster)
                {
                    rosterElement.Character?.HeroObject?.RenameHeroBackIfNeeded();
                    var half = Math.Max(1, rosterElement.Number / 2);
                    party1.AddToCounts(rosterElement.Character, half);
                    var remainder = rosterElement.Number % 2;
                    // 1-3 will have a half of 1.  4 would be 2, and worth adding to second roster
                    if (half > 2)
                    {
                        party2.AddToCounts(rosterElement.Character, Math.Max(1, rosterElement.Number / 2 + remainder));
                    }
                }

                Trace("party1.TotalManCount " + party1.TotalManCount);
                Trace("party2.TotalManCount " + party2.TotalManCount);

                if (__instance.PrisonRoster.TotalManCount > 0)
                {
                    // gross copy paste
                    Trace($"Processing prisoners: {__instance.PrisonRoster.Count} types, {__instance.PrisonRoster.TotalManCount} in total");
                    foreach (var rosterElement in __instance.PrisonRoster)
                    {
                        rosterElement.Character?.HeroObject?.RenameHeroBackIfNeeded();
                        var half = Math.Max(1, rosterElement.Number / 2);
                        prisoners1.AddToCounts(rosterElement.Character, half);
                        var remainder = rosterElement.Number % 2;
                        // 1-3 will have a half of 1.  4 would be 2, and worth adding to second roster
                        if (half > 2)
                        {
                            prisoners2.AddToCounts(rosterElement.Character, Math.Min(1, rosterElement.Number / 2 + remainder));
                        }
                    }
                }

                Trace("prisoners1.TotalManCount " + prisoners1.TotalManCount);
                Trace("prisoners2.TotalManCount " + prisoners2.TotalManCount);

                foreach (var item in __instance.ItemRoster)
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

        private static void CreateNewMilitias(MobileParty original, TroopRoster party1, TroopRoster party2,
            TroopRoster prisoners1, TroopRoster prisoners2, ItemRoster inventory1, ItemRoster inventory2)
        {
            try
            {
                var mobileParty1 = MBObjectManager.Instance.CreateObject<MobileParty>("Bandit_Militia");
                mobileParty1.InitializeMobileParty(
                    new TextObject("Bandit Militia"),
                    party1,
                    prisoners1,
                    original.Position2D,
                    2f);

                var mobileParty2 = MBObjectManager.Instance.CreateObject<MobileParty>("Bandit_Militia");
                mobileParty2.InitializeMobileParty(
                    new TextObject("Bandit Militia"),
                    party2,
                    prisoners2,
                    original.Position2D,
                    2f);

                var originalClan = original.LeaderHero?.Clan;
                original.RemoveParty();
                original.Party.Visuals.SetMapIconAsDirty();

                Traverse.Create(mobileParty1.Party).Property("ItemRoster").SetValue(inventory1);
                Traverse.Create(mobileParty2.Party).Property("ItemRoster").SetValue(inventory2);

                var banditHero = mobileParty2.LeaderHero ??
                                 HeroCreator.CreateHeroAtOccupation(Occupation.Outlaw);
                // active setter
                mobileParty1.Party.Owner = banditHero;
                mobileParty1.MemberRoster.AddToCounts(banditHero.CharacterObject, 1, true);
                Traverse.Create(mobileParty1.Party.Leader).Field("_heroObject").SetValue(banditHero);
                Trace(mobileParty1.Party.Leader == null);
                Trace(mobileParty1.Party.LeaderHero == null);
                mobileParty1.ChangePartyLeader(mobileParty1.Leader);
                if (mobileParty1.LeaderHero == null)
                {
                    throw new NullReferenceException("mobileParty1.LeaderHero == null");
                }

                // TODO refactor with Militia/Patches.cs, too much repetition
                // skip 0 Looters
                mobileParty1.LeaderHero.Clan = originalClan ?? Clan.BanditFactions.ToList()[Rng.Next(1, 5)];
                Traverse.Create(mobileParty1.LeaderHero.Clan).Method("RemoveHero", mobileParty1.LeaderHero);
                var hideout = Settlement.FindAll(x => x.IsHideout() &&
                                                      x.MapFaction != CampaignData.NeutralFaction).GetRandomElement();
                Traverse.Create(banditHero).Property("HomeSettlement").SetValue(hideout);

                banditHero = mobileParty2.LeaderHero ?? HeroCreator.CreateHeroAtOccupation(Occupation.Outlaw);
                mobileParty2.Party.Owner = banditHero;
                mobileParty2.MemberRoster.AddToCounts(banditHero.CharacterObject, 1, true);
                Traverse.Create(mobileParty2.Party.Leader).Field("_heroObject").SetValue(banditHero);
                mobileParty2.ChangePartyLeader(banditHero.CharacterObject);
                if (mobileParty2.LeaderHero == null)
                {
                    throw new NullReferenceException("mobileParty2.LeaderHero == null");
                }

                // skip 0 Looters
                mobileParty2.LeaderHero.Clan = banditHero?.Clan ?? Clan.BanditFactions.ToList()[Rng.Next(1, 5)];
                Traverse.Create(mobileParty2.LeaderHero.Clan).Method("RemoveHero", mobileParty2.LeaderHero);
                hideout = Settlement.FindAll(x => x.IsHideout() &&
                                                  x.MapFaction != CampaignData.NeutralFaction).GetRandomElement();
                Traverse.Create(banditHero).Property("HomeSettlement").SetValue(hideout);

                Log("<<< Split >>>");
                Log($"{mobileParty1.MapFaction} <<>> {mobileParty2.MapFaction}");
                LogMilitiaFormed(mobileParty1);
                LogMilitiaFormed(mobileParty2);
                mobileParty1.LeaderHero.Name = new TextObject("Bandit Militia");
                mobileParty2.LeaderHero.Name = new TextObject("Bandit Militia");
                mobileParty1.Party.Visuals.SetMapIconAsDirty();
                mobileParty2.Party.Visuals.SetMapIconAsDirty();
            }
            catch (Exception e)
            {
                Log(e);
            }
        }

        internal static bool IsValidParty(MobileParty __instance)
        {
            if (__instance.IsGarrison ||
                __instance.IsMilitia ||
                __instance.Party.MemberRoster.TotalManCount == 0 ||
                __instance.IsCurrentlyUsedByAQuest ||
                __instance.IsTooBusyToMerge())
            {
                return false;
            }

            return __instance.IsBandit && !__instance.IsBanditBossParty;
        }

        internal static void MergeParties(MobileParty __instance, PartyBase targetParty, MobileParty mobileParty)
        {
            var party1NoHeroes = __instance.MemberRoster.Where(x => !x.Character.IsHero);
            var party2NoHeroes = targetParty.MemberRoster.Where(x => !x.Character.IsHero);

            var troopRoster = new TroopRoster();
            foreach (var troopRosterElement in party1NoHeroes)
            {
                Traverse.Create(troopRoster).Method("Add", troopRosterElement).GetValue();
            }

            var prisonerRoster = new TroopRoster();
            foreach (var troopRosterElement in party2NoHeroes)
            {
                Traverse.Create(troopRoster).Method("Add", troopRosterElement).GetValue();
            }

            mobileParty.InitializeMobileParty(
                new TextObject("Bandit Militia"),
                troopRoster,
                prisonerRoster,
                __instance.Position2D,
                2f);
        }


        internal static Hero SelectBanditHero(MobileParty __instance, PartyBase targetParty,
            MobileParty mobileParty, out Clan existingClan)
        {
            // set the new leader and move the lower-level hero to the party and give them back their name suffix
            Hero banditHero;
            if (__instance.LeaderHero.Level > targetParty.LeaderHero.Level)
            {
                // set the hero and fix the name, add to roster
                Trace("This hero is higher level than target hero");
                mobileParty.ItemRoster.Add(__instance.ItemRoster);
                banditHero = __instance.LeaderHero;
                existingClan = __instance.LeaderHero.Clan;
                mobileParty.HomeSettlement = __instance.HomeSettlement;
                targetParty.LeaderHero.RenameHeroBackIfNeeded();
                Trace($"Absorbing hero: {targetParty.LeaderHero.Name}");
                mobileParty.MemberRoster.AddToCounts(targetParty.LeaderHero.CharacterObject, 1);
            }
            else
            {
                Trace("This hero is lower level than target hero");
                mobileParty.ItemRoster.Add(targetParty.ItemRoster);
                banditHero = targetParty.LeaderHero;
                existingClan = targetParty.LeaderHero.Clan;
                mobileParty.HomeSettlement = targetParty.MobileParty.HomeSettlement;
                __instance.LeaderHero.RenameHeroBackIfNeeded();
                Trace($"Absorbing hero: {__instance.LeaderHero.Name}");
                mobileParty.MemberRoster.AddToCounts(__instance.LeaderHero.CharacterObject, 1);
            }

            return banditHero;
        }
        
        internal static void FinalizeBadMapEvents(List<MapEvent> mapEvents)
        {
            if (mapEvents == null)
            {
                Trace("No map events to finalize");
                return;
            }
            
            foreach (var mapEvent in mapEvents.Where(x => x.EventType == MapEvent.BattleTypes.FieldBattle))
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

        internal static void RenameHeroBackIfNeeded(this Hero hero)
        {
            var suffix = Traverse.Create(NameGenerator.Current)
                .Field("_suffixes").GetValue<string[]>().GetRandomElement();
            if (hero.Name.Equals("Bandit Militia"))
            {
                hero.Name = new TextObject($"{hero.FirstName}{suffix}");
            }
        }

        private static bool IsTooBusyToMerge(this MobileParty mobileParty)
        {
            return mobileParty.TargetParty != null ||
                   mobileParty.ShortTermBehavior == AiBehavior.EngageParty ||
                   mobileParty.ShortTermBehavior == AiBehavior.RaidSettlement ||
                   mobileParty.ShortTermBehavior == AiBehavior.BesiegeSettlement ||
                   mobileParty.ShortTermBehavior == AiBehavior.AssaultSettlement;
        }
        
    }
}
