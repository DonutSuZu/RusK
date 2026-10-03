"""
Custom Character の絵を作る: ゲームの中で撮ったキャラの絵 (captures/bust.png) を、
お手本 (images_template, 土台のキャラの絵) と同じ大きさ・同じ構図にして images/ に書き出す。

  python make_card_art.py <キャラのフォルダ> <日本語の名前> <英語の名前>
  例: python make_card_art.py ".../RusK/characters/Pyra" ホムラ PYRA

作る絵: character_s (立ち絵) / choose_n (灰色の立ち絵) / BGrole (背景の大きな絵) / rolechoose (顔のアイコン) / Role (ルート画面の横顔)
        blackBar_n (カード) / name_s (名前) / NameBar (名前の帯) / roleName_s (英語の名前)
"""
import os
import sys
from PIL import Image, ImageChops, ImageDraw, ImageEnhance, ImageFilter, ImageFont, ImageOps

FONTS = r"C:\Windows\Fonts"
JP_FONT = os.path.join(FONTS, "YuGothB.ttc")
EN_FONT = os.path.join(FONTS, "ariblk.ttf")


# ---- 撮った絵の下ごしらえ ----

def brighten(im):
    """撮った絵は (ゲームの最後の色補正が無いので) 暗い。明るいところが白に近くなるまで持ち上げる"""
    rgb, a = im.convert("RGB"), im.getchannel("A")
    lum = sorted(v for v, al in zip(rgb.convert("L").getdata(), a.getdata()) if al > 200)
    top = lum[int(len(lum) * 0.985)] if lum else 255
    gain = min(3.5, 235.0 / max(1, top))
    rgb = rgb.point(lambda v: min(255, int(255 * ((v * gain) / 255.0) ** 0.85)))
    rgb = ImageEnhance.Color(rgb).enhance(1.1)
    out = rgb.convert("RGBA")
    out.putalpha(a)
    return out


def head_box(im):
    """頭の位置: 上端 (髪のてっぺん)・首 (頭の下で一番細い行)・頭の中心の x"""
    a = im.getchannel("A")
    x0, top, x1, bottom = a.getbbox()
    w, h = im.size
    px = a.load()

    def row(y):
        xs = [x for x in range(w) if px[x, y] > 128]
        return (xs[0], xs[-1]) if xs else None

    body = bottom - top
    best, neck = 1 << 30, top + body // 5
    for y in range(top + int(body * 0.12), top + int(body * 0.35)):
        r = row(y)
        if r and r[1] - r[0] < best:
            best, neck = r[1] - r[0], y
    # 頭の中心: 髪のてっぺんから首までの行の、左右の端の中点の平均
    cs = [sum(r) / 2 for r in (row(y) for y in range(top, neck)) if r]
    return top, neck, sum(cs) / len(cs)


def place(src, size, top, neck, cx, dst_top, dst_neck, dst_cx):
    """src の頭 (top〜neck, 中心 cx) が、size の絵の dst_top〜dst_neck・dst_cx に来るように置く"""
    s = (dst_neck - dst_top) / float(neck - top)
    big = src.resize((max(1, round(src.width * s)), max(1, round(src.height * s))), Image.LANCZOS)
    out = Image.new("RGBA", size, (0, 0, 0, 0))
    ox, oy = round(dst_cx - cx * s), round(dst_top - top * s)
    out.paste(big, (ox, oy), big)
    return out


def outline(im, width, color):
    """縁取り (絵の形を太らせた部分に色を塗って、下に敷く)"""
    a = im.getchannel("A").point(lambda v: 255 if v > 64 else 0)
    grown = a.filter(ImageFilter.MaxFilter(width * 2 + 1))
    back = Image.new("RGBA", im.size, color)
    back.putalpha(grown)
    back.alpha_composite(im)
    return back


def gray(im, k=1.0, offset=0):
    a = im.getchannel("A")
    g = im.convert("L").point(lambda v: max(0, min(255, int(v * k + offset))))
    out = Image.merge("RGBA", (g, g, g, a))
    return out


# ---- 文字 ----

def text_image(text, font_file, height, fill=(255, 255, 255, 255), italic=0.0, stroke=0, stroke_fill=None, inner=None):
    """文字の絵 (余白なし)。italic は傾き (0.2 くらい)。stroke で縁取り、inner を指定すると中を塗る色"""
    size = height * 2
    font = ImageFont.truetype(font_file, size)
    pad = size
    im = Image.new("RGBA", (int(size * len(text) * 1.2) + pad * 2, size * 2 + pad), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.text((pad, pad // 2), text, font=font, fill=inner if inner is not None else fill,
           stroke_width=stroke * 2, stroke_fill=stroke_fill)
    if italic:
        im = im.transform(im.size, Image.AFFINE, (1, italic, -italic * im.height * 0.5, 0, 1, 0), Image.BICUBIC)
    im = im.crop(im.getchannel("A").getbbox())
    s = height / float(im.height)
    return im.resize((max(1, round(im.width * s)), height), Image.LANCZOS)


def fit(im, w, h):
    s = min(w / float(im.width), h / float(im.height), 1.0)
    return im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))), Image.LANCZOS)


def remove_text(bar):
    """帯の上の白っぽい文字を、帯の赤で塗りつぶす (近くの文字でない色で埋める)"""
    bar = bar.copy()
    px = bar.load()
    w, h = bar.size
    white = lambda p: p[3] > 0 and min(p[:3]) > 150
    mask = [[white(px[x, y]) for x in range(w)] for y in range(h)]
    # 文字のふちのにじみも含めて少し広げる
    grow = [[any(mask[yy][xx] for yy in range(max(0, y - 2), min(h, y + 3)) for xx in range(max(0, x - 2), min(w, x + 3)))
             for x in range(w)] for y in range(h)]
    for y in range(h):
        for x in range(w):
            if not grow[y][x]:
                continue
            for dx in range(1, w):
                for xx in (x - dx, x + dx):
                    if 0 <= xx < w and not grow[y][xx] and px[xx, y][3] > 0:
                        p = px[xx, y]
                        px[x, y] = (p[0], p[1], p[2], px[x, y][3])
                        break
                else:
                    continue
                break
    return bar


# ---- 本体 ----

def main(folder, name_ja, name_en):
    tpl = lambda n: Image.open(os.path.join(folder, "images_template", n + ".png")).convert("RGBA")
    out_dir = os.path.join(folder, "images")
    os.makedirs(out_dir, exist_ok=True)
    save = lambda im, n: im.save(os.path.join(out_dir, n + ".png"))

    bust = brighten(Image.open(os.path.join(folder, "captures", "bust.png")).convert("RGBA"))
    top, neck, cx = head_box(bust)
    print("bust: head top=%d neck=%d cx=%.0f" % (top, neck, cx))

    # 位置の数字は、紅光 (赤悠, 1006) を土台にしたときのお手本 (表示の枠全体) に合わせたもの
    # 立ち絵 (365x1440): 頭のてっぺん y=297、首 y=780、中心 x=190。黒の細い縁取り
    t = tpl("character_s")
    cs = place(bust, t.size, top, neck, cx, 297, 780, 190)
    cs = outline(cs, 2, (20, 20, 22, 255))
    save(cs, "character_s")
    # 灰色の立ち絵
    save(gray(cs, 0.82), "choose_n")

    # 背景の大きな絵 (1200x1440): 灰色で暗め、明るい灰色の太い縁取り
    t = tpl("BGrole")
    bg = place(bust, t.size, top, neck, cx, 97, 640, 663)
    bg = gray(bg, 0.35, 30)
    bg = outline(bg, 9, (105, 106, 105, 255))
    save(bg, "BGrole")

    # 顔のアイコン (210x100、絵があるのは x=35 から右)
    t = tpl("rolechoose")
    rc = place(bust, t.size, top, neck, cx, 3, 64, 93)
    ImageDraw.Draw(rc).rectangle((0, 0, 34, t.height), fill=(0, 0, 0, 0))
    save(outline(rc, 1, (20, 20, 22, 255)), "rolechoose")

    # カード (204x106): 背景は blackBar_h (土台のキャラの顔が無い、同じ赤の帯) を使い、形は blackBar_n に合わせる
    t = tpl("blackBar_n")
    h = tpl("blackBar_h")
    back = h.crop((6, 6, 236, 80)).resize(t.size, Image.LANCZOS)
    back.putalpha(ImageChops.multiply(back.getchannel("A"), t.getchannel("A")))
    face = place(bust, t.size, top, neck, cx, 4, 80, 42)
    face.putalpha(ImageChops.multiply(face.getchannel("A"), t.getchannel("A")))
    back.alpha_composite(outline(face, 1, (25, 15, 15, 255)))
    en = text_image(name_en, EN_FONT, 19, fill=(255, 225, 225, 200), italic=0.2)
    en = fit(en, 120, 19)
    back.alpha_composite(en, (196 - en.width, 60))
    save(back, "blackBar_n")

    # 名前 (449x173、文字があるのは x=27 から右): 白の太字
    t = tpl("name_s")
    nm = fit(text_image(name_ja, JP_FONT, 130, stroke=4, stroke_fill=(255, 255, 255, 255)), 415, 165)
    out = Image.new("RGBA", t.size, (0, 0, 0, 0))
    out.alpha_composite(nm, (27, (t.height - nm.height) // 2))
    save(out, "name_s")

    # 名前の帯 (451x67): 帯の文字を消して、白の縁取りの斜体で書き直す
    t = tpl("NameBar")
    bb = t.convert("L").point(lambda v: 255 if v > 200 else 0).getbbox() or (60, 8, 400, 58)
    bar = remove_text(t)
    en = text_image(name_en, EN_FONT, bb[3] - bb[1], fill=(255, 255, 255, 255), italic=0.2,
                    stroke=1, stroke_fill=(255, 255, 255, 255), inner=(150, 66, 71, 255))
    en = fit(en, 451 - bb[0] - 10, bb[3] - bb[1])
    bar.alpha_composite(en, (bb[0], bb[1]))
    save(bar, "NameBar")

    # 英語の名前 (206x30): 白の斜体、右寄せ
    t = tpl("roleName_s")
    en = fit(text_image(name_en, EN_FONT, 23, italic=0.2), 200, 23)
    out = Image.new("RGBA", t.size, (0, 0, 0, 0))
    out.alpha_composite(en, (203 - en.width, 3))
    save(out, "roleName_s")

    result_art(folder, name_en, bust, (top, neck, cx), tpl, save)
    print("書き出しました:", out_dir)


# ---- リザルト画面・会話の絵

def side_box(im, neck_px):
    """横顔の位置: 上端 (髪のてっぺん)・鼻先の x (頭の下半分で一番前の点)"""
    a = im.getchannel("A")
    w, h = im.size
    px = a.load()
    top = a.getbbox()[1]
    front = 0
    for y in range(top + int(neck_px * 0.55), top + int(neck_px * 0.80)):
        xs = [x for x in range(w) if px[x, y] > 128]
        if xs:
            front = max(front, xs[-1])
    return top, front


def place_side(src, size, top, front, scale, dst_top, dst_front):
    big = src.resize((max(1, round(src.width * scale)), max(1, round(src.height * scale))), Image.LANCZOS)
    out = Image.new("RGBA", size, (0, 0, 0, 0))
    out.paste(big, (round(dst_front - front * scale), round(dst_top - top * scale)), big)
    return out


def template_head(t):
    """お手本の、キャラの絵の頭の位置 (上端・首・中心)。お手本の透明でない部分がキャラだけの絵に使う"""
    return head_box(t)


def fill_hole(im, hole):
    """hole (L、255 = 消す) の部分を、周りの色をぼかして埋める (正規化した畳み込み)"""
    keep = ImageOps.invert(hole)
    rgb = im.convert("RGB")
    num = ImageChops.multiply(rgb, Image.merge("RGB", (keep, keep, keep))).filter(ImageFilter.GaussianBlur(60))
    den = keep.filter(ImageFilter.GaussianBlur(60))
    num_px, den_px = num.load(), den.load()
    out = rgb.copy()
    o = out.load()
    hp = hole.load()
    for y in range(out.height):
        for x in range(out.width):
            if hp[x, y] > 0:
                d = max(1, den_px[x, y])
                c = num_px[x, y]
                o[x, y] = tuple(min(255, int(v * 255 / d)) for v in c)
    res = out.convert("RGBA")
    res.putalpha(im.getchannel("A"))
    return res


def result_art(folder, name_en, bust, bust_head, tpl, save):
    side_file = os.path.join(folder, "captures", "side.png")
    if not os.path.exists(side_file):
        print("captures/side.png が無いので、リザルト画面の絵は作りません (Capture で撮り直してください)")
        return
    side = brighten(Image.open(side_file).convert("RGBA"))
    top, neck, cx = bust_head
    s_top, s_front = side_box(side, neck - top)
    # 横顔 (888x1440): 頭のてっぺん y=194、首 y=1000 くらい、鼻先 x=500 (土台 1006 のお手本に合わせた数字)
    scale = (1000 - 194) / float(neck - top)
    t = tpl("Profile")
    prof = place_side(side, t.size, s_top, s_front, scale, 194, 500)
    # 色: 灰色に寄せ、少し赤みのある中間の明るさ (お手本の平均くらい)
    g = prof.convert("L")
    tint = Image.merge("RGB", (g.point(lambda v: min(255, int(v * 0.62 + 30))), g.point(lambda v: min(255, int(v * 0.57 + 28))),
                               g.point(lambda v: min(255, int(v * 0.56 + 27)))))
    prof_c = tint.convert("RGBA")
    prof_c.putalpha(prof.getchannel("A"))
    save(prof_c, "Profile")

    # ショップ・ルート (WAVE) の画面の横顔 (920x1440): Profile と同じ大きさで、右に 158・上に 5 ずらした位置。色はそのまま
    if os.path.exists(os.path.join(folder, "images_template", "Role.png")):
        t = tpl("Role")
        save(place_side(side, t.size, s_top, s_front, scale, 189, 658), "Role")
    else:
        print("images_template/Role.png が無いので Role は作りません (ゲームでショップ・ルートの画面を開くと書き出されます)")

    # 横顔の形の切り抜き (1360x1440): 黒で、横顔の形だけ透明
    t = tpl("leftNameMask")
    mask = Image.new("RGBA", t.size, (0, 0, 0, 255))
    a = Image.new("L", t.size, 0)
    a.paste(prof.getchannel("A"), (0, 0))
    mask.putalpha(ImageOps.invert(a))
    save(mask, "leftNameMask")

    # リザルト画面の左 (888x1440): お手本から土台のキャラ (Profile の形) を消して周りの色で埋め、横顔を重ねる
    t = tpl("leftFrame")
    old = tpl("Profile").getchannel("A").point(lambda v: 255 if v > 8 else 0).filter(ImageFilter.MaxFilter(9))
    frame = fill_hole(t, old)
    pc = prof_c.copy()
    pc.putalpha(ImageChops.multiply(pc.getchannel("A"), t.getchannel("A")))
    frame.alpha_composite(pc)
    save(frame, "leftFrame")

    # 名前を縦に重ねた文字 (888x1440): 白抜き (透明度 89) と塗り (38) を交互に 8 段
    t = tpl("leftName")
    out = Image.new("RGBA", t.size, (0, 0, 0, 0))
    for i, y in enumerate((96, 250, 410, 560, 720, 880, 1040, 1190)):
        if i % 2 == 0:
            row = text_image(name_en, EN_FONT, 128, fill=(255, 255, 255, 0), stroke=1, stroke_fill=(255, 255, 255, 89),
                             inner=(255, 255, 255, 0))
        else:
            row = text_image(name_en, EN_FONT, 128, fill=(255, 255, 255, 38))
        out.alpha_composite(row, (0, y))
    save(out, "leftName")

    # 暗いバストアップ (1645x1440) と会話の顔 (256x256): お手本の頭の位置に合わせる
    for kind, dark in (("buffResuiltProfile", 0.32), ("dialogBox", None)):
        t = tpl(kind)
        # 会話の顔 (バストアップだけの小さな絵) は、自動の位置 (全身の割合から首を探す) が合わないので数字で
        tt, tn, tc = (60, 160, 182) if kind == "dialogBox" else template_head(t)
        img = place(bust, t.size, top, neck, cx, tt, tn, tc)
        img.putalpha(ImageChops.multiply(img.getchannel("A"), t.getchannel("A").point(lambda v: 255 if v > 0 else 0).filter(ImageFilter.MaxFilter(15))) if kind == "dialogBox" else img.getchannel("A"))
        if dark is not None:
            rgb = ImageEnhance.Color(img.convert("RGB")).enhance(0.6).point(lambda v: int(v * dark))
            d = rgb.convert("RGBA")
            d.putalpha(img.getchannel("A"))
            img = d
        else:
            img = outline(img, 1, (20, 20, 22, 255))
        save(img, kind)


if __name__ == "__main__":
    if len(sys.argv) < 4:
        print(__doc__)
        sys.exit(1)
    main(sys.argv[1], sys.argv[2], sys.argv[3])
