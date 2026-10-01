"""
言語ファイルを作る道具。

  python tools/langgen.py <プロジェクトのフォルダ> [--check]

1. プロジェクトの .cs から、訳す文を集める
   - L.T(...) / RuskLang.T(...) の中の文字列
   - 日本語を含む文字列 (ログ・例外・Model Lab などの開発者向けは除く)。モジュール・設定の説明など、本体が自動で訳すもの
2. <プロジェクト>/lang/translations.tsv (元の文 \t 英語 \t 中国語) の訳を使って
   lang/ja.json・en.json・zh.json を書き出す。ja.json は元の文そのまま (自分で言葉を変えたいとき用のひな形)
3. 訳が無い文は translations.tsv の最後に空欄で足し、一覧を出す
"""
import io, json, os, re, sys

sys.stdout.reconfigure(encoding='utf-8')

LIT = re.compile(r'(\$?@?)"((?:[^"\\\n]|\\.)*)"')
JP = re.compile(r'[぀-ヿ一-鿿]')
# 開発者向け (訳さない): ログ・例外・デバッグ用の書き出し
SKIP_LINE = re.compile(r'TryDo\(|Log\.(Info|Warning|Error|LogInfo|LogWarning|LogError|LogMessage|LogDebug)|Log\?\.|log\?\.|'
                       r'sb\.Append|Section\(|\bEnd\("|Fault\("|throw new|Debug\.Log|\.Log\.|Rusk\.Log|Doctor\.|Warn\(|warn\?\.')
SKIP_FILES = ('ModelLab', 'ModelSwap.cs', 'SpecialAttackProbe', 'PartyLab', 'Probe')


def unescape(s):
    return bytes(s, 'utf-8').decode('unicode_escape').encode('latin-1').decode('utf-8') if '\\' in s else s


def t_calls(text):
    """L.T( ... ) / RuskLang.T( ... ) の引数の範囲 (括弧の対応を数える)"""
    for m in re.finditer(r'\b(?:L|RuskLang)\.T\(', text):
        i = m.end(); depth = 1; instr = False; esc = False
        while i < len(text) and depth > 0:
            c = text[i]
            if instr:
                if esc: esc = False
                elif c == '\\': esc = True
                elif c == '"': instr = False
            else:
                if c == '"': instr = True
                elif c == '(': depth += 1
                elif c == ')': depth -= 1
            i += 1
        yield text[m.end():i - 1]


def collect(project):
    keys = []
    seen = set()

    def add(k):
        k = unescape(k)
        if k and k not in seen:
            seen.add(k); keys.append(k)

    for dirpath, dirs, files in os.walk(project):
        dirs[:] = [d for d in dirs if d not in ('bin', 'obj', 'lang')]
        for f in sorted(files):
            if not f.endswith('.cs') or any(s in f for s in SKIP_FILES):
                continue
            text = io.open(os.path.join(dirpath, f), encoding='utf-8-sig').read()
            # L.T の中の文字列 (日本語でなくても訳す対象)
            for args in t_calls(text):
                first = True
                for m in LIT.finditer(args):
                    if '$' in m.group(1):
                        continue
                    add(m.group(2))
            # 日本語を含む文字列 (モジュール・設定の説明など)
            skip_class = False
            for line in text.splitlines():
                s = line.strip()
                if re.match(r'(internal|public)\s+(static\s+)?(sealed\s+)?class\s+\w*(Lab\w*|Probe\w*|MotionRecorder|PassUi)\b', s):
                    skip_class = True
                elif re.match(r'(internal|public)\s+(static\s+)?(sealed\s+)?class\s+', s):
                    skip_class = False
                if skip_class or s.startswith('//') or SKIP_LINE.search(line):
                    continue
                for m in LIT.finditer(line):
                    if '$' in m.group(1):
                        continue
                    if JP.search(m.group(2)):
                        add(m.group(2))
    return keys


def load_tsv(path):
    table = {}
    order = []
    if os.path.exists(path):
        for line in io.open(path, encoding='utf-8'):
            line = line.rstrip('\n')
            if not line or line.startswith('#'):
                continue
            parts = line.split('\t')
            while len(parts) < 3:
                parts.append('')
            k = parts[0].replace('\\n', '\n')
            table[k] = (parts[1].replace('\\n', '\n'), parts[2].replace('\\n', '\n'))
            order.append(k)
    return table, order


def esc(s):
    return s.replace('\n', '\\n')


def main():
    project = sys.argv[1]
    check_only = '--check' in sys.argv
    lang = os.path.join(project, 'lang')
    os.makedirs(lang, exist_ok=True)
    tsv = os.path.join(lang, 'translations.tsv')
    table, order = load_tsv(tsv)
    keys = collect(project)
    missing = [k for k in keys if k not in table or not table[k][0] or not table[k][1]]
    new = [k for k in keys if k not in table]
    if new and not check_only:
        with io.open(tsv, 'a', encoding='utf-8') as f:
            if not os.path.getsize(tsv) if os.path.exists(tsv) else True:
                f.write('# 元の文\t英語\t中国語 (簡体字)\n')
            for k in new:
                f.write(f'{esc(k)}\t\t\n')
    unused = [k for k in order if k not in set(keys)]

    if not check_only:
        use = [k for k in keys]
        # 翻訳表にだけある文 (本体が訳すモード名など、コードから自動で見つからないもの) も入れる
        for k in order:
            if k not in use:
                use.append(k)
        ja = {k: k for k in use}
        en = {k: table[k][0] for k in use if k in table and table[k][0]}
        zh = {k: table[k][1] for k in use if k in table and table[k][1]}
        for code, d in (('ja', ja), ('en', en), ('zh', zh)):
            with io.open(os.path.join(lang, f'{code}.json'), 'w', encoding='utf-8', newline='\n') as f:
                json.dump(d, f, ensure_ascii=False, indent=2)
                f.write('\n')
    print(f'{project}: 文 {len(keys)} / 訳なし {len(missing)} / 表にだけある {len(unused)}')
    for k in missing:
        print('  訳なし:', esc(k))


if __name__ == '__main__':
    main()
