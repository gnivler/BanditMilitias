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

        internal static readonly AccessTools.FieldRef<Campaign, MBReadOnlyList<CharacterObject>> Characters
            = AccessTools.FieldRefAccess<Campaign, MBReadOnlyList<CharacterObject>>("_characters");

        // howitzer approach to lobotomize the game of bandit heroes
        internal static void RemoveMilitiaHero(this Hero hero)
        {
            try
            {
                // appoint the strongest BM as the leader if the current leader dies
                var leader = hero.Clan.Leader == hero;
                if (leader)
                {
                    hero.Clan.SetLeader(Globals.PartyMilitiaMap.First(k =>
                        k.Key.Party.TotalStrength >= Globals.PartyMilitiaMap.Max(m =>
                            m.Key.Party.TotalStrength)).Key.LeaderHero);
                }

                hero.Clan = null;
                hero.PartyBelongedTo?.MemberRoster.RemoveTroop(hero.CharacterObject);
                Traverse.Create(hero).Field<Hero.CharacterStates>("_heroState").Value = Hero.CharacterStates.NotSpawned;
                LocationComplex.Current?.RemoveCharacterIfExists(hero);
                //Helper.RemoveCharacterFromReadOnlyList(hero.CharacterObject);
                Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<Hero>>("_aliveHeroes").Value.Remove(hero);
                if (hero.CurrentSettlement is not null)
                {
                    var heroesWithoutParty = Globals.HeroesWithoutParty(hero.CurrentSettlement);
                    Traverse.Create(heroesWithoutParty).Field<List<Hero>>("_list").Value.Remove(hero);
                }

                var tempCharacterObjectList = new List<CharacterObject>(Characters(Campaign.Current));
                tempCharacterObjectList.Remove(hero.CharacterObject);
                Characters(Campaign.Current) = new MBReadOnlyList<CharacterObject>(tempCharacterObjectList);
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
