using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.ObjectSystem;

namespace Bandit_Militias.Helpers
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
        internal static void RemoveMilitiaHero(this Hero hero)
        {
            try
            {
                if (hero?.CharacterObject is null
                    || !hero.CharacterObject.StringId.EndsWith("Bandit_Militia"))
                {
                    return;
                }

                hero.PartyBelongedTo?.MemberRoster.RemoveTroop(hero.CharacterObject);
                Traverse.Create(hero).Field<Hero.CharacterStates>("_heroState").Value = Hero.CharacterStates.Dead;
                LocationComplex.Current?.RemoveCharacterIfExists(hero);
                Helper.RemoveCharacterFromReadOnlyList(hero.CharacterObject);
                Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<Hero>>("_aliveHeroes").Value.Remove(hero);
                Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<Hero>>("_allHeroes").Value.Remove(hero);
                if (hero.CurrentSettlement is not null)
                {
                    var heroesWithoutParty = Globals.HeroesWithoutParty(hero.CurrentSettlement);
                    Traverse.Create(heroesWithoutParty).Field<List<Hero>>("_list").Value.Remove(hero);
                }

                Traverse.Create(hero.Clan).Method("RemoveHeroInternal", hero).GetValue();
                MBObjectManager.Instance.UnregisterObject(hero.CharacterObject);
                MBObjectManager.Instance.UnregisterObject(hero);
            }
            catch (Exception ex)
            {
                Mod.Log(ex);
            }
        }
    }
}
