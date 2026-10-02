using System;
using System.IO;
using UnityEngine;

namespace RusK.Mods.Model.Vrm.Pmx;

/// <summary>
/// MMD のモデルのテクスチャを読む。PNG・JPG は Unity (ImageConversion) で、BMP・TGA はここで RGBA に直す。
/// 拡張子と中身が違うことがよくあるので、ファイルの先頭で形式を見分ける
/// </summary>
internal static class ImageDecoder
{
    public static Texture2D Load(string path, out bool hasAlpha)
    {
        hasAlpha = false;
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 8) return null;
        var name = Path.GetFileNameWithoutExtension(path);

        bool png = bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G';
        bool jpg = bytes[0] == 0xFF && bytes[1] == 0xD8;
        if (png || jpg)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = name };
            if (!ImageConversion.LoadImage(tex, bytes)) { UnityEngine.Object.Destroy(tex); return null; }
            // PNG の色の種類 (IHDR の 25 バイト目): 4 = グレー + 透明度、6 = RGBA。3 (パレット) は tRNS があれば透明
            hasAlpha = png && bytes.Length > 25 && (bytes[25] == 4 || bytes[25] == 6 || (bytes[25] == 3 && Contains(bytes, "tRNS")));
            return tex;
        }

        byte[] rgba;
        int w, h;
        if (bytes[0] == 'B' && bytes[1] == 'M') rgba = Bmp(bytes, out w, out h, out hasAlpha);
        else if (bytes[0] == 'D' && bytes[1] == 'D' && bytes[2] == 'S') throw new NotSupportedException("DDS は読めません (PNG に変換してください)");
        else rgba = Tga(bytes, out w, out h, out hasAlpha);

        var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name };
        t.LoadRawTextureData(rgba);
        t.Apply(true);
        return t;
    }

    private static bool Contains(byte[] b, string tag)
    {
        for (int i = 8; i + 4 < b.Length && i < 1 << 16; i++)
            if (b[i] == tag[0] && b[i + 1] == tag[1] && b[i + 2] == tag[2] && b[i + 3] == tag[3]) return true;
        return false;
    }

    /// <summary>BMP (1・4・8 ビットのパレット、24・32 ビット、無圧縮)。結果は下の行から並ぶ (Unity と同じ)</summary>
    private static byte[] Bmp(byte[] b, out int w, out int h, out bool hasAlpha)
    {
        int dataOffset = BitConverter.ToInt32(b, 10);
        int headerSize = BitConverter.ToInt32(b, 14);
        w = BitConverter.ToInt32(b, 18);
        int rawH = BitConverter.ToInt32(b, 22);
        h = Math.Abs(rawH);
        bool topDown = rawH < 0;
        int bpp = BitConverter.ToUInt16(b, 28);
        int compression = BitConverter.ToInt32(b, 30);
        if (compression != 0 && compression != 3) throw new NotSupportedException($"圧縮された BMP ({compression}) は読めません");
        int colors = headerSize >= 36 ? BitConverter.ToInt32(b, 46) : 0;
        if (colors == 0 && bpp <= 8) colors = 1 << bpp;
        int palette = 14 + headerSize;

        int stride = (w * bpp + 31) / 32 * 4;
        var o = new byte[w * h * 4];
        bool anyAlpha = false;
        for (int y = 0; y < h; y++)
        {
            int src = dataOffset + (topDown ? h - 1 - y : y) * stride;
            int dst = y * w * 4;
            for (int x = 0; x < w; x++, dst += 4)
            {
                byte r, g, bl, a = 255;
                if (bpp == 32)
                {
                    int p = src + x * 4;
                    bl = b[p]; g = b[p + 1]; r = b[p + 2]; a = b[p + 3];
                }
                else if (bpp == 24)
                {
                    int p = src + x * 3;
                    bl = b[p]; g = b[p + 1]; r = b[p + 2];
                }
                else if (bpp <= 8)
                {
                    int bit = x * bpp;
                    int idx = (b[src + bit / 8] >> (8 - bpp - bit % 8)) & ((1 << bpp) - 1);
                    int p = palette + Math.Min(idx, colors - 1) * 4;
                    bl = b[p]; g = b[p + 1]; r = b[p + 2];
                }
                else throw new NotSupportedException($"{bpp} ビットの BMP は読めません");
                o[dst] = r; o[dst + 1] = g; o[dst + 2] = bl; o[dst + 3] = a;
                if (a != 255) anyAlpha = true;
            }
        }
        // 32 ビットでも透明度を使っていない (全部 0) BMP は多い。そのときは不透明にする
        if (bpp == 32 && anyAlpha && AllZeroAlpha(o))
        {
            for (int i = 3; i < o.Length; i += 4) o[i] = 255;
            anyAlpha = false;
        }
        hasAlpha = anyAlpha;
        return o;
    }

    private static bool AllZeroAlpha(byte[] rgba)
    {
        for (int i = 3; i < rgba.Length; i += 4)
            if (rgba[i] != 0) return false;
        return true;
    }

    /// <summary>TGA (フルカラー・グレー・パレット、無圧縮と RLE)</summary>
    private static byte[] Tga(byte[] b, out int w, out int h, out bool hasAlpha)
    {
        int idLength = b[0];
        int mapType = b[1];
        int type = b[2];
        int mapStart = BitConverter.ToUInt16(b, 3), mapLength = BitConverter.ToUInt16(b, 5), mapBits = b[7];
        w = BitConverter.ToUInt16(b, 12);
        h = BitConverter.ToUInt16(b, 14);
        int bpp = b[16];
        bool topDown = (b[17] & 0x20) != 0;
        bool rle = type >= 9;
        int baseType = rle ? type - 8 : type;
        if (baseType < 1 || baseType > 3 || w == 0 || h == 0) throw new NotSupportedException("TGA ではないか、読めない形式です");

        int pos = 18 + idLength;
        int mapBytes = (mapBits + 7) / 8;
        int mapPos = pos;
        if (mapType == 1) pos += mapLength * mapBytes;

        int bytesPer = (bpp + 7) / 8;
        var pixels = new byte[w * h * 4];
        bool anyAlpha = false;

        void Decode(int at, int dst)
        {
            byte r, g, bl, a = 255;
            if (baseType == 1)
            {
                int idx = (bytesPer == 1 ? b[at] : BitConverter.ToUInt16(b, at)) - mapStart;
                int p = mapPos + Math.Max(0, Math.Min(idx, mapLength - 1)) * mapBytes;
                Color(b, p, mapBytes, out r, out g, out bl, out a);
            }
            else if (baseType == 3)
            {
                r = g = bl = b[at];
                if (bytesPer == 2) a = b[at + 1];
            }
            else Color(b, at, bytesPer, out r, out g, out bl, out a);
            pixels[dst] = r; pixels[dst + 1] = g; pixels[dst + 2] = bl; pixels[dst + 3] = a;
            if (a != 255) anyAlpha = true;
        }

        int total = w * h;
        int n = 0;
        while (n < total)
        {
            if (rle)
            {
                int header = b[pos++];
                int run = (header & 0x7F) + 1;
                if ((header & 0x80) != 0)
                {
                    for (int k = 0; k < run && n < total; k++, n++) Decode(pos, n * 4);
                    pos += bytesPer;
                }
                else
                {
                    for (int k = 0; k < run && n < total; k++, n++, pos += bytesPer) Decode(pos, n * 4);
                }
            }
            else
            {
                Decode(pos, n * 4);
                pos += bytesPer;
                n++;
            }
        }

        // Unity は下の行から。TGA の上から並ぶものは上下を入れ替える
        if (topDown)
        {
            int row = w * 4;
            var tmp = new byte[row];
            for (int y = 0; y < h / 2; y++)
            {
                Buffer.BlockCopy(pixels, y * row, tmp, 0, row);
                Buffer.BlockCopy(pixels, (h - 1 - y) * row, pixels, y * row, row);
                Buffer.BlockCopy(tmp, 0, pixels, (h - 1 - y) * row, row);
            }
        }
        if (bpp == 32 && anyAlpha && AllZeroAlpha(pixels))
        {
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            anyAlpha = false;
        }
        hasAlpha = anyAlpha;
        return pixels;
    }

    private static void Color(byte[] b, int p, int bytes, out byte r, out byte g, out byte bl, out byte a)
    {
        a = 255;
        if (bytes >= 3)
        {
            bl = b[p]; g = b[p + 1]; r = b[p + 2];
            if (bytes == 4) a = b[p + 3];
        }
        else // 16 ビット (ARRRRRGG GGGBBBBB)
        {
            int v = BitConverter.ToUInt16(b, p);
            r = (byte)(((v >> 10) & 31) * 255 / 31);
            g = (byte)(((v >> 5) & 31) * 255 / 31);
            bl = (byte)((v & 31) * 255 / 31);
        }
    }
}
