using System;
using UnityEngine;

namespace RiftRally.Setup
{
    /// <summary>
    /// Builds a lightweight world around the table. The rink stays the same competitive
    /// surface, while the space beyond it changes biome on every launch so the game does not
    /// feel like it is floating in the default blue scene background.
    /// </summary>
    public static class EnvironmentBuilder
    {
        private struct Biome
        {
            public string Name;
            public Color Sky;
            public Color Ground;
            public Color Far;
            public Color Accent;
            public bool HasTrees;
            public bool HasCrystals;
        }

        private static readonly Biome[] Biomes =
        {
            new Biome { Name = "Alpine Dawn", Sky = new Color(0.22f, 0.16f, 0.20f), Ground = new Color(0.18f, 0.13f, 0.12f), Far = new Color(0.30f, 0.23f, 0.27f), Accent = new Color(0.92f, 0.58f, 0.32f), HasTrees = true },
            new Biome { Name = "Red Mesa", Sky = new Color(0.28f, 0.12f, 0.08f), Ground = new Color(0.24f, 0.10f, 0.055f), Far = new Color(0.42f, 0.18f, 0.09f), Accent = new Color(1.00f, 0.45f, 0.12f), HasCrystals = true },
            new Biome { Name = "Moss Valley", Sky = new Color(0.10f, 0.18f, 0.14f), Ground = new Color(0.075f, 0.13f, 0.085f), Far = new Color(0.12f, 0.25f, 0.16f), Accent = new Color(0.42f, 0.88f, 0.48f), HasTrees = true },
            new Biome { Name = "Violet Moon", Sky = new Color(0.12f, 0.08f, 0.20f), Ground = new Color(0.075f, 0.055f, 0.12f), Far = new Color(0.21f, 0.12f, 0.32f), Accent = new Color(0.70f, 0.36f, 1.00f), HasCrystals = true },
            new Biome { Name = "Golden Steppe", Sky = new Color(0.27f, 0.22f, 0.11f), Ground = new Color(0.22f, 0.17f, 0.075f), Far = new Color(0.40f, 0.30f, 0.13f), Accent = new Color(1.00f, 0.76f, 0.24f), HasTrees = true }
        };

        public static string Build(Transform parent, int seed, out Color skyColor)
        {
            var random = new System.Random(seed);
            Biome biome = Biomes[Mathf.Abs(seed) % Biomes.Length];
            skyColor = biome.Sky;

            var root = new GameObject("ProceduralEnvironment").transform;
            root.SetParent(parent, false);

            Material ground = ProceduralArt.MakeMaterial(biome.Ground, 0f, 0.95f);
            Material far = ProceduralArt.MakeMaterial(biome.Far, 0f, 0.85f);
            Material accent = ProceduralArt.MakeGlowMaterial(biome.Accent, 0.8f);

            ProceduralArt.Box(root, "Ground", new Vector3(0f, -0.065f, 1.2f),
                new Vector3(18f, 0.10f, 18f), ground, false);

            // Layered low-poly ridges make a real horizon without expensive terrain or assets.
            for (int i = 0; i < 14; i++)
            {
                float angle = i / 14f * Mathf.PI * 2f;
                float radius = 5.2f + (float)random.NextDouble() * 1.8f;
                float height = 1.1f + (float)random.NextDouble() * 2.4f;
                float width = 0.8f + (float)random.NextDouble() * 1.2f;
                var mountain = ProceduralArt.Cylinder(root, $"Horizon_{i}",
                    new Vector3(Mathf.Cos(angle) * radius, height * 0.5f - 0.02f,
                                1.2f + Mathf.Sin(angle) * radius), width, height, far, false);
                mountain.transform.localScale = new Vector3(1.6f, 1f, 0.7f);
                mountain.transform.localRotation = Quaternion.Euler(0f, random.Next(0, 360), 0f);
            }

            if (biome.HasTrees)
            {
                for (int i = 0; i < 11; i++)
                {
                    float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    float radius = 3.4f + (float)random.NextDouble() * 4.0f;
                    Vector3 position = new Vector3(Mathf.Cos(angle) * radius, 0f,
                                                   1.2f + Mathf.Sin(angle) * radius);
                    BuildTree(root, $"Tree_{i}", position, 0.55f + (float)random.NextDouble() * 0.45f, far, accent);
                }
            }

            if (biome.HasCrystals)
            {
                for (int i = 0; i < 9; i++)
                {
                    float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    float radius = 3.6f + (float)random.NextDouble() * 3.5f;
                    Vector3 position = new Vector3(Mathf.Cos(angle) * radius, 0.10f,
                                                   1.2f + Mathf.Sin(angle) * radius);
                    var crystal = ProceduralArt.Cylinder(root, $"Crystal_{i}", position,
                        0.07f + (float)random.NextDouble() * 0.05f,
                        0.35f + (float)random.NextDouble() * 0.55f, accent, false);
                    crystal.transform.localRotation = Quaternion.Euler(
                        random.Next(-12, 13), random.Next(0, 360), random.Next(-12, 13));
                }
            }

            return biome.Name;
        }

        private static void BuildTree(Transform parent, string name, Vector3 position, float scale,
                                      Material trunk, Material crown)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = position;
            ProceduralArt.Cylinder(root, "Trunk", new Vector3(0f, 0.42f * scale, 0f),
                0.07f * scale, 0.84f * scale, trunk, false);
            var crownObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crownObject.name = "Crown";
            crownObject.transform.SetParent(root, false);
            crownObject.transform.localPosition = new Vector3(0f, 0.95f * scale, 0f);
            crownObject.transform.localScale = Vector3.one * (0.52f * scale);
            UnityEngine.Object.Destroy(crownObject.GetComponent<Collider>());
            crownObject.GetComponent<MeshRenderer>().sharedMaterial = crown;
        }
    }
}
