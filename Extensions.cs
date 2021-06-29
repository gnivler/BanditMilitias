using System;
using System.Collections.Generic;
using Bandit_Militias.Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
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

        // howitzer approach to lobotomize the game of bandit heroes
        internal static void KillHero(this Hero hero)
        {
            try
            {
                var onBeforeHeroKilled = AccessTools.Method(typeof(CampaignEventDispatcher), "OnBeforeHeroKilled");
                var onHeroKilled = AccessTools.Method(typeof(CampaignEventDispatcher), "OnHeroKilled");
                onBeforeHeroKilled.Invoke(CampaignEventDispatcher.Instance, new object[] {hero, null, KillCharacterAction.KillCharacterActionDetail.DiedInBattle, false});
                onHeroKilled.Invoke(CampaignEventDispatcher.Instance, new object[] {hero, null, KillCharacterAction.KillCharacterActionDetail.DiedInBattle, false});
                LocationComplex.Current.RemoveCharacterIfExists(hero);
                Helper.RemoveHeroFromReadOnlyList(hero);
                MBObjectManager.Instance.UnregisterObject(hero);
                Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<Hero>>("_aliveHeroes").Value.Remove(hero);
                // Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<Hero>>("_deadOrDisabledHeroes").Value.Remove(hero);
                // Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<MobileParty>>("_mobileParties").Value.Remove(hero.PartyBelongedTo);
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
