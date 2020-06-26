using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors;

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

        // skips over the capture option, killing the bandit hero
        [HarmonyPatch(typeof(PlayerEncounter), "DoPlayerVictory")]
        public static class PlayerEncounterDoPlayerVictoryPatch
        {
            private static void Postfix(
                PartyBase ____encounteredParty,
                ref PlayerEncounterState ____mapEventState,
                List<TroopRosterElement> ____capturedHeroes,
                MapEvent ____mapEvent)
            {
                try
                {
                    if (____capturedHeroes == null)
                    {
                        if (____encounteredParty.Name.Equals("Bandit Militia"))
                        {
                            var partyBase = ____mapEvent.GetPartyReceivingLootShare(PartyBase.MainParty);
                            ____capturedHeroes = partyBase.PrisonRoster.RemoveIf(lordElement => lordElement.Character.IsHero).ToList();
                            if (____capturedHeroes.Count > 0)
                            {
                                var capturedHero = ____capturedHeroes[____capturedHeroes.Count - 1];
                                ____capturedHeroes.RemoveRange(____capturedHeroes.Count - 1, 1);
                                var hero = Hero.All.First(x => capturedHero.Character.StringId == x.StringId);
                                AccessTools.Method(typeof(KillCharacterAction), "ApplyInternal")
                                    .Invoke(null, AccessTools.all, null,
                                        new object[]
                                        {
                                            hero,
                                            Hero.MainHero,
                                            KillCharacterAction.KillCharacterActionDetail.DiedInBattle,
                                            true
                                        }, CultureInfo.InvariantCulture);
                                ____mapEventState = PlayerEncounterState.FreeHeroes;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Mod.Log(ex);
                }
            }
        }

        // blocks AI battles from taking Militia hero prisoners
        // need to replace the original since it only looks for one hero
        [HarmonyPatch(typeof(MapEventSide), "CaptureWoundedHeroes")]
        public static class MapEventCaptureWoundedHeroesPatch
        {
            private static bool Prefix(PartyBase defeatedParty)
            {
                try
                {
                    if (defeatedParty.Name.Equals("Bandit Militia"))
                    {
                        for (var i = 0; i < defeatedParty.MemberRoster.Count; i++)
                        {
                            var troop = defeatedParty.MemberRoster.GetElementCopyAtIndex(i);
                            if (troop.Character.IsHero)
                            {
                                defeatedParty.MemberRoster.AddToCountsAtIndex(i, -1, 0, 0, false);
                            }
                        }
        
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Mod.Log(ex);
                }
        
                return true;
            }
        }
    }
}
