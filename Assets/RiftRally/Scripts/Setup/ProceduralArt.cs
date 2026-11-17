using UnityEngine;

namespace RiftRally.Setup
{
    /// <summary>
    /// Mesh and material helpers for building the rink in code.
    ///
    /// The whole playfield is generated at runtime instead of being authored as prefabs. For
    /// a table-scale game that is almost entirely boxes and rings this is the cheaper option:
    /// every dimension comes straight from <see cref="RiftRally.Core.GameConfig"/>, so
    /// retuning the rink cannot leave stale geometry behind, and the repository carries no
    /// binary assets that have to be kept in sync with the code.
    /// </summary>
    public static class ProceduralArt
    {
        private static Material _litTemplate;

        /// <summary>
        /// A material matching the project's render pipeline.
        ///
        /// It is cloned from a throwaway primitive rather than looked up by shader name,
        /// because <c>Shader.Find</c> only resolves shaders that happened to be pulled into
        /// the build, whereas the default primitive material always is.
        /// </summary>
        private static Material LitTemplate
        {
            get
            {
                if (_litTemplate != null) return _litTemplate;

                var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var renderer = probe.GetComponent<MeshRenderer>();
                _litTemplate = renderer != null && renderer.sharedMaterial != null
                    ? new Material(renderer.sharedMaterial)
                    : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                Object.Destroy(probe);
                return _litTemplate;
            }
        }

        /// <summary>Creates an opaque material, optionally glowing.</summary>
        public static Material MakeMaterial(Color color, float emission = 0f,
                                            float smoothness = 0.45f, float metallic = 0f)
        {
            var mat = new Material(LitTemplate) { name = $"RR_{color}" };

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);

            if (emission > 0f && mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetColor("_EmissionColor", color * emission);
            }

            return mat;
        }

        /// <summary>
        /// A glowing material that blends additively.
        ///
        /// Additive blending is the right choice for every effect in this game - halos,
        /// trails, sparks, aim paths - for three reasons: it needs no depth sorting, so
        /// overlapping effects cannot flicker against each other as the head moves; it never
        /// draws black, so an effect fades out by going dim rather than by going grey; and it
        /// is the cheapest blend a mobile GPU can do, which matters at 90 Hz on a Quest.
        /// </summary>
        public static Material MakeGlowMaterial(Color color, float emission = 2.0f)
        {
            var mat = MakeMaterial(color, emission);
            mat.name = $"RR_Glow_{color}";
            ConfigureBlend(mat, additive: true);
            return mat;
        }

        /// <summary>A see-through material that respects alpha, for panels and shadows.</summary>
        public static Material MakeTransparentMaterial(Color color, float emission = 0f)
        {
            var mat = MakeMaterial(color, emission);
            mat.name = $"RR_Alpha_{color}";
            ConfigureBlend(mat, additive: false);
            return mat;
        }

        /// <summary>
        /// Switches a URP Lit material to a blended surface. The float properties and
        /// keywords have to be set together: URP reads the keywords at render time but the
        /// floats are what survive a material being saved or inspected.
        /// </summary>
        private static void ConfigureBlend(Material mat, bool additive)
        {
            const int srcAlpha = 5, one = 1, oneMinusSrcAlpha = 10;

            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);     // transparent
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", additive ? 2f : 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", additive ? one : srcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", additive ? one : oneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 2f);            // back faces off

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.SetShaderPassEnabled("ShadowCaster", false);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        /// <summary>
        /// Creates a box of the given world size. <paramref name="withCollider"/> is false for
        /// pure decoration, which keeps the physics scene as small as possible.
        /// </summary>
        public static GameObject Box(Transform parent, string name, Vector3 localCenter,
                                     Vector3 size, Material material, bool withCollider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = size;

            var collider = go.GetComponent<Collider>();
            if (!withCollider) Object.Destroy(collider);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;
            return go;
        }

        /// <summary>A flat disc lying in the local XZ plane.</summary>
        public static GameObject Disc(Transform parent, string name, Vector3 localCenter,
                                      float radius, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            go.transform.localRotation = Quaternion.identity;

            var mesh = BuildAnnulus(0f, radius, 48);
            mesh.name = $"{name}_Mesh";
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>A flat ring (annulus) lying in the local XZ plane.</summary>
        public static GameObject Ring(Transform parent, string name, Vector3 localCenter,
                                      float innerRadius, float outerRadius, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            go.transform.localRotation = Quaternion.identity;

            var mesh = BuildAnnulus(innerRadius, outerRadius, 64);
            mesh.name = $"{name}_Mesh";
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>
        /// Builds a ring mesh facing +Y. An inner radius of zero produces a solid disc.
        /// </summary>
        public static Mesh BuildAnnulus(float innerRadius, float outerRadius, int segments)
        {
            segments = Mathf.Max(8, segments);
            bool solid = innerRadius <= 0.0001f;

            var verts = new Vector3[solid ? segments + 1 : segments * 2];
            var uvs = new Vector2[verts.Length];
            var tris = new int[solid ? segments * 3 : segments * 6];

            if (solid)
            {
                verts[0] = Vector3.zero;
                uvs[0] = new Vector2(0.5f, 0.5f);
                for (int i = 0; i < segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2f;
                    verts[i + 1] = new Vector3(Mathf.Cos(a) * outerRadius, 0f, Mathf.Sin(a) * outerRadius);
                    uvs[i + 1] = new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f);
                }
                for (int i = 0; i < segments; i++)
                {
                    int next = i + 2 > segments ? 1 : i + 2;
                    tris[i * 3 + 0] = 0;
                    tris[i * 3 + 1] = next;
                    tris[i * 3 + 2] = i + 1;
                }
            }
            else
            {
                for (int i = 0; i < segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2f;
                    float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                    verts[i * 2 + 0] = new Vector3(cos * innerRadius, 0f, sin * innerRadius);
                    verts[i * 2 + 1] = new Vector3(cos * outerRadius, 0f, sin * outerRadius);
                    uvs[i * 2 + 0] = new Vector2(i / (float)segments, 0f);
                    uvs[i * 2 + 1] = new Vector2(i / (float)segments, 1f);
                }
                for (int i = 0; i < segments; i++)
                {
                    int i0 = i * 2;
                    int i1 = i * 2 + 1;
                    int i2 = ((i + 1) % segments) * 2;
                    int i3 = ((i + 1) % segments) * 2 + 1;

                    tris[i * 6 + 0] = i0;
                    tris[i * 6 + 1] = i3;
                    tris[i * 6 + 2] = i1;
                    tris[i * 6 + 3] = i0;
                    tris[i * 6 + 4] = i2;
                    tris[i * 6 + 5] = i3;
                }
            }

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A short upright cylinder: stones, posts and gate pillars.</summary>
        public static GameObject Cylinder(Transform parent, string name, Vector3 localCenter,
                                          float radius, float height, Material material,
                                          bool withCollider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            go.transform.localRotation = Quaternion.identity;

            // Unity's cylinder primitive is 2 units tall and 1 unit across.
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);

            var collider = go.GetComponent<Collider>();
            if (!withCollider) Object.Destroy(collider);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;
            return go;
        }

        /// <summary>A line renderer configured for thin, glowing rink markings and aim paths.</summary>
        public static LineRenderer Line(Transform parent, string name, Material material,
                                        float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = material;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.alignment = LineAlignment.TransformZ;
            line.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        /// <summary>
        /// A tiny burst emitter used for impacts and warps. Configured in code so no
        /// particle assets are needed.
        /// </summary>
        public static ParticleSystem Burst(Transform parent, string name, Color color,
                                           Material material, float size = 0.012f,
                                           int count = 14)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();

            var main = ps.main;
            main.duration = 0.4f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 0.35f;
            main.startSpeed = 0.45f;
            main.startSize = size;
            main.startColor = color;
            main.gravityModifier = 0.25f;
            main.maxParticles = 64;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.01f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                if (material != null) renderer.sharedMaterial = material;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return ps;
        }
    }
}
