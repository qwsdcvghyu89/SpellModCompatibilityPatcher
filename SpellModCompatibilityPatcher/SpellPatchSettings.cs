using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpellModCompatibilityPatcher {
    public class SpellPatchSettings {
        public List<ModKey> BaseMods { get; set; } = [
            ModKey.FromNameAndExtension("Skyrim.esm"),
            ModKey.FromNameAndExtension("Update.esm"),
            ModKey.FromNameAndExtension("Dawnguard.esm"),
            ModKey.FromNameAndExtension("HearthFires.esm"),
            ModKey.FromNameAndExtension("Dragonborn.esm")
            ];
        public List<ModKey> PreferredOverrideOrder { get; set; } = [

            ];
    }
}
