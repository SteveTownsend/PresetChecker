using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DynamicData;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Noggog;
using System.IO.Abstractions;
using Mutagen.Bethesda.Synthesis.Projects;
using static Mutagen.Bethesda.Oblivion.Race;
using System.Reflection;

namespace PresetChecker
{
    public class PresetHandler
    {
        public ISet<string> PresetPaths { get; } = new HashSet<string>();
        private static readonly uint ESPFEPlugin = 0xFE000000;
        private static readonly uint ESPFEForm = 0x00000FFF;
        private static readonly uint ESPForm = 0x00FFFFFF;
        // .jslot uses HeadPart formId field to discriminate ESP and ESPFE - merged plugins with HDPT information
        // should not be ESPFE-ified after the fact
        // private static readonly uint NewFormMask = 0x80000000;
        private ISet<FormKey> _allHeadParts;
        private readonly MergeInfo _mergeInfo;
        private readonly string _inputFolder;
        private JObject? _preset;
        private bool _presetUpdated;
        private string? _presetFileFull;
        private string? _originalFileName;
        private string _prefixProcessed = "backup/";
        private string? _newFileName;
        private ISet<string> _checkedTextures = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        private ISet<string> _badPlugins = new SortedSet<string>(StringComparer.InvariantCultureIgnoreCase);
        private ISet<string> _badTextures = new SortedSet<string>(StringComparer.InvariantCultureIgnoreCase);
        private readonly Textures _textures;
        private readonly string _highPolyMod = "High Poly Head.esm";
        private readonly string _highPolyPrefix = "HighPoly/";
        private readonly string _cotrMod = "COR_AllRace.esp";
        private readonly string _cotrPrefix = "CotR/";
        private readonly string _otherPrefix = "Other/";
        private IDictionary<FormKey, string>? _raceByFacePart;
        private readonly string _facePartKey = "malehead";
        private IDictionary<string, string> _raceTagByRaceName = new Dictionary<string, string>()
        {
            { "argonian", "Argonian" },
            { "breton", "Breton" },
            { "darkelf", "Dunmer" },
            { "highelf", "Altmer" },
            { "imperial", "Imperial" },
            { "khajiit", "Khajiit" },
            { "nord", "Nord" },
            { "orc", "Orsimer" },
            { "redguard", "Redguard" },
            { "woodelf", "Bosmer" },
        };
        private readonly string _validPresetPath = "\\skse\\plugins\\chargen\\presets\\";

        public PresetHandler(string inputFolder, MergeInfo mergeInfo, Textures textures)
        {
            _inputFolder = inputFolder;
            if (_inputFolder.IsNullOrEmpty())
                throw new ArgumentException("Input Folder cannot be blank");
            if (Program.settings.OutputFolder.IsNullOrEmpty())
                throw new ArgumentException("Output Folder cannot be blank");
            _mergeInfo = mergeInfo;
            _textures = textures;
            // Do not overwrite existing files - always need a clean slate
            EnumerationOptions options = new EnumerationOptions { RecurseSubdirectories = true };
            int existingCount = Directory.GetFiles(Program.settings.OutputFolder, "*.jslot", options).Length;
            if (existingCount > 0)
            {
                Program.Logger.Write("WARNING {0} Preset files found in output folder will not be overwritten", existingCount);
                Program.Logger.Write("------- Please ensure Output Directory is not included in MO2 Virtual File System by unchecking the mod before running, to avoid errors or confusion");
            }

            _allHeadParts = Program.State.LoadOrder.PriorityOrder.WinningOverrides<IHeadPartGetter>().Select(s => s.FormKey).ToHashSet();
            if (Program.settings.GroupPresets)
            {
                // find Face formIDs and identify the race
                _raceByFacePart = Program.State.LoadOrder.PriorityOrder.WinningOverrides<IHeadPartGetter>().
                    Where(s => s.EditorID!.IndexOf(_facePartKey, StringComparison.OrdinalIgnoreCase) >= 0).
                    ToDictionary(s => s.FormKey, s => GetRace(s.EditorID!));
            }
            foreach (string presetFile in Directory.GetFiles(_inputFolder, "*.jslot", options))
            {
                _presetUpdated = false;
                if (!presetFile.ToLower().Contains(_validPresetPath))
                {
                    Program.Logger.Write("---- Preset file outside valid location {0}", presetFile);
                    continue;
                }
                string presetExtension = Path.GetExtension(presetFile);
                // read the contexts of the preset file and convert any merged plugin names and formids
                _presetFileFull = presetFile;
                _originalFileName = Path.GetFileName(_presetFileFull);
                _newFileName = _originalFileName;
                PresetPaths.Add(_presetFileFull);
                Program.Logger.Write("---- Preset file checks for {0}", _presetFileFull);

                using (StreamReader reader = File.OpenText(_presetFileFull))
                {
                    _preset = (JObject)JToken.ReadFrom(new JsonTextReader(reader));
                    CheckTextures();
                    if (CheckHeadParts())
                    {
                        CheckTextures();
                    }
                }
                // If we've fixed and updated this preset, or just copied it to a grouping subdirectory, rename and move the old one so we don't process it again
                if (Program.settings.WriteChanged && _presetUpdated)
                {
                    try
                    {
                        string relativePath = Path.GetRelativePath(Program.settings.InputFolder, _presetFileFull);
                        string? dirname = Path.GetDirectoryName(relativePath);
                        string newDirPath = Path.Join(Program.settings.BackupFolder, _prefixProcessed, dirname);
                        Directory.CreateDirectory(newDirPath);
                        string newFilePath = Path.Join(Program.settings.BackupFolder, _prefixProcessed, relativePath);
                        File.Move(_presetFileFull, newFilePath);
                        Program.Logger.Write("Moved original Preset file {0} to {1}", _presetFileFull, newFilePath);
                    }
                    catch (Exception ex)
                    {
                        Program.Logger.Write("WARNING Cannot move Preset file {0} to {1}, it may have been processed before.\nError: {2}.", _presetFileFull, _presetFileFull + '.' + _prefixProcessed, ex.Message);
                    }
                }
            }
            if (_badTextures.Count > 0)
            {
                Program.Logger.Write("{0} Bad Textures Found", _badTextures.Count);
                foreach (string texture in _badTextures)
                {
                    Program.Logger.Write(texture);
                }
            }
            if (_badPlugins.Count > 0)
            {
                Program.Logger.Write("{0} Bad Mods Found", _badPlugins.Count);
                foreach (string mod in _badPlugins)
                {
                    Program.Logger.Write(mod);
                }
            }
        }

        string GetRace(string editorID)
        {
            foreach (var raceTag in _raceTagByRaceName)
            {
                if (editorID.IndexOf(raceTag.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return raceTag.Value;
                }
            }
            // Race not found, no prefix
            Program.Logger.Write("Race unknown for Face Part {0}", editorID);
            return "Unknown";
        }

        private void CheckTextures()
        {
            // introspect preset for all textures used that were not checked before
            ISet<string> presetTextures = new HashSet<string>();
            if (!_preset!.TryGetValue("faceTextures", out JToken? faceTextures))
            {
                Program.Logger.Write("Preset contains no 'faceTextures'");
            }
            else
            {
                var textures = ((JArray)faceTextures).Where(s => s["texture"] is not null).Select(s => (string)s["texture"]!).ToHashSet<string>();
                presetTextures.UnionWith(textures);
            }

            if (!_preset.TryGetValue("overrides", out JToken? overrides))
            {
                Program.Logger.Write("Preset contains no 'overrides'");
            }
            else
            {
                ISet<string> textures = new HashSet<string>();
                foreach (JObject next in (JArray) overrides)
                {
                    foreach (JObject value in (JArray) next["values"]!)
                    {
                        string texture = (string) value["data"]!;
                        if (texture.EndsWith(Textures.TextureSuffix))
                            textures.Add(texture);
                    }
                }
                presetTextures.UnionWith(textures);
            }

            if (!_preset.TryGetValue("tintInfo", out JToken? tintInfo))
            {
                Program.Logger.Write("Preset contains no 'tintInfo'");
            }
            else
            {
                var textures = ((JArray)tintInfo).Where(s => s["texture"] is not null).Select(s => (string)s["texture"]!).ToHashSet<string>();
                presetTextures.UnionWith(textures);
            }
            foreach (string texture in presetTextures)
            {
                if (!_textures.Contains(texture))
                {
                    Program.Logger.Write("Missing {0}", texture);
                    _badTextures.Add(texture);
                }
            }

            _checkedTextures.UnionWith(presetTextures);
        }

        private bool CheckHeadParts()
        {
            // introspect headParts for contained formIdentifiers
            if (!_preset!.TryGetValue("modNames", out JToken? modNames))
            {
                Program.Logger.Write("File contains no 'modNames', skipping");
                return false;
            }
            if (!_preset.TryGetValue("mods", out JToken? mods))
            {
                Program.Logger.Write("File contains no 'mods', skipping");
                return false;
            }

            IDictionary<string, string> pluginsMapped = new Dictionary<string, string>();
            IList<string>? originalMods = ((JArray)modNames).ToObject<IList<string>>();
            if (originalMods is null)
            {
                Program.Logger.Write("File contains 'modNames' in incorrect format, skipping");
                return false;
            }
            bool isHighPoly = originalMods.Contains(_highPolyMod);
            bool isCotR = originalMods.Contains(_cotrMod);

            IList<JObject>? modInfo = ((JArray)mods).ToObject<IList<JObject>>();
            if (modInfo is null)
            {
                Program.Logger.Write("File contains 'mods' in incorrect format, skipping");
                return false;
            }
            // introspect analyze headParts for contained formIdentifiers
            bool updatedFile = false;
            string race = "";
            if (_preset.TryGetValue("headParts", out JToken? headParts))
            {
                foreach (JToken? headPart in headParts)
                {
                    var formIdString = headPart["formId"];
                    if (formIdString is null)
                        continue;
                    var formId = formIdString.Value<uint>();
                    bool isESPFE = (formId & ESPFEPlugin) == ESPFEPlugin;
                    if (isESPFE)
                    {
                        formId &= ESPFEForm;
                    }
                    else
                    {
                        formId &= ESPForm;
                    }
                    var formIdentifierString = headPart["formIdentifier"];
                    if (formIdentifierString is null)
                        continue;
                    var formIdentifier = formIdentifierString.Value<string>();
                    var elements = formIdentifier!.Split('|');
                    var pluginName = elements[0];
                    uint formIdPart = uint.Parse(elements[1], NumberStyles.HexNumber);
                    // this form ID should align with the one from "formID" when appropriately masked
                    if (isESPFE)
                    {
                        if ((formIdPart & ESPFEForm) != formId)
                        {
                            Program.Logger.Write("Contains mismatched HDPT ESPFE Form IDs {0}/{1}",
                                formIdString, formIdentifierString);
                            continue;
                        }
                        Program.Logger.Write("Check HDPT ESPFE Form IDs {0}(0x{1:X8})/{2}",
                            formIdString, formIdString.Value<uint>(), formIdentifierString);
                    }
                    else
                    {
                        if ((formIdPart & ESPForm) != formId)
                        {
                            Program.Logger.Write("Contains mismatched HDPT non-ESPFE Form IDs {0}/{1}",
                                formIdString, formIdentifierString);
                            continue;
                        }
                        Program.Logger.Write("Check HDPT non-ESPFE Form IDs {0}(0x{1:X8})/{2}",
                            formIdString, formIdString.Value<uint>(), formIdentifierString);
                    }

                    if (ModKey.TryFromFileName(pluginName, out ModKey modKey, out string errorReason))
                    {
                        FormKey formKey = new FormKey(modKey, formId);
                        // If there is no HDPT with this FormID, attempt merged plugin resolution
                        if (!_allHeadParts.Contains(formKey))
                        {
                            // mod not found, could be an error or a merged plugin
                            bool isMapped = false;
                            if (_mergeInfo.MergedPlugins.TryGetValue(pluginName, out string? mappedESP))
                            {
                                updatedFile = true;
                                isMapped = true;
                                pluginsMapped.TryAdd(pluginName, mappedESP);
                                ModKey.TryFromFileName(mappedESP, out ModKey mappedModKey, out string reason);
                                if (_mergeInfo.MappedFormIds.TryGetValue(elements[0], out var formMappings))
                                {
                                    // check if FormID needs mapping - we do not include the index here, which should not present problems
                                    if (formMappings.TryGetValue(string.Format("{0:X6}", formId), out var newFormID))
                                    {
                                        // headPart["formId"] = NewFormMask | uint.Parse(newFormID, NumberStyles.AllowHexSpecifier);
                                        headPart["formId"] = uint.Parse(newFormID, NumberStyles.AllowHexSpecifier);
                                        headPart["formIdentifier"] = string.Format("{0}|{1}", mappedESP, newFormID);
                                        formKey = new FormKey(mappedModKey, uint.Parse(newFormID, NumberStyles.AllowHexSpecifier));
                                    }
                                    else
                                    {
                                        // headPart["formId"] = NewFormMask | formId;
                                        headPart["formId"] = formId;
                                        headPart["formIdentifier"] = string.Format("{0}|{1:X6}", mappedESP, formId);
                                        formKey = new FormKey(mappedModKey, formId);
                                    }
                                }
                                else
                                {
                                    // same formid in the merged plugin
                                    // headPart["formId"] = NewFormMask | formId;
                                    headPart["formId"] = formId;
                                    headPart["formIdentifier"] = string.Format("{0}|{1:X6}", mappedESP, formId);
                                    formKey = new FormKey(mappedModKey, formId);
                                }
                                Program.Logger.Write("formIdentifier updated to {0}", headPart["formIdentifier"]);
                            }

                            if (!isMapped)
                            {
                                _badPlugins.Add(pluginName);
                                Program.Logger.Write("Contains bad HDPT {0}", formIdentifier);
                            }
                        }
                        else
                        {
                            // record original plugin still present
                            pluginsMapped.TryAdd(pluginName, pluginName);
                        }

                        if (Program.settings.GroupPresets && race.IsNullOrEmpty())
                        {
                            // Assume for now that face part is not merged - typically HPH or vanilla
                            if (_raceByFacePart!.TryGetValue(formKey, out var raceTag))
                            {
                                race = raceTag;
                            }
                        }
                    }
                }
            }
            // If this file required updates or grouping, output the new lines to the patch location
            if (updatedFile || Program.settings.GroupPresets)
            {
                _presetUpdated = true;
                // get new path ready
                string prefix = "";
                if (Program.settings.GroupPresets)
                {
                    if (isCotR)
                    {
                        prefix = _cotrPrefix;
                    }
                    else
                    {
                        if (isHighPoly)
                        {
                            prefix = _highPolyPrefix;
                        }
                        if (!race.IsNullOrEmpty())
                        {
                            prefix = Path.Join(race, prefix);
                        }
                    }
                    if (prefix.IsNullOrEmpty())
                    {
                        prefix = _otherPrefix;
                    }
                }
                string updatedPath = Path.Join(Program.settings.OutputFolder, _validPresetPath, prefix);
                Directory.CreateDirectory(updatedPath);
                updatedPath = Path.Join(updatedPath, _newFileName);

                ISet<string> pluginsDone = new HashSet<string>();
                // update mods and modNames JSON elements based on plugins refernced in the updated preset
                if (Program.settings.WriteChanged)
                {
                    if (updatedFile)
                    {
                        var newModNames = new HashSet<string>(pluginsMapped.Values);
                        _preset["modNames"] = new JArray(newModNames);
                        var newModInfo = new List<JObject>();
                        foreach (var newMod in newModNames)
                        {
                            if (ModKey.TryFromFileName(newMod, out ModKey newKey, out string errorReason))
                            {
                                // insert an entry for the new plugin in 'mods' in the same location as the one we just removed, to preserve ordering
                                newModInfo.Add(new JObject {
                                {"index", Program.State.LoadOrder.IndexOf(newKey)},
                                {"name", newMod }
                            });
                            }
                        }
                        _preset["mods"] = new JArray(newModInfo);

                        // serialize JSON directly to a file
                        using (StreamWriter file = File.CreateText(updatedPath))
                        {
                            JsonSerializer serializer = new JsonSerializer();
                            serializer.Formatting = Formatting.Indented;
                            serializer.Serialize(file, _preset);
                        }
                        Program.Logger.Write("---- {0} contains updated preset", updatedPath);
                    }
                    else
                    {
                        // just relocating an already-good preset to group for clarity
                        File.Copy(_presetFileFull!, updatedPath);
                        Program.Logger.Write("---- {0} contains grouped preset", updatedPath);
                    }
                }
            }
            return true;
        }
    }
}