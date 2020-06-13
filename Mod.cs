using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.SandBox.Issues;
using TaleWorlds.InputSystem;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using Module = TaleWorlds.MountAndBlade.Module;

// ReSharper disable UnusedMember.Local  
// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    public class Mod : MBSubModuleBase
    {
        private static readonly Harmony harmony = new Harmony("ca.gnivler.bannerlord.BanditMilitias");

        protected override void OnSubModuleLoad()
        {
            //Harmony.DEBUG = true;
            Log("Startup " + DateTime.Now.ToShortTimeString());
            RunManualPatches();
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            //Harmony.DEBUG = false; 
        }

        private static void Log(object input)
        {
            //FileLog.Log($"[Bandit Militias] {input ?? "null"}");
        }

        private static void Trace(object input)
        {
            //FileLog.Log($"[Bandit Militias] {input ?? "null"}");
        }

        private static void RunManualPatches()
        {
            try
            {
                // thank you CommunityPatch
                typeof(Module)
                    .GetField("_splashScreenPlayed", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.SetValue(Module.CurrentModule, true);

                var internalClass = AccessTools.Inner(typeof(MerchantNeedsHelpWithLootersIssueQuestBehavior),
                    "MerchantNeedsHelpWithLootersIssueQuest");
                var hourlyTickParty = AccessTools.Method(internalClass, "HourlyTickParty");
                var mobilePartyDestroyed = AccessTools.Method(internalClass, "MobilePartyDestroyed");
                var hourlyTickPartyPostfix = AccessTools.Method(typeof(Mod),
                    nameof(MerchantNeedsHelpWithLootersIssueQuestHoursTickPartyPatch));
                var mobilePartyDestroyedPostfix = AccessTools.Method(typeof(Mod),
                    nameof(MerchantNeedsHelpWithLootersIssueQuestBehaviorMobilePartyDestroyedPostfix));
                Log("Patching");
                harmony.Patch(hourlyTickParty, null, new HarmonyMethod(hourlyTickPartyPostfix));
                harmony.Patch(mobilePartyDestroyed, null, new HarmonyMethod(mobilePartyDestroyedPostfix));
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }

        // these might eventually be exposed as settings (and other stuff)
        private const float searchRadius = 5;
        private const float mergeDistance = 2f;

        private static float heroPartyStrength;
        private static float maxPartyStrength;
        private static double avgHeroPartyMaxSize;
        private static readonly Random random = new Random();
        private static List<MobileParty> questParties = new List<MobileParty>();
        private static List<MobileParty> splitList = new List<MobileParty>();
        private static TroopRoster party1Roster = new TroopRoster();
        private static TroopRoster party2Roster = new TroopRoster();
        private static TroopRoster pris1Roster = new TroopRoster();

        private static TroopRoster pris2Roster = new TroopRoster();
        //private static CampaignBehaviorManager campaignBehaviorManager;

        // unused...
        //[HarmonyPatch(typeof(CampaignBehaviorManager), MethodType.Constructor, typeof(IEnumerable<CampaignBehaviorBase>))]
        //public static class CampaignBehaviorManagerCtorPatch
        //{
        //    private static void Postfix(CampaignBehaviorManager __instance)
        //    {
        //        campaignBehaviorManager = __instance;
        //    }
        //}

        // keeps updating the list of quest parties and they're omitted from mergers
        private static void MerchantNeedsHelpWithLootersIssueQuestHoursTickPartyPatch(object __instance)
        {
            questParties = Traverse.Create(__instance).Field("_validPartiesList").GetValue<List<MobileParty>>();
        }

        private static void MerchantNeedsHelpWithLootersIssueQuestBehaviorMobilePartyDestroyedPostfix(MobileParty mobileParty)
        {
            try
            {
                questParties.Remove(mobileParty);
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }

        private static bool IsValidBanditParty(MobileParty party) => party.IsBandit && !party.IsBanditBossParty;

        private static int GetMountedTroopHeadcount(TroopRoster troopRoster)
        {
            return troopRoster.Troops.Where(x => x.IsMounted)
                .Sum(troopRoster.GetTroopCount);
        }

        [HarmonyPatch(typeof(MapEventManager), "OnAfterLoad")]
        public static class MapEventManagerOnAfterLoadPatch
        {
            private static void Postfix(List<MapEvent> ___mapEvents)
            {
                foreach (var mapEvent in ___mapEvents.Where(x => x.EventType == MapEvent.BattleTypes.FieldBattle))
                {
                    if (mapEvent.AttackerSide.TroopCount == 0 ||
                        mapEvent.DefenderSide.TroopCount == 0)
                    {
                        Trace($"Removing bad field battle with {mapEvent.AttackerSide.LeaderParty.Name}, {mapEvent.DefenderSide.LeaderParty.Name}");
                        mapEvent.FinalizeEvent();
                    }
                    else
                    {
                        Trace($"Leaving valid field battle with {mapEvent.AttackerSide.LeaderParty.Name}, {mapEvent.DefenderSide.LeaderParty.Name}");
                    }
                }
            }
        }

        [HarmonyPatch(typeof(MapScreen), "OnInitialize")]
        public static class MapScreenOnInitializePatch
        {
            private static void Postfix()
            {
                try
                {
                    Trace("Creating split parties");
                    Trace("Setting Militia leaders");
                    foreach (var party in MobileParty.All.Where(x => x.Name.ToString() == "Bandit Militia"))
                    {
                        var leader = party.Party.MemberRoster.Troops
                            .OrderByDescending(y => y.Tier).FirstOrDefault();
                        if (leader != null)
                        {
                            party.ChangePartyLeader(leader);
                        }
                    }


                    // get rid of 0 troop parties
                    var parties = new List<MobileParty>();
                    MobileParty.All.Where(x => x.MemberRoster?.TotalManCount == 0)
                        .Do(x => parties.Add(x));
                    if (parties.Count > 0)
                    {
                        Trace($"Clearing {parties.Count} empty parties");
                        parties.Do(x =>
                        {
                            Trace($"DisbandPartyAction.ApplyDisband({x})");
                            DisbandPartyAction.ApplyDisband(x);
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }
        }

        private static int hoursPassed;

        // set the variables used in the tick patch so they aren't calculated every frame
        // do it once per day because that's accurate enough
        // it will also reinitialize the militia leaders to the 1st highest tier unit it finds
        [HarmonyPatch(typeof(MapScreen), "OnHourlyTick")]
        public static class MapScreenOnHourlyTickPatch
        {
            private static bool initialized;

            private static void Postfix()
            {
                try
                {
                    if (!initialized ||
                        hoursPassed++ == 24)
                    {
                        initialized = true;
                        hoursPassed = 0;
                        heroPartyStrength = MobileParty.MainParty.Party.TotalStrength;
                        maxPartyStrength = heroPartyStrength * (random.Next(75, 126) / 100f);
                        // set the militia size based on a continually dynamic average?  this is probably stupid
                        avgHeroPartyMaxSize = Math.Round(MobileParty.All
                            .Where(x => x.LeaderHero != null).Select(x => x.Party.PartySizeLimit).Average());
                        splitList.Clear();
                        Trace("PartyMax size average " + avgHeroPartyMaxSize);
                    }
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }
        }

        // cleans up after parties are removed by MemberRoster.Reset()
        //[HarmonyPatch(typeof(MapScreen), "TickCircles")]
        [HarmonyPatch(typeof(Campaign), "Tick")]
        public static class MapScreenTickCirclesPatch
        {
            private static readonly List<MobileParty> tempList = new List<MobileParty>();

            private static void Prefix()
            {
                try
                {
                    tempList.Clear();
                    MobileParty.All.Where(IsValidBanditParty)
                        .Where(x => x.MemberRoster.TotalManCount == 0)
                        .Do(x => tempList.Add(x));
                    if (tempList.Count > 0)
                    {
                        Trace($"Campaign.Tick() Clearing {tempList.Count} empty parties");
                        tempList.Do(x =>
                        {
                            Trace($"DisbandPartyAction.ApplyDisband({x})");
                            DisbandPartyAction.ApplyDisband(x);
                        });
                    }

                    // safety hotkey in case things go sideways
                    if (Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl) &&
                        Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt))
                    {
                        if (Input.IsKeyPressed(InputKey.X))
                        {
                            Trace("Campaign.Tick()");
                            Trace("Kill visible hotkey pressed");
                            tempList.Clear();
                            MobileParty.All.Where(x => x.IsVisible)
                                .Where(IsValidBanditParty)
                                .Where(x => x.MemberRoster.TotalManCount > 50)
                                .Do(x => tempList.Add(x));
                            tempList.Do(x =>
                            {
                                Trace($"Killing {x}");
                                DisbandPartyAction.ApplyDisband(x);
                            });
                        }

                        if (Input.IsKeyPressed(InputKey.Q))
                        {
                            Trace("Campaign.Tick()");
                            Trace("Nuke all hotkey pressed");
                            tempList.Clear();
                            MobileParty.All
                                .Where(x => x.Name.ToString() == "Bandit Militia")
                                .Do(x => tempList.Add(x));
                            tempList.Do(x =>
                            {
                                Trace($"Nuking {x}");
                                DisbandPartyAction.ApplyDisband(x);
                                //x.RemoveParty();
                            });
                        }

                        if (Input.IsKeyPressed(InputKey.F))
                        {
                            Trace("Campaign.Tick()");
                            Trace("Fuck hotkey pressed");
                            tempList.Clear();
                            MobileParty.All
                                .Where(x => x.Party != MobileParty.MainParty.Party)
                                .Where(x => x.IsVisible).Do(x => tempList.Add(x));
                            foreach (var party in tempList)
                            {
                                Trace("Fucking " + party.Name);
                                DisbandPartyAction.ApplyDisband(party);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }
        }

        // pretty sure this doesn't work.  WIP, have seen bandits fleeing from bandits
        [HarmonyPatch(typeof(MobileParty), "GetFleeBehavior")]
        public static class MobilePartyGetFleeBehaviorPatch
        {
            private static void Postfix(ref AiBehavior fleeBehavior, MobileParty partyToFleeFrom)
            {
                //Log($"partyToFleeFrom {partyToFleeFrom}, bandit? {partyToFleeFrom.IsBandit}");
                if (partyToFleeFrom.IsBandit)
                {
                    Trace("Don't flee from bandits!");
                    fleeBehavior = AiBehavior.PatrolAroundPoint;
                }
            }
        }

        // makes bandit parties look for nearby compatible bandit parties and merges them together
        // also checks once per 24 hours by RNG to see if they should split up
        // 25% chance they will split if at 85% their size or strength max
        [HarmonyPatch(typeof(MobileParty), "TickAi")]
        public static class MobilePartyTickAiPatch
        {
            private const float minDistanceFromHideout = 20;
            private const float strengthSplitFactor = 0.85f;
            private const float sizeSplitFactor = 0.85f;
            private const float randomSplitChance = 0.25f;
            private static readonly Random rng = new Random();

            private static void Postfix(MobileParty __instance)
            {
                try
                {
                    if (questParties.Contains(__instance))
                    {
                        Trace("Quest party");
                        return;
                    }

                    if (__instance.Party.MemberRoster.TotalManCount == 0)
                    {
                        return;
                    }

                    if (!IsValidBanditParty(__instance))
                    {
                        return;
                    }

                    // check daily each bandit party against the size factor and a random chance to split up
                    if (hoursPassed == 24)
                    {
                        if (!splitList.Contains(__instance) &&
                            __instance.MemberRoster.TotalManCount != 0)
                        {
                            splitList.Add(__instance);
                            // only want to roll one time per day which the Contains() provides
                            var roll = rng.NextDouble();
                            if (roll > randomSplitChance &&
                                __instance.Party.TotalStrength > maxPartyStrength * strengthSplitFactor ||
                                __instance.Party.MemberRoster.TotalManCount > avgHeroPartyMaxSize * sizeSplitFactor)
                            {
                                Log($"Met some split criteria.  Splitting {__instance.Name} ({__instance.MemberRoster.TotalManCount} + {__instance.PrisonRoster.TotalManCount}p) (strength {__instance.Party.TotalStrength})");
                                SplitRosters(__instance);
                                CreateNewMilitias(__instance);
                                __instance.MemberRoster.Reset();
                                Log("Party fully split up");
                                return;
                            }
                        }
                    }

                    if (__instance.DefaultBehavior == AiBehavior.FleeToPoint)
                    {
                        Trace("Fleeing, abort");
                        return;
                    }

                    if (__instance.Party.Leader == null &&
                        __instance.Party.NumberOfAllMembers > 0)
                    {
                        Trace("Setting militia leader unit");
                        __instance.ChangePartyLeader(
                            __instance.Party.MemberRoster.Troops
                                .OrderByDescending(x => x.Tier).First());
                    }

                    var pos = __instance.Position2D;
                    var targetParty = MobileParty.FindPartiesAroundPosition(pos, searchRadius)
                        .FirstOrDefault(x => x != __instance &&
                                             IsValidBanditParty(x) &&
                                             x.MemberRoster.TotalManCount > 0)?.Party;

                    if (targetParty == null)
                    {
                        return;
                    }

                    // first clause prevents it from running for the 'other' party in a merge
                    // also prevent a militia compromised of more than 50% calvary because ouch
                    // added the null propagation to TargetParty after a game patch seemed to start throwing NREs
                    Traverse.Create(__instance).Property("TargetParty").SetValue(targetParty.MobileParty);
                    if (__instance.TargetParty?.TargetParty == __instance)
                    {
                        return;
                    }

                    var militiaStrength = targetParty.TotalStrength + __instance.Party.TotalStrength;
                    var militiaCavalryCount = GetMountedTroopHeadcount(__instance.MemberRoster) + GetMountedTroopHeadcount(targetParty.MemberRoster);
                    var militiaTotalCount = __instance.MemberRoster.TotalManCount + targetParty.MemberRoster.TotalManCount;

                    if (militiaStrength < maxPartyStrength &&
                        militiaTotalCount < avgHeroPartyMaxSize &&
                        militiaCavalryCount < militiaTotalCount / 2)
                    {
                        var distance = targetParty.Position2D.Distance(__instance.Position2D);
                        // the FindAll is returning pretty fast, small sample average was 600 ticks
                        //var timer = new Stopwatch();
                        //timer.Restart();
                        var closeHideOuts = Settlement.FindAll(x => x.IsHideout())
                            .Where(x => targetParty.Position2D.Distance(x.Position2D) < minDistanceFromHideout).ToList();
                        //Log(timer.ElapsedTicks);
                        if (closeHideOuts.Any())
                        {
                            //Log($"Within {minDistanceFromHideout} distance of hideout - skipping");
                            return;
                        }

                        if (distance > 0f && distance < mergeDistance)
                        {
                            // merge the parties
                            __instance.Party.AddMembers(targetParty.MobileParty.MemberRoster.ToFlattenedRoster());
                            targetParty.MobileParty.MemberRoster.Reset();
                            Trace($"Militia forms: {__instance.Party.NumberOfAllMembers} troops ({GetMountedTroopHeadcount(__instance.MemberRoster)} cavalry), strength {Math.Round(__instance.Party.TotalStrength)}");
                            __instance.Name = new TextObject("Bandit Militia");
                            // might not be a different unit but whatever
                            __instance.ChangePartyLeader(__instance.Party.MemberRoster.Troops
                                .OrderByDescending(x => x.Tier).First());
                        }
                        else
                        {
                            __instance.SetMoveGoToPoint(targetParty.Position2D);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }

            private static void SplitRosters(MobileParty __instance)
            {
                try
                {
                    var troopTypes = __instance.MemberRoster.Troops.ToList();
                    var prisTypes = __instance.PrisonRoster.Troops.ToList();
                    party1Roster.Clear();
                    party2Roster.Clear();
                    pris1Roster.Clear();
                    pris2Roster.Clear();

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
                            party1Roster.Add(new[]
                            {
                                new FlattenedTroopRosterElement(troopType)
                            });

                            //Trace($"{j} Add {troopType.Name} to Party 2");
                            party2Roster.Add(new[]
                            {
                                new FlattenedTroopRosterElement(troopType)
                            });
                        }
                    }

                    Log($"party1Roster " + party1Roster.TotalManCount);
                    Log($"party2Roster " + party2Roster.TotalManCount);

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
                            pris1Roster.Add(new[]
                            {
                                new FlattenedTroopRosterElement(prisType)
                            });

                            //Trace($"{j} Add {troopType.Name} to Party 2");
                            pris2Roster.Add(new[]
                            {
                                new FlattenedTroopRosterElement(prisType)
                            });
                        }
                    }

                    Log($"pris1Roster " + pris1Roster.TotalManCount);
                    Log($"pris2Roster " + pris2Roster.TotalManCount);
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }

            private static void CreateNewMilitias(MobileParty original)
            {
                var mobileParty1 = new MobileParty();
                mobileParty1.InitializeMobileParty(
                    new TextObject("Bandit Militia"),
                    party1Roster,
                    pris1Roster,
                    original.Position2D,
                    2f);

                var mobileParty2 = new MobileParty();
                mobileParty2.InitializeMobileParty(
                    new TextObject("Bandit Militia"),
                    party2Roster,
                    pris2Roster,
                    original.Position2D,
                    2f);

                party1Roster.Clear();
                party2Roster.Clear();
                pris1Roster.Clear();
                pris2Roster.Clear();
                Log($"CreateNewMilitias party 1 {mobileParty1.Name} ({mobileParty1.MemberRoster.TotalManCount} + {mobileParty1.PrisonRoster.TotalManCount}p)");
                Log($"CreateNewMilitias party 2 {mobileParty2.Name} ({mobileParty2.MemberRoster.TotalManCount} + {mobileParty2.PrisonRoster.TotalManCount}p)");
            }
        }
    }
}
