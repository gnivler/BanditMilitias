using System.Linq;
using Bandit_Militias.Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;


// ReSharper disable UnusedMember.Global 
// ReSharper disable UnusedType.Global 
// ReSharper disable UnusedMember.Local   
// ReSharper disable RedundantAssignment  
// ReSharper disable InconsistentNaming

namespace Bandit_Militias.Patches
{
    public static class PrisonerPatches
    {
        // prevents BM hero prisoners being taken after battle
        [HarmonyPatch(typeof(MapEvent), "FinishBattle")]
        public static class MapEventFinishBattlePatch
        {
            private static void Prefix(MapEvent __instance)
            {
                if (__instance.DefeatedSide is BattleSideEnum.None)
                {
                    return;
                }

                var loserBMs = __instance.PartiesOnSide(__instance.DefeatedSide)
                    .Where(p => p.Party?.MobileParty?.LeaderHero?.CharacterObject is not null
                                && p.Party.MobileParty.LeaderHero.CharacterObject.StringId.EndsWith("Bandit_Militia")).ToList();
                foreach (var party in loserBMs)
                {
                    var heroes = party.Party.MemberRoster.RemoveIf(t => t.Character.IsHero)
                        .Where(h => h.Character.StringId.EndsWith("Bandit_Militia")).ToList();
                    for (var i = 0; i < heroes.Count; i++)
                    {
                        Mod.Log($">>> Killing {heroes[i].Character.Name} at FinishBattle.");
                        heroes[i].Character.HeroObject.RemoveMilitiaHero();
                    }

                    // has yet to fire, needs adjustment if in fact any exist
                    if (party.Party.MobileParty.LeaderHero is null)
                    {
                        party.Party.MobileParty.SetCustomName(new TextObject("Leaderless Bandit Militia"));
                    }
                }

                Helper.DoPowerCalculations();
            }
        }

        // prevents BM hero prisoners being taken after battle
        [HarmonyPatch(typeof(MapEvent), "LootDefeatedParties")]
        public static class MapEventLootDefeatedPartiesPatch
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

                Helper.DoPowerCalculations();
            }
        }
    }
}
