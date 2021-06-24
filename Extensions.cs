using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.ObjectSystem;

namespace Bandit_Militias
{
    public static class Extensions
    {
        internal static bool IsUsedByAQuest(this MobileParty mobileParty)
        {
            return Campaign.Current.VisualTrackerManager.CheckTracked(mobileParty);
        }

        internal static bool IsTooBusyToMerge(this MobileParty mobileParty)
        {
            if (mobileParty == mobileParty?.MoveTargetParty?.MoveTargetParty)
            {
                return false;
            }

            return mobileParty.TargetParty is not null
                   || mobileParty.ShortTermTargetParty is not null 
                   || mobileParty.ShortTermBehavior is AiBehavior.EngageParty or AiBehavior.FleeToPoint;
        }

        internal static void KillHero(this Hero hero)
        {
            try
            {
                // howitzer approach to lobotomize the game of any bandit heroes
                hero.ChangeState(Hero.CharacterStates.NotSpawned);
                hero.PartyBelongedTo?.MemberRoster.RemoveTroop(hero.CharacterObject);
                // this is a bit wasteful, if doing a list of heroes since it replaces the whole list each time
                // but it only takes 1000 ticks so whatever?
                var charactersField = Traverse.Create(Campaign.Current).Field<MBReadOnlyList<CharacterObject>>("_characters");
                var tempCharacterObjectList = new List<CharacterObject>(charactersField.Value.ToListQ());
                tempCharacterObjectList.Remove(hero.CharacterObject);
                charactersField.Value = new MBReadOnlyList<CharacterObject>(tempCharacterObjectList);
                MBObjectManager.Instance.UnregisterObject(hero);
                AccessTools.Method(typeof(CampaignEventDispatcher), "OnHeroKilled")
                    .Invoke(CampaignEventDispatcher.Instance, new object[] {hero, hero, KillCharacterAction.KillCharacterActionDetail.None, false});
                Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<Hero>>("_aliveHeroes").Value.Remove(hero);
                Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<Hero>>("_deadOrDisabledHeroes").Value.Remove(hero);
                Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<MobileParty>>("_mobileParties").Value.Remove(hero.PartyBelongedTo);
                if (hero.CurrentSettlement is not null)
                {
                    var heroesWithoutParty = Globals.HeroesWithoutParty(hero.CurrentSettlement);
                    Traverse.Create(heroesWithoutParty).Field<List<Hero>>("_list").Value.Remove(hero);
                }
            }
            catch (Exception ex)
            {
                Mod.Log(ex);
            }
        }
    }
}
