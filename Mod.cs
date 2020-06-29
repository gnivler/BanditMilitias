using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
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
        Debug,
        Error,
        Info,
        Disabled
    }

    public class Mod : MBSubModuleBase
    {
        private static readonly Harmony harmony = new Harmony("ca.gnivler.bannerlord.BanditMilitias");

        protected override void OnSubModuleLoad()
        {
            //Harmony.DEBUG = true;
            Log("Startup " + DateTime.Now.ToShortTimeString(), LogLevel.Info);
            RunManualPatches();
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            //Harmony.DEBUG = false; 
        }

        protected override void OnApplicationTick(float dt)
        {
            if ((Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl)) &&
                (Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt)) &&
                Input.IsKeyPressed(InputKey.N))
            {
                try
                {
                    Log("Clearing mod data.", LogLevel.Info);
                    InformationManager.AddQuickInformation(new TextObject("BANDIT MILITIAS CLEARED"));
                    foreach (var hero in TempList)
                    {
                        Traverse.Create(typeof(KillCharacterAction))
                            .Method("MakeDead", hero).GetValue();
                        MBObjectManager.Instance.UnregisterObject(hero);
                    }

                    KillNullPartyHeroes();

                    var hasLogged = false;
                    var badIssues = Campaign.Current.IssueManager.Issues
                        .Where(x => Clan.BanditFactions.Contains(x.Key.MapFaction)).ToList();

                    // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                    if (!hasLogged)
                    {
                        // ReSharper disable once RedundantAssignment
                        hasLogged = true;
                        Log($"Clearing {badIssues.Count} bad-issue heroes.", LogLevel.Info);
                        foreach (var issue in badIssues)
                        {
                            Traverse.Create(typeof(KillCharacterAction))
                                .Method("MakeDead", issue.Key).GetValue();
                            MBObjectManager.Instance.UnregisterObject(issue.Value);
                        }
                    }

                    var badSettlements = Settlement.All
                        .Where(x => x.IsHideout() && x.OwnerClan == null).ToList();
                    hasLogged = false;
                    if (!hasLogged)
                    {
                        hasLogged = true;
                        Log($"Clearing {badSettlements.Count} bad settlements.", LogLevel.Info);
                        foreach (var settlement in badSettlements)
                        {
                            settlement.OwnerClan = Clan.BanditFactions.ToList()[Rng.Next(1, 5)];
                        }
                    }


                    FinalizeBadMapEvents();
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
            }
            catch (Exception ex)
            {
                Log(ex, LogLevel.Error);
            }
        }
    }
}
