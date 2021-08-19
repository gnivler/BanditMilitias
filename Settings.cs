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

namespace Bandit_Militias
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
                MinPartySize = 30,
                RandomSplitChance = 50,
                SplitStrengthPercent = 0,
                SplitSizePercent = 0,
                PartyStrengthPercent = 125,
                MaxPartySizePercent = 125,
                MinPartySizeToConsiderMerge = 30,
                GrowthChancePercent = 100,
                GrowthPercent = 3,
                MaxItemValue = 8000,
                LooterUpgradePercent = 66,
                MaxStrengthDeltaPercent = 100,
                UpgradeUnitsPercent = 33,
                GlobalPowerPercent = 25,
                MaxTrainingTier = 5
            });
            return basePresets;
        }

        public override string FormatType => "json";
        public override string FolderName => "Bandit Militias";

        [SettingPropertyBool("Train Militias", HintText = "\nBandit heroes will train their militias.", Order = 0, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings", GroupOrder = 0)]
        public bool CanTrain { get; private set; } = true;

        [SettingPropertyDropdown("Militia XP Boost", HintText = "\nHardest grants enough XP to significantly upgrade troops.  No effect if Training is disabled.", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings")]
        public DropdownDefault<string> XpGift { get; private set; } = new(Globals.DifficultyXpMap.Keys, 1);

        [SettingPropertyInteger("Growth Chance Percent", 0, 100, HintText = "\nChance per day that the militia will gain more troops (0 for off).", Order = 2, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings")]
        public int GrowthChancePercent { get; private set; } = 50;

        [SettingPropertyInteger("Growth Percent", 0, 1000, HintText = "\nGrow by this percent when growth occurs.", Order = 3, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings")]
        public int GrowthPercent { get; private set; } = 3;

        [SettingPropertyInteger("Change Cooldown", 0, 168, HintText = "\nAfter merging or splitting, a militia will not change again until this much time passes.", Order = 4, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings")]
        public int CooldownHours { get; private set; } = 24;

        [SettingPropertyDropdown("Bandit Hero Gold Reward", Order = 5, RequireRestart = false)]
        [SettingPropertyGroup("Primary Settings")]
        public DropdownDefault<string> GoldReward { get; private set; } = new(Globals.GoldMap.Keys, 1);

        [SettingPropertyInteger("Minimum Militia Size", 10, 100, HintText = "\nMilitias defeated with fewer than this many remaining troops will be dispersed.", Order = 0, RequireRestart = false)]
        [SettingPropertyGroup("Size Adjustments", GroupOrder = 2)]
        public int MinPartySize { get; private set; } = 20;

        [SettingPropertyInteger("Minimum Size To Merge", 10, 100, HintText = "\nBandit groups smaller than this will not form militias.", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("Size Adjustments")]
        public int MinPartySizeToConsiderMerge { get; private set; } = 20;

        [SettingPropertyInteger("Random Split Chance", 0, 100, HintText = "\nChance per day that any given militia will split in half when requirements are met.", Order = 2, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments", GroupOrder = 1)]
        public int RandomSplitChance { get; private set; } = 25;

        [SettingPropertyInteger("Strength Split", 0, 100, HintText = "\nMilitias won't consider splitting until reaching this percentage of the world average strength.", Order = 3, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public int SplitStrengthPercent { get; private set; } = 80;

        [SettingPropertyInteger("Size Split", 0, 100, HintText = "\nMilitias won't consider splitting until reaching this percentage of the world average party size.", Order = 4, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public int SplitSizePercent { get; private set; } = 80;

        [SettingPropertyInteger("Max Party Strength", 30, 1000, HintText = "\nMilitia's won't merge into something more powerful than this percentage of the world average strength.", Order = 5, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public int PartyStrengthPercent { get; private set; } = 90;

        [SettingPropertyInteger("Max Party Size", 1, 1000, HintText = "\nMilitia's won't merge into something larger than this percentage of the main party's size.", Order = 6, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public int MaxPartySizePercent { get; private set; } = 90;

        [SettingPropertyInteger("Max Item Value", 3000, 1000000, HintText = "\nLimit the per-piece value of equipment given to the Heroes.\nMostly for when other mods give you Hero loot.", Order = 7, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public int MaxItemValue { get; private set; } = 3000;

        [SettingPropertyInteger("Looter Conversions", 0, 100, HintText = "\nThis percentage of all looters are converted to the most-prevalent local faction's basic troops, when training occurs.", Order = 8, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public int LooterUpgradePercent { get; private set; } = 25;

        [SettingPropertyInteger("Limit Militia Engagements", 0, 100, HintText = "\nPercentage of strength difference that will make Militias ignore targets.\n100 removes the limit.", Order = 9, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public int MaxStrengthDeltaPercent { get; private set; } = 10;

        [SettingPropertyInteger("Upgrade Units", 0, 100, HintText = "\nUpgrade at most this percentage of any given troop type quantity when training occurs.", Order = 10, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public int UpgradeUnitsPercent { get; private set; } = 25;

        [SettingPropertyInteger("Global Power", 0, 1000, HintText = "\nThe total Militias power will remain under this percentage of the world total strength.", Order = 11, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public int GlobalPowerPercent { get; private set; } = 15;

        [SettingPropertyInteger("Max Training Tier", 1, 6, HintText = "\nDon't train any units past this tier.", Order = 12, RequireRestart = false)]
        [SettingPropertyGroup("Militia Adjustments")]
        public int MaxTrainingTier { get; private set; } = 4;

        [SettingPropertyBool("Random Banners", HintText = "\nBandit Militias will have unique banners, or basic bandit clan ones.", RequireRestart = false)]
        public bool RandomBanners { get; internal set; } = true;

        [SettingPropertyBool("Debug Logging", HintText = "\nCreates output in the mod folder.", Order = 0, RequireRestart = false)]
        public bool Debug { get; set; }

        [SettingPropertyBool("Testing Mode", HintText = "Teleports BMs to you.", Order = 1, RequireRestart = false)]
        public bool TestingMode { get; set; }

        private string id = "BanditMilitias";
        private string displayName = $"Bandit Militias {typeof(Settings).Assembly.GetName().Version.ToString(3)}";
        public override string Id => id;
        public override string DisplayName => displayName;
    }
}
