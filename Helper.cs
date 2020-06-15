using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
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
            internal const float SearchRadius = 10;
            internal const float MergeDistance = 2;
            internal const float MinDistanceFromHideout = 15;
            internal const float StrengthSplitFactor = 0.75f;
            internal const float SizeSplitFactor = 0.75f;
            internal const float RandomSplitChance = 0.10f;

            internal static float HeroPartyStrength;
            internal static float MaxPartyStrength;
            internal static double AvgHeroPartyMaxSize;
            internal static int HoursPassed;
            internal static readonly Random Rng = new Random();
            internal static List<MobileParty> QuestParties = new List<MobileParty>();
            internal static readonly TroopRoster Party1Roster = new TroopRoster();
            internal static readonly TroopRoster Party2Roster = new TroopRoster();
            internal static readonly TroopRoster Pris1Roster = new TroopRoster();
            internal static readonly TroopRoster Pris2Roster = new TroopRoster();
            internal static List<MobileParty> tempList = new List<MobileParty>();
            internal static IFaction Looters;
        }

        internal static int GetMountedTroopHeadcount(TroopRoster troopRoster)
        {
            return troopRoster.Troops.Where(x => x.IsMounted)
                .Sum(troopRoster.GetTroopCount);
        }

        internal static void CalcMergeCriteria()
        {
            HeroPartyStrength = MobileParty.MainParty.Party.TotalStrength;
            // 75-125% strength of the main party
            MaxPartyStrength = HeroPartyStrength * (Globals.Rng.Next(75, 126) / 100f);
            // maximum size grows over time as clans level up
            AvgHeroPartyMaxSize = Math.Round(MobileParty.All
                .Where(x => x.LeaderHero != null).Select(x => x.Party.PartySizeLimit).Average());
            //Trace("PartyMax size average " + avgHeroPartyMaxSize);
        }

        private static void CreateNewMilitias(MobileParty original)
        {
            try
            {
                var mobileParty1 = MBObjectManager.Instance.CreateObject<MobileParty>("Bandit Militia");
                mobileParty1.InitializeMobileParty(
                    new TextObject("Bandit Militia"),
                    Party1Roster,
                    Pris1Roster,
                    original.Position2D,
                    2f);

                var mobileParty2 = MBObjectManager.Instance.CreateObject<MobileParty>("Bandit Militia");
                mobileParty2.InitializeMobileParty(
                    new TextObject("Bandit Militia"),
                    Party2Roster,
                    Pris2Roster,
                    original.Position2D,
                    2f);

                mobileParty1.IsActive = true;
                mobileParty2.IsActive = true;
                mobileParty1.Ai.SetAIState(AIState.PatrollingAroundLocation);
                mobileParty2.Ai.SetAIState(AIState.PatrollingAroundLocation);
                mobileParty1.HomeSettlement = SettlementHelper.GetRandomTown();
                mobileParty2.HomeSettlement = SettlementHelper.GetRandomTown();

                var aTopTierTroop1 = mobileParty1.Party.MemberRoster.Troops
                    .OrderByDescending(x => x.Tier).First();
                var aTopTierTroop2 = mobileParty2.Party.MemberRoster.Troops
                    .OrderByDescending(x => x.Tier).First();

                mobileParty1.ChangePartyLeader(aTopTierTroop1);
                mobileParty2.ChangePartyLeader(aTopTierTroop2);
                Traverse.Create(mobileParty1.Party.Leader).Property("HeroObject")
                    .SetValue(HeroCreator.CreateHeroAtOccupation(Occupation.Bandit));
                Traverse.Create(mobileParty2.Party.Leader).Property("HeroObject")
                    .SetValue(HeroCreator.CreateHeroAtOccupation(Occupation.Bandit));

                mobileParty1.Ai.EnableAi();
                mobileParty2.Ai.EnableAi();
                mobileParty1.Party.Visuals.SetMapIconAsDirty();
                mobileParty2.Party.Visuals.SetMapIconAsDirty();

                Party1Roster.Clear();
                Party2Roster.Clear();
                Pris1Roster.Clear();
                Pris2Roster.Clear();
                mobileParty1.IsVisible = true;
                mobileParty2.IsVisible = true;

                Log($"CreateNewMilitias party 1 {mobileParty1.Name} ({mobileParty1.MemberRoster.TotalManCount} + {mobileParty1.PrisonRoster.TotalManCount}p)");
                Log($"CreateNewMilitias party 2 {mobileParty2.Name} ({mobileParty2.MemberRoster.TotalManCount} + {mobileParty2.PrisonRoster.TotalManCount}p)");
            }
            catch (Exception e)
            {
                Log(e);
            }
        }

        internal static bool CheckAndSplitParty(MobileParty __instance)
        {
            if (__instance.MemberRoster.TotalManCount != 0)
            {
                // only want to roll one time per day which the Contains() provides
                //TODO 
                var roll = Rng.NextDouble();
                if (roll <= RandomSplitChance &&
                    __instance.Party.TotalStrength > MaxPartyStrength * StrengthSplitFactor ||
                    __instance.Party.MemberRoster.TotalManCount > AvgHeroPartyMaxSize * SizeSplitFactor)
                {
                    // these set globals
                    SplitRosters(__instance);
                    CreateNewMilitias(__instance);
                    Log($"Split {__instance.Name} ({__instance.MemberRoster.TotalManCount} + {__instance.PrisonRoster.TotalManCount}p) (strength {__instance.Party.TotalStrength})");
                    __instance.RemoveParty();
                    return true;
                }
            }

            return false;
        }

        internal static bool IsValidParty(MobileParty __instance)
        {
            if (__instance.LeaderHero != null)
            {
                return false;
            }

            if (__instance.Party.MemberRoster.TotalManCount == 0)
            {
                return false;
            }

            if (QuestParties.Contains(__instance))
            {
                Trace("Quest party");
                return false;
            }

            return __instance.IsBandit && !__instance.IsBanditBossParty;
        }

        internal static void SplitRosters(MobileParty __instance)
        {
            try
            {
                var troopTypes = __instance.MemberRoster.Troops.ToList();
                var prisTypes = __instance.PrisonRoster.Troops.ToList();
                Party1Roster.Clear();
                Party2Roster.Clear();
                Pris1Roster.Clear();
                Pris2Roster.Clear();

                for (var i = 0; i < troopTypes.Count; i++)
                {
                    var troopType = troopTypes[i];
                    var numTroops = __instance.MemberRoster.GetElementCopyAtIndex(i).Number;
                    Log($"Processing troops: {numTroops} {troopType.Name}");

                    // build two new rosters, splitting the troop count
                    // implicitly makes the roster counts even so kills a troop off
                    for (var j = 0; j < numTroops / 2; j++)
                    {
                        //Trace($"{j} Add {troopType.Name} to Party 1");
                        Party1Roster.Add(new[]
                        {
                            new FlattenedTroopRosterElement(troopType)
                        });

                        //Trace($"{j} Add {troopType.Name} to Party 2");
                        Party2Roster.Add(new[]
                        {
                            new FlattenedTroopRosterElement(troopType)
                        });
                    }
                }

                Trace($"party1Roster.TotalManCount " + Party1Roster.TotalManCount);
                Trace($"party2Roster.TotalManCount " + Party2Roster.TotalManCount);

                // gross copy paste
                for (var i = 0; i < prisTypes.Count; i++)
                {
                    var prisType = prisTypes[i];
                    var numPris = __instance.PrisonRoster.GetElementCopyAtIndex(i).Number;
                    Log($"Processing prisoners: {numPris} {prisType.Name}");

                    // build two new rosters, splitting the troop count
                    // implicitly makes the roster counts even so kills a troop off
                    for (var j = 0; j < numPris / 2; j++)
                    {
                        //Trace($"{j} Add {troopType.Name} to Party 1");
                        Pris1Roster.Add(new[]
                        {
                            new FlattenedTroopRosterElement(prisType)
                        });

                        //Trace($"{j} Add {troopType.Name} to Party 2");
                        Pris2Roster.Add(new[]
                        {
                            new FlattenedTroopRosterElement(prisType)
                        });
                    }
                }

                Trace($"pris1Roster.TotalManCount " + Pris1Roster.TotalManCount);
                Trace($"pris2Roster.TotalManCount " + Pris2Roster.TotalManCount);
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }
    }
}
