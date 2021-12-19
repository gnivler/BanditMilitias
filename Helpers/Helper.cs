using System;
using System.Collections.Generic;
using System.Linq;
using Bandit_Militias.Patches;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors;
using TaleWorlds.CampaignSystem.SandBox.GameComponents.Map;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.ObjectSystem;
using static Bandit_Militias.Globals;

// ReSharper disable InconsistentNaming  

namespace Bandit_Militias.Helpers
{
    public static class Helper
    {
        internal static List<ItemObject> Mounts;
        internal static List<ItemObject> Saddles;

        internal static void TrySplitParty(MobileParty mobileParty)
        {
            if (MilitiaPowerPercent > Globals.Settings.GlobalPowerPercent
                || mobileParty.MemberRoster.TotalManCount < MinSplitSize
                || !mobileParty.IsBM()
                || mobileParty.IsTooBusyToMerge())
            {
                return;
            }

            var roll = Rng.Next(0, 101);
            if (roll > Globals.Settings.RandomSplitChance
                || mobileParty.Party.TotalStrength > CalculatedMaxPartyStrength * (1 + Globals.Settings.SplitStrengthPercent / 100) * Variance
                || mobileParty.Party.MemberRoster.TotalManCount > Math.Max(1, CalculatedMaxPartySize * Globals.Settings.SplitSizePercent * Variance))
            {
                return;
            }

            var party1 = TroopRoster.CreateDummyTroopRoster();
            var party2 = TroopRoster.CreateDummyTroopRoster();
            var prisoners1 = TroopRoster.CreateDummyTroopRoster();
            var prisoners2 = TroopRoster.CreateDummyTroopRoster();
            var inventory1 = new ItemRoster();
            var inventory2 = new ItemRoster();
            SplitRosters(mobileParty, party1, party2, prisoners1, prisoners2, inventory1, inventory2);
            CreateSplitMilitias(mobileParty, party1, party2, prisoners1, prisoners2, inventory1, inventory2);
        }

        private static void SplitRosters(MobileParty original, TroopRoster troops1, TroopRoster troops2,
            TroopRoster prisoners1, TroopRoster prisoners2, ItemRoster inventory1, ItemRoster inventory2)
        {
            Mod.Log($"Processing troops: {original.MemberRoster.Count} types, {original.MemberRoster.TotalManCount} in total");
            foreach (var rosterElement in original.MemberRoster.GetTroopRoster().Where(x => x.Character.HeroObject is null))
            {
                SplitRosters(troops1, troops2, rosterElement);
            }

            if (original.PrisonRoster.TotalManCount > 0)
            {
                Mod.Log($"Processing prisoners: {original.PrisonRoster.Count} types, {original.PrisonRoster.TotalManCount} in total");
                foreach (var rosterElement in original.PrisonRoster.GetTroopRoster())
                {
                    SplitRosters(prisoners1, prisoners2, rosterElement);
                }
            }

            foreach (var item in original.ItemRoster)
            {
                if (string.IsNullOrEmpty(item.EquipmentElement.Item?.Name?.ToString()))
                {
                    Mod.Log("Bad item: " + item.EquipmentElement);
                    continue;
                }

                var half = Math.Max(1, item.Amount / 2);
                inventory1.AddToCounts(item.EquipmentElement, half);
                var remainder = item.Amount % 2;
                inventory2.AddToCounts(item.EquipmentElement, half + remainder);
            }
        }

        private static void SplitRosters(TroopRoster troops1, TroopRoster troops2, TroopRosterElement rosterElement)
        {
            // toss a coin (to your Witcher)
            if (rosterElement.Number == 1)
            {
                if (Rng.Next(0, 2) == 0)
                {
                    troops1.AddToCounts(rosterElement.Character, 1);
                }
                else
                {
                    troops2.AddToCounts(rosterElement.Character, 1);
                }
            }
            else
            {
                var half = Math.Max(1, rosterElement.Number / 2);
                troops1.AddToCounts(rosterElement.Character, half);
                var remainder = rosterElement.Number % 2;
                troops2.AddToCounts(rosterElement.Character, Math.Max(1, half + remainder));
            }
        }

        private static void CreateSplitMilitias(MobileParty original, TroopRoster party1, TroopRoster party2,
            TroopRoster prisoners1, TroopRoster prisoners2, ItemRoster inventory1, ItemRoster inventory2)
        {
            try
            {
                var militia1 = new Militia(original.Position2D, party1, prisoners1);
                var militia2 = new Militia(original.Position2D, party2, prisoners2);
                Mod.Log($"{militia1.MobileParty.StringId} <- Split -> {militia2.MobileParty.StringId}");
                Traverse.Create(militia1.MobileParty.Party).Property("ItemRoster").SetValue(inventory1);
                Traverse.Create(militia2.MobileParty.Party).Property("ItemRoster").SetValue(inventory2);
                militia1.MobileParty.Party.Visuals.SetMapIconAsDirty();
                militia2.MobileParty.Party.Visuals.SetMapIconAsDirty();
                Trash(original);
                DoPowerCalculations();
            }
            catch (Exception ex)
            {
                Mod.Log(ex);
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

        // todo more robust solution
        internal static bool IsAvailableBanditParty(MobileParty __instance)
        {
            if (__instance.IsBandit
                || __instance.IsBM()
                && __instance.Party.IsMobile
                && __instance.CurrentSettlement is null
                && __instance.Party.MemberRoster.TotalManCount > 0
                && !__instance.IsTooBusyToMerge()
                && !__instance.IsUsedByAQuest()
                && !verbotenParties.Contains(__instance.StringId))
            {
                return true;
            }

            return false;
        }

        internal static TroopRoster[] MergeRosters(MobileParty sourceParty, PartyBase targetParty)
        {
            var troopRoster = TroopRoster.CreateDummyTroopRoster();
            var prisonerRoster = TroopRoster.CreateDummyTroopRoster();
            var rosters = new List<TroopRoster>
            {
                sourceParty.MemberRoster,
                targetParty.MemberRoster
            };

            var prisoners = new List<TroopRoster>
            {
                sourceParty.PrisonRoster,
                targetParty.PrisonRoster
            };

            // dumps all bandit heroes (shouldn't be more than 2 though...)
            foreach (var roster in rosters)
            {
                foreach (var element in roster.GetTroopRoster().Where(e => e.Character?.HeroObject is null))
                {
                    troopRoster.AddToCounts(element.Character, element.Number,
                        woundedCount: element.WoundedNumber, xpChange: element.Xp);
                }
            }

            foreach (var roster in prisoners)
            {
                foreach (var element in roster.GetTroopRoster().Where(e => e.Character?.HeroObject is null))
                {
                    prisonerRoster.AddToCounts(element.Character, element.Number,
                        woundedCount: element.WoundedNumber, xpChange: element.Xp);
                }
            }

            return new[]
            {
                troopRoster,
                prisonerRoster
            };
        }

        private static void PurgeList(string logMessage, List<MobileParty> mobileParties)
        {
            if (mobileParties.Count > 0)
            {
                Mod.Log(logMessage);
                foreach (var mobileParty in mobileParties)
                {
                    Mod.Log(">>> FLUSH " + mobileParty.Name);
                    Trash(mobileParty);
                }
            }
        }

        internal static void Trash(MobileParty mobileParty)
        {
            //Mod.Log("Trashing " + mobileParty.Name);
            PartyMilitiaMap.Remove(mobileParty);
            mobileParty.LeaderHero?.RemoveMilitiaHero();
            DestroyPartyAction.Apply(null, mobileParty);
        }

        internal static void Nuke()
        {
            Mod.Log("Clearing mod data.");
            FlushBanditMilitias();
            FlushDeadBanditRemnants();
            FlushHeroes();
            FlushMilitiaCharacterObjects();
            FlushMapEvents();
            Flush();
            DoPowerCalculations(true);
        }

        private static void FlushBanditMilitias()
        {
            PartyMilitiaMap.Clear();
            var hasLogged = false;
            var partiesToRemove = MobileParty.All.Where(m => m.StringId.Contains("Bandit_Militia")).ToList();
            foreach (var mobileParty in partiesToRemove)
            {
                if (!hasLogged)
                {
                    Mod.Log($">>> FLUSH {partiesToRemove.Count} Bandit Militias");
                    hasLogged = true;
                }

                Trash(mobileParty);
            }
        }

        // still needed 1.5.10
        private static void FlushDeadBanditRemnants()
        {
            // prisoners somehow of settlements
            foreach (var settlement in Settlement.All)
            {
                var count = settlement.Parties.Sum(p => p.PrisonRoster.Count);
                if (count <= 0)
                {
                    continue;
                }

                foreach (var party in settlement.Parties)
                {
                    for (var i = 0; i < party.PrisonRoster.Count; i++)
                    {
                        try
                        {
                            var prisoner = party.PrisonRoster.GetCharacterAtIndex(i);
                            if (prisoner.StringId.Contains("Bandit_Militia"))
                            {
                                Mod.Log($">>> FLUSH dead bandit hero prisoner {prisoner.HeroObject.Name} at {settlement.Name}.");
                                party.PrisonRoster.AddToCounts(prisoner, -1);
                                MBObjectManager.Instance.UnregisterObject(prisoner);
                            }
                        }
                        catch (Exception ex)
                        {
                            Mod.Log(ex);
                        }
                    }
                }
            }

            // prisoners somehow of parties
            foreach (var mobileParty in MobileParty.All.Where(x => x.Party.PrisonRoster.TotalManCount > 0))
            {
                // GetCharacterAtIndex() throws, if 0...
                var count = Traverse.Create(mobileParty.Party.PrisonRoster).Field<int>("_count").Value;
                if (count < 0)
                {
                    continue;
                }

                for (var i = 0; i < count; i++)
                {
                    var prisoner = mobileParty.Party.PrisonRoster.GetCharacterAtIndex(i);
                    if (prisoner.IsHero &&
                        !prisoner.HeroObject.IsAlive)
                    {
                        mobileParty.Party.PrisonRoster.AddToCounts(prisoner, -1);
                        Mod.Log($">>> FLUSH Dead bandit hero mobile party prisoner {prisoner.HeroObject.Name}.");
                    }
                }
            }
        }

        // TODO verify if needed post-1.6.4
        internal static void FlushMilitiaCharacterObjects()
        {
            // possibly fixed by TWE in 1.6.4
            // leak from CampaignTickPatch, trashing parties there doesn't get rid of all remnants...
            // many hours trying to find proper solution
            // 2-4 ms
            //var COs = new List<CharacterObject>();
            var COs = MBObjectManager.Instance.GetObjectTypeList<CharacterObject>();
            var BMs = COs.WhereQ(c => c.HeroObject?.PartyBelongedTo is null
                                      && c.StringId.EndsWith("Bandit_Militia")
                                      && c.HeroObject is not null
                                      && !c.HeroObject.IsFactionLeader).ToList();
            if (BMs.Any())
            {
                Mod.Log($">>> FLUSH {BMs.Count} BM CharacterObjects");
                BMs.Do(c => MBObjectManager.Instance.UnregisterObject(c));
                var charactersField = Traverse.Create(Campaign.Current).Field<MBReadOnlyList<CharacterObject>>("_characters");
                var tempCharacterObjectList = new List<CharacterObject>(charactersField.Value);
                tempCharacterObjectList = tempCharacterObjectList.Except(BMs).ToListQ();
                charactersField.Value = new MBReadOnlyList<CharacterObject>(tempCharacterObjectList);
            }

            Mod.Log("");
            Mod.Log($"{new string('=', 80)}\nBMs: {PartyMilitiaMap.Count,-4} Power: {GlobalMilitiaPower} / Power Limit: {CalculatedGlobalPowerLimit} = {GlobalMilitiaPower / CalculatedGlobalPowerLimit * 100:f2}% (limit {Globals.Settings.GlobalPowerPercent}%)");
            Mod.Log("");
        }

        internal static void ReHome()
        {
            var tempList = PartyMilitiaMap.Values.Where(x => x?.Hero?.HomeSettlement is null).Select(x => x.Hero).ToList();
            Mod.Log($"Fixing {tempList.Count} null HomeSettlement heroes");
            tempList.Do(x => Traverse.Create(x).Field("_homeSettlement").SetValue(Hideouts.GetRandomElement()));
        }

        internal static void Flush()
        {
            FlushHideoutsOfMilitias();
            FlushNullPartyHeroes();
            FlushEmptyMilitiaParties();
            FlushNeutralBanditParties();
            FlushZeroParties();
            RemoveBMHeroesFromClanLeaderships();
            Mod.Log(">>> FLUSHED.");
        }

        internal static void RemoveBMHeroesFromClanLeaderships()
        {
            foreach (var clan in Clan.BanditFactions)
            {
                // if (clan.Leader?.StringId is not null &&
                //     clan.Leader.StringId.EndsWith("Bandit_Militia"))
                // {
                clan.SetLeader(null);
                // }
            }
        }

        // a bunch of hacks to dispose of leak(s) somewhere
        private static void FlushHeroes()
        {
            var BMHeroes = Hero.AllAliveHeroes.Where(h => h.CharacterObject.StringId.EndsWith("Bandit_Militia")).ToList();
            for (var i = 0; i < BMHeroes.Count; i++)
            {
                Mod.Log(">>> FLUSH hero: " + BMHeroes[i].Name);
                try
                {
                    BMHeroes[i].RemoveMilitiaHero();
                }
                catch (NullReferenceException)
                {
                    // 3.0.2 throws with Heroes Must Die
                    Mod.Log("Squelching NRE at Helper.FlushHeroes");
                }
            }
        }

        private static void FlushZeroParties()
        {
            var parties = MobileParty.All.Where(m =>
                    m != MobileParty.MainParty
                    && m.CurrentSettlement is null
                    && m.MemberRoster.TotalManCount == 0)
                .ToList();
            for (var i = 0; i < parties.Count; i++)
            {
                Mod.Log(">>> FLUSH party without a current settlement or any troops.");
                parties[i].LeaderHero.RemoveMilitiaHero();
                parties[i].RemoveParty();
            }
        }

        private static void FlushMapEvents()
        {
            var mapEvents = Traverse.Create(Campaign.Current.MapEventManager).Field("_mapEvents").GetValue<List<MapEvent>>();
            for (var index = 0; index < mapEvents.Count; index++)
            {
                var mapEvent = mapEvents[index];
                if (mapEvent.InvolvedParties.Any(p =>
                        p.Id.Contains("Bandit_Militia")))
                {
                    Mod.Log(">>> FLUSH MapEvent.");
                    mapEvent.FinalizeEvent();
                }
            }
        }

        private static void FlushHideoutsOfMilitias()
        {
            foreach (var hideout in Settlement.All.Where(s => s.IsHideout).ToList())
            {
                for (var index = 0; index < hideout.Parties.Count; index++)
                {
                    var party = hideout.Parties[index];
                    if (party.IsBM())
                    {
                        Mod.Log(">>> FLUSH Hideout.");
                        LeaveSettlementAction.ApplyForParty(party);
                        SetMilitiaPatrol(party);
                    }
                }
            }
        }

        private static void FlushNullPartyHeroes()
        {
            var heroes = Hero.AllAliveHeroes.Where(h =>
                    h.StringId.Contains("Bandit_Militia")
                    && h.PartyBelongedTo is null)
                .ToList();

            var hasLogged = false;
            foreach (var hero in heroes)
            {
                if (!hasLogged)
                {
                    hasLogged = true;
                    Mod.Log($">>> FLUSH {heroes.Count} null-party heroes.");
                }

                Mod.Log($">>> FLUSH {hero.StringId}");
                hero.RemoveMilitiaHero();
            }
        }


        private static void FlushNeutralBanditParties()
        {
            var tempList = new List<MobileParty>();
            foreach (var mobileParty in PartyMilitiaMap.Values.Where(x => x.MobileParty.MapFaction == CampaignData.NeutralFaction))
            {
                Mod.Log("This bandit shouldn't exist " + mobileParty.MobileParty + " size " + mobileParty.MobileParty.MemberRoster.TotalManCount);
                tempList.Add(mobileParty.MobileParty);
            }

            PurgeList($"FlushNeutralBanditParties Clearing {tempList.Count} weird neutral parties", tempList);
        }

        private static void FlushEmptyMilitiaParties()
        {
            var tempList = new List<MobileParty>();
            foreach (var mobileParty in PartyMilitiaMap.Values.Where(x => x.MobileParty.MemberRoster.TotalManCount == 0))
            {
                tempList.Add(mobileParty.MobileParty);
            }

            PurgeList($"FlushEmptyMilitiaParties Clearing {tempList.Count} empty parties", tempList);
        }

        // Bob's Bandit Militia vs Ross' Bandit Militia
        internal static string Possess(string input)
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

        internal static void PopulateItems()
        {
            var verbotenItems = new List<string>
            {
                "Sparring",
                "Trash Item",
                "Torch",
                "Horse Whip",
                "Push Fork",
                "Bound Crossbow",
                "Hoe",
                "Scythe",
                "Stone",
                "Crafted",
                "Wooden",
                "Practice",
                "Ballista",
                "Boulder",
                "Fire Pot",
                "Banner",
                "Grapeshot"
            };

            Mounts = Items.All.Where(x => x.ItemType == ItemObject.ItemTypeEnum.Horse).Where(x => !x.StringId.Contains("unmountable")).ToList();
            Saddles = Items.All.Where(x => x.ItemType == ItemObject.ItemTypeEnum.HorseHarness && !x.StringId.ToLower().Contains("mule")).ToList();
            var all = Items.All.Where(i =>
                !i.IsCivilian
                && !i.IsCraftedByPlayer
                && i.ItemType is not ItemObject.ItemTypeEnum.Goods
                    or ItemObject.ItemTypeEnum.Horse
                    or ItemObject.ItemTypeEnum.HorseHarness
                    or ItemObject.ItemTypeEnum.Animal
                    or ItemObject.ItemTypeEnum.Banner
                    or ItemObject.ItemTypeEnum.Book
                    or ItemObject.ItemTypeEnum.Invalid
                && i.ItemCategory.StringId != "garment").ToList();

            for (var index = 0; index < all.Count; index++)
            {
                var item = all[index];
                foreach (var word in verbotenItems)
                {
                    if (item.Name.Contains(word))
                    {
                        Mod.Log("Removing " + item.Name);
                        all.Remove(item);
                        index--;
                        break;
                    }
                }
            }

            Arrows = all.Where(i => i.ItemType == ItemObject.ItemTypeEnum.Arrows).ToList();
            Bolts = all.Where(i => i.ItemType == ItemObject.ItemTypeEnum.Bolts).ToList();
            var oneHanded = all.Where(i => i.ItemType == ItemObject.ItemTypeEnum.OneHandedWeapon);
            var twoHanded = all.Where(i => i.ItemType == ItemObject.ItemTypeEnum.TwoHandedWeapon);
            var polearm = all.Where(i => i.ItemType == ItemObject.ItemTypeEnum.Polearm);
            var thrown = all.Where(i => i.ItemType == ItemObject.ItemTypeEnum.Thrown);
            var shields = all.Where(i => i.ItemType == ItemObject.ItemTypeEnum.Shield);
            var bows = all.Where(i => i.ItemType is ItemObject.ItemTypeEnum.Bow or ItemObject.ItemTypeEnum.Crossbow);
            var any = new List<ItemObject>(oneHanded.Concat(twoHanded).Concat(polearm).Concat(thrown).Concat(shields).Concat(bows).ToList());
            any.Do(i => EquipmentItems.Add(new EquipmentElement(i)));
        }

        // builds a set of 4 weapons that won't include more than 1 bow or shield, nor any lack of ammo
        internal static Equipment BuildViableEquipmentSet()
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
                            randomElement = EquipmentItems.Where(x =>
                                x.Item.ItemType != ItemObject.ItemTypeEnum.Bow &&
                                x.Item.ItemType != ItemObject.ItemTypeEnum.Crossbow).ToList().GetRandomElement();
                            break;
                        case 2:
                        case 3:
                            randomElement = EquipmentItems.GetRandomElement();
                            break;
                    }

                    // matches here by obtaining a bow, which then stuffed ammo into [3]
                    if (slot == 3 && !gear[3].IsEmpty)
                    {
                        break;
                    }

                    if (randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Bow ||
                        randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Crossbow)
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
                            if (randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Bow)
                            {
                                gear[3] = new EquipmentElement(Arrows.ToList()[Rng.Next(0, Arrows.Count)]);
                            }
                            else if (randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Crossbow)
                            {
                                gear[3] = new EquipmentElement(Bolts.ToList()[Rng.Next(0, Bolts.Count)]);
                            }

                            continue;
                        }

                        randomElement = EquipmentItems.Where(x =>
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
                //Mod.Log("-----");
                //for (var i = 0; i < 10; i++)
                //{
                //    Mod.Log(gear[i].Item?.Name);
                //}
                // 20% chance to get a mount
                if (Rng.NextDouble() < 0.2f)
                {
                    var mount = Mounts.GetRandomElement();
                    var mountId = mount.StringId.ToLower();
                    gear[10] = new EquipmentElement(mount);
                    if (mountId.Contains("camel"))
                    {
                        gear[11] = new EquipmentElement(Saddles.Where(x =>
                            x.Name.ToString().ToLower().Contains("camel")).ToList().GetRandomElement());
                    }
                    else
                    {
                        gear[11] = new EquipmentElement(Saddles.Where(x =>
                            !x.Name.ToString().ToLower().Contains("camel")).ToList().GetRandomElement());
                    }
                }
            }
            catch (Exception ex)
            {
                Mod.Log(ex);
            }

            //Mod.Log($"GEAR ==> {T.ElapsedTicks / 10000F:F3}ms");
            return gear.Clone();
        }

        // game world measurement
        internal static void DoPowerCalculations(bool force = false)
        {
            if (force
                || LastCalculated < CampaignTime.Now.ToHours - 8)
            {
                LastCalculated = CampaignTime.Now.ToHours;
                var parties = MobileParty.All.Where(p => p.LeaderHero is not null && !p.IsBM()).ToList();
                CalculatedMaxPartySize = (float)parties.Average(p => p.Party.PartySizeLimit) * Variance;
                var totalStrength = parties.Sum(p => p.Party.TotalStrength);
                CalculatedMaxPartyStrength = totalStrength / parties.Count * (1 + Globals.Settings.PartyStrengthPercent / 100) * Variance;
                CalculatedGlobalPowerLimit = totalStrength * Variance;
                GlobalMilitiaPower = PartyMilitiaMap.Keys.Sum(p => p.Party.TotalStrength);
                MilitiaPowerPercent = GlobalMilitiaPower / CalculatedGlobalPowerLimit * 100;
            }
        }

        // leveraged to make looters convert into troop types from nearby cultures
        public static CultureObject GetMostPrevalentFromNearbySettlements(Vec2 position)
        {
            const int arbitraryDistance = 20;
            var settlements = Settlement.FindSettlementsAroundPosition(position, arbitraryDistance);
            var map = new Dictionary<CultureObject, int>();
            foreach (var settlement in settlements)
            {
                if (map.ContainsKey(settlement.Culture))
                {
                    map[settlement.Culture]++;
                }
                else
                {
                    map.Add(settlement.Culture, 1);
                }
            }

            var highest = map.Where(x =>
                x.Value == map.Values.Max()).Select(x => x.Key);
            var result = highest.ToList().GetRandomElement();
            return result;
        }

        public static void ConvertLootersToKingdomCultureRecruits(ref TroopRoster troopRoster, CultureObject culture, int numberToUpgrade)
        {
            var recruit = Recruits.Where(x =>
                    x.Culture == Clan.All.FirstOrDefault(k => k.Culture == culture)?.Culture)
                .ToList().GetRandomElement() ?? Recruits.ToList().GetRandomElement();

            troopRoster.AddToCounts(recruit, numberToUpgrade);
        }

        // 1.5.9 this condition leads to a null ref in DoWait() so we hack it
        internal static void FixMapEventFuckery()
        {
            if (PlayerEncounter.Battle is not null &&
                PartyBase.MainParty.MapEvent is null)
            {
                var sides = Traverse.Create(PlayerEncounter.Battle).Field<MapEventSide[]>("_sides").Value;
                var playerSide = sides.FirstOrDefault(x => x.LeaderParty.LeaderHero == Hero.MainHero);
                var attacker = sides.FirstOrDefault(x => x.MissionSide == BattleSideEnum.Attacker)?.LeaderParty;
                var defender = sides.FirstOrDefault(x => x.MissionSide == BattleSideEnum.Defender)?.LeaderParty;
                PartyBase.MainParty.MapEventSide = playerSide;
                var initialize = AccessTools.Method(typeof(MapEvent), "Initialize", new[] { typeof(PartyBase), typeof(PartyBase), typeof(MapEvent.BattleTypes) });
                initialize.Invoke(PartyBase.MainParty.MapEvent, new object[] { attacker, defender, MapEvent.BattleTypes.None });
            }
        }

        internal static void PrintInstructionsAroundInsertion(List<CodeInstruction> codes, int insertPoint, int insertSize, int adjacentNum = 5)
        {
            Mod.Log($"Inserting {insertSize} at {insertPoint}.");

            // in case insertPoint is near the start of the method's IL
            var adjustedAdjacent = codes.Count - adjacentNum >= 0 ? adjacentNum : Math.Max(0, codes.Count - adjacentNum);
            for (var i = 0; i < adjustedAdjacent; i++)
            {
                // codes[266 - 5 + 0].opcode
                // codes[266 - 5 + 4].opcode
                Mod.Log($"{codes[insertPoint - adjustedAdjacent + i].opcode,-10}{codes[insertPoint - adjustedAdjacent + i].operand}");
            }

            for (var i = 0; i < insertSize; i++)
            {
                Mod.Log($"{codes[insertPoint + i].opcode,-10}{codes[insertPoint + i].operand}");
            }

            // in case insertPoint is near the end of the method's IL
            adjustedAdjacent = insertPoint + adjacentNum <= codes.Count ? adjacentNum : Math.Max(codes.Count, adjustedAdjacent);
            for (var i = 0; i < adjustedAdjacent; i++)
            {
                // 266 + 2 - 5 + 0
                // 266 + 2 - 5 + 4
                Mod.Log($"{codes[insertPoint + insertSize + adjustedAdjacent + i].opcode,-10}{codes[insertPoint + insertSize + adjustedAdjacent + i].operand}");
            }
        }

        internal static void SetMilitiaPatrol(MobileParty mobileParty)
        {
            var settlement = Settlement.All.GetRandomElement();
            mobileParty.SetMovePatrolAroundSettlement(settlement);
        }

        internal static void RunLateManualPatches()
        {
            // this patch prevents nasty problems with saving at an encounter dialog (might be getting old as of 3.3.1)
            // have to patch late because of static constructors (type initialization exception)
            Mod.harmony.Patch(
                AccessTools.Method(typeof(EncounterGameMenuBehavior), "game_menu_encounter_on_init"),
                new HarmonyMethod(AccessTools.Method(typeof(Helper), nameof(FixMapEventFuckery))));

            var original = AccessTools.Method(typeof(DefaultPartySpeedCalculatingModel), "CalculateBaseSpeedForParty");    // TODO check speed
            var transpiler = AccessTools.Method(typeof(MilitiaPatches), nameof(MilitiaPatches.CalculateBasePartySpeedPatch));
            Mod.harmony.Patch(original, transpiler:new HarmonyMethod(transpiler));
        }

        internal static void RemoveUndersizedTracker(PartyBase party)
        {
            if (party.MemberRoster.TotalManCount < Globals.Settings.TrackedSizeMinimum)
            {
                try
                {
                    var tracker = MobilePartyTrackerVM?.Trackers?.FirstOrDefault(t => t.TrackedParty == party.MobileParty);
                    if (tracker is not null)
                    {
                        Mod.Log("Removing small BM tracker.");
                        MobilePartyTrackerVM.Trackers.Remove(tracker);
                    }
                }
                catch (Exception ex)
                {
                    Mod.Log(ex);
                }
            }
        }

        internal static int NumMountedTroops(TroopRoster troopRoster)
        {
            return troopRoster.GetTroopRoster().Where(x => x.Character.IsMounted).Sum(e => e.Number);
        }

        internal static Hero CreateHero()
        {
            var hero = CreateHeroAtOccupationCopy(Occupation.NotAssigned, Hideouts.GetRandomElement()); // HeroCreator.CreateHeroAtOccupation(Occupation.NotAssigned, Hideouts.GetRandomElement());
            hero.StringId += "Bandit_Militia";
            hero.CharacterObject.StringId += "Bandit_Militia";
            if (MBRandom.RandomInt(1) == 0)
            {
                hero.UpdatePlayerGender(true);
                hero.FirstName.SetTextVariable("FEMALE", 1);
                StringHelpers.SetCharacterProperties("HERO", hero.CharacterObject, hero.FirstName);
            }

            EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, BanditEquipment.GetRandomElement());
            if (Globals.Settings.CanTrain)
            {
                Traverse.Create(hero).Method("SetSkillValueInternal", DefaultSkills.Leadership, 150).GetValue();
                Traverse.Create(hero).Method("SetPerkValueInternal", DefaultPerks.Leadership.VeteransRespect, true).GetValue();
            }

            return hero;
        }

        // used for 1.6.5 backport of 1.7 refactor
        public static Hero CreateHeroAtOccupationCopy(
            Occupation neededOccupation,
            Settlement forcedHomeSettlement = null)
        {
            var characterObjects =
                CharacterObject.All.Where(x =>
                    x.Occupation is Occupation.Bandit
                    && x.Name.Contains("Boss")).ToListQ();
            var settlement = forcedHomeSettlement ?? SettlementHelper.GetRandomTown();
            var num1 = 0;
            foreach (var characterObject in characterObjects)
            {
                var num2 = characterObject.GetTraitLevel(DefaultTraits.Frequency) * 10;
                num1 += num2 > 0 ? num2 : 100;
            }

            if (!characterObjects.Any())
            {
                return null;
            }

            var template = (CharacterObject)null;
            var num3 = 1 + (int)(settlement.Random.GetValueNormalized(settlement.Notables.Count()) * (double)(num1 - 1));
            foreach (var characterObject in characterObjects)
            {
                var num4 = characterObject.GetTraitLevel(DefaultTraits.Frequency) * 10;
                num3 -= num4 > 0 ? num4 : 100;
                if (num3 < 0)
                {
                    template = characterObject;
                    break;
                }
            }

            var specialHero = HeroCreator.CreateSpecialHero(template, settlement);
            if (specialHero.HomeSettlement.IsVillage && specialHero.HomeSettlement.Village.Bound != null && specialHero.HomeSettlement.Village.Bound.IsCastle)
            {
                var num5 = MBRandom.RandomFloat * 20f;
                specialHero.AddPower(num5);
            }

            if (neededOccupation != Occupation.Wanderer)
            {
                specialHero.ChangeState(Hero.CharacterStates.Active);
            }

            if (neededOccupation != Occupation.Wanderer)
            {
                EnterSettlementAction.ApplyForCharacterOnly(specialHero, settlement);
            }

            if (neededOccupation != Occupation.Wanderer)
            {
                var amount = 10000;
                GiveGoldAction.ApplyBetweenCharacters(null, specialHero, amount, true);
            }

            var heroObject = specialHero.Template?.HeroObject;
            specialHero.SupporterOf = heroObject == null || specialHero.Template.HeroObject.Clan == null || !specialHero.Template.HeroObject.Clan.IsMinorFaction ? HeroHelper.GetRandomClanForNotable(specialHero) : specialHero.Template.HeroObject.Clan;
            if (neededOccupation != Occupation.Wanderer)
            {
                AccessTools.Method(typeof(HeroCreator), "AddRandomVarianceToTraits").Invoke(null, new object[] { specialHero });
            }

            return specialHero;
        }
    }
}
