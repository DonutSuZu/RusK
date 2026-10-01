using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace RusK.Manager;

/// <summary>DLL の [RuskMod] 属性と、埋め込みの言語ファイル (lang.en.json など) の中身</summary>
internal sealed class ModDllInfo
{
    public string Id, Name, Version, Author = "", Description = "", GameVersion = "";
    /// <summary>言語コード (en / zh …) → 元の文 (日本語) → 訳</summary>
    public readonly Dictionary<string, Dictionary<string, string>> Lang = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>元の文を今の言語に訳す (訳が無ければそのまま)</summary>
    public string Translate(string text)
    {
        if (string.IsNullOrEmpty(text) || Strings.Current == "ja") return text;
        return Lang.TryGetValue(Strings.Current, out var t) && t.TryGetValue(text, out var s) && !string.IsNullOrEmpty(s) ? s : text;
    }
}

/// <summary>
/// Mod の DLL を読み込まずに (ゲームの .NET 6 用の DLL は、この .NET Framework のアプリでは読み込めない)、
/// PE と .NET のメタデータ (ECMA-335) を直接読んで [RuskMod("id", "name", "version", Author = …, Description = …)] を取り出す。
/// RusK の Mod でない DLL や、壊れた DLL なら null
/// </summary>
internal static class ModInfoReader
{
    public static ModDllInfo Read(string path)
    {
        try { return new Reader(File.ReadAllBytes(path)).Read(); }
        catch { return null; }
    }

    private sealed class Reader
    {
        private readonly byte[] _b;
        private (uint va, uint size, uint raw)[] _sections;
        private int _strings, _blob;
        private bool _bigStr, _bigGuid, _bigBlob;
        private readonly int[] _rows = new int[64];
        private readonly int[] _tableOffset = new int[64];
        private readonly int[] _rowSize = new int[64];
        private uint _resourcesRva;

        public Reader(byte[] bytes) => _b = bytes;

        private ushort U2(int o) => BitConverter.ToUInt16(_b, o);
        private uint U4(int o) => BitConverter.ToUInt32(_b, o);

        private int Offset(uint rva)
        {
            foreach (var s in _sections)
                if (rva >= s.va && rva < s.va + Math.Max(s.size, 1)) return (int)(rva - s.va + s.raw);
            throw new InvalidDataException("rva");
        }

        public ModDllInfo Read()
        {
            // PE ヘッダー → セクション → CLI ヘッダー
            if (_b.Length < 0x40 || _b[0] != 'M' || _b[1] != 'Z') return null;
            int pe = (int)U4(0x3C);
            if (U4(pe) != 0x00004550) return null;
            int coff = pe + 4;
            int sections = U2(coff + 2), optSize = U2(coff + 16);
            int opt = coff + 20;
            int dirs = U2(opt) == 0x20B ? opt + 112 : opt + 96;
            uint cliRva = U4(dirs + 14 * 8);
            if (cliRva == 0) return null; // .NET の DLL ではない
            _sections = new (uint, uint, uint)[sections];
            int sec = opt + optSize;
            for (int i = 0; i < sections; i++, sec += 40)
                _sections[i] = (U4(sec + 12), Math.Max(U4(sec + 8), U4(sec + 16)), U4(sec + 20));
            int cli = Offset(cliRva);
            int meta = Offset(U4(cli + 8));
            _resourcesRva = U4(cli + 24);

            // メタデータのルート → ストリーム (#~ / #Strings / #Blob)
            if (U4(meta) != 0x424A5342) return null;
            int verLen = (int)U4(meta + 12);
            int p = meta + 16 + verLen + 2;
            int streams = U2(p);
            p += 2;
            int tables = -1;
            for (int i = 0; i < streams; i++)
            {
                int off = (int)U4(p), name = p + 8;
                int end = name;
                while (_b[end] != 0) end++;
                var n = Encoding.ASCII.GetString(_b, name, end - name);
                if (n == "#~" || n == "#-") tables = meta + off;
                else if (n == "#Strings") _strings = meta + off;
                else if (n == "#Blob") _blob = meta + off;
                p = name + ((end - name + 4) & ~3);
            }
            if (tables < 0) return null;

            // 表の行数 → 1 行の大きさ → 表の位置
            byte heap = _b[tables + 6];
            _bigStr = (heap & 1) != 0;
            _bigGuid = (heap & 2) != 0;
            _bigBlob = (heap & 4) != 0;
            ulong valid = BitConverter.ToUInt64(_b, tables + 8);
            p = tables + 24;
            for (int i = 0; i < 64; i++)
                if ((valid & (1UL << i)) != 0) { _rows[i] = (int)U4(p); p += 4; }
            if ((heap & 0x20) != 0) p += 4; // 圧縮していない #- の追加データ
            for (int i = 0; i < Schema.Length; i++)
            {
                _rowSize[i] = RowSize(Schema[i]);
                _tableOffset[i] = p;
                p += _rowSize[i] * _rows[i];
            }

            var info = FindRuskMod();
            if (info == null) return null;
            ReadLang(info);
            return info;
        }

        // ---------------------------------------------------------------- 表の形

        // 列の種類: 1/2/4 = 固定長、S = 文字列、G = GUID、B = Blob、T{n} = 表 n の番号、C{k} = 符号化インデックス k
        private static readonly string[][] Schema =
        {
            /*00 Module*/ new[] { "2", "S", "G", "G", "G" },
            /*01 TypeRef*/ new[] { "C:ResolutionScope", "S", "S" },
            /*02 TypeDef*/ new[] { "4", "S", "S", "C:TypeDefOrRef", "T04", "T06" },
            /*03 FieldPtr*/ new[] { "T04" },
            /*04 Field*/ new[] { "2", "S", "B" },
            /*05 MethodPtr*/ new[] { "T06" },
            /*06 MethodDef*/ new[] { "4", "2", "2", "S", "B", "T08" },
            /*07 ParamPtr*/ new[] { "T08" },
            /*08 Param*/ new[] { "2", "2", "S" },
            /*09 InterfaceImpl*/ new[] { "T02", "C:TypeDefOrRef" },
            /*0A MemberRef*/ new[] { "C:MemberRefParent", "S", "B" },
            /*0B Constant*/ new[] { "2", "C:HasConstant", "B" },
            /*0C CustomAttribute*/ new[] { "C:HasCustomAttribute", "C:CustomAttributeType", "B" },
            /*0D FieldMarshal*/ new[] { "C:HasFieldMarshal", "B" },
            /*0E DeclSecurity*/ new[] { "2", "C:HasDeclSecurity", "B" },
            /*0F ClassLayout*/ new[] { "2", "4", "T02" },
            /*10 FieldLayout*/ new[] { "4", "T04" },
            /*11 StandAloneSig*/ new[] { "B" },
            /*12 EventMap*/ new[] { "T02", "T14" },
            /*13 EventPtr*/ new[] { "T14" },
            /*14 Event*/ new[] { "2", "S", "C:TypeDefOrRef" },
            /*15 PropertyMap*/ new[] { "T02", "T17" },
            /*16 PropertyPtr*/ new[] { "T17" },
            /*17 Property*/ new[] { "2", "S", "B" },
            /*18 MethodSemantics*/ new[] { "2", "T06", "C:HasSemantics" },
            /*19 MethodImpl*/ new[] { "T02", "C:MethodDefOrRef", "C:MethodDefOrRef" },
            /*1A ModuleRef*/ new[] { "S" },
            /*1B TypeSpec*/ new[] { "B" },
            /*1C ImplMap*/ new[] { "2", "C:MemberForwarded", "S", "T1A" },
            /*1D FieldRVA*/ new[] { "4", "T04" },
            /*1E EncLog*/ new[] { "4", "4" },
            /*1F EncMap*/ new[] { "4" },
            /*20 Assembly*/ new[] { "4", "2", "2", "2", "2", "4", "B", "S", "S" },
            /*21 AssemblyProcessor*/ new[] { "4" },
            /*22 AssemblyOS*/ new[] { "4", "4", "4" },
            /*23 AssemblyRef*/ new[] { "2", "2", "2", "2", "4", "B", "S", "S", "B" },
            /*24 AssemblyRefProcessor*/ new[] { "4", "T23" },
            /*25 AssemblyRefOS*/ new[] { "4", "4", "4", "T23" },
            /*26 File*/ new[] { "4", "S", "B" },
            /*27 ExportedType*/ new[] { "4", "4", "S", "S", "C:Implementation" },
            /*28 ManifestResource*/ new[] { "4", "4", "S", "C:Implementation" },
        };

        // 符号化インデックス: タグのビット数と、指せる表 (-1 は使われていない枠)
        private static readonly Dictionary<string, (int bits, int[] tables)> Coded = new()
        {
            ["TypeDefOrRef"] = (2, new[] { 0x02, 0x01, 0x1B }),
            ["HasConstant"] = (2, new[] { 0x04, 0x08, 0x17 }),
            ["HasCustomAttribute"] = (5, new[] { 0x06, 0x04, 0x01, 0x02, 0x08, 0x09, 0x0A, 0x00, 0x0E, 0x17, 0x14, 0x11, 0x1A, 0x1B, 0x20, 0x23, 0x26, 0x27, 0x28, 0x2A, 0x2C, 0x2B }),
            ["HasFieldMarshal"] = (1, new[] { 0x04, 0x08 }),
            ["HasDeclSecurity"] = (2, new[] { 0x02, 0x06, 0x20 }),
            ["MemberRefParent"] = (3, new[] { 0x02, 0x01, 0x1A, 0x06, 0x1B }),
            ["HasSemantics"] = (1, new[] { 0x14, 0x17 }),
            ["MethodDefOrRef"] = (1, new[] { 0x06, 0x0A }),
            ["MemberForwarded"] = (1, new[] { 0x04, 0x06 }),
            ["Implementation"] = (2, new[] { 0x26, 0x23, 0x27 }),
            ["CustomAttributeType"] = (3, new[] { -1, -1, 0x06, 0x0A, -1 }),
            ["ResolutionScope"] = (2, new[] { 0x00, 0x1A, 0x23, 0x01 }),
        };

        private int ColSize(string c)
        {
            switch (c[0])
            {
                case 'S': return _bigStr ? 4 : 2;
                case 'G': return _bigGuid ? 4 : 2;
                case 'B': return _bigBlob ? 4 : 2;
                case 'T': return _rows[Convert.ToInt32(c.Substring(1), 16)] > 0xFFFF ? 4 : 2;
                case 'C':
                    var (bits, tables) = Coded[c.Substring(2)];
                    int max = 0;
                    foreach (var t in tables) if (t >= 0) max = Math.Max(max, _rows[t]);
                    return max < (1 << (16 - bits)) ? 2 : 4;
                default: return int.Parse(c);
            }
        }

        private int RowSize(string[] cols)
        {
            int n = 0;
            foreach (var c in cols) n += ColSize(c);
            return n;
        }

        /// <summary>表 table の row 行目 (1 始まり) の col 列目の値</summary>
        private uint Cell(int table, int row, int col)
        {
            var cols = Schema[table];
            int o = _tableOffset[table] + (row - 1) * _rowSize[table];
            for (int i = 0; i < col; i++) o += ColSize(cols[i]);
            return ColSize(cols[col]) == 4 ? U4(o) : U2(o);
        }

        private string Str(uint index)
        {
            int s = _strings + (int)index, e = s;
            while (_b[e] != 0) e++;
            return Encoding.UTF8.GetString(_b, s, e - s);
        }

        /// <summary>Blob の中身の位置と長さ</summary>
        private (int at, int len) Blob(uint index)
        {
            int p = _blob + (int)index;
            int len = ReadCompressed(ref p);
            return (p, len);
        }

        private int ReadCompressed(ref int p)
        {
            byte a = _b[p];
            if ((a & 0x80) == 0) { p += 1; return a; }
            if ((a & 0xC0) == 0x80) { int v = ((a & 0x3F) << 8) | _b[p + 1]; p += 2; return v; }
            int w = ((a & 0x1F) << 24) | (_b[p + 1] << 16) | (_b[p + 2] << 8) | _b[p + 3];
            p += 4;
            return w;
        }

        private string SerString(ref int p)
        {
            if (_b[p] == 0xFF) { p++; return null; }
            int len = ReadCompressed(ref p);
            var s = Encoding.UTF8.GetString(_b, p, len);
            p += len;
            return s;
        }

        // ---------------------------------------------------------------- [RuskMod] と言語ファイル

        private ModDllInfo FindRuskMod()
        {
            const int CustomAttribute = 0x0C, MemberRef = 0x0A, TypeRef = 0x01;
            for (int r = 1; r <= _rows[CustomAttribute]; r++)
            {
                uint type = Cell(CustomAttribute, r, 1);
                if ((type & 7) != 3) continue; // MemberRef (別の DLL の属性のコンストラクター) だけ
                int mr = (int)(type >> 3);
                uint parent = Cell(MemberRef, mr, 0);
                if ((parent & 7) != 1) continue; // TypeRef
                int tr = (int)(parent >> 3);
                if (Str(Cell(TypeRef, tr, 1)) != "RuskModAttribute" || Str(Cell(TypeRef, tr, 2)) != "RusK.API") continue;

                var (at, len) = Blob(Cell(CustomAttribute, r, 2));
                int p = at, end = at + len;
                if (U2(p) != 1) continue;
                p += 2;
                var info = new ModDllInfo { Id = SerString(ref p), Name = SerString(ref p), Version = SerString(ref p) };
                int named = U2(p);
                p += 2;
                for (int i = 0; i < named && p < end; i++)
                {
                    p++; // 0x53 フィールド / 0x54 プロパティ
                    if (_b[p++] != 0x0E) break; // 文字列以外はこの Mod 用の属性には無い
                    var name = SerString(ref p);
                    var value = SerString(ref p) ?? "";
                    switch (name)
                    {
                        case "Author": info.Author = value; break;
                        case "Description": info.Description = value; break;
                        case "GameVersion": info.GameVersion = value; break;
                    }
                }
                return info;
            }
            return null;
        }

        private void ReadLang(ModDllInfo info)
        {
            const int ManifestResource = 0x28;
            if (_resourcesRva == 0) return;
            int baseOffset = Offset(_resourcesRva);
            var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            for (int r = 1; r <= _rows[ManifestResource]; r++)
            {
                if (Cell(ManifestResource, r, 3) != 0) continue; // 別のファイルにあるもの
                var name = Str(Cell(ManifestResource, r, 2));
                if (!name.StartsWith("lang.", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                var code = name.Substring(5, name.Length - 10);
                try
                {
                    int o = baseOffset + (int)Cell(ManifestResource, r, 0);
                    int len = (int)U4(o);
                    var text = Encoding.UTF8.GetString(_b, o + 4, len).TrimStart('﻿');
                    if (json.DeserializeObject(text) is not Dictionary<string, object> d) continue;
                    var table = new Dictionary<string, string>();
                    foreach (var kv in d) if (kv.Value is string s) table[kv.Key] = s;
                    info.Lang[code] = table;
                }
                catch { }
            }
        }
    }
}
