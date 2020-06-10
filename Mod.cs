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
            FileLog.Log($"[Bandit Militias] {input ?? "null"}");
        }

        private static void RunManualPatches()
        {
            try
            {
                var internalClass = AccessTools.Inner(typeof(MerchantNeedsHelpWithLootersIssueQuestBehavior), "MerchantNeedsHelpWithLootersIssueQuest");
                var original = AccessTools.Method(internalClass, "HourlyTickParty");
                var postfix = AccessTools.Method(typeof(Mod), nameof(MerchantNeedsHelpWithLootersIssueQuestHoursTickPartyPatch));
                Log("Patching");
                harmony.Patch(original, null, new HarmonyMethod(postfix));
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

        // just written not tested.  supposed to retrieve the quest parties and exclude them eventually
        private static void MerchantNeedsHelpWithLootersIssueQuestHoursTickPartyPatch(object __instance)
        {
            Log("ping");
            Log(__instance.GetType());
            var foo = Traverse.Create(__instance).Field("_validPartiesList").GetValue<List<MobileParty>>();
            Log(foo);
            foo?.Do(Log);
            questParties = foo;
            // TODO complete checks against these units
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
                        Log($"Removing bad field battle with {mapEvent.AttackerSide.LeaderParty.Name}, {mapEvent.DefenderSide.LeaderParty.Name}");
                        mapEvent.FinalizeEvent();
                    }
                    else
                    {
                        Log($"Leaving valid field battle with {mapEvent.AttackerSide.LeaderParty.Name}, {mapEvent.DefenderSide.LeaderParty.Name}");
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
                    Log("Setting Militia leaders");
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
                        Log($"Clearing {parties.Count} empty parties");
                        parties.Do(x =>
                        {
                            Log($"DisbandPartyAction.ApplyDisband({x})");
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
                        Log("PartyMax size average " + avgHeroPartyMaxSize);
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
                        Log($"Campaign.Tick() Clearing {tempList.Count} empty parties");
                        tempList.Do(x =>
                        {
                            Log($"DisbandPartyAction.ApplyDisband({x})");
                            DisbandPartyAction.ApplyDisband(x);
                        });
                    }

                    // safety hotkey in case things go sideways
                    if (Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl) &&
                        Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt))
                    {
                        if (Input.IsKeyPressed(InputKey.X))
                        {
                            Log("Campaign.Tick()");
                            Log("Kill visible hotkey pressed");
                            tempList.Clear();
                            MobileParty.All.Where(x => x.IsVisible)
                                .Where(IsValidBanditParty)
                                .Where(x => x.MemberRoster.TotalManCount > 50)
                                .Do(x => tempList.Add(x));
                            tempList.Do(x =>
                            {
                                Log($"Killing {x}");
                                DisbandPartyAction.ApplyDisband(x);
                            });
                        }

                        if (Input.IsKeyPressed(InputKey.Q))
                        {
                            Log("Campaign.Tick()");
                            Log("Nuke all hotkey pressed");
                            tempList.Clear();
                            MobileParty.All
                                .Where(x => x.Name.ToString() == "Bandit Militia")
                                .Do(x => tempList.Add(x));
                            tempList.Do(x =>
                            {
                                Log($"Nuking {x}");
                                DisbandPartyAction.ApplyDisband(x);
                                //x.RemoveParty();
                            });
                        }

                        if (Input.IsKeyPressed(InputKey.F))
                        {
                            Log("Campaign.Tick()");
                            Log("Fuck hotkey pressed");
                            tempList.Clear();
                            MobileParty.All
                                .Where(x => x.Party != MobileParty.MainParty.Party)
                                .Where(x => x.IsVisible).Do(x => tempList.Add(x));
                            foreach (var party in tempList)
                            {
                                Log("Fucking " + party.Name);
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

        // makes bandit parties look for nearby compatible bandit parties and merges them together
        [HarmonyPatch(typeof(MobileParty), "TickAi")]
        public static class MobilePartyTickAiPatch
        {
            private static void Postfix(MobileParty __instance)
            {
                try
                {
                    if (__instance.Party.MemberRoster.TotalManCount == 0)
                    {
                        return;
                    }

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
                    if (__instance.TargetParty?.TargetParty != __instance &&
                        targetParty.TotalStrength + __instance.Party.TotalStrength < maxPartyStrength &&
                        targetParty.NumberOfAllMembers + __instance.Party.NumberOfAllMembers <= avgHeroPartyMaxSize &&
                        GetMountedTroopHeadcount(__instance.MemberRoster) + GetMountedTroopHeadcount(targetParty.MemberRoster) <=
                        (__instance.MemberRoster.TotalManCount + targetParty.MemberRoster.TotalManCount) / 2)
                    {
                        var distance = targetParty.Position2D.Distance(__instance.Position2D);
                        if (distance > 0f && distance < mergeDistance)
                        {
                            // merge the parties
                            __instance.Party.AddMembers(targetParty.MobileParty.MemberRoster.ToFlattenedRoster());
                            targetParty.MobileParty.MemberRoster.Reset();
                            Log($"Militia forms: {__instance.Party.NumberOfAllMembers} troops ({GetMountedTroopHeadcount(__instance.MemberRoster)} cavalry), strength {Math.Round(__instance.Party.TotalStrength)}");
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
        }
    }
}
