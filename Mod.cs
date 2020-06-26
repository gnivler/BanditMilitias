using System;
using System.Collections.Generic;
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

        protected override void OnApplicationTick(float dt)
        {
            if ((Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl)) &&
                (Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt)) &&
                Input.IsKeyPressed(InputKey.N))
            {
                try
                {
                    Log("Clearing mod data.");
                    InformationManager.AddQuickInformation(new TextObject("BANDIT MILITIAS CLEARED"));
                    TempList.Clear();
                    TempList = MobileParty.All.Where(x => x.Name.Equals("Bandit Militia")).ToList();
                    Log($"Clearing {TempList.Count} Bandit Militia parties.");
                    foreach (var mobileParty in TempList)
                    {
                        Trash(mobileParty);
                    }

                    var heroes = new List<Hero>();
                    foreach (var hero in Hero.All)
                    {
                        if (hero.Name.Equals("Bandit Militia"))
                        {
                            heroes.Add(hero);
                        }
                    }

                    foreach (var hero in heroes)
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
                        Log($"Clearing {badIssues.Count} bad-issue heroes.");
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
                        Log($"Clearing {badSettlements.Count} bad settlements.");
                        foreach (var settlement in badSettlements)
                        {
                            settlement.OwnerClan = Clan.BanditFactions.ToList()[Rng.Next(1, 5)];
                        }
                    }

                    FinalizeBadMapEvents();
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }

            // lobotomize AI
            if ((Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl)) &&
                (Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt)) &&
                Input.IsKeyPressed(InputKey.L))
            {
                try
                {
                    foreach (var party in MobileParty.All.Where(x => Clan.BanditFactions.Contains(x.Party?.Owner?.Clan)))
                    {
                        Log("Lobotomizing " + party);
                        Traverse.Create(party).Property("DefaultBehavior").SetValue(AiBehavior.None);
                        Traverse.Create(party).Property("ShortTermBehavior").SetValue(AiBehavior.None);
                        party.RecalculateShortTermAi();
                    }
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }
        }

        internal static void Log(object input)
        {
            //FileLog.Log($"[Bandit Militias] {input ?? "null"}");
        }

        internal static void Trace(object input)
        {
            //FileLog.Log($"[Bandit Militias] {input ?? "null"}");
        }

        private static void RunManualPatches()
        {
            try
            {
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }
    }
}
