#!/usr/bin/env python3
"""Unity を開かずに、ワークツリーの C# がコンパイルできるかだけを確かめる。

本体のチェックアウトにある Unity 生成の Assembly-CSharp.csproj / Assembly-CSharp-Editor.csproj から
参照(HintPath)・DefineConstants・LangVersion を読み、Unity 同梱の Roslyn で
ワークツリーの Assets/**/*.cs をコンパイルする(ランタイム → Editor の順)。
動き(プレイ)の確認にはならない。

使い方(ワークツリーの最上位で):
    python3 -I Tools/compile_check.py            # ランタイムと Editor
    python3 -I Tools/compile_check.py --runtime  # ランタイムだけ
終了コード 0 ならエラーなし。
"""
import os
import re
import subprocess
import sys
import tempfile

UNITY = "/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/Resources/Scripting"
DOTNET = f"{UNITY}/NetCoreRuntime/dotnet"
CSC = f"{UNITY}/DotNetSdkRoslyn/csc.dll"


def git(cwd, *args):
    return subprocess.run(["git", "-C", cwd, *args], capture_output=True, text=True, check=True).stdout.strip()


def read_csproj(path):
    text = open(path, encoding="utf-8").read()
    base = os.path.dirname(path)
    refs = [r if os.path.isabs(r) else os.path.join(base, r)
            for r in re.findall(r"<HintPath>(.*?)</HintPath>", text)]
    # パッケージを上げた直後など、csproj が古くて消えた DLL を指していることがある。無いものは飛ばす
    missing = [r for r in refs if not os.path.exists(r)]
    if missing:
        print(f"  ({os.path.basename(path)} の参照のうち {len(missing)} 個が見つからないので飛ばした。"
              "Unity でプロジェクトを開き直すと csproj が作り直される)")
        refs = [r for r in refs if os.path.exists(r)]
    defines = re.search(r"<DefineConstants>(.*?)</DefineConstants>", text)
    lang = re.search(r"<LangVersion>(.*?)</LangVersion>", text)
    return refs, (defines.group(1) if defines else ""), (lang.group(1) if lang else "9.0")


def is_editor(rel):
    return "/Editor/" in f"/{rel}"


def compile_one(name, sources, refs, defines, lang, out, tmp):
    rsp = os.path.join(tmp, f"{name}.rsp")
    with open(rsp, "w", encoding="utf-8") as f:
        f.write("-target:library\n-nostdlib\n-noconfig\n-nologo\n-nowarn:CS0169,CS0414,CS0649,CS0618\n")
        f.write(f"-langversion:{lang}\n-out:\"{out}\"\n")
        if defines:
            f.write(f"-define:{defines}\n")
        for r in refs:
            f.write(f"-r:\"{r}\"\n")
        for s in sources:
            f.write(f"\"{s}\"\n")
    r = subprocess.run([DOTNET, CSC, f"@{rsp}"], capture_output=True, text=True)
    out_lines = (r.stdout + r.stderr).splitlines()
    errors = [l for l in out_lines if "error CS" in l]
    if r.returncode != 0 and not errors:
        errors = out_lines[-20:]
    print(f"[{name}] {len(sources)} 本, エラー {len(errors)}")
    for l in errors:
        print("  " + l)
    return r.returncode == 0 and not errors


def main():
    runtime_only = "--runtime" in sys.argv
    top = git(".", "rev-parse", "--show-toplevel")
    main_root = os.path.dirname(git(".", "rev-parse", "--path-format=absolute", "--git-common-dir"))
    rt_proj = os.path.join(main_root, "Assembly-CSharp.csproj")
    ed_proj = os.path.join(main_root, "Assembly-CSharp-Editor.csproj")
    if not os.path.exists(rt_proj):
        sys.exit(f"{rt_proj} がない。本体の Unity で一度プロジェクトを開いて csproj を作る必要がある。")
    if not os.path.exists(CSC):
        sys.exit(f"{CSC} がない(Unity のバージョンが違う?)。")

    rt, ed = [], []
    for d, _, files in os.walk(os.path.join(top, "Assets")):
        for fn in files:
            if fn.endswith(".cs"):
                p = os.path.join(d, fn)
                (ed if is_editor(os.path.relpath(p, top)) else rt).append(p)

    with tempfile.TemporaryDirectory() as tmp:
        refs, defines, lang = read_csproj(rt_proj)
        refs = [r for r in refs if not r.endswith("Assembly-CSharp.dll")]
        rt_out = os.path.join(tmp, "Assembly-CSharp.dll")
        ok = compile_one("runtime", rt, refs, defines, lang, rt_out, tmp)
        if ok and not runtime_only and ed and os.path.exists(ed_proj):
            refs, defines, lang = read_csproj(ed_proj)
            refs = [r for r in refs if not r.endswith("Assembly-CSharp.dll")] + [rt_out]
            ok = compile_one("editor", ed, refs, defines, lang, os.path.join(tmp, "Editor.dll"), tmp)
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
