using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
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
                if (hero is null
                    || !hero.CharacterObject.StringId.EndsWith("Bandit_Militia"))
                {
                    return;
                }

                Traverse.Create(hero).Field<Hero.CharacterStates>("_heroState").Value = Hero.CharacterStates.Dead;
                LocationComplex.Current?.RemoveCharacterIfExists(hero);
                Helper.RemoveHeroFromReadOnlyList(hero);
                Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<Hero>>("_aliveHeroes").Value.Remove(hero);
                if (hero.CurrentSettlement is not null)
                {
                    var heroesWithoutParty = Globals.HeroesWithoutParty(hero.CurrentSettlement);
                    Traverse.Create(heroesWithoutParty).Field<List<Hero>>("_list").Value.Remove(hero);
                }

                MBObjectManager.Instance.UnregisterObject(hero.CharacterObject);
                MBObjectManager.Instance.UnregisterObject(hero);
            }
            catch (Exception ex)
            {
                Mod.Log(ex);
            }
        }

        private static AccessTools.FieldRef<Campaign, MBReadOnlyList<CharacterObject>> _characters =
            AccessTools.FieldRefAccess<Campaign, MBReadOnlyList<CharacterObject>>("_characters");

        // unfortunately around 1ms even with FieldRef
        internal static void RemoveCharacterObject(this Hero hero)
        {
            try
            {
                var characters = _characters(Campaign.Current);
                var tempList = new List<CharacterObject>(characters.Except(new[] {hero.CharacterObject}));
                _characters(Campaign.Current) = new MBReadOnlyList<CharacterObject>(tempList);
                MBObjectManager.Instance.UnregisterObject(hero.CharacterObject);
            }
            catch (Exception ex)
            {
                FileLog.Log(ex.ToString());
            }
        }
    }
}
