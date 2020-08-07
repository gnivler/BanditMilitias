using System.Linq;
using Bandit_Militias.Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;

// ReSharper disable UnusedMember.Global 
// ReSharper disable UnusedType.Global 
// ReSharper disable UnusedMember.Local   
// ReSharper disable RedundantAssignment  
// ReSharper disable InconsistentNaming

namespace Bandit_Militias.Patches
{
    public class PrisonerPatches
    {
        [HarmonyPatch(typeof(TakePrisonerAction), "Apply")]
        public class TakePrisonerActionApplyPatch
        {
            private static bool Prefix(Hero prisonerCharacter)
            {
                return !prisonerCharacter.NeverBecomePrisoner;
            }
        }

        [HarmonyPatch(typeof(MapEvent), "LootDefeatedParties")]
        public class MapEventFinishBattlePatch
        {
            private static void Prefix(MapEvent __instance)
            {
                var loser = __instance.BattleState != BattleState.AttackerVictory
                    ? __instance.AttackerSide
                    : __instance.DefenderSide;
                if (loser.LeaderParty.MobileParty != null &&
                    !loser.LeaderParty.MobileParty.StringId.StartsWith("Bandit_Militia") ||
                    loser.LeaderParty.MobileParty == null)
                {
                    return;
                }

                foreach (var party in loser.Parties)
                {
                    var heroes = party.MemberRoster.RemoveIf(x => x.Character.IsHero).ToList();
                    for (var i = 0; i < heroes.Count; i++)
                    {
                        Mod.Log($"Killing {heroes[i].Character.Name} at LootDefeatedParties");
                        heroes[i].Character.HeroObject.KillHero();
                    }
                }
            }
        }
    }
}
