using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;

namespace PresetChecker
{
    public class Textures
    {
        public static readonly string TexturePrefix = "textures\\";
        public static readonly string TextureSuffix = ".dds";
        public static readonly string TextureFilter = "*" + TextureSuffix;

        private readonly IDictionary<string, byte> _textureNames = new ConcurrentDictionary<string, byte>();

        public Textures()
        {
            // inventory merge candidates in the current directory
            string textureRoot = Program.State.DataFolderPath + '\\'+ TexturePrefix;
            EnumerationOptions options = new EnumerationOptions { RecurseSubdirectories = true };
            //Program.Logger.Write("Loose Textures");
            foreach (string textureFile in Directory.GetFiles(Program.State.DataFolderPath, TextureFilter, options))
            {
                if (!textureFile.Contains(TexturePrefix, StringComparison.InvariantCultureIgnoreCase))
                    continue;
                string normalizedPath = Path.GetRelativePath(textureRoot, textureFile.ToLower());
                if (_textureNames.TryAdd(normalizedPath, (byte)0))
                {
                    //Program.Logger.Write("{0}", textureFile);
                }
            }
            //Program.Logger.Write("Loose Textures done");

            // Introspect all known BSAs to locate textures not found as loose files. Dups are ignored - first find wins.
            foreach (var bsaFile in Archive.GetApplicableArchivePaths(GameRelease.SkyrimSE, Program.State.DataFolderPath))
            {
                //Program.Logger.Write("BSA file {0}", bsaFile);
                var bsaReader = Archive.CreateReader(GameRelease.SkyrimSE, bsaFile);
                bsaReader.Files.AsParallel().
                    Where(candidate => candidate.Path.ToLower().EndsWith(TextureSuffix)).
                    ForAll(bsaTexture =>
                    {
                        if (!bsaTexture.Path.StartsWith(TexturePrefix, StringComparison.InvariantCultureIgnoreCase))
                            return;
                        string normalizedPath = bsaTexture.Path.Substring(TexturePrefix.Length).ToLower();
                        if (_textureNames.TryAdd(normalizedPath, (byte)0))
                        {
                            //Program.Logger.Write("{0}", bsaTexture.Path);
                        }
                    });
                //Program.Logger.Write("BSA done {0}", bsaFile);
            }
        }

        public bool Contains(string textureName)
        {
            return _textureNames.ContainsKey(textureName.ToLower());
        }
    }
}
