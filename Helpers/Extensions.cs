using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.ObjectSystem;

namespace Bandit_Militias.Helpers
{
    public static class Extensions
    {
        private static readonly AccessTools.FieldRef<CampaignObjectManager, List<Hero>> aliveHeroes = AccessTools.FieldRefAccess<CampaignObjectManager, List<Hero>>("_aliveHeroes");
        private static readonly AccessTools.FieldRef<CampaignObjectManager, List<Hero>> deadOrDisabledHeroes = AccessTools.FieldRefAccess<CampaignObjectManager, List<Hero>>("_deadOrDisabledHeroes");

        internal static bool IsUsedByAQuest(this MobileParty mobileParty)
        {
            return Campaign.Current.VisualTrackerManager.CheckTracked(mobileParty);
        }

        //[HarmonyPatch(typeof(AiPatrollingBehavior), "AiHourlyTick")]
        //public class asidfjasoifj
        //{
        //    public static Exception Finalizer(Exception __exception, MobileParty mobileParty)
        //    {
        //        if (__exception is not null)
        //        {
        //            FileLog.Log(mobileParty.Name.ToString());
        //        }
        //
        //        return null;
        //    }
        //}
        
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

        internal static void RemoveMilitiaHero(this Hero hero)
        {
            Traverse.Create(typeof(KillCharacterAction)).Method("MakeDead", hero).GetValue();
            aliveHeroes(Campaign.Current.CampaignObjectManager).Remove(hero);
            deadOrDisabledHeroes(Campaign.Current.CampaignObjectManager).Remove(hero);
            MBObjectManager.Instance.UnregisterObject(hero.CharacterObject);
            MBObjectManager.Instance.UnregisterObject(hero);
        }

        // ReSharper disable once InconsistentNaming
        internal static bool IsBM(this MobileParty mobileParty)
        {
            return mobileParty.PartyComponent is ModBanditMilitiaPartyComponent;
        }
    }
}
