using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace BanditMilitias
{
    internal static class Extensions
    {
        private static readonly AccessTools.FieldRef<CampaignObjectManager, List<Hero>> DeadOrDisabledHeroes =
            AccessTools.FieldRefAccess<CampaignObjectManager, List<Hero>>("_deadOrDisabledHeroes");

        private static readonly AccessTools.FieldRef<MobileParty, bool> IsCurrentlyUsedByAQuest =
            AccessTools.FieldRefAccess<MobileParty, bool>("_isCurrentlyUsedByAQuest");

        internal static bool IsUsedByAQuest(this MobileParty mobileParty)
        {
            return IsCurrentlyUsedByAQuest(mobileParty);
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
        internal static bool IsBM(this CharacterObject characterObject) => characterObject.Occupation is Occupation.Bandit && characterObject.OriginalCharacter is not null && (characterObject.OriginalCharacter.StringId.StartsWith("bm_hero_") || characterObject.OriginalCharacter.StringId.StartsWith("lord_"));

        // ReSharper disable once InconsistentNaming
        internal static bool IsBM(this Hero hero) => hero.CharacterObject.IsBM();

        // ReSharper disable once InconsistentNaming
        internal static ModBanditMilitiaPartyComponent GetBM(this MobileParty mobileParty)
        {
            if (mobileParty.PartyComponent is ModBanditMilitiaPartyComponent bm)
                return bm;

            return null;
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
