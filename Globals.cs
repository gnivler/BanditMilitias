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
        internal static readonly Random Rng = new Random();
        internal static readonly Dictionary<MobileParty, Militia> PartyMilitiaMap = new Dictionary<MobileParty, Militia>();
        internal static readonly Dictionary<ItemObject.ItemTypeEnum, List<ItemObject>> ItemTypes = new Dictionary<ItemObject.ItemTypeEnum, List<ItemObject>>();
        internal static readonly List<EquipmentElement> EquipmentItems = new List<EquipmentElement>();
        internal static List<Settlement> Hideouts = new List<Settlement>();
        internal static Settings Settings;
        internal static List<ItemObject> Arrows = new List<ItemObject>();
        internal static List<ItemObject> Bolts = new List<ItemObject>();
        internal static readonly Stopwatch T = new Stopwatch();
        internal static readonly List<Equipment> BanditEquipment = new List<Equipment>();
        internal static IEnumerable<CharacterObject> Recruits;
        internal static readonly List<Banner> Banners = new List<Banner>();
        
        // FieldRefs
        internal static readonly AccessTools.FieldRef<Settlement, MBReadOnlyList<Hero>> HeroesWithoutParty =
            AccessTools.FieldRefAccess<Settlement, MBReadOnlyList<Hero>>("<HeroesWithoutParty>k__BackingField");
        
        internal static readonly Dictionary<string, int> DifficultyXpMap = new Dictionary<string, int>
        {
            {"OFF", 0},
            {"NORMAL", 300},
            {"HARD", 600},
            {"HARDEST", 900},
        };

        internal static readonly Dictionary<string, int> GoldMap = new Dictionary<string, int>
        {
            {"LOW", 250},
            {"NORMAL", 500},
            {"RICH", 900},
            {"RICHEST", 2000},
        };
    }
}
