using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors;
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
        // small parties splitting during testing were causing NREs
        [HarmonyPatch(typeof(PrisonerEscapeCampaignBehavior), "ApplyEscapeChanceToExceededPrisoners")]
        public static class PrisonerEscapeCampaignBehaviorApplyEscapeChanceToExceededPrisonersPatch
        {
            public static bool Prefix(CharacterObject character) => character != null;
        }

        // prevent message about (about to die) hero getting "captured by Neutral"
        [HarmonyPatch(typeof(DefaultLogsCampaignBehavior), "OnPrisonerTaken")]
        public static class DefaultLogsCampaignBehaviorOnPrisonerTakenPath
        {
            // skip the original method to silence logs
            private static bool Prefix(Hero hero) => !hero.Name.ToString().EndsWith("- Bandit Militia");
        }

        [HarmonyPatch(typeof(MapEvent), "LootDefeatedParties")]
        public class MapEventFinishBattlePatch
        {
            private static void Prefix(MapEvent __instance)
            {
                var loser = GetLosingSide(__instance);
                if (loser.LeaderParty.Name.Equals("Bandit Militia"))
                {
                    //var capturedHeroes = new List<TroopRosterElement>();
                    foreach (var party in loser.Parties)
                    {
                        foreach (var hero in party.MemberRoster.RemoveIf(x => x.Character.IsHero).ToList())
                        {
                            Mod.Log("Culling militia hero", LogLevel.Debug);
                            hero.Character.HeroObject.KillHero();
                        }
                    }
                }
            }

            private static MapEventSide GetLosingSide(MapEvent mapEvent)
            {
                if (mapEvent.BattleState != BattleState.AttackerVictory)
                {
                    return mapEvent.AttackerSide;
                }

                return mapEvent.DefenderSide;
            }
        }
    }
}
