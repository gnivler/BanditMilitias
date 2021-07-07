using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using static Bandit_Militias.Helpers.Helper;

// ReSharper disable UnusedMember.Global 
// ReSharper disable UnusedType.Global 
// ReSharper disable UnusedMember.Local   
// ReSharper disable RedundantAssignment  
// ReSharper disable InconsistentNaming

namespace Bandit_Militias.Patches
{
    public static class PrisonerPatches
    {
        // both patches necessary in 3.0.3 for 1.5.10
        // blocks NPC battles from taking prisoners
        [HarmonyPatch(typeof(TakePrisonerAction), "ApplyInternal")]
        public static class TakePrisonerActionApplyInternalPatch
        {
            private static bool Prefix(Hero prisonerCharacter)
            {
                if (prisonerCharacter?.PartyBelongedTo is null)
                {
                    return true;
                }

                return !prisonerCharacter.PartyBelongedTo.IsBM();
            }
        }

        // still blocks prisoners but apparently works with bandits reinforcing militias
        [HarmonyPatch(typeof(MapEventSide), "CaptureWoundedHeroes")]
        public static class MapEventSideCaptureWoundedHeroesPatch
        {
            private static bool Prefix(PartyBase defeatedParty)
            {
                return !defeatedParty.MobileParty.IsBM();
            }
        }
    }
}
