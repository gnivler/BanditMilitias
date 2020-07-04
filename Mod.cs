using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SandBox.Quests.QuestBehaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.SandBox.Issues;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static Bandit_Militias.Helper;
using static Bandit_Militias.Helper.Globals;

// ReSharper disable ConditionIsAlwaysTrueOrFalse  
// ReSharper disable ClassNeverInstantiated.Global  
// ReSharper disable UnusedMember.Local  
// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    public enum LogLevel
    {
        Disabled,
        Info,
        Error,
        Debug
    }

    public class Mod : MBSubModuleBase
    {
        private static readonly Harmony harmony = new Harmony("ca.gnivler.bannerlord.BanditMilitias");

        internal static void Log(object input, LogLevel logLevel)
        {
            if (logging >= logLevel)
            {
                FileLog.Log($"[Bandit Militias] {input ?? "null"}");
            }
        }

        protected override void OnSubModuleLoad()
        {
            //Harmony.DEBUG = true;
            Log("Startup " + DateTime.Now.ToShortTimeString(), LogLevel.Info);
            //RunManualPatches();
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            //Harmony.DEBUG = false; 
        }

        protected override void OnApplicationTick(float dt)
        {
            if ((Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl)) &&
                (Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt)) &&
                (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift)) &&
                Input.IsKeyPressed(InputKey.F12))
            {
                testingMode = !testingMode;
            }

            if ((Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl)) &&
                (Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt)) &&
                Input.IsKeyPressed(InputKey.N))
            {
                try
                {
                    Log("Clearing mod data.", LogLevel.Info);
                    InformationManager.AddQuickInformation(new TextObject("BANDIT MILITIAS CLEARED"));
                    var tempList = MobileParty.All
                        .Where(x => x.Name.Equals("Bandit Militia")).ToList();
                    var hasLogged = false;
                    foreach (var mobileParty in tempList)
                    {
                        if (!hasLogged)
                        {
                            Log($"Clearing {tempList.Count} Bandit Militias", LogLevel.Info);
                            hasLogged = true;
                        }
                        Trash(mobileParty);
                    }

                    KillNullPartyHeroes();

                    var badIssues = Campaign.Current.IssueManager.Issues
                        .Where(x => Clan.BanditFactions.Contains(x.Key.MapFaction)).ToList();
                    hasLogged = false;
                    foreach (var issue in badIssues)
                    {
                        if (!hasLogged)
                        {
                            hasLogged = true;
                            Log($"Clearing {badIssues.Count} bad-issue heroes.", LogLevel.Info);
                        }

                        issue.Key.KillHero();
                    }

                    var badSettlements = Settlement.All
                        .Where(x => x.IsHideout() && x.OwnerClan == null).ToList();
                    
                    hasLogged = false;
                    foreach (var settlement in badSettlements)
                    {
                        if (!hasLogged)
                        {
                            hasLogged = true;
                            Log($"Clearing {badSettlements.Count} bad settlements.", LogLevel.Info);
                        }

                        settlement.OwnerClan = Clan.BanditFactions.ToList()[Rng.Next(1, 5)];
                    }


                    FinalizeBadMapEvents();
                    PurgeNullRefDescriptionIssues(true);

                    Militia.All.Clear();
                }
                catch (Exception ex)
                {
                    Log(ex, LogLevel.Error);
                }
            }

            //if ((Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl)) &&
            //    (Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt)) &&
            //    Input.IsKeyPressed(InputKey.F))
            //{
            //    try
            //    {
            //        foreach (var hero in MobileParty.All.Where(x => x.Name.Equals("Bandit Militia") && x.LeaderHero == null).Select(x => x.LeaderHero))
            //        {
            //            Log("Fixing party without a hero", LogLevel.Debug);
            //            Militia.FindHeroMilitia(hero).Configure();
            //        }
            //    }
            //    catch (Exception ex)
            //    {
            //        Log(ex, LogLevel.Error);
            //    }
            //}

            // lobotomize AI
            //if ((Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl)) &&
            //    (Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt)) &&
            //    Input.IsKeyPressed(InputKey.L))
            //{
            //    try
            //    {
            //        foreach (var party in MobileParty.All.Where(x => Clan.BanditFactions.Contains(x.Party?.Owner?.Clan)))
            //        {
            //            Log("Lobotomizing " + party, LogLevel.Debug);
            //            Traverse.Create(party).Property("DefaultBehavior").SetValue(AiBehavior.None);
            //            Traverse.Create(party).Property("ShortTermBehavior").SetValue(AiBehavior.None);
            //            party.RecalculateShortTermAi();
            //        }
            //    }
            //    catch (Exception ex)
            //    {
            //        Log(ex, LogLevel.Error);
            //    }
            //}
        }

        private static void RunManualPatches()
        {
            try
            {
                var type = AccessTools.Inner(typeof(HeadmanNeedsGrainIssueBehavior), "HeadmanNeedsGrainIssue");
                var original = AccessTools.Method(type, "get_IssueQuestSolutionExplanationByIssueGiver");
                var finalizer = AccessTools.Method(typeof(Helper), nameof(SuppressingFinalizer));
                harmony.Patch(original, null, null, null, new HarmonyMethod(finalizer));

                type = AccessTools.Inner(typeof(ExtortionByDesertersIssueBehavior), "ExtortionByDesertersIssue");
                original = AccessTools.Method(type, "get_IssueBriefByIssueGiver");
                harmony.Patch(original, null, null, null, new HarmonyMethod(finalizer));

                type = AccessTools.Inner(typeof(RivalGangMovingInIssueBehavior), "RivalGangMovingInIssue");
                original = AccessTools.Method(type, "get_IssueQuestSolutionExplanationByIssueGiver");
                harmony.Patch(original, null, null, null, new HarmonyMethod(finalizer));

                type = AccessTools.Inner(typeof(HeadmanNeedsToDeliverAHerdIssueBehavior), "HeadmanNeedsToDeliverAHerdIssue");
                original = AccessTools.Method(type, "get_HerdTypeToDeliver");
                harmony.Patch(original, null, null, null, new HarmonyMethod(finalizer));

                original = AccessTools.Method(type, "get_AnimalCountToDeliver");
                harmony.Patch(original, null, null, null, new HarmonyMethod(finalizer));

                original = AccessTools.Method(type, "get_IssueBriefByIssueGiver");
                harmony.Patch(original, null, null, null, new HarmonyMethod(finalizer));

                original = AccessTools.Method(type, "get_IssueQuestSolutionAcceptByPlayer");
                harmony.Patch(original, null, null, null, new HarmonyMethod(finalizer));

                original = AccessTools.Method(type, "get_IssueAlternativeSolutionAcceptByPlayer");
                harmony.Patch(original, null, null, null, new HarmonyMethod(finalizer));
            }
            catch (Exception ex)
            {
                Log(ex, LogLevel.Error);
            }
        }
    }
}
