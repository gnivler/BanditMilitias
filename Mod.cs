using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.InputSystem;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    public class Mod : MBSubModuleBase
    {
        private readonly Harmony harmony = new Harmony("ca.gnivler.bannerlord.BanditMilitias");

        protected override void OnSubModuleLoad()
        {
            //Harmony.DEBUG = true;
            Log("Startup " + DateTime.Now.ToShortTimeString());
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            //Harmony.DEBUG = false;
        }

        private static void Log(object input)
        {
            //FileLog.Log($"[Bandit Militias] {input ?? "null"}");
        }

        // these might eventually be exposed as settings (and other stuff)
        private const float searchRadius = 5;
        private const float mergeDistance = 2f;

        private static float heroPartyStrength;
        private static float maxPartyStrength;
        private static double avgHeroPartyMaxSize;
        private static readonly Random random = new Random();

        private static bool IsValidBanditParty(MobileParty mobileParty)
        {
            return mobileParty.IsBandit &&
                   !mobileParty.IsBanditBossParty;
        }

        private static int GetMountedTroopHeadcount(TroopRoster troopRoster)
        {
            return troopRoster.Troops.Where(x => x.IsMounted)
                .Sum(troopRoster.GetTroopCount);
        }


        // safety mechanism to clear any problematic map encounters
        // I stumbled onto one caravan fighting a militia that had no troops and was stuck
        // this is a hotfix until/if the root cause is found
        [HarmonyPatch(typeof(MapEvent), "OnAfterLoad")]
        public static class MapEventOnAfterLoadPatch
        {
            private static void Postfix(MapEvent __instance, MapEventSide[] ____sides, MapEvent.BattleTypes ____mapEventType)
            {
                if (____mapEventType != MapEvent.BattleTypes.FieldBattle)
                {
                    return;
                }

                if (____sides.Any(x => x.LeaderParty.Name.ToString() == "Bandit Militia"))
                {
                    if (____sides.Any(x => x.TroopCount == 0))
                    {
                        Log($"Removing bad Bandit Militia field battle with {____sides[0].LeaderParty.Name}, {____sides[1].LeaderParty.Name}");
                        __instance.FinalizeEvent();
                    }
                }
            }
        }

        // set the variables used in the tick patch so they aren't calculated every frame
        // do it once per day because that's accurate enough
        // it will also reinitialize the militia leaders to the 1st highest tier unit it finds
        [HarmonyPatch(typeof(MapScreen), "OnHourlyTick")]
        public static class MapScreenOnHourlyTickPatch
        {
            private static int hoursPassed;
            private static bool initialized;

            private static void Postfix()
            {
                if (!initialized ||
                    hoursPassed++ == 24)
                {
                    // just reset the leaders every startup to avoid problems
                    if (!initialized)
                    {
                        foreach (var party in MobileParty.All.Where(IsValidBanditParty))
                        {
                            var leader = party.Party.MemberRoster.Troops
                                .OrderByDescending(y => y.Tier).First();
                            Log($"{party.Name} new leader => {leader.Name}");
                            party.ChangePartyLeader(party.Party.MemberRoster.Troops
                                .OrderByDescending(y => y.Tier).First());
                        }
                    }

                    initialized = true;
                    hoursPassed = 0;
                    heroPartyStrength = MobileParty.MainParty.Party.TotalStrength;
                    maxPartyStrength = heroPartyStrength * (random.Next(75, 126) / 100f);
                    // set the militia size based on a continually dynamic average?  this is probably stupid
                    avgHeroPartyMaxSize = Math.Round(MobileParty.All
                        .Where(x => x.LeaderHero != null).Select(x => x.Party.PartySizeLimit).Average());
                }
            }
        }

        // cleans up after parties are removed by MemberRoster.Reset()
        [HarmonyPatch(typeof(MapScreen), "TickCircles")]
        public static class MapScreenTickCirclesPatch
        {
            private static readonly List<MobileParty> tempList = new List<MobileParty>();

            private static void Prefix()
            {
                // safety hotkey in case things go sideways
                try
                {
                    if (Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl) &&
                        Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt))
                    {
                        if (Input.IsKeyPressed(InputKey.X))
                        {
                            Log("Purge hotkey pressed");
                            MobileParty.All.Where(x => x.IsVisible)
                                .Where(IsValidBanditParty)
                                .Where(x => x.MemberRoster.TotalManCount > 50)
                                .Do(x => tempList.Add(x));
                            tempList.Do(x => x.RemoveParty());
                        }

                        if (Input.IsKeyPressed(InputKey.Q))
                        {
                            Log("Nuke hotkey pressed");
                            MobileParty.All
                                .Where(x => x.Name.ToString() == "Bandit Militia")
                                .Do(x => tempList.Add(x));
                            tempList.Do(x => x.RemoveParty());
                        }
                    }

                    // get rid of 0 troop parties... (purged in TickAi patch) 
                    MobileParty.All.Where(IsValidBanditParty)
                        .Where(x => x.MemberRoster.TotalManCount == 0)
                        .Do(x => tempList.Add(x));
                    tempList.Do(x => x.RemoveParty());
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }
        }

        // makes bandit parties look for nearby compatible bandit parties and merges them together
        [HarmonyPatch(typeof(MobileParty), "TickAi")]
        public static class MobilePartyTickAiPatch
        {
            private static void Postfix(MobileParty __instance)
            {
                if (!IsValidBanditParty(__instance))
                {
                    return;
                }

                if (__instance.DefaultBehavior == AiBehavior.FleeToPoint)
                {
                    Log("Fleeing, abort");
                    return;
                }

                if (__instance.Party.Leader == null &&
                    __instance.Party.NumberOfAllMembers > 0)
                {
                    Log("Setting militia leader unit");
                    __instance.ChangePartyLeader(
                        __instance.Party.MemberRoster.Troops.OrderByDescending(x => x.Tier).First());
                }

                var pos = __instance.Position2D;
                var targetParty = MobileParty.FindPartiesAroundPosition(pos, searchRadius)
                    .FirstOrDefault(x => x != __instance && x.IsBandit && !x.IsBanditBossParty)?.Party;

                if (targetParty == null)
                {
                    return;
                }

                // first clause prevents it from running for the 'other' party in a merge
                // also prevent a militia compromised of more than 50% calvary because ouch
                Traverse.Create(__instance).Property("TargetParty").SetValue(targetParty.MobileParty);
                if (__instance.TargetParty.TargetParty != __instance &&
                    targetParty.TotalStrength + __instance.Party.TotalStrength < maxPartyStrength &&
                    targetParty.NumberOfAllMembers + __instance.Party.NumberOfAllMembers <= avgHeroPartyMaxSize &&
                    GetMountedTroopHeadcount(__instance.MemberRoster) + GetMountedTroopHeadcount(targetParty.MemberRoster) <=
                    (__instance.MemberRoster.TotalManCount + targetParty.MemberRoster.TotalManCount) / 2)
                {
                    var distance = targetParty.Position2D.Distance(__instance.Position2D);
                    if (distance > 0f && distance < mergeDistance)
                    {
                        // merge the parties
                        __instance.MemberRoster.Add(targetParty.MemberRoster);
                        Log($"Merging {__instance.Party.Name} with {targetParty.Name}. {__instance.Party.NumberOfAllMembers} troops ({GetMountedTroopHeadcount(__instance.MemberRoster)} cavalry), strength {Math.Round(__instance.Party.TotalStrength)}");
                        if (__instance.Name.ToString() != "Bandit Militia")
                        {
                            __instance.Name = new TextObject("Bandit Militia");
                        }

                        // might not be a different unit but whatever
                        __instance.ChangePartyLeader(
                            __instance.Party.MemberRoster.Troops.OrderByDescending(x => x.Tier).First());

                        // clear the roster and the party gets removed in TickCircles
                        targetParty.MemberRoster.Reset();
                    }
                    else
                    {
                        __instance.SetMoveGoToPoint(targetParty.Position2D);
                    }
                }
            }
        }
    }
}
