using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using static Bandit_Militias.Helpers.Helper;

// ReSharper disable UnusedMember.Global 
// ReSharper disable UnusedType.Global 
// ReSharper disable UnusedMember.Local   
// ReSharper disable RedundantAssignment  
// ReSharper disable InconsistentNaming

namespace Bandit_Militias.Patches
{
    public class PrisonerPatches
    {
        // both patches appear to be needed in 1.5.8
        // blocks NPC battles from taking prisoners
        [HarmonyPatch(typeof(TakePrisonerAction), "ApplyInternal")]
        public class TakePrisonerActionApplyInternalPatch
        {
            private static bool Prefix(Hero prisonerCharacter)
            {
                if (prisonerCharacter?.PartyBelongedTo == null)
                {
                    return true;
                }

                return !IsBM(prisonerCharacter.PartyBelongedTo);
            }
        }

        // prevents BM hero prisoners being taken after battle
        [HarmonyPatch(typeof(MapEvent), "LootDefeatedParties")]
        public class MapEventFinishBattlePatch
        {
            private static void Prefix(MapEvent __instance)
            {
                var parties = CheckIfMilitiaMapEvent(__instance);
                foreach (var party in parties)
                {
                    var heroes = party.Party.MemberRoster.RemoveIf(x => x.Character.IsHero).ToList();
                    for (var i = 0; i < heroes.Count; i++)
                    {
                        Mod.Log($"Killing {heroes[i].Character.Name} at LootDefeatedParties.");
                        heroes[i].Character.HeroObject.KillHero();
                    }
                }
            }

            private static void Postfix(MapEvent __instance)
            {
                var parties = CheckIfMilitiaMapEvent(__instance);
                parties.Do(p => Trash(p.Party.MobileParty));
            }

            private static IEnumerable<MapEventParty> CheckIfMilitiaMapEvent(MapEvent __instance)
            {
                var loser = __instance.BattleState != BattleState.AttackerVictory
                    ? __instance.AttackerSide
                    : __instance.DefenderSide;
                var parties = loser.Parties.Where(x => Globals.PartyMilitiaMap.Keys.Any(y => y == x.Party.MobileParty)).ToList();
                if (loser.LeaderParty?.MobileParty != null &&
                    !parties.Any(x => Globals.PartyMilitiaMap.Keys.Any(y => y == x.Party.MobileParty)))
                {
                    return parties;
                }

                return parties;
            }

        }

        // still blocks prisoners but apparently works with bandits reinforcing militias
        [HarmonyPatch(typeof(MapEventSide), "CaptureWoundedHeroes")]
        public class MapEventSideCaptureWoundedHeroesPatch
        {
            private static bool Prefix(PartyBase defeatedParty)
            {
                return !IsBM(defeatedParty.MobileParty);
            }
        }
    }
}
