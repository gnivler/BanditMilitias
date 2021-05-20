using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace Bandit_Militias
{
    public static class Globals
    {
        // dev
        internal static bool TestingMode;

        // how close before merging
        internal const float MergeDistance = 2;
        internal static float FindRadius = 7;
        internal static float MinDistanceFromHideout = 8;

        // holders for criteria
        internal static int CalculatedMaxPartyStrength;
        internal static int CalculatedMaxPartySize;
        internal static int CalculatedGlobalPowerLimit;
        internal static int GlobalMilitiaPower;

        // misc
        internal static readonly Random Rng = new();
        internal static readonly Dictionary<MobileParty, Militia> PartyMilitiaMap = new();
        internal static readonly Dictionary<ItemObject.ItemTypeEnum, List<ItemObject>> ItemTypes = new();
        internal static readonly List<EquipmentElement> EquipmentItems = new();
        internal static List<Settlement> Hideouts = new();
        internal static Settings Settings;
        internal static List<ItemObject> Arrows = new();
        internal static List<ItemObject> Bolts = new();
        internal static readonly Stopwatch T = new();
        internal static readonly List<Equipment> BanditEquipment = new();
        internal static IEnumerable<CharacterObject> Recruits;
        internal static readonly List<Banner> Banners = new();
        
        // FieldRefs
        internal static readonly AccessTools.FieldRef<Settlement, MBReadOnlyList<Hero>> HeroesWithoutParty =
            AccessTools.FieldRefAccess<Settlement, MBReadOnlyList<Hero>>("<HeroesWithoutParty>k__BackingField");
        
        internal static readonly Dictionary<string, int> DifficultyXpMap = new()
        {
            {"OFF", 0},
            {"NORMAL", 300},
            {"HARD", 600},
            {"HARDEST", 900},
        };

        internal static readonly Dictionary<string, int> GoldMap = new()
        {
            {"LOW", 250},
            {"NORMAL", 500},
            {"RICH", 900},
            {"RICHEST", 2000},
        };
    }
}
