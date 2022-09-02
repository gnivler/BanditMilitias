using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BanditMilitias.Patches;
using HarmonyLib;
using Helpers;
using SandBox.View.Map;
using SandBox.ViewModelCollection.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using static BanditMilitias.Globals;

// ReSharper disable InconsistentNaming  

namespace BanditMilitias.Helpers
{
    public static class Helper
    {
        private const float ReductionFactor = 0.8f;
        private const float SplitDivisor = 2;
        private const float RemovedHero = 1;

        public static readonly AccessTools.FieldRef<MobileParty, bool> IsBandit =
            AccessTools.FieldRefAccess<MobileParty, bool>("<IsBandit>k__BackingField");

        internal static readonly AccessTools.FieldRef<NameGenerator, TextObject[]> GangLeaderNames =
            AccessTools.FieldRefAccess<NameGenerator, TextObject[]>("_gangLeaderNames");

        internal static readonly AccessTools.FieldRef<Hero, Settlement> _homeSettlement = AccessTools.FieldRefAccess<Hero, Settlement>("_homeSettlement");

        private static readonly AccessTools.StructFieldRef<EquipmentElement, ItemModifier> ItemModifier =
            AccessTools.StructFieldRefAccess<EquipmentElement, ItemModifier>("<ItemModifier>k__BackingField");

        internal static readonly AccessTools.FieldRef<PartyBase, ItemRoster> ItemRoster =
            AccessTools.FieldRefAccess<PartyBase, ItemRoster>("<ItemRoster>k__BackingField");

        internal static readonly AccessTools.FieldRef<BasicCharacterObject, MBEquipmentRoster> EquipmentRoster =
            AccessTools.FieldRefAccess<BasicCharacterObject, MBEquipmentRoster>("_equipmentRoster");

        internal static readonly AccessTools.FieldRef<MBEquipmentRoster, List<Equipment>> Equipments =
            AccessTools.FieldRefAccess<MBEquipmentRoster, List<Equipment>>("_equipments");

        // ReSharper disable once StringLiteralTypo
        internal static readonly AccessTools.FieldRef<CharacterObject, bool> HiddenInEncyclopedia =
            AccessTools.FieldRefAccess<CharacterObject, bool>("<HiddenInEncylopedia>k__BackingField");

        internal static readonly AccessTools.FieldRef<CampaignObjectManager, MBReadOnlyList<MobileParty>> PartiesWithoutPartyComponent =
            AccessTools.FieldRefAccess<CampaignObjectManager, MBReadOnlyList<MobileParty>>("<PartiesWithoutPartyComponent>k__BackingField");

        private static readonly AccessTools.FieldRef<MobileParty, Clan> actualClan =
            AccessTools.FieldRefAccess<MobileParty, Clan>("_actualClan");

        internal static readonly AccessTools.FieldRef<MBObjectBase, bool> IsRegistered =
            AccessTools.FieldRefAccess<MBObjectBase, bool>("<IsRegistered>k__BackingField");

        internal static PartyUpgraderCampaignBehavior UpgraderCampaignBehavior;

        public static bool TrySplitParty(MobileParty mobileParty)
        {
            if (MilitiaPowerPercent > Globals.Settings.GlobalPowerPercent
                || mobileParty.Party.MemberRoster.TotalManCount / SplitDivisor - RemovedHero < Globals.Settings.MinPartySize
                || !mobileParty.IsBM()
                || mobileParty.IsTooBusyToMerge())
            {
                return false;
            }

            var roll = Rng.Next(0, 101);
            if (roll > Globals.Settings.RandomSplitChance
                || mobileParty.Party.MemberRoster.TotalManCount > Math.Max(1, CalculatedMaxPartySize * ReductionFactor))
            {
                return false;
            }

            var party1 = TroopRoster.CreateDummyTroopRoster();
            var party2 = TroopRoster.CreateDummyTroopRoster();
            var prisoners1 = TroopRoster.CreateDummyTroopRoster();
            var prisoners2 = TroopRoster.CreateDummyTroopRoster();
            var inventory1 = new ItemRoster();
            var inventory2 = new ItemRoster();
            SplitRosters(mobileParty, party1, party2, prisoners1, prisoners2, inventory1, inventory2);
            CreateSplitMilitias(mobileParty, party1, party2, prisoners1, prisoners2, inventory1, inventory2);
            return true;
        }

        private static void SplitRosters(MobileParty original, TroopRoster troops1, TroopRoster troops2,
            TroopRoster prisoners1, TroopRoster prisoners2, ItemRoster inventory1, ItemRoster inventory2)
        {
            //DeferringLogger.Instance.Debug?.Log($"Processing troops: {original.MemberRoster.Count} types, {original.MemberRoster.TotalManCount} in total");
            foreach (var rosterElement in original.MemberRoster.GetTroopRoster().WhereQ(x => x.Character.HeroObject is null))
            {
                //if (!IsRegistered(rosterElement.Character))
                //    Meow();
                SplitRosters(troops1, troops2, rosterElement);
            }

            if (original.PrisonRoster.TotalManCount > 0)
            {
                //DeferringLogger.Instance.Debug?.Log($"Processing prisoners: {original.PrisonRoster.Count} types, {original.PrisonRoster.TotalManCount} in total");
                foreach (var rosterElement in original.PrisonRoster.GetTroopRoster())
                {
                    SplitRosters(prisoners1, prisoners2, rosterElement);
                }
            }

            foreach (var item in original.ItemRoster)
            {
                if (string.IsNullOrEmpty(item.EquipmentElement.Item?.Name?.ToString()))
                {
                    DeferringLogger.Instance.Debug?.Log("Bad item: " + item.EquipmentElement);
                    continue;
                }

                var half = Math.Max(1, item.Amount / 2);
                inventory1.AddToCounts(item.EquipmentElement, half);
                var remainder = item.Amount % 2;
                inventory2.AddToCounts(item.EquipmentElement, half + remainder);
            }
        }

        private static void SplitRosters(TroopRoster roster1, TroopRoster roster2, TroopRosterElement rosterElement)
        {
            // toss a coin (to your Witcher)
            if (rosterElement.Number == 1)
            {
                if (Rng.Next(0, 2) == 0)
                {
                    roster1.AddToCounts(rosterElement.Character, 1);
                }
                else
                {
                    roster2.AddToCounts(rosterElement.Character, 1);
                }
            }
            else
            {
                var half = Math.Max(1, rosterElement.Number / 2);
                roster1.AddToCounts(rosterElement.Character, half);
                var remainder = rosterElement.Number % 2;
                roster2.AddToCounts(rosterElement.Character, Math.Max(1, half + remainder));
            }
        }

        private static void CreateSplitMilitias(MobileParty original, TroopRoster party1, TroopRoster party2,
            TroopRoster prisoners1, TroopRoster prisoners2, ItemRoster inventory1, ItemRoster inventory2)
        {
            try
            {
                while (party1.TotalManCount < Globals.Settings.MinPartySize)
                {
                    // using 1, not 0 because 0 is the BM hero
                    var troop = party1.GetCharacterAtIndex(Rng.Next(1, party1.Count));
                    if (!IsRegistered(troop))
                        Meow();
                    party1.AddToCounts(troop, 1);
                }

                while (party2.TotalManCount < Globals.Settings.MinPartySize)
                {
                    var troop = party2.GetCharacterAtIndex(Rng.Next(1, party1.Count));
                    if (!IsRegistered(troop))
                        Meow();
                    party2.AddToCounts(troop, 1);
                }

                if (original.ActualClan is null) Meow();

                var bm1 = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(original.ActualClan), m => m.ActualClan = original.ActualClan);
                var bm2 = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(original.ActualClan), m => m.ActualClan = original.ActualClan);
                var rosters1 = new[]
                {
                    party1,
                    prisoners1
                };
                var rosters2 = new[]
                {
                    party2,
                    prisoners2
                };
                InitMilitia(bm1, rosters1, original.Position2D);
                InitMilitia(bm2, rosters2, original.Position2D);
                bm1.GetBM().Avoidance = original.GetBM().Avoidance;
                bm2.GetBM().Avoidance = original.GetBM().Avoidance;
                DeferringLogger.Instance.Debug?.Log($">>> {bm1.Name} <- Split {original.Name} Split -> {bm2.Name}");
                ItemRoster(bm1.Party) = inventory1;
                ItemRoster(bm2.Party) = inventory2;
                bm1.Party.Visuals.SetMapIconAsDirty();
                bm2.Party.Visuals.SetMapIconAsDirty();
                Trash(original);
                DoPowerCalculations();
            }
            catch (Exception ex)
            {
                DeferringLogger.Instance.Debug?.Log(ex);
            }
        }

        private static readonly List<string> verbotenParties = new()
        {
            "ebdi_deserters_party",
            "caravan_ambush_quest",
            "arzagos_banner_piece_quest_raider_party",
            "istiana_banner_piece_quest_raider_party",
            "rescue_family_quest_raider_party",
            "destroy_raiders_conspiracy_quest",
            "radagos_raider_party",
            "locate_and_rescue_traveller_quest_raider_party",
            "company_of_trouble",
            "villagers_of_landlord_needs_access_to_village_common_quest",
            //Calradia Expanded Kingdoms in 3.0.2
            "manhunter"
        };

        public static bool IsAvailableBanditParty(MobileParty __instance)
        {
            return __instance.IsBandit
                   && __instance.CurrentSettlement is null
                   && __instance.MapEvent is null
                   && __instance.Party.MemberRoster.TotalManCount > 0
                   && !__instance.IsTooBusyToMerge()
                   && !__instance.IsUsedByAQuest()
                   && !verbotenParties.Contains(__instance.StringId);
        }

        public static TroopRoster[] MergeRosters(MobileParty sourceParty, MobileParty targetParty)
        {
            var outMembers = TroopRoster.CreateDummyTroopRoster();
            var outPrisoners = TroopRoster.CreateDummyTroopRoster();
            var members = new List<TroopRoster>
            {
                sourceParty.MemberRoster,
                targetParty.MemberRoster
            };

            var prisoners = new List<TroopRoster>
            {
                sourceParty.PrisonRoster,
                targetParty.PrisonRoster
            };

            foreach (var roster in members)
            {
                foreach (var element in roster.GetTroopRoster().WhereQ(e => !e.Character.IsHero))
                {
                    if (!IsRegistered(element.Character))
                        Meow();
                    outMembers.AddToCounts(element.Character, element.Number,
                        woundedCount: element.WoundedNumber, xpChange: element.Xp);
                    if (!IsRegistered(element.Character))
                        Meow();
                    if (outMembers.GetTroopRoster().AnyQ(r => !IsRegistered(r.Character)))
                        Meow();
                }
            }

            foreach (var roster in prisoners)
            {
                foreach (var element in roster.GetTroopRoster().WhereQ(e => e.Character?.HeroObject is null))
                {
                    if (!IsRegistered(element.Character))
                        Meow();
                    outPrisoners.AddToCounts(element.Character, element.Number,
                        woundedCount: element.WoundedNumber, xpChange: element.Xp);
                }
            }

            return new[]
            {
                outMembers,
                outPrisoners
            };
        }

        public static void Trash(MobileParty mobileParty)
        {
            try
            {
                mobileParty.IsActive = false;
                DestroyPartyAction.Apply(null, mobileParty);
            }
            catch (Exception ex)
            {
                DeferringLogger.Instance.Debug?.Log(ex);
            }

            mobileParty.LeaderHero?.RemoveMilitiaHero();
            var parties = PartiesWithoutPartyComponent(Campaign.Current.CampaignObjectManager).ToListQ();
            if (parties.Remove(mobileParty))
            {
                PartiesWithoutPartyComponent(Campaign.Current.CampaignObjectManager) = new MBReadOnlyList<MobileParty>(parties);
            }
        }

        public static void Nuke()
        {
            try
            {
                LegacyFlushBanditMilitias();
                foreach (var BM in GetCachedBMs(true))
                {
                    Trash(BM.MobileParty);
                }

                for (var index = 0; index < Troops.Count; index++)
                {
                    var troop = Troops[index];
                    try
                    {
                        troop.IsReady = false;
                        MBObjectManager.Instance.UnregisterObject(troop);
                        var party = troop.FindParty(out _); // TODO use bool
                        if (party is null)
                            continue;
                        if (party.MemberRoster.Contains(troop))
                            party.MemberRoster.RemoveTroop(troop);
                        else
                            party.PrisonRoster.RemoveTroop(troop);
                    }
                    catch (Exception ex)
                    {
                        DeferringLogger.Instance.Debug?.Log(ex);
                    }
                }

                var characters = MBObjectManager.Instance.GetObjectTypeList<CharacterObject>()
                    .WhereQ(c => c.Name.ToString().StartsWith("Upgraded")).ToListQ();
                for (var index = 0; index < characters.Count; index++)
                {
                    var troop = characters[index];
                    try
                    {
                        troop.IsReady = false;
                        MBObjectManager.Instance.UnregisterObject(troop);
                        var party = troop.FindParty(out _); // TODO use bool
                        if (party is null)
                            continue;
                        if (party.MemberRoster.Contains(troop))
                            party.MemberRoster.RemoveTroop(troop);
                        else
                            party.PrisonRoster.RemoveTroop(troop);
                    }
                    catch (Exception ex)
                    {
                        DeferringLogger.Instance.Debug?.Log(ex);
                    }
                }

                FlushMapEvents();
                Hacks.PurgeBadTroops();
                // update ticker - still required in 3.9?
                var _listField = Traverse.Create(Ticker).Field<IReadOnlyList<MobileParty>>("_list");
                var replacement = _listField.Value.ToListQ().Except(_listField.Value.WhereQ(m => m.IsBM()));
                _listField.Value = new MBReadOnlyList<MobileParty>(replacement.ToListQ());
                Traverse.Create(Ticker).Field<IReadOnlyList<MobileParty>>("_list").Value = new MBReadOnlyList<MobileParty>(new List<MobileParty>());
                PartyImageMap.Clear();
                // move these out of try?
                LootRecord.Clear();
                EquipmentMap.Clear();
                Troops.Clear();
                //BanditMilitiaCharacters.Clear();
                Heroes.Clear();
            }
            catch (Exception ex)
            {
                DeferringLogger.Instance.Debug?.Log(ex);
            }
        }

        // deprecated with 3.9 but necessary to clean up older versions
        private static void LegacyFlushBanditMilitias()
        {
            var parties = Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<MobileParty>>("_partiesWithoutPartyComponent").Value
                .WhereQ(m => m.IsBM()).ToListQ();
            if (parties.Count > 0)
            {
                Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<MobileParty>>("_partiesWithoutPartyComponent").Value =
                    Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<MobileParty>>("_partiesWithoutPartyComponent").Value.Except(parties).ToListQ();
                DeferringLogger.Instance.Debug?.Log($">>> FLUSH {parties.Count} {Globals.Settings.BanditMilitiaString}");
                foreach (var mobileParty in parties)
                {
                    try
                    {
                        Trash(mobileParty);
                    }
                    catch (Exception ex)
                    {
                        DeferringLogger.Instance.Debug?.Log(ex);
                        Meow();
                    }
                }
            }

            // still needed post 1.7?
            // prisoners somehow of settlements
            foreach (var settlement in Settlement.All
                         .WhereQ(s => s.Party.PrisonRoster.GetTroopRoster()
                             .AnyQ(e => e.Character.StringId.EndsWith("Bandit_Militia"))))
            {
                for (var i = 0; i < settlement.Party.PrisonRoster.Count; i++)
                    try
                    {
                        var prisoner = settlement.Party.PrisonRoster.GetCharacterAtIndex(i);
                        if (prisoner.StringId.EndsWith("Bandit_Militia"))
                        {
                            //Debugger.Break();
                            DeferringLogger.Instance.Debug?.Log($">>> FLUSH BM hero prisoner {prisoner.HeroObject?.Name} at {settlement.Name}.");
                            settlement.Party.PrisonRoster.AddToCounts(prisoner, -1);
                            prisoner.HeroObject.RemoveMilitiaHero();
                        }
                    }
                    catch (Exception ex)
                    {
                        DeferringLogger.Instance.Debug?.Log(ex);
                    }
            }

            var leftovers = Hero.AllAliveHeroes.WhereQ(h => h.StringId.EndsWith("Bandit_Militia")).ToListQ();
            for (var index = 0; index < leftovers.Count; index++)
            {
                var hero = leftovers[index];
                DeferringLogger.Instance.Debug?.Log("Removing leftover hero " + hero);
                hero.RemoveMilitiaHero();
            }
        }

        // deprecated
        public static void FlushPrisoners()
        {
            // stupid overkill, 0-sequences
            var prisoners = Hero.AllAliveHeroes.WhereQ(h =>
                h.CharacterObject.StringId.Contains("Bandit_Militia") && h.IsPrisoner).ToListQ();
            prisoners = prisoners.Concat(Hero.DeadOrDisabledHeroes.WhereQ(h => h.CharacterObject.StringId.Contains("Bandit_Militia") && h.IsPrisoner)).ToListQ();
            prisoners = prisoners.Concat(MobileParty.MainParty.PrisonRoster.GetTroopRoster().WhereQ(e => e.Character.StringId.Contains("Bandit_Militia")).SelectQ(e => e.Character.HeroObject)).ToListQ();
            for (var index = 0; index < prisoners.Count; index++)
            {
                Debugger.Break();
                var prisoner = prisoners[index];
                //DeferringLogger.Instance.Debug?.Log($"{new string('=', 80)}");
                //DeferringLogger.Instance.Debug?.Log($">>> PRISONER {prisoner.Name,-20}: {prisoner.IsPrisoner} ({prisoner.PartyBelongedToAsPrisoner is not null})");
                prisoner.RemoveMilitiaHero();
                //DeferringLogger.Instance.Debug?.Log($"{new string('=', 80)}");
            }

            foreach (var prisonRoster in MobileParty.All.SelectQ(m => m.PrisonRoster))
            {
                foreach (var element in prisonRoster.ToFlattenedRoster())
                {
                    if (element.Troop.StringId.Contains("Bandit_Militia"))
                    {
                        Debugger.Break();
                        prisonRoster.RemoveTroop(element.Troop);
                    }
                }
            }
        }

        public static void RemoveBMHeroesFromClanLeaderships()
        {
            foreach (var clan in Clan.BanditFactions)
            {
                if (clan.Leader?.StringId is not null &&
                    clan.Leader.StringId.EndsWith("Bandit_Militia"))
                {
                    clan.SetLeader(null);
                }
            }
        }

        private static void FlushMapEvents()
        {
            var mapEvents = Traverse.Create(Campaign.Current.MapEventManager).Field<List<MapEvent>>("_mapEvents").Value;
            for (var index = 0; index < mapEvents.Count; index++)
            {
                var mapEvent = mapEvents[index];
                if (mapEvent.InvolvedParties.AnyQ(p => p.IsMobile && p.MobileParty.IsBM()))
                {
                    var sides = Traverse.Create(mapEvent).Field<MapEventSide[]>("_sides").Value;
                    foreach (var side in sides)
                    {
                        foreach (var party in side.Parties.WhereQ(p => p.Party.IsMobile && p.Party.MobileParty.IsBM()))
                        {
                            // gets around a crash in UpgradeReadyTroops()
                            party.Party.MobileParty.IsActive = false;
                        }
                    }

                    DeferringLogger.Instance.Debug?.Log(">>> FLUSH MapEvent.");
                    Traverse.Create(mapEvent).Field<MapEventState>("_state").Value = MapEventState.Wait;
                    mapEvent.FinalizeEvent();
                }
            }
        }

        // Bob's Bandit Militia vs Ross' Bandit Militia
        // apologies to non-English players, I don't know how to localize this
        public static string Possess(string input)
        {
            // game tries to redraw the PartyNamePlateVM after combat with multiple militias
            // and crashes because mobileParty.Party.LeaderHero?.FirstName.ToString() is null
            if (input is null)
            {
                return null;
            }

            var lastChar = input[input.Length - 1];
            return $"{input}{(lastChar == 's' ? "'" : "'s")}";
        }

        public static void PopulateItems()
        {
            var verbotenItemsStringIds = new List<string>
            {
                "bound_adarga",
                "old_kite_sparring_shield_shoulder",
                "old_horsemans_kite_shield_shoulder",
                "western_riders_kite_sparring_shield_shoulder",
                "old_horsemans_kite_shield",
                "banner_mid",
                "banner_big",
                "campaign_banner_small",
                "battania_targe_b_sparring",
                "eastern_spear_1_t2_blunt",
                "khuzait_polearm_1_t4_blunt",
                "eastern_javelin_1_t2_blunt",
                "aserai_axe_2_t2_blunt",
                "battania_2haxe_1_t2_blunt",
                "western_javelin_1_t2_blunt",
                "empire_lance_1_t3_blunt",
                "billhook_polearm_t2_blunt",
                "vlandia_lance_1_t3_blunt",
                "sturgia_axe_2_t2_blunt",
                "northern_throwing_axe_1_t1_blunt",
                "northern_spear_1_t2_blunt",
                "torch",
                "wooden_sword_t1",
                "wooden_sword_t2",
                "wooden_2hsword_t1",
                "practice_spear_t1",
                "horse_whip",
                "push_fork",
                "mod_banner_1",
                "mod_banner_2",
                "mod_banner_3",
                "throwing_stone",
                "ballista_projectile",
                "ballista_projectile_burning",
                "boulder",
                "pot",
                "grapeshot_stack",
                "grapeshot_fire_stack",
                "grapeshot_projectile",
                "grapeshot_fire_projectile",
                "bound_desert_round_sparring_shield",
                "northern_round_sparring_shield",
                "western_riders_kite_sparring_shield",
                "western_kite_sparring_shield",
                "oval_shield",
                "old_kite_sparring_shield ",
                "western_kite_sparring_shield_shoulder"
            };

            var verbotenSaddles = new List<string>
            {
                "celtic_frost",
                "saddle_of_aeneas",
                "fortunas_choice",
                "aseran_village_harness",
                "bandit_saddle_steppe",
                "bandit_saddle_desert"
            };

            Mounts = Items.All.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.Horse)
                .WhereQ(i => !i.StringId.Contains("unmountable")).WhereQ(i => i.Value <= Globals.Settings.MaxItemValue).ToList();
            Saddles = Items.All.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.HorseHarness
                                            && !i.StringId.Contains("mule")
                                            && !verbotenSaddles.Contains(i.StringId)).WhereQ(i => i.Value <= Globals.Settings.MaxItemValue).ToList();
            var all = Items.All.WhereQ(i =>
                    !i.IsCraftedByPlayer
                    && i.ItemType is not ItemObject.ItemTypeEnum.Goods
                    && i.ItemType is not ItemObject.ItemTypeEnum.Horse
                    && i.ItemType is not ItemObject.ItemTypeEnum.HorseHarness
                    && i.ItemType is not ItemObject.ItemTypeEnum.Animal
                    && i.ItemType is not ItemObject.ItemTypeEnum.Banner
                    && i.ItemType is not ItemObject.ItemTypeEnum.Book
                    && i.ItemType is not ItemObject.ItemTypeEnum.Invalid
                    && i.ItemCategory.StringId != "garment")
                .WhereQ(i => i.Value <= Globals.Settings.MaxItemValue).ToList();
            var runningCivilizedMod = AppDomain.CurrentDomain.GetAssemblies().AnyQ(a => a.FullName.Contains("Civilized"));
            if (!runningCivilizedMod)
            {
                all = Items.All.WhereQ(i => !i.IsCivilian).ToList();
            }

            all.RemoveAll(item => verbotenItemsStringIds.Contains(item.StringId));
            Arrows = all.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.Arrows).WhereQ(i => i.Value <= Globals.Settings.MaxItemValue).ToList();
            Bolts = all.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.Bolts).WhereQ(i => i.Value <= Globals.Settings.MaxItemValue).ToList();
            var oneHanded = all.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.OneHandedWeapon);
            var twoHanded = all.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.TwoHandedWeapon);
            var polearm = all.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.Polearm);
            var thrown = all.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.Thrown);
            var shields = all.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.Shield);
            var bows = all.WhereQ(i => i.ItemType is ItemObject.ItemTypeEnum.Bow or ItemObject.ItemTypeEnum.Crossbow);
            var any = new List<ItemObject>(oneHanded.Concat(twoHanded).Concat(polearm).Concat(thrown).Concat(shields).Concat(bows).WhereQ(i => i.Value <= Globals.Settings.MaxItemValue).ToList());
            any.Do(i => EquipmentItems.Add(new EquipmentElement(i)));
        }

        // builds a set of 4 weapons that won't include more than 1 bow or shield, nor any lack of ammo
        public static Equipment BuildViableEquipmentSet()
        {
            //T.Restart();
            var gear = new Equipment();
            var haveShield = false;
            var haveBow = false;
            try
            {
                for (var slot = 0; slot < 4; slot++)
                {
                    EquipmentElement randomElement = default;
                    switch (slot)
                    {
                        case 0:
                        case 1:
                            randomElement = EquipmentItems.GetRandomElement();
                            break;
                        case 2 when !gear[3].IsEmpty:
                            randomElement = EquipmentItems.WhereQ(x =>
                                x.Item.ItemType != ItemObject.ItemTypeEnum.Bow &&
                                x.Item.ItemType != ItemObject.ItemTypeEnum.Crossbow).ToList().GetRandomElement();
                            break;
                        case 2:
                        case 3:
                            randomElement = EquipmentItems.GetRandomElement();
                            break;
                    }

                    if (randomElement.Item.HasArmorComponent)
                        ItemModifier(ref randomElement) = randomElement.Item.ArmorComponent.ItemModifierGroup?.ItemModifiers.GetRandomElementWithPredicate(i => i.PriceMultiplier > 1);

                    if (randomElement.Item.HasWeaponComponent)
                        ItemModifier(ref randomElement) = randomElement.Item.WeaponComponent.ItemModifierGroup?.ItemModifiers.GetRandomElementWithPredicate(i => i.PriceMultiplier > 1);

                    // matches here by obtaining a bow, which then stuffed ammo into [3]
                    if (slot == 3 && !gear[3].IsEmpty)
                        break;

                    if (randomElement.Item.ItemType is ItemObject.ItemTypeEnum.Bow or ItemObject.ItemTypeEnum.Crossbow)
                    {
                        if (slot < 3)
                        {
                            // try again, try harder
                            if (haveBow)
                            {
                                slot--;
                                continue;
                            }

                            haveBow = true;
                            gear[slot] = randomElement;
                            if (randomElement.Item.ItemType is ItemObject.ItemTypeEnum.Bow)
                                gear[3] = new EquipmentElement(Arrows.ToList()[Rng.Next(0, Arrows.Count)]);
                            else if (randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Crossbow)
                                gear[3] = new EquipmentElement(Bolts.ToList()[Rng.Next(0, Bolts.Count)]);
                            continue;
                        }

                        randomElement = EquipmentItems.WhereQ(x =>
                            x.Item.ItemType != ItemObject.ItemTypeEnum.Bow &&
                            x.Item.ItemType != ItemObject.ItemTypeEnum.Crossbow).ToList().GetRandomElement();
                    }

                    if (randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Shield)
                    {
                        // try again, try harder
                        if (haveShield)
                        {
                            slot--;
                            continue;
                        }

                        haveShield = true;
                    }

                    gear[slot] = randomElement;
                }

                gear[5] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.HeadArmor].GetRandomElement());
                gear[6] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.BodyArmor].GetRandomElement());
                gear[7] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.LegArmor].GetRandomElement());
                gear[8] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.HandArmor].GetRandomElement());
                gear[9] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.Cape].GetRandomElement());
            }
            catch (Exception ex)
            {
                DeferringLogger.Instance.Debug?.Log(ex);
                DeferringLogger.Instance.Debug?.Log($"Armour loaded: {ItemTypes.Select(k=>k.Value).Sum(v => v.Count)}\n\tNon-armour loaded: {Globals.EquipmentItems.Count}\n\tArrows:{Globals.Arrows.Count}\n\tBolts:{Globals.Bolts.Count}\n\tMounts: {Globals.Mounts.Count}\n\tSaddles: {Globals.Saddles.Count}");
            }

            //DeferringLogger.Instance.Debug?.Log($"GEAR ==> {T.ElapsedTicks / 10000F:F3}ms");
            return gear.Clone();
        }

        // game world measurement
        public static void DoPowerCalculations(bool force = false)
        {
            if (force || LastCalculated < CampaignTime.Now.ToHours - 8)
            {
                var parties = MobileParty.All.WhereQ(p => p.LeaderHero is not null && !p.IsBM()).ToListQ();
                var medianSize = (float)parties.OrderBy(p => p.MemberRoster.TotalManCount)
                    .ElementAt(parties.CountQ() / 2).MemberRoster.TotalManCount;
                Globals.CalculatedMaxPartySize = Math.Max(medianSize, Math.Max(1, MobileParty.MainParty.MemberRoster.TotalManCount) * Variance);
                //Globals.CalculatedMaxPartySize = Math.Max(Globals.CalculatedMaxPartySize, Globals.Settings.MinPartySize);
                Globals.LastCalculated = CampaignTime.Now.ToHours;
                Globals.CalculatedGlobalPowerLimit = parties.SumQ(p => p.Party.TotalStrength) * Variance;
                Globals.GlobalMilitiaPower = GetCachedBMs(true).SumQ(m => m.Party.TotalStrength);
                Globals.MilitiaPowerPercent = Globals.GlobalMilitiaPower / Globals.CalculatedGlobalPowerLimit * 100;
                Globals.MilitiaPartyAveragePower = Globals.GlobalMilitiaPower / GetCachedBMs().CountQ();
            }
        }

        // leveraged to make looters convert into troop types from nearby cultures
        public static CultureObject GetMostPrevalentFromNearbySettlements(Vec2 position)
        {
            const int arbitraryDistance = 100;
            var settlements = Settlement.FindSettlementsAroundPosition(position, arbitraryDistance, s => !s.IsHideout);
            var map = new Dictionary<CultureObject, int>();
            foreach (var settlement in settlements)
            {
                if (map.TryGetValue(settlement.Culture, out _))
                {
                    map[settlement.Culture]++;
                }
                else
                {
                    map.Add(settlement.Culture, 1);
                }
            }

            if (BlackFlag is not null)
            {
                map.Remove(BlackFlag);
            }

            var highest = map.WhereQ(x =>
                x.Value == map.Values.Max()).SelectQ(x => x.Key);
            var result = highest.ToList().GetRandomElement();
            return result ?? MBObjectManager.Instance.GetObject<CultureObject>("empire");
        }

        public static void ConvertLootersToRecruits(TroopRoster troopRoster, CultureObject culture, int numberToUpgrade)
        {
        }

        public static void PrintInstructionsAroundInsertion(List<CodeInstruction> codes, int insertPoint, int insertSize, int adjacentNum = 5)
        {
            DeferringLogger.Instance.Debug?.Log($"Inserting {insertSize} at {insertPoint}.");

            // in case insertPoint is near the start of the method's IL
            var adjustedAdjacent = codes.Count - adjacentNum >= 0 ? adjacentNum : Math.Max(0, codes.Count - adjacentNum);
            for (var i = 0; i < adjustedAdjacent; i++)
            {
                // codes[266 - 5 + 0].opcode
                // codes[266 - 5 + 4].opcode
                DeferringLogger.Instance.Debug?.Log($"{codes[insertPoint - adjustedAdjacent + i].opcode,-10}{codes[insertPoint - adjustedAdjacent + i].operand}");
            }

            for (var i = 0; i < insertSize; i++)
            {
                DeferringLogger.Instance.Debug?.Log($"{codes[insertPoint + i].opcode,-10}{codes[insertPoint + i].operand}");
            }

            // in case insertPoint is near the end of the method's IL
            adjustedAdjacent = insertPoint + adjacentNum <= codes.Count ? adjacentNum : Math.Max(codes.Count, adjustedAdjacent);
            for (var i = 0; i < adjustedAdjacent; i++)
            {
                // 266 + 2 - 5 + 0
                // 266 + 2 - 5 + 4
                DeferringLogger.Instance.Debug?.Log($"{codes[insertPoint + insertSize + adjustedAdjacent + i].opcode,-10}{codes[insertPoint + insertSize + adjustedAdjacent + i].operand}");
            }
        }

        public static void RemoveUndersizedTracker(MobileParty party)
        {
            if (!party.IsBM())
                Debugger.Break();
            if (party.MemberRoster.TotalManCount < Globals.Settings.TrackedSizeMinimum)
            {
                var tracker = Globals.MapMobilePartyTrackerVM.Trackers.FirstOrDefaultQ(t => t.TrackedParty == party);
                if (tracker is not null)
                    Globals.MapMobilePartyTrackerVM.Trackers.Remove(tracker);
            }
        }

        public static int NumMountedTroops(TroopRoster troopRoster)
        {
            return troopRoster.GetTroopRoster().WhereQ(e => e.Character.Equipment[10].Item is not null).SumQ(e => e.Number);
        }

        public static Hero CreateHero(Clan clan)
        {
            var hero = CustomizedCreateHeroAtOccupation(Hideouts.GetRandomElement(), clan);
            Heroes.Add(hero);
            if (Rng.Next(0, 2) == 0)
            {
                hero.UpdatePlayerGender(true);
                hero.FirstName.SetTextVariable("FEMALE", 1);
                StringHelpers.SetCharacterProperties("HERO", hero.CharacterObject, hero.FirstName);
            }

            EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, BanditEquipment.GetRandomElement());
            if (Globals.Settings.CanTrain)
            {
                hero.HeroDeveloper.AddPerk(DefaultPerks.Leadership.VeteransRespect);
                hero.HeroDeveloper.AddSkillXp(DefaultSkills.Leadership, 150);
            }

            return hero;
        }

        public static void AdjustCavalryCount(TroopRoster troopRoster)
        {
            try
            {
                while (NumMountedTroops(troopRoster) - Convert.ToInt32(troopRoster.TotalManCount / 2) is var delta && delta > 0)
                {
                    var mountedTroops = troopRoster.GetTroopRoster().WhereQ(c =>
                            !c.Character.Equipment[10].IsEmpty
                            && !c.Character.IsHero)
                        .WhereQ(c => !c.Character.Name.Contains("Upgraded"))
                        .ToListQ();
                    var element = mountedTroops.GetRandomElement();
                    var count = Rng.Next(1, delta + 1);
                    count = Math.Min(element.Number, count);
                    troopRoster.AddToCounts(element.Character, -count);
                }
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage("Problem adjusting cavalry count, please open a bug report."));
                DeferringLogger.Instance.Debug?.Log(ex);
            }
        }

        private static AccessTools.FieldRef<MobileParty, CampaignTime> ignoredUntilTime = AccessTools.FieldRefAccess<MobileParty, CampaignTime>("_ignoredUntilTime");

        private static void ConfigureMilitia(MobileParty mobileParty)
        {
            // this is a workaround because MobileParty.GetBestInitiativeBehavior checks a radius for all parties (saw up to 4 parties in range during testing)
            ignoredUntilTime(mobileParty) = CampaignTime.Never;
            mobileParty.LeaderHero.Gold = Convert.ToInt32(mobileParty.Party.TotalStrength * GoldMap[Globals.Settings.GoldReward.SelectedValue]);
            mobileParty.MemberRoster.AddToCounts(mobileParty.GetBM().Leader.CharacterObject, 1, false, 0, 0, true, 0);
            actualClan(mobileParty) = Clan.BanditFactions.First(c => c.Culture == mobileParty.HomeSettlement.Culture);
            if (PartyImageMap.TryGetValue(mobileParty, out _))
                PartyImageMap[mobileParty] = new ImageIdentifierVM(mobileParty.GetBM().Banner);
            else
                PartyImageMap.Add(mobileParty, new ImageIdentifierVM(mobileParty.GetBM().Banner));

            if (Rng.Next(0, 2) == 0)
            {
                var mount = Mounts.GetRandomElement();
                mobileParty.GetBM().Leader.BattleEquipment[10] = new EquipmentElement(mount);
                if (mount.HorseComponent.Monster.MonsterUsage == "camel")
                    mobileParty.GetBM().Leader.BattleEquipment[11] = new EquipmentElement(Saddles.WhereQ(saddle =>
                        saddle.StringId.Contains("camel")).ToList().GetRandomElement());
                else
                    mobileParty.GetBM().Leader.BattleEquipment[11] = new EquipmentElement(Saddles.WhereQ(saddle =>
                        !saddle.StringId.Contains("camel")).ToList().GetRandomElement());
            }

            mobileParty.SetCustomName(mobileParty.GetBM().Name);
            if (Globals.Settings.Trackers && mobileParty.MemberRoster.TotalManCount >= Globals.Settings.TrackedSizeMinimum)
            {
                var tracker = new MobilePartyTrackItemVM(mobileParty, MapScreen.Instance.MapCamera, null);
                Globals.MapMobilePartyTrackerVM.Trackers.Add(tracker);
            }
        }

        public static void TrainMilitia(MobileParty mobileParty)
        {
            try
            {
                if (mobileParty.MemberRoster.TotalManCount == 0)
                {
                    Meow();
                    DeferringLogger.Instance.Debug?.Log("Trying to configure militia with no troops, trashing");
                    Trash(mobileParty);
                    return;
                }

                if (!Globals.Settings.CanTrain || MilitiaPowerPercent > Globals.Settings.GlobalPowerPercent)
                    return;

                int iterations = default;
                switch (Globals.Settings.XpGift.SelectedValue)
                {
                    case "Off":
                        break;
                    case "Normal":
                        iterations = 1;
                        break;
                    case "Hard":
                        iterations = 2;
                        break;
                    case "Hardest":
                        iterations = 4;
                        break;
                }

                int number, numberToUpgrade;
                if (Globals.Settings.LooterUpgradePercent > 0)
                {
                    // upgrade any looters first, then go back over and iterate further upgrades
                    var allLooters = mobileParty.MemberRoster.GetTroopRoster().WhereQ(e =>
                        e.Character == Looters.BasicTroop
                        || e.Character.OriginalCharacter == Looters.BasicTroop).ToList();
                    if (allLooters.Any())
                    {
                        var culture = GetMostPrevalentFromNearbySettlements(mobileParty.Position2D);
                        foreach (var looter in allLooters)
                        {
                            number = looter.Number;
                            numberToUpgrade = Convert.ToInt32(number * Globals.Settings.LooterUpgradePercent / 100f);
                            if (numberToUpgrade == 0)
                                continue;

                            if (!IsRegistered(Looters.BasicTroop))
                                Meow();
                            mobileParty.MemberRoster.AddToCounts(Looters.BasicTroop, -numberToUpgrade);
                            var recruit = Globals.Recruits[culture][Rng.Next(0, Recruits[culture].Count)];
                            if (!IsRegistered(recruit))
                                Meow();
                            mobileParty.MemberRoster.AddToCounts(recruit, numberToUpgrade);
                        }
                    }
                }

                var troopUpgradeModel = Campaign.Current.Models.PartyTroopUpgradeModel;
                for (var i = 0; i < iterations && GlobalMilitiaPower <= Globals.Settings.GlobalPowerPercent; i++)
                {
                    var validTroops = mobileParty.MemberRoster.GetTroopRoster().WhereQ(x =>
                        x.Character.Tier < Globals.Settings.MaxTrainingTier
                        && !x.Character.IsHero
                        && troopUpgradeModel.IsTroopUpgradeable(mobileParty.Party, x.Character));
                    var troopToTrain = validTroops.ToList().GetRandomElement();
                    number = troopToTrain.Number;
                    if (number < 1)
                    {
                        continue;
                    }

                    var minNumberToUpgrade = Convert.ToInt32(Globals.Settings.UpgradeUnitsPercent / 100 * number * Rng.NextDouble());
                    minNumberToUpgrade = Math.Max(1, minNumberToUpgrade);
                    numberToUpgrade = Convert.ToInt32(Rng.Next(minNumberToUpgrade, Convert.ToInt32((number + 1) / 2f)));
                    DeferringLogger.Instance.Debug?.Log($"^^^ {mobileParty.LeaderHero.Name} is training up to {numberToUpgrade} of {number} \"{troopToTrain.Character.Name}\".");
                    var xpGain = numberToUpgrade * DifficultyXpMap[Globals.Settings.XpGift.SelectedValue];
                    mobileParty.MemberRoster.AddXpToTroop(xpGain, troopToTrain.Character);
                    UpgraderCampaignBehavior ??= Campaign.Current.GetCampaignBehavior<PartyUpgraderCampaignBehavior>();
                    UpgraderCampaignBehavior.UpgradeReadyTroops(mobileParty.Party);
                    if (Globals.Settings.TestingMode)
                    {
                        var party = Hero.MainHero.PartyBelongedTo ?? Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;
                        mobileParty.Position2D = party.Position2D;
                    }
                }
            }
            catch (Exception ex)
            {
                DeferringLogger.Instance.Debug?.Log("Bandit Militias is failing to configure parties!  Exception: " + ex);
                Trash(mobileParty);
            }
        }

        public static IEnumerable<ModBanditMilitiaPartyComponent> GetCachedBMs(bool forceRefresh = false)
        {
            if (forceRefresh || PartyCacheInterval < CampaignTime.Now.ToHours - 1)
            {
                PartyCacheInterval = CampaignTime.Now.ToHours;
                AllBMs = MobileParty.All.WhereQ(m => m.IsBM())
                    .SelectQ(m => m.PartyComponent as ModBanditMilitiaPartyComponent).ToListQ();
            }

            return AllBMs;
        }

        public static void InitMilitia(MobileParty militia, TroopRoster[] rosters, Vec2 position)
        {
            var index = Globals.MapMobilePartyTrackerVM.Trackers.FindIndexQ(t => t.TrackedParty == militia);
            if (index >= 0)
                Globals.MapMobilePartyTrackerVM.Trackers.RemoveAt(index);
            militia.InitializeMobilePartyAtPosition(rosters[0], rosters[1], position);
            ConfigureMilitia(militia);
            TrainMilitia(militia);
        }

        public static void LogMilitiaFormed(MobileParty mobileParty)
        {
            try
            {
                if (DeferringLogger.Instance.Debug is null)
                    return;
                var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
                var strengthString = $"{Math.Round(mobileParty.Party.TotalStrength)} strength";
                DeferringLogger.Instance.Debug?.Log($"{$"New Bandit Militia led by {mobileParty.LeaderHero?.Name}",-70} | {troopString,10} | {strengthString,12} | >>> {GlobalMilitiaPower / CalculatedGlobalPowerLimit * 100}%");
            }
            catch (Exception ex)
            {
                DeferringLogger.Instance.Debug?.Log(ex);
            }
        }

        public static void Meow()
        {
            if (SubModule.MEOWMEOW)
            {
                //Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
                //Debugger.Break();
            }
        }

        public static void DecreaseAvoidance(List<Hero> loserHeroes, MapEventParty mep)
        {
            foreach (var loserHero in loserHeroes)
            {
                if (mep.Party.MobileParty.GetBM().Avoidance.TryGetValue(loserHero, out _))
                    mep.Party.MobileParty.GetBM().Avoidance[loserHero] -= MilitiaBehavior.Increment;
                else
                    mep.Party.MobileParty.GetBM().Avoidance.Add(loserHero, Globals.Rng.Next(15, 35));
            }
        }

        public static Hero CustomizedCreateHeroAtOccupation(Settlement settlement, Clan clan)
        {
            var max = 0;
            foreach (var characterObject in HeroTemplates)
            {
                var num = characterObject.GetTraitLevel(DefaultTraits.Frequency) * 10;
                max += num > 0 ? num : 100;
            }

            var template = (CharacterObject)null;
            var num1 = settlement.RandomIntWithSeed((uint)Rng.Next(), 1, max);
            foreach (var characterObject in HeroTemplates)
            {
                var num2 = characterObject.GetTraitLevel(DefaultTraits.Frequency) * 10;
                num1 -= num2 > 0 ? num2 : 100;
                if (num1 < 0)
                {
                    template = characterObject;
                    break;
                }
            }

            var specialHero = HeroCreator.CreateSpecialHero(template, settlement, clan, clan);
            var num3 = MBRandom.RandomFloat * 20f;
            specialHero.AddPower(num3);
            specialHero.ChangeState(Hero.CharacterStates.Active);
            GiveGoldAction.ApplyBetweenCharacters(null, specialHero, 10000, true);
            specialHero.SupporterOf = specialHero.Clan;
            Traverse.Create(typeof(HeroCreator)).Method("AddRandomVarianceToTraits", specialHero);
            return specialHero;
        }
    }
}
