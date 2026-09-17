#!/usr/bin/env python3
"""从 steam-description.md 生成 workshop.json 的标题和各语言正文。

创意工坊页面的正文是 BBCode，不是 markdown。steam-description.md 里两个语言的正文段落
已经按 BBCode 写好，这个脚本只负责把它们原样搬进 JSON 字符串，避免手工复制时两边走样。

    python publish/build-workshop-json.py

`english` 同时写进顶层 description，作为其他语言的回退。
依赖项（创意工坊 item id）、可见性和 changeNote 保留 workshop.json 里的原有值，不被覆盖。
"""

import json
import pathlib
import re

HERE = pathlib.Path(__file__).parent
DESC = HERE / "steam-description.md"
OUT = HERE / "workshop.json"

# 小节标题 -> Steam 语言代码。Steam 用 schinese / english 这套名字，不是 zh-CN / en-US。
LANGUAGES = {
    "简体中文": "schinese",
    "English": "english",
}
FALLBACK = "english"

DEFAULTS = {
    "title": "平衡尖塔适配 | AutoRebalancedSpire",
    "visibility": "private",
    "changeNote": "首次发布。",
    "tags": [],
    # 创意工坊 item id，不是 mod id。
    #   3747602295 = RitsuLib
    #   3747498062 = 平衡尖塔（ty / Oroboro）
    #   3790899961 = 自动战斗求解器
    "dependencies": [3747602295, 3747498062, 3790899961],
    "minBranch": None,
    "maxBranch": None,
}

KEY_ORDER = ("title", "description", "localizations", "visibility", "changeNote",
             "tags", "dependencies", "minBranch", "maxBranch")


def main() -> None:
    text = DESC.read_text(encoding="utf-8")

    title = re.search(r"## 标题\s*\n+```\n(.+?)\n```", text, re.S)
    if title is None:
        raise SystemExit("steam-description.md 里找不到「## 标题」下的代码块。")
    title = title.group(1).strip()

    bodies = {}
    for name, code in LANGUAGES.items():
        section = re.search(
            rf"## 正文 · {re.escape(name)}\s*\n+---\n(.*?)(?=\n## |\Z)", text, re.S)
        if section is None:
            raise SystemExit(f"steam-description.md 里找不到「## 正文 · {name}」一节。")
        body = section.group(1).strip()
        if "**" in body:
            raise SystemExit(f"{name} 正文里还有 markdown 的 **粗体**，创意工坊只认 [b][/b]。")
        bodies[code] = body

    existing = json.loads(OUT.read_text(encoding="utf-8")) if OUT.exists() else {}
    data = {**DEFAULTS, **existing}
    data["title"] = title
    data["description"] = bodies[FALLBACK]
    data["localizations"] = {
        code: {"title": title, "description": body} for code, body in bodies.items()
    }

    ordered = {k: data[k] for k in KEY_ORDER}
    OUT.write_text(json.dumps(ordered, ensure_ascii=False, indent=2) + "\n",
                   encoding="utf-8", newline="\n")
    print(f"写好 {OUT.name}：标题「{title}」，" +
          "，".join(f"{code} {len(b)} 字符" for code, b in bodies.items()))


if __name__ == "__main__":
    main()
