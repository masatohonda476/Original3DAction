#!/usr/bin/env python3
"""Claude Code の会話ログ(.jsonl)から、作者が打ち込んだ発言だけを抜き出して出す。

会話ログは数十 MB あるので、進捗表の更新(progress-keeper)などで作者の頼みごとを拾うときはこれを通す。
作者が選択肢(AskUserQuestion)で答えた決定も「[選択]」の印を付けて出す。
窓口役が開いた会話の依頼文(【窓口役からの依頼】で始まる)は「[窓口役]」の印を付ける(作者の発言ではない)。

使い方:
    python3 -I Tools/author_lines.py <会話ログ.jsonl> [--tail 40] [--width 300]
"""
import argparse
import json
import re
import sys

SKIP_PREFIXES = ("<command-", "<local-command", "<task-notification", "[Request interrupted",
                 "Caveat:", "Tool loaded.", "<bash-",
                 "This session is being continued from a previous conversation")
STRIP_TAGS = re.compile(r"<(system-reminder|command-[a-z-]+|local-command-[a-z-]+)>.*?</\1>", re.S)
REQUEST_PREFIX = "【窓口役からの依頼】"
ANSWER_PAIR = re.compile(r'"((?:[^"\\]|\\.)*)"="((?:[^"\\]|\\.)*)"')


def user_text(entry):
    """作者が打ち込んだ発言なら本文を返す。そうでなければ None。"""
    if entry.get("isSidechain"):
        return None
    att = entry.get("attachment")
    if entry.get("type") == "attachment" and isinstance(att, dict):
        if att.get("type") == "queued_command" and (att.get("origin") or {}).get("kind") == "human":
            text = STRIP_TAGS.sub("", str(att.get("prompt") or "")).strip()
            return text if text and not text.startswith(SKIP_PREFIXES) else None
        return None
    if entry.get("type") != "user" or entry.get("isMeta"):
        return None
    if entry.get("isCompactSummary") or entry.get("isVisibleInTranscriptOnly"):
        return None
    content = (entry.get("message") or {}).get("content")
    parts = []
    if isinstance(content, str):
        parts.append(content)
    elif isinstance(content, list):
        if any(c.get("type") == "tool_result" for c in content):
            return None
        for c in content:
            if c.get("type") == "text":
                parts.append(c.get("text", ""))
            elif c.get("type") == "image":
                parts.append("[画像を添付]")
    text = STRIP_TAGS.sub("", "\n".join(parts)).strip()
    if not text or text.startswith(SKIP_PREFIXES):
        return None
    return text


def answers(entry):
    """AskUserQuestion への作者の答えを「問い → 答え」の文字列のリストで返す。"""
    if entry.get("type") != "user" or entry.get("isSidechain"):
        return []
    content = (entry.get("message") or {}).get("content")
    if not isinstance(content, list):
        return []
    out = []
    for c in content:
        if c.get("type") != "tool_result":
            continue
        body = c.get("content")
        if isinstance(body, list):
            body = "\n".join(b.get("text", "") for b in body if isinstance(b, dict))
        body = str(body or "")
        if "answered" not in body and "answers" not in body:
            continue
        out += [f"{q} → {a}" for q, a in ANSWER_PAIR.findall(body)]
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("jsonl")
    ap.add_argument("--tail", type=int, default=40, help="最後の何件を出すか(0 で全部)")
    ap.add_argument("--width", type=int, default=300, help="1 件の最大文字数")
    a = ap.parse_args()

    lines = []
    with open(a.jsonl, encoding="utf-8") as f:
        for raw in f:
            try:
                e = json.loads(raw)
            except Exception:
                continue
            t = user_text(e)
            if t:
                mark = "[窓口役] " if t.startswith(REQUEST_PREFIX) else ""
                lines.append(f"- {mark}{t[:a.width]}")
            for qa in answers(e):
                lines.append(f"- [選択] {qa[:a.width]}")
    for l in (lines[-a.tail:] if a.tail else lines):
        print(l)


if __name__ == "__main__":
    sys.exit(main())
