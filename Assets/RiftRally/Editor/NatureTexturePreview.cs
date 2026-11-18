using System.IO;
using UnityEditor;
using UnityEngine;
using RiftRally.Scenery;

namespace RiftRally.EditorTools
{
    /// <summary>
    /// Writes the baked landscape surfaces out as PNGs so they can be looked at.
    ///
    /// The landscape has no source art, which is the point of it - but it also means there is
    /// nothing to open when a biome comes out wrong. Checking the ground bake used to mean
    /// building, deploying to a headset and standing in the world, which is a very long loop
    /// for "the moor is too grey". This dumps what the bake actually produced, at the
    /// resolution it would ship at, in a couple of seconds.
    ///
    /// It is also how the screenshots for the store listing and the submission get made.
    /// </summary>
    public static class NatureTexturePreview
    {
        private const string MenuPath = "Rift Rally/Export Landscape Textures";

        [MenuItem(MenuPath)]
        public static void ExportFromMenu()
        {
            string folder = Path.Combine(Path.GetTempPath(), "RiftRallyLandscapes");
            Export(folder, NatureTextures.High);
            EditorUtility.RevealInFinder(folder);
        }

        /// <summary>
        /// Batch-mode entry point. The output folder and resolution come from the command
        /// line: <c>-outputPath &lt;folder&gt;</c> and <c>-textureBudget &lt;pixels&gt;</c>.
        /// </summary>
        public static void ExportFromCommandLine()
        {
            string folder = Path.Combine(Path.GetTempPath(), "RiftRallyLandscapes");
            int budget = NatureTextures.High;

            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-outputPath") folder = args[i + 1];
                else if (args[i] == "-textureBudget") int.TryParse(args[i + 1], out budget);
            }

            Export(folder, budget);
        }

        /// <summary>
        /// Bakes every biome's ground and sky at <paramref name="budget"/> and writes them
        /// into <paramref name="folder"/>.
        /// </summary>
        public static void Export(string folder, int budget)
        {
            Directory.CreateDirectory(folder);

            // Read the texels back: the whole job here is to save them to a file.
            NatureTextures.KeepReadable = true;
            NatureTextures.Budget = budget;

            int written = 0;
            foreach (Landscape landscape in LandscapeCatalog.All)
            {
                string slug = landscape.Name.Replace(" ", "");

                NatureTextures.GroundSet ground = NatureTextures.Ground(
                    landscape, landscape.Name.GetHashCode(), 62f, 3.6f, 5.5f);

                written += Write(ground.Albedo, Path.Combine(folder, $"{slug}_ground.png"));
                written += Write(ground.Normal, Path.Combine(folder, $"{slug}_ground_normal.png"));
                written += Write(NatureTextures.Sky(landscape),
                                 Path.Combine(folder, $"{slug}_sky.png"));

                // One biome at a time: eight 4096 ground bakes held at once is a gigabyte and
                // a half, and the point of the cache is that it does not do that.
                NatureTextures.Flush();
            }

            written += Write(NatureTextures.Bark(), Path.Combine(folder, "surface_bark.png"));
            written += Write(NatureTextures.Leaves(), Path.Combine(folder, "surface_leaves.png"));
            written += Write(NatureTextures.Stone(), Path.Combine(folder, "surface_stone.png"));
            written += Write(NatureTextures.Blades(), Path.Combine(folder, "surface_grass.png"));
            written += Write(NatureTextures.Ripples(), Path.Combine(folder, "surface_water.png"));

            NatureTextures.Flush();
            NatureTextures.KeepReadable = false;

            Debug.Log($"[RiftRally] Wrote {written} landscape textures at {budget}px to {folder}");
        }

        private static int Write(Texture2D texture, string path)
        {
            if (texture == null) return 0;

            File.WriteAllBytes(path, texture.EncodeToPNG());
            return 1;
        }
    }
}
