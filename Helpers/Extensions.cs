using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.LinQuick;
using TaleWorlds.ObjectSystem;

namespace BanditMilitias.Helpers
{
    internal static class Extensions
    {
        private static readonly AccessTools.FieldRef<CampaignObjectManager, List<Hero>> DeadOrDisabledHeroes =
            AccessTools.FieldRefAccess<CampaignObjectManager, List<Hero>>("_deadOrDisabledHeroes");

        internal static bool IsUsedByAQuest(this MobileParty mobileParty)
        {
            return Campaign.Current.VisualTrackerManager.CheckTracked(mobileParty);
        }

        internal static bool IsTooBusyToMerge(this MobileParty mobileParty)
        {
            return mobileParty.TargetParty is not null
                   || mobileParty.ShortTermTargetParty is not null
                   || mobileParty.ShortTermBehavior is AiBehavior.EngageParty
                       or AiBehavior.FleeToPoint
                       or AiBehavior.RaidSettlement;
        }

        internal static void RemoveMilitiaHero(this Hero hero)
        {
            MBObjectManager.Instance.UnregisterObject(hero.CharacterObject);
            KillCharacterAction.ApplyByRemove(hero);
            DeadOrDisabledHeroes(Campaign.Current.CampaignObjectManager).Remove(hero);
            Globals.Heroes.Remove(hero);
        }

        // ReSharper disable once InconsistentNaming
        internal static bool IsBM(this MobileParty mobileParty) => mobileParty?.PartyComponent is ModBanditMilitiaPartyComponent;

        // ReSharper disable once InconsistentNaming
        internal static ModBanditMilitiaPartyComponent GetBM(this MobileParty mobileParty)
        {
            if (mobileParty.PartyComponent is ModBanditMilitiaPartyComponent bm)
                return bm;

            return null;
        }

        internal static MobileParty FindParty(this CharacterObject characterObject)
        {
            var mobileParties = MobileParty.All.Concat(Settlement.All.Select(s => s.Party.MobileParty));
            foreach (var party in mobileParties)
            {
                if (party.MemberRoster.GetTroopRoster().WhereQ(t => t.Character.OriginalCharacter is not null)
                    .AnyQ(t => t.Character == characterObject))
                    return party;

                if (party.PrisonRoster.GetTroopRoster().WhereQ(t => t.Character.OriginalCharacter is not null)
                    .AnyQ(t => t.Character == characterObject))
                    return party;
            }

            return null;
        }

        internal static TroopRoster FindRoster(this CharacterObject characterObject)
        {
            return FindParty(characterObject).MemberRoster;
        }

        internal static int CountMounted(this TroopRoster troopRoster)
        {
            return troopRoster.GetTroopRoster().WhereQ(t => !t.Character.FirstBattleEquipment[10].IsEmpty).SumQ(t => t.Number);
        }

        internal static bool Contains(this Equipment equipment, EquipmentElement element)
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
