using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;

// ReSharper disable UnusedMember.Global 
// ReSharper disable UnusedType.Global 
// ReSharper disable UnusedMember.Local   
// ReSharper disable RedundantAssignment  
// ReSharper disable InconsistentNaming

namespace Bandit_Militias.Prisoners
{
    public class Patches
    {
        // prevent message about (about to die) hero getting captured
        //[HarmonyPatch(typeof(DefaultLogsCampaignBehavior), "OnPrisonerTaken")]
        //public static class DefaultLogsCampaignBehaviorOnPrisonerTakenPath
        //{
        //    // skip the original method to silence logs
        //    private static bool Prefix(Hero hero) => !hero.Name.Equals("Bandit Militia");
        //}

        // prevent prisoners.  it might screw up distribution of heroes among participants, low benefit improvement
        [HarmonyPatch(typeof(TakePrisonerAction), "Apply")]
        public class TakePrisonerActionApplyPatch
        {
            private static bool Prefix(Hero prisonerCharacter) => !prisonerCharacter.Name.Equals("Bandit Militia");
        }

        [HarmonyPatch(typeof(MapEvent), "LootDefeatedParties")]
        public class MapEventFinishBattlePatch
        {
            private static void Prefix(MapEvent __instance)
            {
                var loser = __instance.BattleState != BattleState.AttackerVictory
                    ? __instance.AttackerSide
                    : __instance.DefenderSide;
                if (!loser.LeaderParty.Name.Equals("Bandit Militia"))
                {
                    return;
                }

                foreach (var party in loser.Parties)
                {
                    var heroes = party.MemberRoster.RemoveIf(x => x.Character.IsHero).ToList();
                    for (var i = 0; i < heroes.Count; i++)
                    {
                        Mod.Log("Killing militia hero at LootDefeatedParties", LogLevel.Debug);
                        heroes[i].Character.HeroObject.KillHero();
                    }
                }
            }
        }
    }
}
