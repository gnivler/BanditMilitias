using System;
using System.Collections.Generic;
using System.Linq;
using BanditMilitias.Helpers;
using HarmonyLib;
using SandBox.View.Map;
using SandBox.ViewModelCollection.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using static BanditMilitias.Helpers.Helper;
using static BanditMilitias.Globals;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Local
// ReSharper disable RedundantAssignment
// ReSharper disable InconsistentNaming

namespace BanditMilitias.Patches
{
    public static class MiscPatches
    {
        // idea from True Battle Loot
        [HarmonyPatch(typeof(MapEventSide), "OnTroopKilled")]
        public static class MapEventSideOnTroopKilled
        {
            public static void Postfix(MapEventSide __instance, CharacterObject ____selectedSimulationTroop)
            {
                if (!Globals.Settings.UpgradeTroops || MapEvent.PlayerMapEvent is not null && ____selectedSimulationTroop is null)
                    return;
                EquipmentMap.Remove(____selectedSimulationTroop.StringId);
                // makes all loot drop in any BM-involved fight which isn't with the main party
                var BMs = __instance.Parties.WhereQ(p =>
                    p.Party.MobileParty?.PartyComponent is ModBanditMilitiaPartyComponent).SelectQ(p => p.Party);
                if (BMs.Any() && !__instance.IsMainPartyAmongParties())
                {
                    for (var index = 0; index < Equipment.EquipmentSlotLength; index++)
                    {
                        var item = ____selectedSimulationTroop.Equipment[index];
                        if (item.IsEmpty)
                            continue;

                        if (Rng.Next(0, 101) < 66)
                            continue;
                        if (LootRecord.TryGetValue(__instance, out _))
                            LootRecord[__instance].Add(new EquipmentElement(item));
                        else
                            LootRecord.Add(__instance, new List<EquipmentElement> { item });
                    }
                }
            }
        }

        [HarmonyPatch(typeof(BattleCampaignBehavior), "CollectLoots")]
        public static class MapEventSideDistributeLootAmongWinners
        {
            public static void Prefix(MapEvent mapEvent, PartyBase winnerParty, ref Dictionary<PartyBase, ItemRoster> lootedItems)
            {
                if (!Globals.Settings.UpgradeTroops || !mapEvent.HasWinner || !winnerParty.IsMobile || !winnerParty.MobileParty.IsBM())
                    return;
                if (LootRecord.TryGetValue(winnerParty.MapEventSide, out var equipment))
                {
                    var itemRoster = new ItemRoster();
                    foreach (var e in equipment)
                        itemRoster.AddToCounts(e, 1);

                    lootedItems.Add(winnerParty, itemRoster);
                    if (lootedItems[winnerParty].AnyQ(i => !i.IsEmpty))
                        UpgradeEquipment(winnerParty, lootedItems[winnerParty]);
                }

                Globals.LootRecord.Remove(winnerParty.MobileParty.MapEventSide);
            }
        }

        [HarmonyPatch(typeof(MapScreen), "OnInitialize")]
        public static class MapScreenOnInitializePatch
        {
            public static void Prefix()
            {
                if (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift))
                    Nuke();
            }

            public static void Postfix()
            {
                Log("MapScreen.OnInitialize");
                ClearGlobals();
                PopulateItems();
                Looters = Clan.BanditFactions.First(c => c.StringId == "looters");
                Globals.CampaignPeriodicEventManager = Traverse.Create(Campaign.Current).Field<CampaignPeriodicEventManager>("_campaignPeriodicEventManager").Value;
                Ticker = AccessTools.Field(typeof(CampaignPeriodicEventManager), "_partiesWithoutPartyComponentsPartialHourlyAiEventTicker").GetValue(Globals.CampaignPeriodicEventManager);
                Hideouts = Settlement.All.WhereQ(s => s.IsHideout).ToListQ();
                RaidCap = Convert.ToInt32(Settlement.FindAll(s => s.IsVillage).CountQ() / 10f);
                BMHeroes = CharacterObject.All.Where(c =>
                    c.Occupation is Occupation.Bandit && c.StringId.StartsWith("lord_")).ToListQ();

                var filter = new List<string>
                {
                    "regular_fighter",
                    "veteran_borrowed_troop",
                    "_basic_root", // MyLittleWarband StringIds
                    "_elite_root"
                };

                var allRecruits = CharacterObject.All.Where(c =>
                    c.Level == 11
                    && c.Occupation == Occupation.Soldier
                    && filter.All(s => !c.StringId.Contains(s))
                    && !c.StringId.EndsWith("_tier_1"));

                foreach (var recruit in allRecruits)
                {
                    if (Recruits.ContainsKey(recruit.Culture))
                    {
                        Recruits[recruit.Culture].Add(recruit);
                    }
                    else
                    {
                        Recruits.Add(recruit.Culture, new List<CharacterObject> { recruit });
                    }
                }

                // used for armour
                foreach (ItemObject.ItemTypeEnum itemType in Enum.GetValues(typeof(ItemObject.ItemTypeEnum)))
                {
                    Globals.ItemTypes[itemType] = Items.All.Where(i =>
                        i.Type == itemType
                        && i.Value >= 1000
                        && i.Value <= Globals.Settings.MaxItemValue).ToList();
                }

                // front-load
                for (var i = 0; i < 3000; i++)
                {
                    BanditEquipment.Add(BuildViableEquipmentSet());
                }

                DoPowerCalculations(true);
                var bmCount = MobileParty.All.CountQ(m => m.IsBM());
                Log($"Militias: {bmCount}."); //  Upgraded BM troops: {MobileParty.All.SelectMany(m => m.MemberRoster.ToFlattenedRoster()).CountQ(e => e.Troop.StringId.Contains("Bandit_Militia"))}.  Troop prisoners: {MobileParty.All.SelectMany(m => m.PrisonRoster.ToFlattenedRoster()).CountQ(e => e.Troop.StringId.Contains("Bandit_Militia"))}.");
            }
        }

        [HarmonyPatch(typeof(MapMobilePartyTrackerVM), MethodType.Constructor, typeof(Camera), typeof(Action<Vec2>))]
        public static class MapMobilePartyTrackerVMCtorPatch
        {
            public static void Postfix(MapMobilePartyTrackerVM __instance)
            {
                Globals.MapMobilePartyTrackerVM = __instance;
            }
        }

        [HarmonyPatch(typeof(SaveableCampaignTypeDefiner), "DefineContainerDefinitions")]
        public class SaveableCampaignTypeDefinerDefineContainerDefinitions
        {
            public static void Postfix(SaveableCampaignTypeDefiner __instance)
            {
                AccessTools.Method(typeof(CampaignBehaviorBase.SaveableCampaignBehaviorTypeDefiner),
                    "ConstructContainerDefinition").Invoke(__instance, new object[] { typeof(Dictionary<Hero, float>) });
                AccessTools.Method(typeof(CampaignBehaviorBase.SaveableCampaignBehaviorTypeDefiner),
                    "ConstructContainerDefinition").Invoke(__instance, new object[] { typeof(Dictionary<string, Equipment>) });
            }
        }
    }
}
