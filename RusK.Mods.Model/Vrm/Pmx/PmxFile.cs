using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace RusK.Mods.Model.Vrm.Pmx;

/// <summary>
/// PMX (MikuMikuDance のモデル、2.0 / 2.1) を読む。座標・単位は PMX のまま (変換は PmxLoader)。
/// 仕様: 頂点・面・テクスチャ・材質・ボーン・モーフ・表示枠・剛体・ジョイント (ソフトボディは読まない)
/// </summary>
internal sealed class PmxFile
{
    public sealed class Vertex
    {
        public Vector3 Position, Normal;
        public Vector2 Uv;
        public int B0, B1 = -1, B2 = -1, B3 = -1;
        public float W0 = 1f, W1, W2, W3;
    }

    public sealed class Material
    {
        public string Name, NameEn;
        public Color Diffuse;
        public Vector3 Ambient;
        public byte Flags; // 1: 両面描画
        public int Texture = -1;
        public int IndexCount;
        public bool DoubleSided => (Flags & 1) != 0;
    }

    public sealed class Bone
    {
        public string Name, NameEn;
        public Vector3 Position;
        public int Parent = -1;
        public ushort Flags;
        public int TailBone = -1;
        public Vector3 TailOffset;
        public int InheritParent = -1;
        public float InheritWeight;
        public bool InheritRotation => (Flags & 0x0100) != 0;
        public bool InheritTranslation => (Flags & 0x0200) != 0;
        public bool IsIk => (Flags & 0x0020) != 0;
    }

    public sealed class Morph
    {
        public string Name, NameEn;
        public byte Type; // 0: グループ、1: 頂点
        public List<(int vertex, Vector3 offset)> Vertices;
        public List<(int morph, float weight)> Group;
    }

    public sealed class RigidBody
    {
        public string Name;
        public int Bone = -1;
        public byte Group;
        public ushort NoCollisionMask; // ビットが立っているグループとは当たらない
        public byte Shape;             // 0: 球、1: 箱、2: カプセル
        public Vector3 Size, Position, Rotation;
        public float Mass, MoveDamping, RotationDamping;
        public byte Mode;              // 0: ボーンに従う、1: 物理、2: 物理 + ボーンの位置
    }

    public string Name, NameEn, Comment;
    public float Version;
    public readonly List<Vertex> Vertices = new();
    public int[] Indices;
    public readonly List<string> Textures = new();
    public readonly List<Material> Materials = new();
    public readonly List<Bone> Bones = new();
    public readonly List<Morph> Morphs = new();
    public readonly List<RigidBody> RigidBodies = new();

    private BinaryReader _r;
    private Encoding _enc;
    private int _vertexSize, _textureSize, _materialSize, _boneSize, _morphSize, _rigidSize, _extraUv;

    public static PmxFile Load(string path)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        using var r = new BinaryReader(stream);
        var f = new PmxFile { _r = r };
        f.Read();
        f._r = null;
        return f;
    }

    private void Read()
    {
        if (Encoding.ASCII.GetString(_r.ReadBytes(4)) != "PMX ")
            throw new InvalidDataException("PMX ではありません (PMD は読めません。PMX エディタで PMX に変換してください)");
        Version = _r.ReadSingle();
        int globals = _r.ReadByte();
        var g = _r.ReadBytes(globals);
        if (g.Length < 8) throw new InvalidDataException("PMX のヘッダーが壊れています");
        _enc = g[0] == 0 ? Encoding.Unicode : Encoding.UTF8;
        _extraUv = g[1];
        _vertexSize = g[2];
        _textureSize = g[3];
        _materialSize = g[4];
        _boneSize = g[5];
        _morphSize = g[6];
        _rigidSize = g[7];

        Name = Text();
        NameEn = Text();
        Comment = Text();
        Text(); // 英語のコメント

        ReadVertices();
        int faces = _r.ReadInt32();
        Indices = new int[faces];
        for (int i = 0; i < faces; i++) Indices[i] = VertexIndex();
        int textures = _r.ReadInt32();
        for (int i = 0; i < textures; i++) Textures.Add(Text());
        ReadMaterials();
        ReadBones();
        ReadMorphs();
        SkipDisplayFrames();
        ReadRigidBodies();
        // ジョイント・ソフトボディは使わない
    }

    private string Text()
    {
        int len = _r.ReadInt32();
        return len <= 0 ? "" : _enc.GetString(_r.ReadBytes(len));
    }

    private Vector3 V3() => new(_r.ReadSingle(), _r.ReadSingle(), _r.ReadSingle());

    /// <summary>頂点の番号は符号なし、ほかの番号は符号あり (-1 = なし)</summary>
    private int VertexIndex() => _vertexSize switch
    {
        1 => _r.ReadByte(),
        2 => _r.ReadUInt16(),
        _ => _r.ReadInt32(),
    };

    private int Index(int size) => size switch
    {
        1 => _r.ReadSByte(),
        2 => _r.ReadInt16(),
        _ => _r.ReadInt32(),
    };

    private void ReadVertices()
    {
        int count = _r.ReadInt32();
        Vertices.Capacity = count;
        for (int i = 0; i < count; i++)
        {
            var v = new Vertex { Position = V3(), Normal = V3(), Uv = new Vector2(_r.ReadSingle(), _r.ReadSingle()) };
            for (int k = 0; k < _extraUv; k++) _r.ReadBytes(16);
            byte type = _r.ReadByte();
            switch (type)
            {
                case 0: // BDEF1
                    v.B0 = Index(_boneSize);
                    break;
                case 1: // BDEF2
                    v.B0 = Index(_boneSize);
                    v.B1 = Index(_boneSize);
                    v.W0 = _r.ReadSingle();
                    v.W1 = 1f - v.W0;
                    break;
                case 2: // BDEF4
                case 4: // QDEF (BDEF4 として扱う)
                    v.B0 = Index(_boneSize); v.B1 = Index(_boneSize); v.B2 = Index(_boneSize); v.B3 = Index(_boneSize);
                    v.W0 = _r.ReadSingle(); v.W1 = _r.ReadSingle(); v.W2 = _r.ReadSingle(); v.W3 = _r.ReadSingle();
                    break;
                case 3: // SDEF (BDEF2 として扱う)
                    v.B0 = Index(_boneSize);
                    v.B1 = Index(_boneSize);
                    v.W0 = _r.ReadSingle();
                    v.W1 = 1f - v.W0;
                    _r.ReadBytes(36); // C・R0・R1
                    break;
                default:
                    throw new InvalidDataException($"頂点 {i} のウェイトの種類 {type} が分かりません");
            }
            _r.ReadSingle(); // エッジの倍率
            Vertices.Add(v);
        }
    }

    private void ReadMaterials()
    {
        int count = _r.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            var m = new Material { Name = Text(), NameEn = Text() };
            m.Diffuse = new Color(_r.ReadSingle(), _r.ReadSingle(), _r.ReadSingle(), _r.ReadSingle());
            _r.ReadBytes(16); // スペキュラー・強さ
            m.Ambient = V3();
            m.Flags = _r.ReadByte();
            _r.ReadBytes(20); // エッジの色・大きさ
            m.Texture = Index(_textureSize);
            Index(_textureSize); // スフィア
            _r.ReadByte();       // スフィアのモード
            byte sharedToon = _r.ReadByte();
            if (sharedToon == 0) Index(_textureSize);
            else _r.ReadByte();
            Text(); // メモ
            m.IndexCount = _r.ReadInt32();
            Materials.Add(m);
        }
    }

    private void ReadBones()
    {
        int count = _r.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            var b = new Bone { Name = Text(), NameEn = Text(), Position = V3(), Parent = Index(_boneSize) };
            _r.ReadInt32(); // 変形の階層
            b.Flags = _r.ReadUInt16();
            if ((b.Flags & 0x0001) != 0) b.TailBone = Index(_boneSize);
            else b.TailOffset = V3();
            if ((b.Flags & 0x0300) != 0)
            {
                b.InheritParent = Index(_boneSize);
                b.InheritWeight = _r.ReadSingle();
            }
            if ((b.Flags & 0x0400) != 0) V3();              // 軸の固定
            if ((b.Flags & 0x0800) != 0) { V3(); V3(); }    // ローカル軸
            if ((b.Flags & 0x2000) != 0) _r.ReadInt32();    // 外部の親
            if (b.IsIk)
            {
                Index(_boneSize);
                _r.ReadInt32();
                _r.ReadSingle();
                int links = _r.ReadInt32();
                for (int k = 0; k < links; k++)
                {
                    Index(_boneSize);
                    if (_r.ReadByte() != 0) { V3(); V3(); }
                }
            }
            Bones.Add(b);
        }
    }

    private void ReadMorphs()
    {
        int count = _r.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            var m = new Morph { Name = Text(), NameEn = Text() };
            _r.ReadByte(); // パネル
            m.Type = _r.ReadByte();
            int n = _r.ReadInt32();
            switch (m.Type)
            {
                case 0: // グループ
                case 9: // フリップ (2.1。グループとして扱う)
                    m.Group = new List<(int, float)>(n);
                    for (int k = 0; k < n; k++) m.Group.Add((Index(_morphSize), _r.ReadSingle()));
                    if (m.Type == 9) m.Type = 0;
                    break;
                case 1: // 頂点
                    m.Vertices = new List<(int, Vector3)>(n);
                    for (int k = 0; k < n; k++) m.Vertices.Add((VertexIndex(), V3()));
                    break;
                case 2: // ボーン
                    for (int k = 0; k < n; k++) { Index(_boneSize); _r.ReadBytes(28); }
                    break;
                case 3: case 4: case 5: case 6: case 7: // UV・追加 UV
                    for (int k = 0; k < n; k++) { VertexIndex(); _r.ReadBytes(16); }
                    break;
                case 8: // 材質
                    for (int k = 0; k < n; k++) { Index(_materialSize); _r.ReadBytes(1 + 112); }
                    break;
                case 10: // インパルス (2.1)
                    for (int k = 0; k < n; k++) { Index(_rigidSize); _r.ReadBytes(1 + 24); }
                    break;
                default:
                    throw new InvalidDataException($"モーフ {m.Name} の種類 {m.Type} が分かりません");
            }
            Morphs.Add(m);
        }
    }

    private void SkipDisplayFrames()
    {
        int count = _r.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            Text();
            Text();
            _r.ReadByte();
            int n = _r.ReadInt32();
            for (int k = 0; k < n; k++)
            {
                byte type = _r.ReadByte();
                Index(type == 0 ? _boneSize : _morphSize);
            }
        }
    }

    private void ReadRigidBodies()
    {
        if (_r.BaseStream.Position >= _r.BaseStream.Length) return;
        int count = _r.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            var b = new RigidBody { Name = Text() };
            Text();
            b.Bone = Index(_boneSize);
            b.Group = _r.ReadByte();
            b.NoCollisionMask = _r.ReadUInt16();
            b.Shape = _r.ReadByte();
            b.Size = V3();
            b.Position = V3();
            b.Rotation = V3();
            b.Mass = _r.ReadSingle();
            b.MoveDamping = _r.ReadSingle();
            b.RotationDamping = _r.ReadSingle();
            _r.ReadSingle(); // 反発
            _r.ReadSingle(); // 摩擦
            b.Mode = _r.ReadByte();
            RigidBodies.Add(b);
        }
    }
}
