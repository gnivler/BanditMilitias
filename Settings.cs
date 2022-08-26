// ReSharper disable UnusedMember.Global   
// ReSharper disable FieldCanBeMadeReadOnly.Global 
// ReSharper disable ConvertToConstant.Global

using System;
using System.Collections.Generic;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Dropdown;
using MCM.Abstractions.Settings.Base;
using MCM.Abstractions.Settings.Base.Global;

namespace BanditMilitias
{
    public class Settings : AttributeGlobalSettings<Settings>
    {
        public override IDictionary<string, Func<BaseSettings>> GetAvailablePresets()
        {
            var basePresets = base.GetAvailablePresets();
            basePresets.Add("Hard Mode", () => new Settings
            {
                Debug = false,
                CanTrain = true,
                RandomBanners = true,
                XpGift = new DropdownDefault<string>(Globals.DifficultyXpMap.Keys, 3),
                GoldReward = new DropdownDefault<string>(Globals.GoldMap.Keys, 2),
                CooldownHours = 8,
                DisperseSize = 30,
                RandomSplitChance = 50,
                MergeableSize = 10,
                GrowthChance = 100,
                GrowthPercent = 3,
                MilitiaSpawn = true,
                SpawnChance = 50,
                MaxItemValue = 10000,
                LooterUpgradePercent = 66,
                UpgradeUnitsPercent = 33,
                GlobalPowerPercent = 33,
                MaxTrainingTier = 5,
            });
            return basePresets;
        }

        public override string FormatType => "json";
        public override string FolderName => "BanditMilitias";

        [SettingPropertyBool("Train Militias", HintText = "\nBandit heroes will train their militias.", Order = 0, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings", GroupOrder = 0)]
        public bool CanTrain { get; private set; } = true;

        //[SettingPropertyBool("Upgrade Militias (!!EXPERIMENTAL - novel mod!!)", HintText = "\nAll BM troops will upgrade their gear from loot won.  BM is the first mod to try this.", Order = 0, RequireRestart = false)]
        //[SettingPropertyGroup("Primary Settings", GroupOrder =1)]
        public bool UpgradeTroops { get; private set; } = false;

        [SettingPropertyInteger("Daily Training Chance", 0, 100, HintText = "\nEach day they might train further.", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings", GroupOrder = 2)]
        public float TrainingChance { get; set; } = 10;

        [SettingPropertyDropdown("Militia XP Boost", HintText = "\nHardest grants enough XP to significantly upgrade troops.  Off grants no bonus XP.", Order = 2, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings")]
        public DropdownDefault<string> XpGift { get; private set; } = new(Globals.DifficultyXpMap.Keys, 1);

        [SettingPropertyInteger("Growth Chance Percent", 0, 100, HintText = "\nChance per day that the militia will gain more troops (0 for off).", Order = 3, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings")]
        public int GrowthChance { get; private set; } = 50;

        [SettingPropertyInteger("Growth Percent", 0, 100, HintText = "\nGrow each troop type by this percent.", Order = 4, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings")]
        public int GrowthPercent { get; private set; } = 1;

        [SettingPropertyBool("Ignore Villagers/Caravans", HintText = "\nThey won't be attacked by BMs.", Order = 5, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings")]
        public bool IgnoreVillagersCaravans { get; private set; } = false;

        [SettingPropertyBool("BM Spawn", HintText = "\nNew BM will form spontaneously as well as by merging together normally.", Order = 6, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings")]
        public bool MilitiaSpawn { get; private set; } = true;

        [SettingPropertyInteger("Spawn Chance Percent", 1, 100, HintText = "\nBM will spawn hourly at this likelihood.", Order = 7, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings")]
        public int SpawnChance { get; private set; } = 30;

        [SettingPropertyInteger("Change Cooldown", 0, 168, HintText = "\nBM won't merge or split a second time until this many hours go by.", Order = 8, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings")]
        public int CooldownHours { get; private set; } = 24;

        [SettingPropertyDropdown("Bandit Hero Gold Reward", Order = 9, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings")]
        public DropdownDefault<string> GoldReward { get; private set; } = new(Globals.GoldMap.Keys, 1);

        [SettingPropertyInteger("Disperse Militia Size", 10, 100, HintText = "\nMilitias defeated with fewer than this many remaining troops will be dispersed.", Order = 0, RequireRestart = false)]
        [SettingPropertyGroup("Size Adjustments", GroupOrder = 2)]
        public int DisperseSize { get; private set; } = 20;

        [SettingPropertyInteger("Minimum Size", 1, 100, HintText = "\nNo BMs smaller than this will form.", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("Size Adjustments")]
        public int MinPartySize { get; private set; } = 20;

        [SettingPropertyInteger("Mergeable party size", 1, 100, HintText = "\nSmall looter and bandit parties won't merge.", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("Size Adjustments")]
        public int MergeableSize { get; private set; } = 10;

        [SettingPropertyInteger("Random Split Chance", 0, 100, HintText = "\nHow likely BM is to split when large enough.", Order = 2, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments", GroupOrder = 1)]
        public int RandomSplitChance { get; private set; } = 10;

        [SettingPropertyInteger("Max Item Value", 1000, 1000000, HintText = "\nLimit the per-piece value of equipment given to the Heroes.\nMostly for when other mods give you Hero loot.", Order = 7, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public int MaxItemValue { get; private set; } = 3000;

        [SettingPropertyInteger("Looter Conversions", 0, 100, HintText = "\nHow many looters get made into better units when training.", Order = 8, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public int LooterUpgradePercent { get; private set; } = 15;

        [SettingPropertyInteger("Upgrade Units", 0, 100, HintText = "\nUpgrade (at most) this percentage of troops when training occurs.", Order = 10, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public int UpgradeUnitsPercent { get; private set; } = 25;

        [SettingPropertyInteger("Global Power", 0, 1000, HintText = "\nMajor setting.  Setting higher means more, bigger BMs.", Order = 11, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public int GlobalPowerPercent { get; set; } = 15;

        [SettingPropertyInteger("Max Training Tier", 1, 6, HintText = "\nBM won't train any units past this tier.", Order = 13, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public int MaxTrainingTier { get; private set; } = 4;

        [SettingPropertyInteger("Ignore Weaker Parties", 0, 100, HintText = "\n10 means any party 10% weaker will be ignored.\n100 attacks without restriction.", Order = 9, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public int MaxStrengthDeltaPercent { get; private set; } = 10;

        [SettingPropertyBool("AllowPillaging", HintText = "\nAllow PILLAGING!.", Order = 10, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public bool AllowPillaging { get; private set; } = true;

        [SettingPropertyText("Bandit Militia String", Order = 0, HintText = "Box may appear empty but still works.", RequireRestart = false)]
        public string BanditMilitiaString { get; set; } = "Bandit Militia";

        [SettingPropertyText("Leaderless Bandit Militia String", Order = 1, HintText = "Box may appear empty but still works.", RequireRestart = false)]
        public string LeaderlessBanditMilitiaString { get; set; } = "Leaderless Bandit Militia";

        [SettingPropertyBool("Militia Map Markers", HintText = "\nHave omniscient view of BMs.", Order = 2, RequireRestart = false)]
        public bool Trackers { get; private set; } = false;

        [SettingPropertyInteger("Minimum BM Size To Track", 1, 500, HintText = "Any smaller BMs won't be tracked.", Order = 3, RequireRestart = false)]
        public int TrackedSizeMinimum { get; private set; } = 50;

        [SettingPropertyBool("Random Banners", HintText = "\nBMs will have unique banners, or basic bandit clan ones.", Order = 4, RequireRestart = false)]
        public bool RandomBanners { get; set; } = true;

        [SettingPropertyBool("Village raid notices", HintText = "\\When your fiefs are raided you'll see a banner message.", Order = 5, RequireRestart = false)]
        public bool ShowRaids { get; set; } = true;

        [SettingPropertyBool("Debug Logging", HintText = "\nCreates logfile output in the mod folder.", Order = 6, RequireRestart = false)]
        public bool Debug { get; set; }

        [SettingPropertyBool("Testing Mode", HintText = "Teleports BMs to you.", Order = 7, RequireRestart = false)]
        public bool TestingMode { get; set; }


        private string id = "BanditMilitias";
        private string displayName = $"BanditMilitias {typeof(Settings).Assembly.GetName().Version.ToString(3)}";

        public override string Id => id;
        public override string DisplayName => displayName;
    }
}
