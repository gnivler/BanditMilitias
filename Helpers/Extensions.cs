using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.LinQuick;
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
            return mobileParty?.PartyComponent is ModBanditMilitiaPartyComponent;
        }

        public static ModBanditMilitiaPartyComponent GetBM(this MobileParty mobileParty)
        {
            if (mobileParty.PartyComponent is ModBanditMilitiaPartyComponent BM)
            {
                return BM;
            }
            return null;
        }

        public static MobileParty FindParty(this CharacterObject characterObject)
        {
            return MobileParty.All.FirstOrDefaultQ(m => m.MemberRoster.Contains(characterObject));
        }

        public static int MountedCount(this TroopRoster troopRoster)
        {
            return troopRoster.GetTroopRoster().Count(t => !t.Character.BattleEquipments.First()[10].IsEmpty);
        }
    }
}
