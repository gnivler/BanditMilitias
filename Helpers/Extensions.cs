using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.ObjectSystem;

namespace BanditMilitias.Helpers
{
    public static class Extensions
    {
        private static readonly AccessTools.FieldRef<CampaignObjectManager, List<Hero>> AliveHeroes =
            AccessTools.FieldRefAccess<CampaignObjectManager, List<Hero>>("_aliveHeroes");

        private static readonly AccessTools.FieldRef<CampaignObjectManager, List<Hero>> DeadOrDisabledHeroes =
            AccessTools.FieldRefAccess<CampaignObjectManager, List<Hero>>("_deadOrDisabledHeroes");

        public static bool IsUsedByAQuest(this MobileParty mobileParty)
        {
            return Campaign.Current.VisualTrackerManager.CheckTracked(mobileParty);
        }

        public static bool IsTooBusyToMerge(this MobileParty mobileParty)
        {
            if (mobileParty == mobileParty?.MoveTargetParty?.MoveTargetParty)
            {
                return false;
            }

            return mobileParty.TargetParty is not null
                   || mobileParty.ShortTermTargetParty is not null
                   || mobileParty.ShortTermBehavior is AiBehavior.EngageParty
                       or AiBehavior.FleeToPoint
                       or AiBehavior.RaidSettlement;
        }

        public static void RemoveMilitiaHero(this Hero hero)
        {
            Traverse.Create(typeof(KillCharacterAction)).Method("MakeDead", hero).GetValue();
            AliveHeroes(Campaign.Current.CampaignObjectManager).Remove(hero);
            DeadOrDisabledHeroes(Campaign.Current.CampaignObjectManager).Remove(hero);
            MBObjectManager.Instance.UnregisterObject(hero.CharacterObject);
            MBObjectManager.Instance.UnregisterObject(hero);
        }

        // ReSharper disable once InconsistentNaming
        public static bool IsBM(this MobileParty mobileParty)
        {
            return mobileParty.PartyComponent is ModBanditMilitiaPartyComponent;
        }

        public static ModBanditMilitiaPartyComponent BM(this MobileParty mobileParty)
        {
            return (ModBanditMilitiaPartyComponent)mobileParty.PartyComponent;
        }
    }
}
