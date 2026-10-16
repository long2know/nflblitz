using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.AssetImport
{
    public static class PlayerImportLimits
    {
        public const int SourceBytes = 8 * 1024 * 1024;
        public const int Declarations = 4096;
        public const int ArrayElements = 262144;
        public const int Tokens = 2000000;
        public const int Nodes = 256;
        public const int Materials = 128;
        public const int TotalTriangles = 262144;
        public const int TextureBytes = 64 * 1024 * 1024;
        public const long TotalInputBytes = 128L * 1024 * 1024;
        public const float CoordinateMagnitude = 1000000f;
    }

    public struct SourceLocation
    {
        public readonly string File;
        public readonly int Line;
        public readonly int Column;

        public SourceLocation(string file, int line, int column)
        {
            File = file;
            Line = line;
            Column = column;
        }

        public override string ToString() { return File + ":" + Line + ":" + Column; }
    }

    public sealed class PlayerAssetException : Exception
    {
        public SourceLocation Location { get; private set; }

        public PlayerAssetException(SourceLocation location, string message)
            : base(location + ": " + message) { Location = location; }

        public PlayerAssetException(string message) : base(message) { }
    }

    public struct AssetVector2
    {
        public readonly float X;
        public readonly float Y;
        public AssetVector2(float x, float y) { X = x; Y = y; }
    }

    public struct AssetVector3
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;
        public AssetVector3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static AssetVector3 operator -(AssetVector3 a, AssetVector3 b)
        {
            return new AssetVector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }
    }

    public sealed class PlayerSymbolSelection
    {
        public readonly string ModelLimbs;
        public readonly string Skeleton;
        public readonly string SkeletonLimbs;
        public readonly string Materials;

        public PlayerSymbolSelection(string modelLimbs, string skeleton, string skeletonLimbs, string materials)
        {
            ModelLimbs = RequiredSymbol(modelLimbs);
            Skeleton = RequiredSymbol(skeleton);
            SkeletonLimbs = RequiredSymbol(skeletonLimbs);
            Materials = RequiredSymbol(materials);
        }

        private static string RequiredSymbol(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 128)
                throw new PlayerAssetException("A selected C symbol must be 1..128 characters.");
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (!(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z') && c != '_' &&
                    !(i > 0 && c >= '0' && c <= '9'))
                    throw new PlayerAssetException("Invalid selected C symbol '" + name + "'.");
            }
            return name;
        }
    }

    public sealed class SourceFace
    {
        public readonly ReadOnlyCollection<int> VertexIndices;
        public readonly ReadOnlyCollection<int> TextureCoordinateIndices;
        public readonly int MaterialIndex;
        public readonly SourceLocation Location;

        internal SourceFace(int[] vertices, int[] textureCoordinates, int material, SourceLocation location)
        {
            VertexIndices = Array.AsReadOnly(vertices);
            TextureCoordinateIndices = Array.AsReadOnly(textureCoordinates);
            MaterialIndex = material;
            Location = location;
        }
    }

    public sealed class SourceLimb
    {
        public readonly string Symbol;
        public readonly ReadOnlyCollection<AssetVector3> Vertices;
        public readonly ReadOnlyCollection<AssetVector3> Normals;
        public readonly ReadOnlyCollection<AssetVector2> TextureCoordinates;
        public readonly ReadOnlyCollection<SourceFace> Faces;

        internal SourceLimb(string symbol, AssetVector3[] vertices, AssetVector3[] normals,
            AssetVector2[] textureCoordinates, SourceFace[] faces)
        {
            Symbol = symbol;
            Vertices = Array.AsReadOnly(vertices);
            Normals = normals == null ? null : Array.AsReadOnly(normals);
            TextureCoordinates = Array.AsReadOnly(textureCoordinates);
            Faces = Array.AsReadOnly(faces);
        }
    }

    public sealed class SourceRigNode
    {
        public readonly int Index;
        public readonly int ParentIndex;
        public readonly AssetVector3 BindPivot;
        public readonly int Flags;
        public readonly ReadOnlyCollection<int> Children;
        public bool DrawEnabled { get { return (Flags & 2) == 0; } }

        internal SourceRigNode(int index, int parent, AssetVector3 pivot, int flags, int[] children)
        {
            Index = index;
            ParentIndex = parent;
            BindPivot = pivot;
            Flags = flags;
            Children = Array.AsReadOnly(children);
        }
    }

    public sealed class SourcePlayer
    {
        public readonly PlayerSymbolSelection Symbols;
        public readonly ReadOnlyCollection<SourceLimb> Limbs;
        public readonly ReadOnlyCollection<SourceRigNode> Nodes;
        public readonly ReadOnlyCollection<string> MaterialSlots;

        internal SourcePlayer(PlayerSymbolSelection symbols, SourceLimb[] limbs,
            SourceRigNode[] nodes, string[] slots)
        {
            Symbols = symbols;
            Limbs = Array.AsReadOnly(limbs);
            Nodes = Array.AsReadOnly(nodes);
            MaterialSlots = Array.AsReadOnly(slots);
        }
    }

    public enum SignedSourceAxis { PositiveX, NegativeX, PositiveY, NegativeY, PositiveZ, NegativeZ }
    public enum SourceTriangleOrder { Declared123, Renderer321 }

    public sealed class PlayerCoordinateConversion
    {
        public readonly SignedSourceAxis UnityX;
        public readonly SignedSourceAxis UnityY;
        public readonly SignedSourceAxis UnityZ;
        public readonly SourceTriangleOrder SourceOrder;
        public readonly int Determinant;
        public bool ReverseDeclaredTriangle { get { return (SourceOrder == SourceTriangleOrder.Renderer321) ^ (Determinant < 0); } }

        public PlayerCoordinateConversion(SignedSourceAxis unityX, SignedSourceAxis unityY,
            SignedSourceAxis unityZ, SourceTriangleOrder sourceOrder)
        {
            int x = AxisIndex(unityX), y = AxisIndex(unityY), z = AxisIndex(unityZ);
            if (x == y || x == z || y == z || !Enum.IsDefined(typeof(SourceTriangleOrder), sourceOrder))
                throw new PlayerAssetException("Coordinate conversion must be a signed permutation of X/Y/Z and an explicit triangle order.");
            UnityX = unityX; UnityY = unityY; UnityZ = unityZ; SourceOrder = sourceOrder;
            int inversions = (x > y ? 1 : 0) + (x > z ? 1 : 0) + (y > z ? 1 : 0);
            Determinant = (inversions % 2 == 0 ? 1 : -1) * Sign(unityX) * Sign(unityY) * Sign(unityZ);
        }

        public AssetVector3 Convert(AssetVector3 point)
        {
            return new AssetVector3(Component(point, UnityX), Component(point, UnityY), Component(point, UnityZ));
        }

        private static int AxisIndex(SignedSourceAxis axis)
        {
            if (!Enum.IsDefined(typeof(SignedSourceAxis), axis))
                throw new PlayerAssetException("Unknown signed source axis.");
            return (int)axis / 2;
        }
        private static int Sign(SignedSourceAxis axis) { return (int)axis % 2 == 0 ? 1 : -1; }
        private static float Component(AssetVector3 p, SignedSourceAxis axis)
        {
            int index = AxisIndex(axis);
            return (index == 0 ? p.X : index == 1 ? p.Y : p.Z) * Sign(axis);
        }
    }

    public sealed class PlayerMaterialBinding
    {
        public readonly string Slot;
        public readonly string TexturePath;
        public readonly float SourceSUnits;
        public readonly float SourceTUnits;
        public readonly bool FlipV;
        public readonly float AlphaCutoff;

        public PlayerMaterialBinding(string slot, string texturePath, float sourceSUnits, float sourceTUnits, bool flipV, float alphaCutoff = 0f)
        {
            if (string.IsNullOrEmpty(slot))
                throw new PlayerAssetException("Material binding needs an exact source slot name.");
            if (!FinitePositive(sourceSUnits) || !FinitePositive(sourceTUnits))
                throw new PlayerAssetException("UV divisors for '" + slot + "' must be finite and in 1..1000000; no texture-size guess is made.");
            if (float.IsNaN(alphaCutoff) || float.IsInfinity(alphaCutoff) || alphaCutoff < 0 || alphaCutoff > 1)
                throw new PlayerAssetException("Alpha cutoff must be finite in 0..1; zero selects opaque, positive values are an explicit preview adaptation.");
            Slot = slot; TexturePath = texturePath;
            SourceSUnits = sourceSUnits; SourceTUnits = sourceTUnits; FlipV = flipV;
            AlphaCutoff = alphaCutoff;
        }

        private static bool FinitePositive(float n)
        {
            return !float.IsNaN(n) && !float.IsInfinity(n) && n >= 1f && n <= PlayerImportLimits.CoordinateMagnitude;
        }
    }

    public sealed class BindPoseLimbMesh
    {
        public readonly string Symbol;
        public readonly AssetVector3 LocalRestOffset;
        public readonly ReadOnlyCollection<AssetVector3> Positions;
        public readonly ReadOnlyCollection<AssetVector3> Normals;
        public readonly ReadOnlyCollection<AssetVector2> Uvs;
        public readonly ReadOnlyCollection<ReadOnlyCollection<int>> SubmeshTriangles;
        public readonly bool RecalculatedNormals;

        internal BindPoseLimbMesh(string symbol, AssetVector3 rest, AssetVector3[] positions,
            AssetVector3[] normals, AssetVector2[] uvs, IList<int[]> triangles, bool recalculated)
        {
            Symbol = symbol; LocalRestOffset = rest;
            Positions = Array.AsReadOnly(positions); Normals = Array.AsReadOnly(normals); Uvs = Array.AsReadOnly(uvs);
            var groups = new List<ReadOnlyCollection<int>>();
            foreach (int[] group in triangles) groups.Add(Array.AsReadOnly(group));
            SubmeshTriangles = groups.AsReadOnly();
            RecalculatedNormals = recalculated;
        }
    }
}
