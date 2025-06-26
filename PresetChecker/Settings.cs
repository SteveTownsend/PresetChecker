using Mutagen.Bethesda.Synthesis.Settings;

namespace PresetChecker
{
    internal class Settings
    {
        [SynthesisSettingName("Log File Name")]
        [SynthesisTooltip("Console output can be verbose if a lot of presets are used so file log is written as well. This must be a valid file path on your computer. Typically this points to a file is a new mod directory in your Mod Manager VFS, e.g. 'D:/ModdedSkyrim/mods/Preset Checker'. There is no default value.")]
        [SynthesisDescription("Path where diagnostic output is written.")]
        public string LogFileName { get; set; } = "j:/OmegaLOTD/Tools/Mods/Preset Checker/log.txt";

        [SynthesisSettingName("Input Folder")]
        [SynthesisTooltip("This must be a valid path on your computer. Typically this points to the physical path of the mod directory in your Mod Manager VFS, e.g. 'D:/ModdedSkyrim/mods'. There is no default value, in order to preserve the relative path of relocated backup presets.")]
        [SynthesisDescription("Path to be scanned for merged plugins and preset .jslot files.")]
        public string InputFolder { get; set; } = "j:/OmegaLOTD/Tools/Mods/";

        [SynthesisSettingName("Output Folder")]
        [SynthesisTooltip("This must be a valid path on your computer. Typically this points to a new mod directory in your Mod Manager VFS, e.g. 'D:/ModdedSkyrim/mods/Preset Checker'. There is no default value.")]
        [SynthesisDescription("Path where remapped preset .jslot files are written.")]
        public string OutputFolder { get; set; } = "j:/OmegaLOTD/Tools/Mods/Preset Checker/";

        [SynthesisSettingName("Backup Folder")]
        [SynthesisTooltip("This must be a valid path on your computer. Typically this points to a directory outside your Mod Manager VFS, e.g. 'D:/Preset Backups'. There is no default value.")]
        [SynthesisDescription("Path where the original preset .jslot files are backed up.")]
        public string BackupFolder { get; set; } = "j:/Preset Backups/";

        [SynthesisSettingName("Write changed presets")]
        [SynthesisTooltip("If true, updated presets referencing the correct merged plugins will be written and the old ones relocated preserving their relative paths, to avoid confusion. *** Make sure all errors are fixed before setting this to true. ***")]
        [SynthesisDescription("Add head mesh type and race-identifying prefix to preset filepath.")]
        public bool WriteChanged { get; set; } = false;

        [SynthesisSettingName("Group presets")]
        [SynthesisTooltip("If true, each preset will be placed in a subdirectory that group presets of the same race and FacePart for easier location. Only takes effect if 'Write Changed Presets' is true. If true, both changed and unchanged presets will be grouped.")]
        [SynthesisDescription("Add race-identifying prefix to preset filename.")]
        public bool GroupPresets { get; set; } = true;
    }
}
