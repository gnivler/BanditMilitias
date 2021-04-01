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
                return !IsBanditMilitia(prisonerCharacter.PartyBelongedTo);
            }
        }

        // prevents prisoners being taken
        // bug possible - causing battles with reinforcements to crash at loot?
        [HarmonyPatch(typeof(MapEvent), "LootDefeatedParties")]
        public class MapEventFinishBattlePatch
        {
            private static void Prefix(MapEvent __instance)
            {
                var loser = __instance.BattleState != BattleState.AttackerVictory
                    ? __instance.AttackerSide
                    : __instance.DefenderSide;
                var parties = loser.Parties.Where(x => Globals.Militias.Any(y => y.MobileParty == x.Party.MobileParty)).ToList();
                if (loser.LeaderParty.MobileParty != null &&
                    !parties.Any(x => Globals.Militias.Any(y => y.MobileParty == x.Party.MobileParty)))
                {
                    return;
                }
                
                foreach (var party in parties)
                {
                    Globals.Militias.Remove(Militia.FindMilitiaByParty(party.Party.MobileParty));
                    var heroes = party.Party.MemberRoster.RemoveIf(x => x.Character.IsHero).ToList();
                    for (var i = 0; i < heroes.Count; i++)
                    {
                        Mod.Log($"Killing {heroes[i].Character.Name} at LootDefeatedParties.");
                        heroes[i].Character.HeroObject.KillHero();
                    }
                }
            }
        }

        // still blocks prisoners but apparently works with bandits reinforcing militias
        [HarmonyPatch(typeof(MapEventSide), "CaptureWoundedHeroes")]
        public class MapEventSideCaptureWoundedHeroesPatch
        {
            private static bool Prefix(PartyBase defeatedParty)
            {
                return !IsBanditMilitia(defeatedParty.MobileParty);
            }
        }
    }
}
