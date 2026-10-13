using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.Procedural
{
    /// <summary>
    /// Accumulates simple shapes (ellipsoids, lathes, boxes, tubes) into one mesh.
    /// Each shape gets UVs from a <see cref="UvMapper"/> and an optional bone index, so one builder
    /// can make a rigid-skinned character (one draw call) or a combined static prop mesh.
    /// All positions are in mesh space. Winding is clockwise = front face (Unity convention).
    /// </summary>
    public sealed class MeshBuilder
    {
        /// <summary>Maps a vertex (mesh-space position, normal) to a UV.</summary>
        public delegate Vector2 UvMapper(Vector3 position, Vector3 normal);

        private readonly List<Vector3> _positions = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int> _bones = new List<int>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _triangles = new List<int>();

        /// <summary>
        /// Vertex colour for shapes added from now on. Shaders that read it: RGB = albedo multiplier,
        /// A = snow-cap mask (SnowCapLit: 1 = may hold snow, 0 = never). Default white.
        /// </summary>
        public Color Tint = Color.white;

        public int VertexCount => _positions.Count;

        // ------------------------------------------------------------ Shapes

        /// <summary>Smooth ellipsoid. <paramref name="keep"/> can drop faces (by unit-sphere normal) to cut openings.</summary>
        public void Ellipsoid(Vector3 center, Vector3 radii, Quaternion rotation, UvMapper uv, int bone,
            int segments = 12, int rings = 8, Func<Vector3, bool> keep = null, bool inward = false)
        {
            int start = _positions.Count;
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * r / rings; // 0 = top
                for (int s = 0; s <= segments; s++)
                {
                    float theta = 2f * Mathf.PI * s / segments;
                    var unit = new Vector3(Mathf.Sin(phi) * Mathf.Sin(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Cos(theta));
                    var local = Vector3.Scale(unit, radii);
                    // Ellipsoid normal: gradient of (x/a)^2 + ... -> unit / radii.
                    var n = new Vector3(unit.x / radii.x, unit.y / radii.y, unit.z / radii.z).normalized;
                    if (inward) n = -n;
                    AddVertex(center + rotation * local, rotation * n, uv, bone);
                }
            }

            int stride = segments + 1;
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                if (keep != null)
                {
                    float phi = Mathf.PI * (r + 0.5f) / rings;
                    float theta = 2f * Mathf.PI * (s + 0.5f) / segments;
                    var mid = new Vector3(Mathf.Sin(phi) * Mathf.Sin(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Cos(theta));
                    if (!keep(mid)) continue;
                }
                int a = start + r * stride + s;
                int b = a + stride;
                Quad(a, a + 1, b + 1, b, !inward);
            }
        }

        /// <summary>
        /// Surface of revolution around local +Y. <paramref name="profile"/> is (height, radius) from bottom to top.
        /// <paramref name="rowShape"/>(row, angle) returns (radius multiplier, height offset) per vertex, for ragged hems
        /// and folds. Normals ignore it (fine for small offsets).
        /// </summary>
        public void Lathe(Vector3 origin, Quaternion rotation, IReadOnlyList<Vector2> profile, UvMapper uv, int bone,
            int segments = 12, Vector2? scaleXZ = null, bool capBottom = false, bool capTop = false,
            Func<int, float, Vector2> rowShape = null)
        {
            var scale = scaleXZ ?? Vector2.one;
            int rows = profile.Count;
            int start = _positions.Count;

            for (int i = 0; i < rows; i++)
            {
                // Profile slope for normals.
                Vector2 prev = profile[Mathf.Max(i - 1, 0)];
                Vector2 next = profile[Mathf.Min(i + 1, rows - 1)];
                float dy = next.x - prev.x;
                float dr = next.y - prev.y;

                for (int s = 0; s <= segments; s++)
                {
                    float theta = 2f * Mathf.PI * s / segments;
                    float sin = Mathf.Sin(theta), cos = Mathf.Cos(theta);
                    var shape = rowShape != null ? rowShape(i, theta) : new Vector2(1f, 0f);
                    float radius = profile[i].y * shape.x;
                    var local = new Vector3(sin * radius * scale.x, profile[i].x + shape.y, cos * radius * scale.y);
                    // Normal: outward in XZ, tilted by the profile slope.
                    var n = new Vector3(sin / scale.x * dy, -dr, cos / scale.y * dy).normalized;
                    if (n.sqrMagnitude < 1e-6f) n = new Vector3(sin, 0f, cos);
                    AddVertex(origin + rotation * local, rotation * n, uv, bone);
                }
            }

            int stride = segments + 1;
            for (int i = 0; i < rows - 1; i++)
            for (int s = 0; s < segments; s++)
            {
                int a = start + i * stride + s;
                int b = a + stride;
                Quad(a, a + 1, b + 1, b, false);
            }

            if (capBottom) Cap(origin, rotation, profile[0], scale, segments, uv, bone, false);
            if (capTop) Cap(origin, rotation, profile[rows - 1], scale, segments, uv, bone, true);
        }

        /// <summary>Flat-shaded box (hard edges).</summary>
        public void Box(Vector3 center, Vector3 size, Quaternion rotation, UvMapper uv, int bone)
        {
            var h = size * 0.5f;
            Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var n in normals)
            {
                // Two axes spanning the face.
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(n, u);
                var faceCenter = Vector3.Scale(n, h);
                var du = Vector3.Scale(u, h);
                var dv = Vector3.Scale(v, h);
                int start = _positions.Count;
                var worldN = rotation * n;
                AddVertex(center + rotation * (faceCenter - du - dv), worldN, uv, bone);
                AddVertex(center + rotation * (faceCenter + du - dv), worldN, uv, bone);
                AddVertex(center + rotation * (faceCenter + du + dv), worldN, uv, bone);
                AddVertex(center + rotation * (faceCenter - du + dv), worldN, uv, bone);
                _triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
        }

        /// <summary>Flat quad a-b-c-d (clockwise seen from the front), one shared normal.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, UvMapper uv, int bone)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            int start = _positions.Count;
            AddVertex(a, n, uv, bone);
            AddVertex(b, n, uv, bone);
            AddVertex(c, n, uv, bone);
            AddVertex(d, n, uv, bone);
            _triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        /// <summary>Round tube along a polyline. <paramref name="radius"/> is sampled per point (t in 0..1).</summary>
        public void Tube(IReadOnlyList<Vector3> points, Func<float, float> radius, UvMapper uv, int bone, int sides = 6, bool caps = true)
        {
            int count = points.Count;
            int start = _positions.Count;
            Vector3 refUp = Vector3.up;
            for (int i = 0; i < count; i++)
            {
                Vector3 tangent = (points[Mathf.Min(i + 1, count - 1)] - points[Mathf.Max(i - 1, 0)]).normalized;
                if (Mathf.Abs(Vector3.Dot(tangent, refUp)) > 0.95f) refUp = Vector3.right;
                Vector3 side = Vector3.Cross(tangent, refUp).normalized;
                Vector3 up = Vector3.Cross(side, tangent).normalized;
                float r = radius(i / (float)(count - 1));
                for (int s = 0; s <= sides; s++)
                {
                    float a = 2f * Mathf.PI * s / sides;
                    var n = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                    AddVertex(points[i] + n * r, n, uv, bone);
                }
            }

            int stride = sides + 1;
            for (int i = 0; i < count - 1; i++)
            for (int s = 0; s < sides; s++)
            {
                int a = start + i * stride + s;
                int b = a + stride;
                Quad(a, a + 1, b + 1, b, true);
            }

            if (!caps) return;
            TubeCap(points[0], (points[0] - points[1]).normalized, start, sides, uv, bone, false);
            TubeCap(points[count - 1], (points[count - 1] - points[count - 2]).normalized, start + (count - 1) * stride, sides, uv, bone, true);
        }

        /// <summary>
        /// Smooth height-field style surface: (nx + 1) x (nz + 1) vertices from <paramref name="position"/>(i, j).
        /// Normals come from neighbour differences, so dunes and beach slopes shade softly. Front face points +Y
        /// when i runs along +X and j along +Z.
        /// </summary>
        public void Surface(int nx, int nz, Func<int, int, Vector3> position, UvMapper uv, int bone = 0)
        {
            int start = _positions.Count;
            var p = new Vector3[nx + 1, nz + 1];
            for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
                p[i, j] = position(i, j);
            for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                var du = p[Mathf.Min(i + 1, nx), j] - p[Mathf.Max(i - 1, 0), j];
                var dv = p[i, Mathf.Min(j + 1, nz)] - p[i, Mathf.Max(j - 1, 0)];
                AddVertex(p[i, j], Vector3.Cross(dv, du).normalized, uv, bone);
            }
            int stride = nx + 1;
            for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = start + j * stride + i;
                _triangles.AddRange(new[] { a, a + stride, a + stride + 1, a, a + stride + 1, a + 1 });
            }
        }

        /// <summary>Copies another builder's geometry (keeps its UVs and bones).</summary>
        public void Append(MeshBuilder other, Matrix4x4 transform)
        {
            int offset = _positions.Count;
            for (int i = 0; i < other._positions.Count; i++)
            {
                _positions.Add(transform.MultiplyPoint3x4(other._positions[i]));
                _normals.Add(transform.MultiplyVector(other._normals[i]).normalized);
                _uvs.Add(other._uvs[i]);
                _bones.Add(other._bones[i]);
                _colors.Add(other._colors[i]);
            }
            foreach (int t in other._triangles) _triangles.Add(t + offset);
        }

        /// <summary>Every triangle as three positions (for placement tests, e.g. keep grass off the floor).</summary>
        public IEnumerable<(Vector3 a, Vector3 b, Vector3 c)> Triangles()
        {
            for (int i = 0; i + 2 < _triangles.Count; i += 3)
                yield return (_positions[_triangles[i]], _positions[_triangles[i + 1]], _positions[_triangles[i + 2]]);
        }

        // ------------------------------------------------------------ Output

        /// <summary>Writes into <paramref name="mesh"/>. With bones, adds 1-bone weights + bindposes (rigid skinning).</summary>
        public void WriteTo(Mesh mesh, Matrix4x4[] bindposes = null)
        {
            mesh.Clear();
            mesh.indexFormat = _positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_positions);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_triangles, 0);
            if (bindposes != null)
            {
                var weights = new BoneWeight[_bones.Count];
                for (int i = 0; i < weights.Length; i++)
                    weights[i] = new BoneWeight { boneIndex0 = _bones[i], weight0 = 1f };
                mesh.boneWeights = weights;
                mesh.bindposes = bindposes;
            }
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            mesh.UploadMeshData(false);
        }

        // ------------------------------------------------------------ Internals

        private void AddVertex(Vector3 position, Vector3 normal, UvMapper uv, int bone)
        {
            _positions.Add(position);
            _normals.Add(normal);
            _uvs.Add(uv(position, normal));
            _bones.Add(bone);
            _colors.Add(Tint);
        }

        private void Quad(int a, int b, int c, int d, bool flip)
        {
            if (flip) _triangles.AddRange(new[] { a, c, b, a, d, c });
            else _triangles.AddRange(new[] { a, b, c, a, c, d });
        }

        private void Cap(Vector3 origin, Quaternion rotation, Vector2 ring, Vector2 scale, int segments, UvMapper uv, int bone, bool top)
        {
            var n = rotation * (top ? Vector3.up : Vector3.down);
            int center = _positions.Count;
            AddVertex(origin + rotation * new Vector3(0f, ring.x, 0f), n, uv, bone);
            for (int s = 0; s <= segments; s++)
            {
                float theta = 2f * Mathf.PI * s / segments;
                var p = new Vector3(Mathf.Sin(theta) * ring.y * scale.x, ring.x, Mathf.Cos(theta) * ring.y * scale.y);
                AddVertex(origin + rotation * p, n, uv, bone);
            }
            for (int s = 0; s < segments; s++)
            {
                if (top) _triangles.AddRange(new[] { center, center + 1 + s, center + 2 + s });
                else _triangles.AddRange(new[] { center, center + 2 + s, center + 1 + s });
            }
        }

        private void TubeCap(Vector3 point, Vector3 normal, int ringStart, int sides, UvMapper uv, int bone, bool end)
        {
            int center = _positions.Count;
            AddVertex(point, normal, uv, bone);
            int first = _positions.Count;
            for (int s = 0; s <= sides; s++) AddVertex(_positions[ringStart + s], normal, uv, bone);
            for (int s = 0; s < sides; s++)
            {
                if (end) _triangles.AddRange(new[] { center, first + s + 1, first + s });
                else _triangles.AddRange(new[] { center, first + s, first + s + 1 });
            }
        }
    }
}
