using System;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors;
using TaleWorlds.Core;

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

        // this is copied from DoCaptureHeroes
        // kill all the heroes so they don't get captured
        // skips Capture by setting PlayerEncounterState.FreeHeroes 
        //[HarmonyPatch(typeof(PlayerEncounter), "DoPlayerVictory")]
        public static class PlayerEncounterDoPlayerVictoryPatch
        {
            private static void Postfix(PartyBase ____encounteredParty, ref PlayerEncounterState ____mapEventState)
            {
                var encounteredParty = ____encounteredParty;
                var mapEventState = ____mapEventState;

                try
                {
                    if (encounteredParty.Name.Equals("Bandit Militia"))
                    {
                        var capturedHeroes = PartyBase.MainParty.PrisonRoster.RemoveIf(x => x.Character.IsHero).ToList();
                        foreach (var hero in capturedHeroes)
                        {
                            hero.Character.HeroObject.KillHero();
                        }

                        mapEventState = PlayerEncounterState.FreeHeroes;
                    }
                }
                catch (Exception ex)
                {
                    Mod.Log(ex, LogLevel.Error);
                }
            }
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
                        //capturedHeroes.AddRange(party.MemberRoster.RemoveIf(x => x.Character.IsHero));
                    }
                    //
                    //for (var i = 0; i < capturedHeroes.Count; i++)
                    //{
                    //    capturedHeroes[i].Character.HeroObject.KillHero();
                    //}
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

        // blocks AI battles from taking Militia hero prisoners
        // need to replace the original since it only looks for one hero
        // TODO maybe double check this
        //[HarmonyPatch(typeof(MapEventSide), "CaptureWoundedHeroes")]
        public static class MapEventCaptureWoundedHeroesPatch
        {
            private static bool Prefix(PartyBase defeatedParty)
            {
                try
                {
                    if (defeatedParty.Name.Equals("Bandit Militia"))
                    {
                        var capturedHeroes = defeatedParty.MemberRoster.RemoveIf(x => x.Character.IsHero).ToList();
                        foreach (var hero in capturedHeroes)
                        {
                            Mod.Log("Culling militia hero", LogLevel.Debug);
                            hero.Character.HeroObject.KillHero();
                        }

                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Mod.Log(ex, LogLevel.Error);
                }

                return true;
            }
        }
    }
}
