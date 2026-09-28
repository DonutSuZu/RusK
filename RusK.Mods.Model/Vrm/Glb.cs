using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace RusK.Mods.Model.Vrm;

/// <summary>
/// glb (バイナリの glTF 2.0) の読み込み。VRM は glb の拡張なので、まずこれで中身を取り出す。
/// ヘッダ (12 バイト) → JSON チャンク → BIN チャンク の順に並んでいる。
/// </summary>
internal sealed class Glb : IDisposable
{
    public JsonDocument Doc { get; }
    public JsonElement Json => Doc.RootElement;
    public byte[] Bin { get; }

    private Glb(JsonDocument doc, byte[] bin)
    {
        Doc = doc;
        Bin = bin;
    }

    public static Glb Load(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 20 || BitConverter.ToUInt32(data, 0) != 0x46546C67) // "glTF"
            throw new InvalidDataException("glb ファイルではありません (先頭が glTF ではない)");
        uint version = BitConverter.ToUInt32(data, 4);
        if (version != 2) throw new InvalidDataException($"glTF のバージョン {version} には対応していません (2 のみ)");

        JsonDocument doc = null;
        byte[] bin = Array.Empty<byte>();
        int pos = 12;
        while (pos + 8 <= data.Length)
        {
            int len = BitConverter.ToInt32(data, pos);
            uint type = BitConverter.ToUInt32(data, pos + 4);
            pos += 8;
            if (type == 0x4E4F534A) // JSON
                doc = JsonDocument.Parse(Encoding.UTF8.GetString(data, pos, len));
            else if (type == 0x004E4942) // BIN
            {
                bin = new byte[len];
                Buffer.BlockCopy(data, pos, bin, 0, len);
            }
            pos += len;
        }
        if (doc == null) throw new InvalidDataException("glb に JSON がありません");
        return new Glb(doc, bin);
    }

    public void Dispose() => Doc.Dispose();

    // ------------------------------------------------------------------ アクセサ (データの読み出し)

    /// <summary>bufferView の中身 (バイト列の位置と長さ)</summary>
    public (int offset, int length, int stride) View(int index)
    {
        var v = Json.GetProperty("bufferViews")[index];
        int buffer = v.TryGetProperty("buffer", out var b) ? b.GetInt32() : 0;
        if (buffer != 0) throw new NotSupportedException("外部バッファを使う glTF には対応していません");
        int offset = v.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
        int length = v.GetProperty("byteLength").GetInt32();
        int stride = v.TryGetProperty("byteStride", out var s) ? s.GetInt32() : 0;
        return (offset, length, stride);
    }

    public byte[] ViewBytes(int index)
    {
        var (offset, length, _) = View(index);
        var bytes = new byte[length];
        Buffer.BlockCopy(Bin, offset, bytes, 0, length);
        return bytes;
    }

    private static int Components(string type) => type switch
    {
        "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16,
        _ => throw new NotSupportedException($"アクセサの型 {type}"),
    };

    private static int ComponentSize(int componentType) => componentType switch
    {
        5120 or 5121 => 1, // byte / unsigned byte
        5122 or 5123 => 2, // short / unsigned short
        5125 or 5126 => 4, // unsigned int / float
        _ => throw new NotSupportedException($"componentType {componentType}"),
    };

    /// <summary>アクセサを float の配列として読む (整数は normalized なら 0～1 に、そうでなければそのまま)</summary>
    public float[] ReadFloats(int accessor, out int components)
    {
        var a = Json.GetProperty("accessors")[accessor];
        int count = a.GetProperty("count").GetInt32();
        components = Components(a.GetProperty("type").GetString());
        int ct = a.GetProperty("componentType").GetInt32();
        bool normalized = a.TryGetProperty("normalized", out var n) && n.GetBoolean();
        var result = new float[count * components];

        if (a.TryGetProperty("bufferView", out var bvEl))
        {
            var (offset, _, stride) = View(bvEl.GetInt32());
            offset += a.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0;
            int size = ComponentSize(ct);
            if (stride == 0) stride = size * components;
            for (int i = 0; i < count; i++)
            {
                int p = offset + i * stride;
                for (int c = 0; c < components; c++)
                    result[i * components + c] = ReadComponent(p + c * size, ct, normalized);
            }
        }

        // 疎なアクセサ (モーフの差分でよく使われる): 一部の要素だけ値を上書きする
        if (a.TryGetProperty("sparse", out var sparse))
        {
            int sc = sparse.GetProperty("count").GetInt32();
            var idx = sparse.GetProperty("indices");
            var val = sparse.GetProperty("values");
            var (io, _, _) = View(idx.GetProperty("bufferView").GetInt32());
            io += idx.TryGetProperty("byteOffset", out var iof) ? iof.GetInt32() : 0;
            int ict = idx.GetProperty("componentType").GetInt32();
            var (vo, _, _) = View(val.GetProperty("bufferView").GetInt32());
            vo += val.TryGetProperty("byteOffset", out var vof) ? vof.GetInt32() : 0;
            int vsize = ComponentSize(ct);
            for (int i = 0; i < sc; i++)
            {
                int target = (int)ReadComponent(io + i * ComponentSize(ict), ict, false);
                for (int c = 0; c < components; c++)
                    result[target * components + c] = ReadComponent(vo + (i * components + c) * vsize, ct, normalized);
            }
        }
        return result;
    }

    /// <summary>アクセサを整数の配列として読む (インデックス・関節番号)</summary>
    public int[] ReadInts(int accessor, out int components)
    {
        var a = Json.GetProperty("accessors")[accessor];
        int count = a.GetProperty("count").GetInt32();
        components = Components(a.GetProperty("type").GetString());
        int ct = a.GetProperty("componentType").GetInt32();
        var result = new int[count * components];
        if (!a.TryGetProperty("bufferView", out var bvEl)) return result;
        var (offset, _, stride) = View(bvEl.GetInt32());
        offset += a.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0;
        int size = ComponentSize(ct);
        if (stride == 0) stride = size * components;
        for (int i = 0; i < count; i++)
        {
            int p = offset + i * stride;
            for (int c = 0; c < components; c++)
                result[i * components + c] = (int)ReadComponent(p + c * size, ct, false);
        }
        return result;
    }

    private float ReadComponent(int p, int ct, bool normalized)
    {
        switch (ct)
        {
            case 5126: return BitConverter.ToSingle(Bin, p);
            case 5121: return normalized ? Bin[p] / 255f : Bin[p];
            case 5123: { ushort v = BitConverter.ToUInt16(Bin, p); return normalized ? v / 65535f : v; }
            case 5125: return BitConverter.ToUInt32(Bin, p);
            case 5120: { sbyte v = (sbyte)Bin[p]; return normalized ? Math.Max(v / 127f, -1f) : v; }
            case 5122: { short v = BitConverter.ToInt16(Bin, p); return normalized ? Math.Max(v / 32767f, -1f) : v; }
            default: throw new NotSupportedException($"componentType {ct}");
        }
    }
}
