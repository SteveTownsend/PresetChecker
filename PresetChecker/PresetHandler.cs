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
        private static readonly uint NewFormMask = 0x80000000;
        private ISet<FormKey> _allHeadParts;
        private readonly MergeInfo _mergeInfo;
        private readonly string _inputFolder;
        private JObject? _preset;
        private string? _presetFile;
        private ISet<string> _checkedTextures = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        private readonly Textures _textures;

        public PresetHandler(string inputFolder, MergeInfo mergeInfo, Textures textures)
        {
            _inputFolder = inputFolder;
            if (String.IsNullOrEmpty(_inputFolder))
                _inputFolder = Program.State.DataFolderPath;
            _mergeInfo = mergeInfo;
            _textures = textures;

            _allHeadParts = Program.State.LoadOrder.PriorityOrder.WinningOverrides<IHeadPartGetter>().Select(s => s.FormKey).ToHashSet();
            // inventory merge candidates in the current directory
            EnumerationOptions options = new EnumerationOptions { RecurseSubdirectories = true };
            foreach (string presetFile in Directory.GetFiles(_inputFolder, "*.jslot", options))
            {
                // read the contexts of the preset file and convert any merged plugin names and formids
                _presetFile = presetFile;
                PresetPaths.Add(_presetFile);
                Console.WriteLine("---- Preset file {0}", _presetFile);
                using (StreamReader reader = File.OpenText(_presetFile))
                {
                    _preset = (JObject)JToken.ReadFrom(new JsonTextReader(reader));
                    CheckTextures();
                    if (CheckHeadParts())
                    {
                        CheckTextures();
                    }
                }
            }
        }

        private void CheckTextures()
        {
            // introspect preset for all textures used that were not checked before
            ISet<string> presetTextures = new HashSet<string>();
            if (!_preset!.TryGetValue("faceTextures", out JToken? faceTextures))
            {
                Console.WriteLine("Preset contains no 'faceTextures'");
            }
            else
            {
                var textures = ((JArray)faceTextures).Where(s => s["texture"] is not null).Select(s => (string)s["texture"]!).ToHashSet<string>();
                presetTextures.UnionWith(textures);
            }

            if (!_preset.TryGetValue("overrides", out JToken? overrides))
            {
                Console.WriteLine("Preset contains no 'overrides'");
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
                Console.WriteLine("Preset contains no 'tintInfo'");
            }
            else
            {
                var textures = ((JArray)tintInfo).Where(s => s["texture"] is not null).Select(s => (string)s["texture"]!).ToHashSet<string>();
                presetTextures.UnionWith(textures);
            }
            foreach (string texture in presetTextures)
            {
                if (!_checkedTextures.Contains(texture))
                {
                    if (!_textures.Contains(texture))
                    {
                        Console.WriteLine("Missing {0}", texture);
                    }
                    _checkedTextures.Add(texture);
                }
            }

            _checkedTextures.UnionWith(presetTextures);
        }

        private bool CheckHeadParts()
        {
            // introspect headParts for contained formIdentifiers
            if (!_preset!.TryGetValue("modNames", out JToken? modNames))
            {
                Console.WriteLine("File contains no 'modNames', skipping");
                return false;
            }
            if (!_preset.TryGetValue("mods", out JToken? mods))
            {
                Console.WriteLine("File contains no 'mods', skipping");
                return false;
            }

            IDictionary<string, string> pluginsMapped = new Dictionary<string, string>();
            IList<string>? referencedMods = ((JArray)modNames).ToObject<IList<string>>();
            if (referencedMods is null)
            {
                Console.WriteLine("File contains 'modNames' in incorrect format, skipping");
                return false;
            }
            IList<JObject>? modInfo = ((JArray)mods).ToObject<IList<JObject>>();
            if (modInfo is null)
            {
                Console.WriteLine("File contains 'mods' in incorrect format, skipping");
                return false;
            }

            // introspect analyze headParts for contained formIdentifiers
            bool updatedFile = false;
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
                    uint formIdPart = uint.Parse(elements[1], NumberStyles.HexNumber);
                    // this form ID should align with the one from "formID" when appropriately masked
                    if (isESPFE)
                    {
                        if ((formIdPart & ESPFEForm) != formId)
                        {
                            Console.WriteLine("Contains mismatched HDPT ESPFE Form IDs {0}/{1}",
                                formIdString, formIdentifierString);
                            continue;
                        }
                        Console.WriteLine("Check HDPT ESPFE Form IDs {0}(0x{1:X8})/{2}",
                            formIdString, formIdString.Value<uint>(), formIdentifierString);
                    }
                    else
                    {
                        if ((formIdPart & ESPForm) != formId)
                        {
                            Console.WriteLine("Contains mismatched HDPT non-ESPFE Form IDs {0}/{1}",
                                formIdString, formIdentifierString);
                            continue;
                        }
                        Console.WriteLine("Check HDPT non-ESPFE Form IDs {0}(0x{1:X8})/{2}",
                            formIdString, formIdString.Value<uint>(), formIdentifierString);
                    }

                    if (ModKey.TryFromFileName(elements[0], out ModKey modKey, out string errorReason))
                    {
                        FormKey formKey = new FormKey(modKey, formId);
                        // If there is no HDPT with this FormID, attempt merged plugin resolution
                        if (!_allHeadParts.Contains(formKey))
                        {
                            // mod not found, could be an error or a merged plugin
                            bool isMapped = false;
                            if (_mergeInfo.MergedPlugins.TryGetValue(elements[0], out string? mappedESP))
                            {
                                updatedFile = true;
                                isMapped = true;
                                pluginsMapped.TryAdd(elements[0], mappedESP);

                                if (isMapped && _mergeInfo.MappedFormIds.TryGetValue(elements[0], out var formMappings))
                                {
                                    // check if FormID needs mapping - we do not include the index here, which should not present problems
                                    if (formMappings.TryGetValue(string.Format("{0:X6}", formId), out var newFormID))
                                    {
                                        headPart["formId"] = NewFormMask | uint.Parse(newFormID, NumberStyles.AllowHexSpecifier);
                                        headPart["formIdentifier"] = string.Format("{0}|{1}", mappedESP, newFormID);
                                    }
                                    else
                                    {
                                        headPart["formId"] = NewFormMask | formId;
                                        headPart["formIdentifier"] = string.Format("{0}|{1:X6}", mappedESP, formId);
                                    }
                                }
                            }

                            if (!isMapped)
                            {
                                Console.WriteLine("Contains bad HDPT {0}", formIdentifier);
                            }
                        }
                    }
                }
            }
            // If this file required updates, output the new lines to the patch location
            if (updatedFile)
            {
                ISet<string> pluginsDone = new HashSet<string>();
                // update mods element based on plugins found to be merged
                foreach (var mappedMod in pluginsMapped)
                {
                    referencedMods.Replace(mappedMod.Key, mappedMod.Value);
                    // find the existing entry for this plugin - we will use its index, which does not seem to have informational value
                    // beyond being unique
                    int index = 0;
                    int modIndex = -1;
                    foreach (var nextMod in modInfo)
                    {
                        if (nextMod.TryGetValue("name", StringComparison.InvariantCultureIgnoreCase,
                                out JToken? entry) && entry.Value<string>() == mappedMod.Key)
                        {
                            modIndex = (int)nextMod["index"]!.Value<uint>();
                            modInfo.RemoveAt(index);
                            break;
                        }
                        ++index;
                    }

                    if (pluginsDone.Add(mappedMod.Value))
                    {
                        // insert an entry for the new plugin in 'mods' in the same location as the one we just removed, to preserve ordering
                        modInfo.Insert(index, new JObject {
                                    {"index", modIndex},
                                    {"name", mappedMod.Value }
                                });
                    }
                }

                // remove duplicates from the mapped plugin list and update the JSON graph
                referencedMods = new HashSet<string>(referencedMods).ToList<string>();
                _preset["modNames"] = new JArray(referencedMods);

                string updatedPath = Path.GetRelativePath(_inputFolder, _presetFile!);
                updatedPath = Program.settings.OutputFolder + '\\' + updatedPath;
                Directory.CreateDirectory(Path.GetDirectoryName(updatedPath)!);
                // serialize JSON directly to a file
                using (StreamWriter file = File.CreateText(updatedPath))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    serializer.Formatting = Formatting.Indented;
                    serializer.Serialize(file,_preset);
                }
                Console.WriteLine("---- {0} written", updatedPath);
            }

            return true;
        }
    }
}