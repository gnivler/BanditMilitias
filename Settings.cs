// ReSharper disable UnusedMember.Global   
// ReSharper disable FieldCanBeMadeReadOnly.Global 
// ReSharper disable ConvertToConstant.Global

namespace Bandit_Militias
{
    public class Settings
    {
        public bool CanTrain = true;
        public bool RandomBanners = true;
        public string XpGift = "NORMAL";
        public string GoldReward = "NORMAL";
        public float CooldownHours = 24;
        public int MinPartySize = 20;
        public int MaxPartySize = 300;
        public float RandomSplitChance = 0.25f;
        public float StrengthSplitFactor = 0.8f;
        public float SizeSplitFactor = 0.8f;
        public float PartyStrengthFactor = 0.8f;
        public float MaxPartySizeFactor = 0.8f;
        public bool Growth = true;
        public float GrowthChance = 0.66f;
        public float GrowthInPercent = 1;
        public int MaxItemValue = 3000;
        public float LooterUpgradeFactor = 0.25f;
        public float MilitiaLimitFactor = 0.5f;
        public int MaxStrengthDelta = 10;
        public float UpgradeUnitsFactor = 0.25f;
    }
}
