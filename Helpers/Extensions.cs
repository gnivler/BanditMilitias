using System.Collections.Generic;
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
            Globals.Heroes.Remove(hero);
            MBObjectManager.Instance.UnregisterObject(hero.CharacterObject);
        }

        // ReSharper disable once InconsistentNaming
        public static bool IsBM(this MobileParty mobileParty) => mobileParty?.PartyComponent is ModBanditMilitiaPartyComponent;

        // ReSharper disable once InconsistentNaming
        public static ModBanditMilitiaPartyComponent GetBM(this MobileParty mobileParty)
        {
            if (mobileParty.PartyComponent is ModBanditMilitiaPartyComponent bm)
                return bm;

            return null;
        }

        public static MobileParty FindParty(this CharacterObject characterObject, out bool prisoner)
        {
            foreach (var party in MobileParty.All)
            {
                if (party.MemberRoster.GetTroopRoster().WhereQ(t => t.Character.OriginalCharacter is not null).AnyQ(t => t.Character.StringId == characterObject.StringId))
                {
                    prisoner = false;
                    return party;
                }

                if (party.PrisonRoster.GetTroopRoster().WhereQ(t => t.Character.OriginalCharacter is not null).AnyQ(t => t.Character.StringId == characterObject.StringId))
                {
                    prisoner = true;
                    return party;
                }
            }

            prisoner = false;
            return null;
        }

        public static int CountMounted(this TroopRoster troopRoster)
        {
            return troopRoster.GetTroopRoster().WhereQ(t => !t.Character.FirstBattleEquipment[10].IsEmpty).SumQ(t => t.Number);
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
