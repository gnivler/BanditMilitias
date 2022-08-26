using TaleWorlds.SaveSystem;

namespace BanditMilitias
{
    // ReSharper disable once UnusedType.Global
    // class is loaded by reflection automatically
    public class MilitiaSaveableTypeDefiner  : SaveableTypeDefiner
    {
        public MilitiaSaveableTypeDefiner() : base(42069)
        {
        }

        protected override void DefineClassTypes()
        {
            AddClassDefinition(typeof(ModBanditMilitiaPartyComponent), 42069);
        }
    }
}
