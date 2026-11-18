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
        private static Material _unlitTemplate;

        /// <summary>
        /// A material matching the project's render pipeline.
        ///
        /// The shader is looked up by name first, and the primitive's own material is only a
        /// fallback. It used to be the other way round, which worked perfectly in the Editor
        /// and shipped a completely invisible rink: <c>CreatePrimitive</c> assigns the
        /// built-in Standard material, the Editor silently substitutes URP's Lit shader when
        /// it renders it, and a built player does not - the shader is not in the build at
        /// all, so every surface drew nothing. Nothing in the logs, nothing in a test; just a
        /// black headset.
        ///
        /// <see cref="RiftRally.EditorTools.RiftRallyBuild"/> adds these shaders to the
        /// always-included list so the name lookup cannot fail in a player.
        /// </summary>
        private static Material LitTemplate
        {
            get
            {
                if (_litTemplate != null) return _litTemplate;

                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");

                if (shader != null)
                {
                    _litTemplate = new Material(shader) { name = "RR_LitTemplate" };
                    return _litTemplate;
                }

                // Last resort: whatever a primitive comes with. Better than a null material,
                // and loud about it, because this path means the build is misconfigured.
                Debug.LogError("[RiftRally] URP shaders are missing from this build; " +
                               "falling back to the primitive default material.");

                var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var renderer = probe.GetComponent<MeshRenderer>();
                _litTemplate = renderer != null && renderer.sharedMaterial != null
                    ? new Material(renderer.sharedMaterial)
                    : null;
                Object.Destroy(probe);
                return _litTemplate;
            }
        }

        /// <summary>
        /// The unlit counterpart of <see cref="LitTemplate"/>, found by the same name-first
        /// lookup and for the same reason.
        ///
        /// The scenery needs it: a sky, a sun disc and a distant cloud bank must not react to
        /// the scene light at all, or the horizon darkens whenever the sun angle changes and
        /// the sky stops reading as a sky. Unlit is also the cheapest surface a mobile GPU
        /// can draw, which is what makes a sixty-metre world affordable at 90 Hz.
        /// </summary>
        private static Material UnlitTemplate
        {
            get
            {
                if (_unlitTemplate != null) return _unlitTemplate;

                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null)
                {
                    _unlitTemplate = new Material(shader) { name = "RR_UnlitTemplate" };
                    return _unlitTemplate;
                }

                // No unlit shader in the build: fall back to the lit one rather than to
                // nothing. Slightly wrong shading beats an invisible sky.
                _unlitTemplate = LitTemplate != null ? new Material(LitTemplate) : null;
                return _unlitTemplate;
            }
        }

        /// <summary>
        /// An unlit flat-colour material, optionally alpha blended. Used for everything in
        /// the scenery that is meant to look like light rather than like a surface.
        /// </summary>
        public static Material MakeUnlitMaterial(Color color, bool transparent = false)
        {
            var mat = new Material(UnlitTemplate) { name = $"RR_Unlit_{color}" };

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);

            if (transparent) MakeAlphaBlended(mat);
            return mat;
        }

        /// <summary>
        /// An unlit textured material. <paramref name="cullFront"/> flips which side is
        /// drawn, which is how a sky dome is viewed from the inside without needing an
        /// inside-out mesh, and <paramref name="renderQueue"/> pushes the sky behind
        /// everything else.
        /// </summary>
        public static Material MakeTexturedUnlit(Texture texture, bool cullFront = false,
                                                  bool transparent = false,
                                                  int renderQueue = -1)
        {
            var mat = new Material(UnlitTemplate) { name = "RR_UnlitTex" };

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);

            if (transparent) MakeAlphaBlended(mat);
            // 0 = both sides, 1 = front faces culled (so the inside is what you see),
            // 2 = back faces culled, which is the usual default.
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", cullFront ? 1f : 2f);
            if (cullFront) mat.SetShaderPassEnabled("ShadowCaster", false);
            if (renderQueue >= 0) mat.renderQueue = renderQueue;

            return mat;
        }

        /// <summary>Switches an existing material to ordinary alpha blending.</summary>
        public static void MakeAlphaBlended(Material material) => ConfigureBlend(material, false);

        /// <summary>
        /// Bakes a colour ramp into a 1D texture.
        ///
        /// This is the trick the whole landscape leans on. URP's Lit and Unlit shaders do not
        /// read vertex colours, so a gradient sky or a terrain that shades from grass to rock
        /// to snow would otherwise need either a custom shader - another thing that can be
        /// stripped out of a build - or one material per band, which multiplies the draw
        /// calls. A ramp texture sampled by a UV the mesh builder writes from height gives
        /// both, with one material and no shader of our own.
        /// </summary>
        public static Texture2D MakeRamp(params Color[] stops)
        {
            const int width = 128;
            var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
            {
                name = "RR_Ramp",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            if (stops == null || stops.Length == 0) stops = new[] { Color.magenta };

            var pixels = new Color[width];
            for (int x = 0; x < width; x++)
            {
                float t = stops.Length == 1 ? 0f : x / (float)(width - 1) * (stops.Length - 1);
                int i = Mathf.Clamp((int)t, 0, stops.Length - 2 < 0 ? 0 : stops.Length - 2);
                float f = Mathf.Clamp01(t - i);
                pixels[x] = stops.Length == 1 ? stops[0] : Color.Lerp(stops[i], stops[i + 1], f);
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>
        /// Fades an additively blended material.
        ///
        /// Alpha does nothing on an additive surface: the blend is <c>src + dst</c>, so the
        /// shaded colour is added to the frame whatever its alpha says. Fading therefore has
        /// to scale the colour itself towards black. Getting this wrong is not subtle - every
        /// halo, trail and highlight in the game sat permanently at full brightness, so the
        /// hand reach-halos glowed at the player even with nothing to grab.
        /// </summary>
        public static void SetGlowFade(Material material, Color rgb, float visibility,
                                       float emission = 2f)
        {
            if (material == null) return;

            float v = Mathf.Clamp01(visibility);
            Color scaled = new Color(rgb.r * v, rgb.g * v, rgb.b * v, v);

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", scaled);
            if (material.HasProperty("_Color")) material.SetColor("_Color", scaled);
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", rgb * (emission * v));
        }

        /// <summary>Logs which shader the art is actually being drawn with.</summary>
        public static string ShaderReport =>
            LitTemplate != null && LitTemplate.shader != null
                ? LitTemplate.shader.name
                : "<none>";

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
        /// A lit material wearing a texture, optionally with a normal map.
        ///
        /// The tint multiplies the texture rather than replacing it, which is what lets one
        /// baked greyscale surface - bark, leaf mass, rock - serve every biome in the
        /// catalogue at its own colour. Eight biomes sharing four textures is four bakes
        /// instead of thirty-two, and the tint is what makes spruce bark and palm bark
        /// different things.
        /// </summary>
        public static Material MakeTexturedMaterial(Texture albedo, Color tint,
                                                    Texture normal = null,
                                                    float normalScale = 1f,
                                                    float smoothness = 0.1f,
                                                    Vector2 tiling = default)
        {
            var mat = MakeMaterial(tint, 0f, smoothness);
            mat.name = $"RR_Tex_{tint}";

            if (albedo != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", albedo);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", albedo);
            }

            if (tiling != default)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTextureScale("_BaseMap", tiling);
                if (mat.HasProperty("_MainTex")) mat.SetTextureScale("_MainTex", tiling);
            }

            if (normal != null && mat.HasProperty("_BumpMap"))
            {
                // The keyword is not optional. URP compiles the normal-map sample out of the
                // shader unless _NORMALMAP is on, so a material with a bump map assigned and
                // no keyword renders exactly as if the map were not there - silently.
                mat.SetTexture("_BumpMap", normal);
                mat.SetFloat("_BumpScale", normalScale);
                mat.EnableKeyword("_NORMALMAP");
            }

            return mat;
        }

        /// <summary>
        /// A material that clips its own transparent texels away rather than blending them.
        ///
        /// This is how a flat quad becomes a clump of grass. Alpha clipping is the right
        /// choice over blending for foliage for two reasons that both matter in VR: a clipped
        /// surface still writes depth, so a thousand tufts cannot sort incorrectly against
        /// each other as the head moves, and it needs no back-to-front ordering, which at this
        /// object count would cost more CPU than the grass costs GPU.
        /// </summary>
        public static Material MakeCutoutMaterial(Texture albedo, Color tint,
                                                  float cutoff = 0.45f,
                                                  bool doubleSided = true)
        {
            var mat = MakeTexturedMaterial(albedo, tint);
            mat.name = $"RR_Cutout_{tint}";

            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);   // still opaque
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1f);
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", cutoff);
            if (doubleSided && mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);

            mat.EnableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");

            // AlphaTest, not Transparent: clipped geometry belongs in the opaque pass, after
            // the solid surfaces so it gets the depth rejection, before anything blended.
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
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
            // Always replace the primitive's material, never leave the built-in default:
            // it is not a URP shader and will not render in a player.
            if (renderer != null) renderer.sharedMaterial = material != null ? material : MakeMaterial(Color.gray);
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
            if (renderer != null) renderer.sharedMaterial = material != null ? material : MakeMaterial(Color.gray);
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
