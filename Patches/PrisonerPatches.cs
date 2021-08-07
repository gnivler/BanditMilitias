using System.Linq;
using Bandit_Militias.Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;


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
                if (prisonerCharacter?.PartyBelongedTo is null
                    || !prisonerCharacter.PartyBelongedTo.IsBM())
                {
                    //Mod.Log(">>> early TakePrisonerActionApplyInternalPatch");
                    return true;
                }

                //Mod.Log(">>> late TakePrisonerActionApplyInternalPatch");
                prisonerCharacter.RemoveMilitiaHero();
                return false;
            }
        }

        // prevents BM hero prisoners being taken after battle
        [HarmonyPatch(typeof(MapEvent), "LootDefeatedParties")]
        public static class MapEventFinishBattlePatch
        {
            private static void Prefix(MapEvent __instance)
            {
                var loserBMs = __instance.PartiesOnSide(__instance.DefeatedSide)
                    .Where(p => p.Party?.MobileParty?.LeaderHero?.CharacterObject is not null
                                && p.Party.MobileParty.LeaderHero.CharacterObject.StringId.EndsWith("Bandit_Militia")).ToList();
                foreach (var party in loserBMs)
                {
                    var heroes = party.Party.MemberRoster.RemoveIf(t => t.Character.IsHero)
                        .Where(h => h.Character.StringId.EndsWith("Bandit_Militia")).ToList();
                    for (var i = 0; i < heroes.Count; i++)
                    {
                        Mod.Log($">>> Killing {heroes[i].Character.Name} at LootDefeatedParties.");
                        heroes[i].Character.HeroObject.RemoveMilitiaHero();
                    }
                }
            }
        }

        // still blocks prisoners but apparently works with bandits reinforcing militias
        [HarmonyPatch(typeof(MapEventSide), "CaptureWoundedHeroes")]
        public static class MapEventSideCaptureWoundedHeroesPatch
        {
            private static bool Prefix(PartyBase defeatedParty)
            {
                if (defeatedParty?.MobileParty is not null
                    && defeatedParty.MobileParty.IsBM())
                {
                    defeatedParty.LeaderHero?.RemoveMilitiaHero();
                    return false;
                }

                return true;
            }
        }
    }
}
