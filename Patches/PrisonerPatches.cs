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
    public class PrisonerPatches
    {
        // blocks NPC battles from taking prisoners
        [HarmonyPatch(typeof(TakePrisonerAction), "ApplyInternal")]
        public class TakePrisonerActionApplyInternalPatch
        {
            private static bool Prefix(Hero prisonerCharacter)
            {
                return !prisonerCharacter.NeverBecomePrisoner;
            }
        }

        // still blocks post-battle dialog
        // still doesn't solve militias not reinforcing other battles
        [HarmonyPatch(typeof(MapEventSide), "CaptureWoundedHeroes")]
        public class MapEventSideCaptureWoundedHeroesPatch
        {
            private static bool Prefix(MapEventSide __instance, PartyBase defeatedParty)
            {
                return !defeatedParty.MobileParty.StringId.StartsWith("Bandit_Militia");
                //if (__instance?.Parties != null)
                //{
                //    return __instance.Parties.Select(x => x?.Party).All(x => x != defeatedParty);
                //}
                //
                //return true;
            }
        }

        // this blocks the prisoner dialog in player battles (no heroes to capture)
        // bug possible - causing battles with reinforcements to crash at loot?
        //[HarmonyPatch(typeof(MapEvent), "LootDefeatedParties")]
        //public class MapEventFinishBattlePatch
        //{
        //    private static void Prefix(MapEvent __instance)
        //    {
        //        var loser = __instance.BattleState != BattleState.AttackerVictory
        //            ? __instance.AttackerSide
        //            : __instance.DefenderSide;
        //        if (loser.LeaderParty.MobileParty != null &&
        //            !loser.LeaderParty.MobileParty.StringId.StartsWith("Bandit_Militia") ||
        //            loser.LeaderParty.MobileParty == null)
        //        {
        //            return;
        //        }
        //
        //        foreach (var party in loser.Parties)
        //        {
        //            Globals.Militias.Remove(Militia.FindMilitiaByParty(party.Party.MobileParty));
        //            var heroes = party.Party.MemberRoster.RemoveIf(x => x.Character.IsHero).ToList();
        //            for (var i = 0; i < heroes.Count; i++)
        //            {
        //                Mod.Log($"Killing {heroes[i].Character.Name} at LootDefeatedParties");
        //                heroes[i].Character.HeroObject.KillHero();
        //            }
        //        }
        //    }
        //}
    }
}
