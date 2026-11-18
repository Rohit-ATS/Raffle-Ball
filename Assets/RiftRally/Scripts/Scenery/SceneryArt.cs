using UnityEngine;
using RiftRally.Setup;

namespace RiftRally.Scenery
{
    /// <summary>
    /// Mesh builders for the world outside the table.
    ///
    /// The rink's own helpers in <see cref="ProceduralArt"/> deal in boxes, discs and rings
    /// at centimetre scale. The landscape needs a different vocabulary - a sky you are inside
    /// of, ground that rolls, cones for conifers - at tens of metres, and it has a constraint
    /// the rink does not: it is drawn every frame behind everything else, at a distance where
    /// nobody can see detail, so every mesh here is built to be as cheap as it can be while
    /// still reading correctly in silhouette.
    ///
    /// The one piece of shared machinery worth knowing about is the ramp texture. URP's
    /// stock shaders do not read vertex colours, so a gradient - sky, or ground shading from
    /// grass through rock to snow - is produced by writing a UV from height in the mesh
    /// builder and sampling a 1D ramp. That keeps the entire sky and the entire terrain to one
    /// material each.
    /// </summary>
    public static class SceneryArt
    {
        /// <summary>
        /// Fraction of the height range that sits below zero, giving the ground hollows for
        /// water to collect in. Shared by the mesh builder and by the shading coordinate, so
        /// the two cannot disagree about where the waterline is.
        /// </summary>
        public const float GroundBaseline = 0.25f;

        /// <summary>
        /// An inside-out dome. UV.x runs from the horizon (0) to the zenith (1), which is
        /// what a sky ramp is sampled by; UV.y is unused and parked at the texture centre.
        /// </summary>
        public static Mesh BuildSkyDome(float radius, int segments = 32, int rings = 12)
        {
            segments = Mathf.Max(8, segments);
            rings = Mathf.Max(3, rings);

            // One extra ring below the horizon, so the dome closes visually even when the
            // player's eye sits a little above the terrain and can see under its rim.
            int ringCount = rings + 1;
            var verts = new Vector3[(segments + 1) * (ringCount + 1)];
            var uvs = new Vector2[verts.Length];
            var tris = new int[segments * ringCount * 6];

            for (int r = 0; r <= ringCount; r++)
            {
                // Latitude from a little below the horizon up to the pole.
                float t = r / (float)ringCount;
                float latitude = Mathf.Lerp(-0.12f, 1f, t) * Mathf.PI * 0.5f;
                float y = Mathf.Sin(latitude);
                float ring = Mathf.Cos(latitude);

                // The ramp coordinate is clamped at the horizon so the skirt below it keeps
                // the horizon colour instead of sampling off the end of the ramp.
                float ramp = Mathf.Clamp01(y);

                for (int s = 0; s <= segments; s++)
                {
                    float angle = s / (float)segments * Mathf.PI * 2f;
                    int i = r * (segments + 1) + s;
                    verts[i] = new Vector3(Mathf.Cos(angle) * ring * radius,
                                           y * radius * 0.92f,
                                           Mathf.Sin(angle) * ring * radius);
                    // UV.x is the horizon-to-zenith ramp coordinate, unchanged. UV.y is the
                    // compass bearing, which a 1D ramp ignores - it is one pixel tall - but
                    // which a baked sky uses as its second axis. Writing it costs nothing,
                    // and it is what lets the sky differ depending which way you face.
                    uvs[i] = new Vector2(ramp, s / (float)segments);
                }
            }

            int tri = 0;
            for (int r = 0; r < ringCount; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = r * (segments + 1) + s;
                    int b = a + 1;
                    int c = a + segments + 1;
                    int d = c + 1;

                    tris[tri++] = a; tris[tri++] = c; tris[tri++] = b;
                    tris[tri++] = b; tris[tri++] = c; tris[tri++] = d;
                }
            }

            var mesh = new Mesh { name = "RR_SkyDome" };
            mesh.indexFormat = verts.Length > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            // A sky is never culled by its own bounds; keeping them generous stops the dome
            // popping out of view when the player leans.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * radius * 2.4f);
            return mesh;
        }

        /// <summary>
        /// Rolling ground.
        ///
        /// The grid is flat inside <paramref name="flatRadius"/> and rises outside it, so the
        /// table always stands on level ground no matter how craggy the biome is. The blend is
        /// smoothed rather than stepped, because a hard ring of hills around a flat disc looks
        /// exactly like what it is.
        ///
        /// UV0 is a planar world-space coordinate, so the baked ground texture from
        /// <see cref="NatureTextures"/> lands on the terrain exactly where it was painted -
        /// grass in the hollows, rock on the steep faces, the same places the height function
        /// put them. UV1 keeps the older height-and-slope shading coordinate, which is what a
        /// plain colour ramp is sampled by when no baked texture is available.
        /// </summary>
        public static Mesh BuildTerrain(Landscape landscape, int seed, float extent = 62f,
                                        int resolution = 144, float flatRadius = 3.6f,
                                        float flatFalloff = 5.5f)
        {
            // The ceiling used to be 160, which was a budget decision taken when the ground
            // was a flat colour: more vertices bought nothing because there was no detail for
            // them to carry. With a baked surface on it the terrain is the one mesh in the
            // world worth spending vertices on, so the ceiling is raised and the preset
            // decides where in the range to sit.
            resolution = Mathf.Clamp(resolution, 8, 384);

            int side = resolution + 1;
            var verts = new Vector3[side * side];
            var uvs = new Vector2[verts.Length];
            var shadeUvs = new Vector2[verts.Length];
            var tris = new int[resolution * resolution * 6];

            float step = extent * 2f / resolution;
            var noise = new TerrainNoise(seed, landscape.HillRoughness);

            for (int z = 0; z <= resolution; z++)
            {
                for (int x = 0; x <= resolution; x++)
                {
                    float wx = -extent + x * step;
                    float wz = -extent + z * step;
                    int i = z * side + x;

                    float height = HeightAt(noise, landscape, wx, wz, flatRadius, flatFalloff);
                    verts[i] = new Vector3(wx, height, wz);
                }
            }

            // Shading coordinate per vertex, from normalised height plus a slope term taken
            // from the neighbouring samples. Slope is what makes a cliff face read as rock
            // while the plateau above it stays green.
            for (int z = 0; z <= resolution; z++)
            {
                for (int x = 0; x <= resolution; x++)
                {
                    int i = z * side + x;
                    int left = z * side + Mathf.Max(0, x - 1);
                    int right = z * side + Mathf.Min(resolution, x + 1);
                    int down = Mathf.Max(0, z - 1) * side + x;
                    int up = Mathf.Min(resolution, z + 1) * side + x;

                    float slope = (Mathf.Abs(verts[right].y - verts[left].y)
                                   + Mathf.Abs(verts[up].y - verts[down].y)) / (step * 2f);

                    float range = Mathf.Max(0.01f, landscape.HillHeight);
                    float normalised = Mathf.Clamp01(
                        Mathf.InverseLerp(-GroundBaseline * range,
                                          (1f - GroundBaseline) * range, verts[i].y));
                    float shade = Mathf.Clamp01(normalised * 0.78f + slope * 0.55f);
                    shadeUvs[i] = new Vector2(shade, 0.5f);

                    // The planar coordinate the baked ground texture is addressed by. It maps
                    // the whole world square onto the whole image once, with no tiling: a
                    // tiled ground texture repeats visibly at this size, and the point of
                    // baking the biome's own terrain into it is that it is not a repeat.
                    uvs[i] = new Vector2((verts[i].x + extent) / (extent * 2f),
                                         (verts[i].z + extent) / (extent * 2f));
                }
            }

            int tri = 0;
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int a = z * side + x;
                    int b = a + 1;
                    int c = a + side;
                    int d = c + 1;

                    tris[tri++] = a; tris[tri++] = c; tris[tri++] = b;
                    tris[tri++] = b; tris[tri++] = c; tris[tri++] = d;
                }
            }

            var mesh = new Mesh { name = "RR_Terrain" };
            mesh.indexFormat = verts.Length > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, shadeUvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();

            // Tangents, which a mesh built by hand does not get for free. Without them the
            // ground's normal map is sampled against an undefined tangent frame, and the
            // relief lights from a direction that has nothing to do with the sun.
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The terrain height function, exposed so anything being planted on the ground can
        /// ask where the ground is rather than guessing and floating.
        /// </summary>
        public static float HeightAt(TerrainNoise noise, Landscape landscape, float x, float z,
                                     float flatRadius = 3.6f, float flatFalloff = 5.5f)
        {
            // The baseline is subtracted before scaling, so roughly the lowest quarter of the
            // range comes out below zero. Without it every sample is positive, the ground has
            // no hollows at all, and a water plane set below zero is buried everywhere - which
            // is exactly how the lagoon looked at first: a biome with no lagoon in it.
            float raw = (noise.Sample(x, z) - GroundBaseline) * landscape.HillHeight;

            // Smooth the hills out to nothing near the table. SmoothStep rather than a
            // linear ramp: a linear blend leaves a visible crease where the slope changes.
            float distance = Mathf.Sqrt(x * x + z * z);
            float blend = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((distance - flatRadius) / Mathf.Max(0.01f, flatFalloff)));

            return raw * blend;
        }

        /// <summary>
        /// Layered value noise, in the shape the terrain wants.
        ///
        /// <see cref="Mathf.PerlinNoise"/> does the sampling - no reason to hand-roll a
        /// gradient noise when the engine ships one - but it is deterministic and unseeded, so
        /// the seed is folded in as an offset in the sample plane. Two octaves and a ridged
        /// third give rolling ground with the occasional sharper crest, which is what reads as
        /// natural at this distance without a third of a million triangles.
        /// </summary>
        public struct TerrainNoise
        {
            private readonly float _offsetX;
            private readonly float _offsetZ;
            private readonly float _roughness;

            public TerrainNoise(int seed, float roughness)
            {
                var random = new System.Random(seed);
                _offsetX = (float)random.NextDouble() * 1000f;
                _offsetZ = (float)random.NextDouble() * 1000f;
                _roughness = Mathf.Max(0.05f, roughness);
            }

            public float Sample(float x, float z)
            {
                float sx = x * 0.016f + _offsetX;
                float sz = z * 0.016f + _offsetZ;

                float broad = Mathf.PerlinNoise(sx, sz);
                float medium = Mathf.PerlinNoise(sx * 2.7f, sz * 2.7f) * 0.45f;
                float fine = Mathf.PerlinNoise(sx * 6.1f, sz * 6.1f) * 0.16f;

                // The ridged octave is 1 - |2n-1|, which turns smooth humps into crests.
                float ridgeNoise = Mathf.PerlinNoise(sx * 1.6f + 31.7f, sz * 1.6f + 11.3f);
                float ridge = (1f - Mathf.Abs(ridgeNoise * 2f - 1f)) * 0.35f * _roughness;

                float sum = (broad + medium + fine) / 1.61f;
                return Mathf.Clamp01(sum * 0.82f + ridge);
            }
        }

        /// <summary>A cone standing on its base, for conifers and rock spires.</summary>
        public static Mesh BuildCone(float radius, float height, int segments = 10)
        {
            segments = Mathf.Max(3, segments);

            // Flat shaded: each side is its own triangle with its own vertices, so the facets
            // stay crisp instead of being smoothed into a lumpy sphere by shared normals.
            var verts = new Vector3[segments * 3 + segments * 3];
            var tris = new int[verts.Length];
            var uvs = new Vector2[verts.Length];

            int v = 0;
            for (int s = 0; s < segments; s++)
            {
                float a0 = s / (float)segments * Mathf.PI * 2f;
                float a1 = (s + 1) / (float)segments * Mathf.PI * 2f;

                Vector3 p0 = new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                Vector3 tip = new Vector3(0f, height, 0f);

                verts[v + 0] = p0; verts[v + 1] = tip; verts[v + 2] = p1;

                // Wrapped round the cone and up to the tip, so a needle or leaf texture runs
                // the way the foliage does rather than being a flat tint per facet.
                uvs[v + 0] = new Vector2(s / (float)segments, 0f);
                uvs[v + 1] = new Vector2((s + 0.5f) / segments, 1f);
                uvs[v + 2] = new Vector2((s + 1) / (float)segments, 0f);
                v += 3;

                // Base cap, wound the other way so it is visible from underneath.
                verts[v + 0] = p1; verts[v + 1] = Vector3.zero; verts[v + 2] = p0;
                uvs[v + 0] = new Vector2((s + 1) / (float)segments, 0f);
                uvs[v + 1] = new Vector2((s + 0.5f) / segments, 0.1f);
                uvs[v + 2] = new Vector2(s / (float)segments, 0f);
                v += 3;
            }

            for (int i = 0; i < tris.Length; i++) tris[i] = i;

            var mesh = new Mesh { name = "RR_Cone" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A faceted blob: an octahedron pushed out onto a sphere and subdivided once.
        ///
        /// This stands in for every round thing in the landscape - tree crowns, boulders,
        /// clouds - and it exists because Unity ships a sphere primitive of around seven
        /// hundred and sixty triangles. At a hundred and fifty trees that is a hundred
        /// thousand triangles of foliage nobody can resolve at thirty metres. This is
        /// thirty-two, it reads as a low-poly crown on purpose rather than as a sphere that
        /// failed, and the flat facets catch the directional light in a way that genuinely
        /// helps the silhouette.
        /// </summary>
        public static Mesh BuildBlob()
        {
            // An octahedron: six points, eight faces, each then split into four.
            Vector3[] points =
            {
                Vector3.up, Vector3.down,
                Vector3.left, Vector3.right,
                Vector3.forward, Vector3.back
            };

            int[][] faces =
            {
                new[] { 0, 4, 3 }, new[] { 0, 3, 5 }, new[] { 0, 5, 2 }, new[] { 0, 2, 4 },
                new[] { 1, 3, 4 }, new[] { 1, 5, 3 }, new[] { 1, 2, 5 }, new[] { 1, 4, 2 }
            };

            var verts = new System.Collections.Generic.List<Vector3>(96);
            var tris = new System.Collections.Generic.List<int>(96);
            var uvs = new System.Collections.Generic.List<Vector2>(96);

            foreach (int[] face in faces)
            {
                Vector3 a = points[face[0]], b = points[face[1]], c = points[face[2]];

                // Edge midpoints, renormalised: that is what pushes the flat face out onto
                // the sphere instead of just cutting it into four coplanar pieces.
                Vector3 ab = (a + b).normalized;
                Vector3 bc = (b + c).normalized;
                Vector3 ca = (c + a).normalized;

                AddFlatTriangle(verts, tris, uvs, a, ab, ca);
                AddFlatTriangle(verts, tris, uvs, ab, b, bc);
                AddFlatTriangle(verts, tris, uvs, ca, bc, c);
                AddFlatTriangle(verts, tris, uvs, ab, bc, ca);
            }

            var mesh = new Mesh { name = "RR_Blob" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Adds one triangle with its own three vertices, so neighbouring faces do not share
        /// normals and the facets stay crisp.
        /// </summary>
        private static void AddFlatTriangle(System.Collections.Generic.List<Vector3> verts,
                                            System.Collections.Generic.List<int> tris,
                                            System.Collections.Generic.List<Vector2> uvs,
                                            Vector3 a, Vector3 b, Vector3 c)
        {
            int start = verts.Count;

            // Halved, so the blob is a unit-diameter shape and localScale reads as a size.
            verts.Add(a * 0.5f);
            verts.Add(b * 0.5f);
            verts.Add(c * 0.5f);

            // A spherical coordinate per corner, so a leaf-mass or rock texture varies across
            // the blob instead of painting every facet the same colour. These used to all be
            // the texture centre, which was fine when nothing here was textured.
            uvs.Add(SphericalUv(a));
            uvs.Add(SphericalUv(b));
            uvs.Add(SphericalUv(c));

            tris.Add(start);
            tris.Add(start + 1);
            tris.Add(start + 2);
        }

        /// <summary>
        /// A latitude-longitude UV for a point on a unit sphere. The seam where longitude
        /// wraps is left as it is: everything mapped this way wears a noise texture with no
        /// features to misalign, and hiding the seam properly would mean duplicating a column
        /// of vertices on a thirty-two triangle mesh.
        /// </summary>
        private static Vector2 SphericalUv(Vector3 direction)
        {
            Vector3 n = direction.normalized;
            float u = Mathf.Atan2(n.z, n.x) / (Mathf.PI * 2f) + 0.5f;
            float v = n.y * 0.5f + 0.5f;
            return new Vector2(u * 2f, v * 2f);
        }

        /// <summary>
        /// A prism standing on the XZ plane with its base at the origin and its top closed.
        /// Trunks, cactus limbs and standing stones are all prisms: six sides is plenty at
        /// this distance, against the two hundred and forty triangles of a stock cylinder.
        /// </summary>
        public static Mesh BuildPrism(int sides, float radius, float height,
                                      float topScale = 0.72f)
        {
            sides = Mathf.Max(3, sides);

            var verts = new Vector3[sides * 2 + 1];
            var uvs = new Vector2[verts.Length];
            var tris = new int[sides * 6 + sides * 3];

            for (int i = 0; i < sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                verts[i] = new Vector3(cos * radius, 0f, sin * radius);
                verts[i + sides] = new Vector3(cos * radius * topScale, height,
                                               sin * radius * topScale);

                // Wrapped round the trunk and running up it, so bark reads as bark. The
                // vertical coordinate is the height in mesh units rather than 0..1, which
                // keeps the grain the same size whatever length of trunk this becomes.
                uvs[i] = new Vector2(i / (float)sides, 0f);
                uvs[i + sides] = new Vector2(i / (float)sides, height);
            }

            // A top cap, so a trunk is not hollow when a player leans over the table and
            // looks down the length of it. The base sits in the ground and needs none.
            int topCentre = sides * 2;
            verts[topCentre] = new Vector3(0f, height, 0f);
            uvs[topCentre] = new Vector2(0.5f, height);

            int t = 0;
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;

                tris[t++] = i; tris[t++] = i + sides; tris[t++] = next;
                tris[t++] = next; tris[t++] = i + sides; tris[t++] = next + sides;

                tris[t++] = topCentre; tris[t++] = i + sides; tris[t++] = next + sides;
            }

            var mesh = new Mesh { name = "RR_Prism" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A soft tiling ripple pattern for water, now baked by
        /// <see cref="NatureTextures.Ripples"/> at the current texture budget instead of at a
        /// fixed sixty-four pixels, and cached rather than regenerated per build.
        ///
        /// The pattern itself is unchanged - low contrast sine terms at whole numbers of
        /// cycles, so it tiles seamlessly, with the job of breaking up a flat plane just
        /// enough that scrolling it reads as movement.
        /// </summary>
        public static Texture2D MakeRippleTexture() => NatureTextures.Ripples();

        /// <summary>
        /// A quad in the local XY plane, centred on its own origin. Clouds, wings and petals
        /// are all quads; it is the cheapest thing that can be drawn.
        /// </summary>
        /// <param name="doubleSided">
        /// Winds the quad both ways. Grass and palm fronds need it: a single-sided quad is
        /// invisible from behind, and two crossed blades still leave two directions you can
        /// stand in and see nothing at all. Six extra indices is a far cheaper fix than
        /// turning culling off on the shared foliage material, which would double the fill
        /// cost of every leaf in the biome.
        /// </param>
        public static Mesh BuildQuad(float width, float height, bool doubleSided = false)
        {
            float hw = width * 0.5f, hh = height * 0.5f;

            var corners = new[]
            {
                new Vector3(-hw, -hh, 0f), new Vector3(hw, -hh, 0f),
                new Vector3(-hw,  hh, 0f), new Vector3(hw,  hh, 0f)
            };
            var coords = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f)
            };

            var mesh = new Mesh { name = "RR_Quad" };

            if (!doubleSided)
            {
                mesh.SetVertices(corners);
                mesh.SetUVs(0, coords);
                mesh.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
                mesh.SetNormals(new[] { -Vector3.forward, -Vector3.forward,
                                        -Vector3.forward, -Vector3.forward });
                mesh.RecalculateBounds();
                return mesh;
            }

            // A two-sided quad gets two sets of vertices rather than one set wound twice,
            // and its normals are written rather than calculated.
            //
            // This is not a refinement. RecalculateNormals averages the face normals meeting
            // at each vertex, and the two faces of a quad wound both ways point exactly
            // opposite each other - so every normal averages to zero, and a lit surface with
            // a zero normal takes no light at all. Every tuft of grass, every flower, every
            // reed and both wings of every bird in the game rendered as a solid black card,
            // in all eight biomes, and the black cards read as holes punched in the
            // landscape. Eight vertices is what fixes it.
            mesh.SetVertices(new[]
            {
                corners[0], corners[1], corners[2], corners[3],
                corners[0], corners[1], corners[2], corners[3]
            });
            mesh.SetUVs(0, new[]
            {
                coords[0], coords[1], coords[2], coords[3],
                coords[0], coords[1], coords[2], coords[3]
            });
            mesh.SetNormals(new[]
            {
                -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward,
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward
            });
            mesh.SetTriangles(new[]
            {
                0, 2, 1, 2, 3, 1,          // front
                5, 6, 4, 5, 7, 6           // back, wound the other way
            }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A clump of grass: several tapered blades standing from a common root, each one
        /// leaning a different way, as real geometry rather than as a texture on a card.
        ///
        /// Ground cover is the one thing in the landscape where the shape has to come from
        /// the mesh. The obvious alternative - a couple of crossed quads wearing a blade
        /// texture with an alpha cutout - is cheaper, and it is what this used to be, and it
        /// failed in the one place that matters: the player. URP strips shader variants it
        /// believes nothing uses, every material in this game is created at run time so
        /// nothing in the project declares that it needs alpha clipping, and the variant went
        /// with the strip. In the Editor the grass was grass; in the build it was several
        /// hundred solid rectangles standing in a field. Geometry cannot be stripped.
        ///
        /// A tuft is a unit shape: one metre tall, standing on its own origin, so localScale
        /// reads as a size the same way the rest of the scenery does.
        /// </summary>
        /// <param name="blades">How many blades. Five reads as a clump; three as weeds.</param>
        /// <param name="spread">How far the tips lean out, as a fraction of the height.</param>
        public static Mesh BuildGrassTuft(int seed, int blades = 5, float spread = 0.36f,
                                          float width = 0.13f)
        {
            blades = Mathf.Clamp(blades, 1, 12);
            var random = new System.Random(seed);

            var verts = new System.Collections.Generic.List<Vector3>(blades * 6);
            var normals = new System.Collections.Generic.List<Vector3>(blades * 6);
            var uvs = new System.Collections.Generic.List<Vector2>(blades * 6);
            var tris = new System.Collections.Generic.List<int>(blades * 6);

            for (int b = 0; b < blades; b++)
            {
                // Blades fan out around the root rather than all leaning the same way, which
                // is the difference between a clump of grass and a handful of flat cards.
                float yaw = (b / (float)blades + (float)random.NextDouble() * 0.12f)
                            * Mathf.PI * 2f;
                Vector3 out3 = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                Vector3 side = new Vector3(-out3.z, 0f, out3.x);

                float height = 0.62f + (float)random.NextDouble() * 0.48f;
                float lean = spread * (0.4f + (float)random.NextDouble() * 1.1f);
                float halfWidth = width * (0.7f + (float)random.NextDouble() * 0.6f) * 0.5f;

                Vector3 root = out3 * (halfWidth * 0.7f);
                Vector3 left = root - side * halfWidth;
                Vector3 right = root + side * halfWidth;
                Vector3 tip = root + out3 * lean + Vector3.up * height;

                // The blade's own normal: across the blade, up its length.
                Vector3 normal = Vector3.Cross(tip - left, right - left).normalized;
                if (normal.sqrMagnitude < 0.5f) normal = Vector3.up;

                int start = verts.Count;

                // Front face and back face get their own vertices and opposite normals, so a
                // blade is lit from whichever side it is seen - and never averages to zero.
                verts.Add(left); verts.Add(right); verts.Add(tip);
                verts.Add(left); verts.Add(right); verts.Add(tip);

                normals.Add(normal); normals.Add(normal); normals.Add(normal);
                normals.Add(-normal); normals.Add(-normal); normals.Add(-normal);

                // V runs up the blade, so a material can darken the root and catch the tip.
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(0.5f, 1f));
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(0.5f, 1f));

                tris.Add(start); tris.Add(start + 2); tris.Add(start + 1);
                tris.Add(start + 4); tris.Add(start + 5); tris.Add(start + 3);
            }

            var mesh = new Mesh { name = "RR_GrassTuft" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A triangle in the local XY plane with its root at the origin, pointing along +X.
        /// This is a bird's wing: one triangle, rotated about the body to flap.
        /// </summary>
        public static Mesh BuildWing(float span, float chord)
        {
            var points = new[]
            {
                new Vector3(0f, 0f, chord * 0.5f),
                new Vector3(span, 0f, chord * 0.18f),
                new Vector3(0f, 0f, -chord * 0.5f)
            };

            var mesh = new Mesh { name = "RR_Wing" };

            // Visible from above and below, which a bird has to be - and, like the two-sided
            // quad, with its own vertices per side. Winding one set of three vertices both
            // ways is six indices instead of nine, and it also averages both face normals to
            // zero, which is why the flocks used to fly as black paper cut-outs.
            mesh.SetVertices(new[]
            {
                points[0], points[1], points[2],
                points[0], points[1], points[2]
            });
            mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f)
            });
            mesh.SetNormals(new[]
            {
                Vector3.up, Vector3.up, Vector3.up,
                Vector3.down, Vector3.down, Vector3.down
            });
            mesh.SetTriangles(new[] { 0, 1, 2, 5, 4, 3 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Attaches a mesh to a new child object with no collider. Everything in the scenery
        /// is decoration: a stone must never be able to hit a tree sixty metres away, and
        /// keeping the physics scene to the rink alone is most of why this game holds its
        /// frame budget.
        /// </summary>
        public static GameObject Attach(Transform parent, string name, Mesh mesh,
                                        Material material, Vector3 localPosition,
                                        Quaternion localRotation, Vector3 localScale,
                                        bool castShadows = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return go;
        }

        /// <summary>
        /// Scatters points on a ring around the table, biased outwards.
        ///
        /// The square root on the radius is what makes the scatter even. Interpolating the
        /// radius linearly puts the same number of trees in the small inner ring as in the
        /// huge outer one, which plants a visible thicket right at the table edge and leaves
        /// the distance bare. Taking the root distributes by area instead.
        /// </summary>
        public static Vector2 ScatterPoint(System.Random random, float innerRadius,
                                           float outerRadius)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float t = (float)random.NextDouble();
            float radius = Mathf.Sqrt(Mathf.Lerp(innerRadius * innerRadius,
                                                 outerRadius * outerRadius, t));
            return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }
    }
}
