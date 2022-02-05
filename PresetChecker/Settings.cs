﻿using Mutagen.Bethesda.Synthesis.Settings;

namespace PresetChecker
{
    public class Settings
    {
        [SynthesisSettingName("Input Folder")]
        [SynthesisTooltip("This must be a valid path on your computer. Typically this points to the mod directory in your Mod Manager VFS, e.g. 'D:/ModdedSkyrim/mods'.")]
        [SynthesisDescription("Path to be scanned for merged plugins and preset .jslot files.")]
        public string InputFolder { get; set; } = "";
        //public string InputFolder { get; set; } = "j:/OmegaLOTD/Tools/Mods";

        [SynthesisSettingName("Output Folder")]
        [SynthesisTooltip("This must be a valid path on your computer. Typically this points to a new mod directory in your Mod Manager VFS, e.g. 'D:/ModdedSkyrim/mods/Preset Checker'.")]
        [SynthesisDescription("Path where remapped preset .jslot files are written.")]
        //public string OutputFolder { get; set; } = "";
        public string OutputFolder { get; set; } = "j:/OmegaLOTD/Tools/Mods/Preset Checker";
    }
}
