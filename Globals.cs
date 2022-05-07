using System;
using System.Collections.Generic;
using System.Diagnostics;
using SandBox.ViewModelCollection.MobilePartyTracker;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace Bandit_Militias
{
    // ReSharper disable once ClassNeverInstantiated.Global
    internal struct Globals
    {
        // merge/split criteria
        internal const float MergeDistance = 2;
        internal const float FindRadius = 7;
        internal const float MinDistanceFromHideout = 8;

        // holders for criteria
        internal static float CalculatedMaxPartySize;
        internal static float CalculatedGlobalPowerLimit;
        internal static float GlobalMilitiaPower;
        internal static float MilitiaPowerPercent;

        // dictionary maps
        internal static readonly Dictionary<MobileParty, Militia> PartyMilitiaMap = new();
        internal static readonly Dictionary<MobileParty, ImageIdentifierVM> PartyImageMap = new();
        internal static readonly Dictionary<ItemObject.ItemTypeEnum, List<ItemObject>> ItemTypes = new();
        internal static readonly Dictionary<CultureObject, List<CharacterObject>> Recruits = new();

        // misc
        internal static readonly Random Rng = new();
        internal static readonly Stopwatch T = new();
        internal static Settings Settings;
        internal static List<Settlement> Hideouts = new();
        internal static readonly List<EquipmentElement> EquipmentItems = new();
        internal static List<ItemObject> Arrows = new();
        internal static List<ItemObject> Bolts = new();
        internal static readonly List<Equipment> BanditEquipment = new();
        internal static readonly List<Banner> Banners = new();
        internal static double LastCalculated;

        // ReSharper disable once InconsistentNaming
        internal static MobilePartyTrackerVM MobilePartyTrackerVM;

        internal static float Variance => MBRandom.RandomFloatRanged(0.925f, 1.075f);

        // ArmsDealer compatibility
        internal static CultureObject BlackFlag => MBObjectManager.Instance.GetObject<CultureObject>("ad_bandit_blackflag");

        internal static readonly Dictionary<string, int> DifficultyXpMap = new()
        {
            { "Off", 0 },
            { "Normal", 300 },
            { "Hard", 600 },
            { "Hardest", 900 },
        };

        internal static readonly Dictionary<string, int> GoldMap = new()
        {
            { "Low", 250 },
            { "Normal", 500 },
            { "Rich", 900 },
            { "Richest", 2000 },
        };
    }
}
