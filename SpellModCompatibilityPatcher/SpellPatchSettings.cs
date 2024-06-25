using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.WPF.Reflection.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpellModCompatibilityPatcher {
    public class SpellPatchSettings {


        //public List<ModKey> BaseMods = [
        //    ModKey.FromNameAndExtension("Skyrim.esm"),
        //    ModKey.FromNameAndExtension("Update.esm"),
        //    ModKey.FromNameAndExtension("Dawnguard.esm"),
        //    ModKey.FromNameAndExtension("HearthFires.esm"),
        //    ModKey.FromNameAndExtension("Dragonborn.esm")
        //    ];

        [SettingName("Order of overwrites from highest priority to lowest priority")]
        [Tooltip("This decides the order by which spells & spell tomes are overwritten. The higher the mod is on this list, the higher its priority. ")]
        public List<ModKey> PreferredOverrideOrder = [

            ];
    }
}
