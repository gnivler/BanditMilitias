using System;
using System.Collections.Generic;
using System.Diagnostics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

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
        internal static readonly HashSet<Militia> Militias = new HashSet<Militia>();
        internal static readonly Dictionary<ItemObject.ItemTypeEnum, List<ItemObject>> ItemTypes = new Dictionary<ItemObject.ItemTypeEnum, List<ItemObject>>();
        internal static readonly List<EquipmentElement> EquipmentItems = new List<EquipmentElement>();
        internal static List<Settlement> Hideouts = new List<Settlement>();
        internal static Settings Settings;
        internal static List<ItemObject> Arrows = new List<ItemObject>();
        internal static List<ItemObject> Bolts = new List<ItemObject>();
        internal static readonly Stopwatch T = new Stopwatch();
        internal static readonly Dictionary<MobileParty, CampaignTime> MergeMap = new Dictionary<MobileParty, CampaignTime>();
        internal static readonly List<Equipment> BanditEquipment = new List<Equipment>();
        internal static List<CharacterObject> Recruits = new List<CharacterObject>();

        internal static readonly Dictionary<string, int> DifficultyXpMap = new Dictionary<string, int>
        {
            {"OFF", 0},
            {"NORMAL", 100},
            {"HARD", 300},
            {"HARDEST", 600},
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
