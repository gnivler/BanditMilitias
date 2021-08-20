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
                hero.PartyBelongedTo?.MemberRoster.RemoveTroop(hero.CharacterObject);
                Traverse.Create(hero).Field<Hero.CharacterStates>("_heroState").Value = Hero.CharacterStates.NotSpawned;
                LocationComplex.Current?.RemoveCharacterIfExists(hero);
                Helper.RemoveCharacterFromReadOnlyList(hero.CharacterObject);
                Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<Hero>>("_aliveHeroes").Value.Remove(hero);
                if (hero.CurrentSettlement is not null)
                {
                    var heroesWithoutParty = Globals.HeroesWithoutParty(hero.CurrentSettlement);
                    Traverse.Create(heroesWithoutParty).Field<List<Hero>>("_list").Value.Remove(hero);
                }

                MBObjectManager.Instance.UnregisterObject(hero.CharacterObject);
                MBObjectManager.Instance.UnregisterObject(hero);
            }
            catch // (Exception ex)
            {
                //ignore Mod.Log(ex);
            }
        }

        // ReSharper disable once InconsistentNaming
        internal static bool IsBM(this MobileParty mobileParty)
        {
            return mobileParty?.LeaderHero is not null
                   && Globals.PartyMilitiaMap.ContainsKey(mobileParty);
        }
    }
}
