using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.LinQuick;
using TaleWorlds.ObjectSystem;

namespace BanditMilitias.Helpers
{
    public static class Extensions
    {
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
            KillCharacterAction.ApplyByRemove(hero);
            DeadOrDisabledHeroes(Campaign.Current.CampaignObjectManager).Remove(hero);
            Globals.BanditMilitiaHeroes.Remove(hero);
            Globals.BanditMilitiaCharacters.Remove(hero.CharacterObject);
            MBObjectManager.Instance.UnregisterObject(hero.CharacterObject);
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
            foreach (var party in MobileParty.All)
            {
                if (party.MemberRoster.ToFlattenedRoster().Troops.AnyQ(troop => troop == characterObject)
                    || party.PrisonRoster.ToFlattenedRoster().Troops.AnyQ(troop => troop == characterObject))
                {
                    return party;
                }
            }

            return null;
        }

        public static int CountMounted(this TroopRoster troopRoster)
        {
            return troopRoster.GetTroopRoster().WhereQ(t => !t.Character.FirstBattleEquipment[10].IsEmpty).Sum(t => t.Number);
        }

        public static bool Contains(this Equipment equipment, EquipmentElement element)
        {
            for (var index = 0; index < Equipment.EquipmentSlotLength; index++)
            {
                if (equipment[index].Item?.StringId == element.Item?.StringId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
