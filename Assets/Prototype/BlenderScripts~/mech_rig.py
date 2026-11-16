# メカ1v3 のリグとアニメーションを作るスクリプト(IK版)。
# 使い方: Blender -b "~/Documents/Blender/メカ1v3_腰修正版.blend" --python mech_rig.py -- --export
# 元の.blendは変更せず、メカ1v3_リグ.blend を別名保存し、Unity用FBX(Mech1v3_Rig.fbx)を書き出す。
# 書き出し後、Unityのメニュー Prototype > HuntRelease > シーンを組み立てる で反映。
#
# 精度のための仕組み
#  - 足:かかと・つま先の接地点を目標にした数値IK。接地中の足は地面に固定され、滑らない
#    (歩幅はゲームの walkSpeed × stepTime に合わせる)。かかと着地とつま先離地の回転つき
#  - 右手:握り位置・刃の向き・刃筋(刃の向いている側)を目標にしたIK。斬撃は剣の軌道を弧で指定する
#  - 指:手を握る(右は剣を握る拳、左は軽く握る)
#  - 剣の握りの位置(R_武器)・切っ先の向き(R_剣先)・刃の向き(R_剣刃)をボーンで書き出し、Unity側で剣を合わせる
import bpy, math, sys, os
import numpy as np
from mathutils import Vector, Matrix, Quaternion

OUT_BLEND = "/Users/masato/Documents/Blender/メカ1v3_リグ.blend"
OUT_FBX = "/Users/masato/Documents/Unity/Projects/Original3DAction/Assets/Prototype/Models/Mech1v3_Rig.fbx"
RENDER_DIR = "/tmp/mech_rig_poses/"  # 確認用レンダリングの出力先
DO_EXPORT = "--export" in sys.argv
NO_RENDER = "--no-render" in sys.argv

# ゲーム側の値(ProtoPlayer)と揃える
STEP_LEN = 7.0 * 0.55          # walkSpeed × stepTime = 1歩で進む距離
SWORD_LEN, SWORD_GRIP = 6.65, 5.15   # 剣の全長 / 握りから切っ先まで

scene = bpy.context.scene
scene.render.fps = 30
vl = bpy.context.view_layer

hidden = {c.name for c in bpy.data.collections if c.hide_render or c.hide_viewport}
def ok(o): return o.visible_get() and not any(c.name in hidden for c in o.users_collection)
def desc(o):
    yield o
    for c in o.children: yield from desc(c)

root = bpy.data.objects["メカ1.0_CTRL_00_全体移動.001"]
def is_joint(o): return o.type == 'EMPTY' and ok(o) and '指試作' not in o.name
def short(o):
    if o == root: return "Root"
    return o.name.replace('.001', '').replace('メカ1.0_CTRL_', '').replace('_CTRL', '')

all_desc = list(desc(root))
joints = [o for o in all_desc if is_joint(o)]
empties = [o for o in all_desc if o.type == 'EMPTY']
meshes = [o for o in all_desc if o.type == 'MESH' and ok(o)]
def joint_parent(o):
    p = o.parent
    while p is not None and not is_joint(p): p = p.parent
    return p
owner = joint_parent

# ───────── アーマチュア:ボーンの向き=親の座標系(=今のEmptyの回転軸がそのままボーンのローカル軸になる) ─────────
arm_data = bpy.data.armatures.new("MechRig")
arm = bpy.data.objects.new("MechRig", arm_data)
scene.collection.objects.link(arm)
bpy.ops.object.select_all(action='DESELECT')
vl.objects.active = arm
arm.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
ebs = {}
for j in joints:
    eb = arm_data.edit_bones.new(short(j))
    eb.head = (0, 0, 0); eb.tail = (0, 0.3, 0)
    jp = j.parent
    rot = jp.matrix_world.to_quaternion() if jp is not None else Quaternion()
    eb.matrix = Matrix.LocRotScale(j.matrix_world.translation.copy(), rot, None)
    ebs[j] = eb
for j in joints:
    p = joint_parent(j)
    if p is not None: ebs[j].parent = ebs[p]
bpy.ops.object.mode_set(mode='OBJECT')

P = arm.pose.bones
for j in joints:
    pb = P[short(j)]
    pb.rotation_mode = 'XYZ'
    pb.lock_rotation = tuple(j.lock_rotation)
# 右肘は左肘と軸が食い違っていて曲がらないため、左に揃えてZ軸で曲げる
P["R_肘曲げ_Y"].lock_rotation = (True, True, False)

# ───────── スキン:各部品を所属関節のボーンにウェイト1で追従 ─────────
for o in meshes:
    if o.data.users > 1: o.data = o.data.copy()
for o in meshes:
    vg = o.vertex_groups.new(name=short(owner(o)))
    vg.add(list(range(len(o.data.vertices))), 1.0, 'REPLACE')
for o in meshes:
    mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
bpy.ops.object.select_all(action='DESELECT')
for o in meshes: o.select_set(True)
vl.objects.active = meshes[0]
bpy.ops.object.join()
body = vl.objects.active
body.name = "MechBody"
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
body.parent = arm
body.matrix_parent_inverse = Matrix()
mod = body.modifiers.new("Armature", 'ARMATURE')
mod.object = arm
for e in empties:
    if e.name in bpy.data.objects: bpy.data.objects.remove(e, do_unlink=True)
print("RIG bones", len(arm_data.bones), "body verts", len(body.data.vertices), "vgroups", len(body.vertex_groups))
mod.show_viewport = False   # IKを解く間はメッシュ変形を止めて速くする

# ───────── ポーズ道具:符号は実際に回して確かめる(機体の前=+X、左=+Y、上=+Z) ─────────
def reset():
    for pb in P:
        pb.rotation_euler = (0, 0, 0); pb.location = (0, 0, 0)
def wpt(bn, local): return P[bn].matrix @ local
def plocal(bn, world_pt): return P[bn].bone.matrix_local.inverted() @ world_pt
def dlocal(bn, world_dir): return P[bn].bone.matrix_local.to_3x3().inverted() @ Vector(world_dir)
def head_rest(bn): return P[bn].bone.head_local.copy()
def free_axis(bn): return [i for i in range(3) if not P[bn].lock_rotation[i]][0]

SEM = {}
def calib(key, bn, probe, offset, desired, axis=None):
    ax = free_axis(bn) if axis is None else axis
    pl = plocal(probe, head_rest(probe) + Vector(offset))
    vl.update(); a = wpt(probe, pl)
    old = P[bn].rotation_euler[ax]
    P[bn].rotation_euler[ax] = old + 0.2; vl.update(); b = wpt(probe, pl)
    P[bn].rotation_euler[ax] = old; vl.update()
    SEM[key] = (bn, ax, 1 if (b - a).dot(Vector(desired)) > 0 else -1)

def setp(key, deg):
    bn, ax, s = SEM[key]
    P[bn].rotation_euler[ax] = math.radians(deg) * s

reset()
for S, sy in (("L", 1), ("R", -1)):
    calib(f"arm_lower_{S}", f"{S}_肩開閉_X", f"{S}手_全体", (0, 0, 0), (0, 0, -1))
    calib(f"hip_fwd_{S}", f"{S}_股関節前後_Y", f"{S}_足首前後_Y", (0, 0, 0), (1, 0, 0))
    calib(f"knee_{S}", f"{S}_膝_Y", f"{S}_足首前後_Y", (0, 0, 0), (-1, 0, 0))
    calib(f"toe_up_{S}", f"{S}_足首前後_Y", f"{S}_足首前後_Y", (1, 0, 0), (0, 0, 1))
calib("chest_left", "胸部_腰ひねり_Z", "胸部_腰ひねり_Z", (2, 0, 0), (0, 1, 0))
calib("neck_left", "頭部_首ひねり_Z", "頭部_首ひねり_Z", (2, 0, 0), (0, 1, 0))
calib("pelvis_left", "骨盤", "骨盤", (2, 0, 0), (0, 1, 0), axis=2)
calib("pelvis_fwd", "骨盤", "骨盤", (0, 0, 2), (1, 0, 0), axis=1)
calib("pelvis_rollL", "骨盤", "骨盤", (0, 0, 2), (0, 1, 0), axis=0)
setp("arm_lower_L", 75); setp("arm_lower_R", 75); vl.update()
for S in ("L", "R"):
    calib(f"arm_fwd_{S}", f"{S}_肩前後_Y", f"{S}手_全体", (0, 0, 0), (1, 0, 0))
    calib(f"elbow_{S}", f"{S}_肘曲げ_Y", f"{S}手_全体", (0, 0, 0), (1, 0, 0))
reset()

# 指:指先が手のひら側(T字では下)へ動く向きを「曲げ」とする
FINGERS = ("親指", "人差指", "中指", "薬指", "小指")
CURL_J = ("付け根曲げ_X", "第二関節曲げ_X", "第一関節曲げ_X")
for S, sy in (("L", 1), ("R", -1)):
    for fn in FINGERS:
        for cj in CURL_J:
            bn = f"{S}手_{fn}_{cj}"
            if bn in P: calib(f"curl_{S}_{fn}_{cj}", bn, bn, (0, sy * 0.15, 0), (0, 0, -1))
FIST = {"指": (80, 95, 70), "親指": (30, 40, 45)}
RELAX = {"指": (30, 40, 25), "親指": (10, 15, 15)}
def curl(S, shape):
    for fn in FINGERS:
        vals = shape["親指" if fn == "親指" else "指"]
        for cj, v in zip(CURL_J, vals):
            k = f"curl_{S}_{fn}_{cj}"
            if k in SEM: setp(k, v)
print("SEM", {k: (v[1], v[2]) for k, v in SEM.items() if not k.startswith("curl")})

# ───────── 剣の握り:拳の中心を握り位置にし、剣の向きをボーンで書き出す ─────────
# 拳から刃が出る向き=親指側(T字で前 +X)、刃筋=拳の正面(T字で腕の伸びる向き)
curl("R", FIST); vl.update()
pts = []
for fn in ("人差指", "中指", "薬指", "小指"):
    a = P[f"R手_{fn}_付け根曲げ_X"].head; b = P[f"R手_{fn}_第二関節曲げ_X"].head; c = P[f"R手_{fn}_第一関節曲げ_X"].head
    pts += [a, b, c, c + (c - b).normalized() * 0.13]
GRIP_REST = sum(pts, Vector()) / len(pts)
BLADE_REST, EDGE_REST = Vector((1, 0, 0)), Vector((0, -1, 0))
reset()
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True); vl.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
E = arm_data.edit_bones
def add_bone(name, loc, parent):
    eb = E.new(name); eb.head = (0, 0, 0); eb.tail = (0, 0.3, 0)
    eb.matrix = Matrix.LocRotScale(loc, Quaternion(), None)
    eb.parent = parent; eb.use_deform = False
    return eb
wb = add_bone("R_武器", GRIP_REST, E["R手_全体"])
add_bone("R_剣先", GRIP_REST + BLADE_REST * SWORD_GRIP, wb)
add_bone("R_剣刃", GRIP_REST + EDGE_REST * 0.5, wb)
bpy.ops.object.mode_set(mode='OBJECT')
P = arm.pose.bones
for n in ("R_武器", "R_剣先", "R_剣刃"): P[n].rotation_mode = 'XYZ'
print("GRIP", tuple(round(x, 3) for x in GRIP_REST))

# ───────── 数値IK(減衰最小二乗)。左脚・右脚・右腕は互いに独立なので同時に微分をとる ─────────
class Chain:
    def __init__(self, params, resid, reg, lims=None):
        self.params, self.resid, self.reg, self.lims = params, resid, reg, lims or {}
    def full(self):
        r = list(self.resid())
        for i, (bn, ax) in enumerate(self.params):
            v = P[bn].rotation_euler[ax]
            r.append(self.reg[i] * v)
        for i, (s, lo, hi) in self.lims.items():
            bn, ax = self.params[i]
            d = math.degrees(P[bn].rotation_euler[ax] * s)
            r.append(0.05 * (min(0.0, d - lo) + max(0.0, d - hi)))
        return np.array(r)

def solve(chains, iters=40, eps=1e-3, lam=1e-3):
    for it in range(iters):
        vl.update()
        r0 = [c.full() for c in chains]
        n = max(len(c.params) for c in chains)
        J = [np.zeros((len(r0[k]), len(c.params))) for k, c in enumerate(chains)]
        for i in range(n):
            for c in chains:
                if i < len(c.params):
                    bn, ax = c.params[i]; P[bn].rotation_euler[ax] += eps
            vl.update()
            for k, c in enumerate(chains):
                if i < len(c.params):
                    J[k][:, i] = (c.full() - r0[k]) / eps
                    bn, ax = c.params[i]; P[bn].rotation_euler[ax] -= eps
        biggest = 0.0
        for k, c in enumerate(chains):
            A, r = J[k], r0[k]
            d = np.linalg.solve(A.T @ A + lam * np.eye(A.shape[1]), -A.T @ r)
            m = float(np.max(np.abs(d)))
            if m > 0.25: d *= 0.25 / m
            biggest = max(biggest, m)
            for (bn, ax), dv in zip(c.params, d): P[bn].rotation_euler[ax] += float(dv)
        if biggest < 2e-4: break
    vl.update()
    return [float(np.linalg.norm(c.resid())) for c in chains]

# 足の接地点(足首ボーンのローカル)
G = -0.033
ANK = {S: f"{S}_足首前後_Y" for S in "LR"}
FOOT_Y = {S: head_rest(ANK[S]).y for S in "LR"}
HEEL_X, TOE_X = -0.73, 2.48
FL = TOE_X - HEEL_X
HEEL_L = {S: plocal(ANK[S], Vector((HEEL_X, FOOT_Y[S], G))) for S in "LR"}
TOE_L = {S: plocal(ANK[S], Vector((TOE_X, FOOT_Y[S], G))) for S in "LR"}
SIDE_L = {S: plocal(ANK[S], Vector((HEEL_X, FOOT_Y[S] + (0.45 if S == "L" else -0.45), G))) for S in "LR"}
A_OFS = head_rest(ANK["L"]) - Vector((HEEL_X, FOOT_Y["L"], G))     # かかと→足首
HIP = {S: f"{S}_股関節前後_Y" for S in "LR"}
LEG_LEN = (head_rest(HIP["L"]) - head_rest(ANK["L"])).length

LEG_LIMITS = False
def leg_chain(S, Ht, Tt):
    ank = ANK[S]
    names = (f"{S}_股関節開脚_X", f"{S}_股関節前後_Y", f"{S}_股関節ひねり_Z", f"{S}_膝_Y", f"{S}_足首傾斜_X", ank)
    params = [(bn, free_axis(bn)) for bn in names]
    def resid():
        M = P[ank].matrix
        H, T, Sd = M @ HEEL_L[S], M @ TOE_L[S], M @ SIDE_L[S]
        return [*(H - Ht), *(T - Tt), Sd.z - H.z]
    # 股関節の開き・ひねりと足首の傾きには動ける範囲を付ける(届かない目標で脚がねじれて崩れるのを防ぐ)
    lims = {3: (SEM[f"knee_{S}"][2], 4, 130)}
    if LEG_LIMITS:   # 走りだけ:届かない目標で脚がねじれて崩れるのを防ぐ(踏み込みの斬撃は大きくひねるので付けない)
        lims.update({0: (1, -30, 30), 2: (1, -35, 35), 4: (1, -25, 25)})
    return Chain(params, resid, [0.03, 0.0, 0.05, 0.0, 0.05, 0.0], lims)

# 左手の握り:右手と左右対称(拳の中心、刃は親指側 +X、刃筋は拳の正面 +Y)
def mir(v): return Vector((v.x, -v.y, v.z))
GRIP_REST_L = mir(GRIP_REST)
HANDS = {"R": "R手_全体", "L": "L手_全体"}
HAND = HANDS["R"]
ARM_LOCAL = {
    "R": (plocal("R手_全体", GRIP_REST), dlocal("R手_全体", BLADE_REST), dlocal("R手_全体", EDGE_REST)),
    "L": (plocal("L手_全体", GRIP_REST_L), dlocal("L手_全体", BLADE_REST), dlocal("L手_全体", mir(EDGE_REST))),
}
def arm_chain(Gt, Bt, Et, S="R"):
    hand = HANDS[S]
    gl, bl, el = ARM_LOCAL[S]
    params = [(bn, free_axis(bn)) for bn in (f"{S}_肩前後_Y", f"{S}_肩開閉_X", f"{S}_上腕ひねり_Z", f"{S}_肘曲げ_Y", f"{S}_手首ひねり_Z")]
    params += [(hand, 0), (hand, 1), (hand, 2)]
    def resid():
        M = P[hand].matrix; R3 = M.to_3x3()
        return [*(M @ gl - Gt), *((R3 @ bl - Bt) * 1.0), *((R3 @ el - Et) * 0.5)]
    lims = {3: (SEM[f"elbow_{S}"][2], 3, 145), 4: (1, -90, 90), 5: (1, -80, 80), 6: (1, -80, 80), 7: (1, -80, 80)}
    return Chain(params, resid, [0.01, 0.01, 0.02, 0.0, 0.03, 0.05, 0.05, 0.05], lims)

def hand_frame(S):
    """今のポーズでの、その手の握り位置・刃の向き・刃筋(ワールド)。"""
    M = P[HANDS[S]].matrix; R3 = M.to_3x3()
    gl, bl, el = ARM_LOCAL[S]
    return M @ gl, R3 @ bl, R3 @ el

def foot_targets(hx, hy, yaw, pitch, lift=0.0, mid=None):
    """足の目標点(かかと・つま先)。pitch>0 はかかと支点でつま先が上がる、<0 はつま先支点でかかとが上がる。
    (hx,hy) は足を平らに置いたときのかかと位置。mid を渡すと、その点を足の中央にする(空中の足)。"""
    yr, pr = math.radians(yaw), math.radians(pitch)
    f = Vector((math.cos(yr), math.sin(yr), 0))
    d = f * math.cos(pr) + Vector((0, 0, math.sin(pr)))
    if mid is not None:
        H = mid - d * FL / 2
    elif pitch >= 0:
        H = Vector((hx, hy, G))
    else:
        H = Vector((hx, hy, G)) + f * FL - d * FL
    H = H + Vector((0, 0, lift))
    return H, H + d * FL

def ankle_from(H, T):
    f = (T - H).normalized()
    side = Vector((0, 0, 1)).cross(f).normalized()
    up = f.cross(side)
    return H + f * A_OFS.x + up * A_OFS.z

def chest_M():
    pb = P["胸部_腰ひねり_Z"]
    return pb.matrix @ pb.bone.matrix_local.inverted()

def ortho(b, e):
    b = Vector(b).normalized(); e = Vector(e)
    return b, (e - b * e.dot(b)).normalized()

STATE = {}
def body_pose(sem, pel=(0, 0, 0), curlL=RELAX, chains_keep=()):
    reset()
    for k, v in sem.items(): setp(k, v)
    pb = P["骨盤"]
    pb.location = pb.bone.matrix_local.to_3x3().inverted() @ Vector(pel)
    curl("L", curlL); curl("R", FIST)
    for (bn, ax), v in STATE.items(): P[bn].rotation_euler[ax] = v
    vl.update()

def reach_drop(feet, slack=0.985):
    """脚が伸びきらないように骨盤を下げる量。"""
    drop = 0.0
    for S, (H, T) in feet.items():
        hip, ank = P[HIP[S]].head, ankle_from(H, T)
        h = math.hypot(hip.x - ank.x, hip.y - ank.y)
        lim = LEG_LEN * slack
        v = hip.z - ank.z
        if h < lim and h * h + v * v > lim * lim: drop = max(drop, v - math.sqrt(lim * lim - h * h))
    return drop

WORST = {}
def pose_ik(name, sem, pel, feet=None, sword=None, curlL=RELAX, ground_slack=0.985, swordL=None):
    """胴体を置き、骨盤の高さを脚の届く範囲に収め、足と右手のIKを解く。"""
    body_pose(sem, pel, curlL)
    if feet:
        dz = reach_drop(feet, ground_slack)
        if dz > 0:
            pel = (pel[0], pel[1], pel[2] - dz)
            body_pose(sem, pel, curlL)
    chains = []
    if feet: chains += [leg_chain(S, *feet[S]) for S in "LR"]
    if sword: chains.append(arm_chain(*sword))
    if swordL: chains.append(arm_chain(*swordL, S="L"))
    err = solve(chains) if chains else []
    for c in chains:
        for bn, ax in c.params: STATE[(bn, ax)] = P[bn].rotation_euler[ax]
    labels = (["脚", "脚"] if feet else []) + (["腕"] if sword else []) + (["左腕"] if swordL else [])
    for lb, e in zip(labels, err):
        WORST[name + lb] = max(WORST.get(name + lb, 0.0), e)
        if "--debug" in sys.argv and e > 0.05: print("IKERR", name, lb, round(e, 3), [round(x, 3) for x in chains[labels.index(lb)].resid()] if "腕" in lb else
            "")
    return err

def key_all(f):
    for pb in P:
        pb.keyframe_insert("rotation_euler", frame=f)
        pb.keyframe_insert("location", frame=f)

if arm.animation_data is None: arm.animation_data_create()
ACTIONS = {}
def new_action(name):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data.action = act
    ACTIONS[name] = act
    STATE.clear()
    return act

def lerp(a, b, t): return a + (b - a) * t
def smooth(t): t = min(max(t, 0.0), 1.0); return t * t * (3 - 2 * t)
def hermite(p0, p1, m0, m1, s):
    s2, s3 = s * s, s * s * s
    return p0 * (2 * s3 - 3 * s2 + 1) + m0 * (s3 - 2 * s2 + s) + p1 * (-2 * s3 + 3 * s2) + m1 * (s3 - s2)

def catmull(keys, f):
    """非等間隔の Catmull-Rom。keys=[(frame, value)]、value は float か Vector。端は速度0。"""
    ks = sorted(keys, key=lambda k: k[0])
    if f <= ks[0][0]: return ks[0][1]
    if f >= ks[-1][0]: return ks[-1][1]
    i = max(j for j in range(len(ks) - 1) if ks[j][0] <= f)
    (t0, p0), (t1, p1) = ks[i], ks[i + 1]
    def tan(j):
        if j <= 0 or j >= len(ks) - 1: return p0 * 0
        (ta, pa), (tb, pb) = ks[j - 1], ks[j + 1]
        return (pb - pa) / (tb - ta)
    dt = t1 - t0
    return hermite(p0, p1, tan(i) * dt, tan(i + 1) * dt, (f - t0) / dt)

def qslerp(keys, f):
    ks = sorted(keys, key=lambda k: k[0])
    if f <= ks[0][0]: return ks[0][1]
    if f >= ks[-1][0]: return ks[-1][1]
    i = max(j for j in range(len(ks) - 1) if ks[j][0] <= f)
    (t0, q0), (t1, q1) = ks[i], ks[i + 1]
    return q0.slerp(q1, (f - t0) / (t1 - t0))

def frame_q(b, e):
    b, e = ortho(b, e)
    return Matrix((b, e, b.cross(e))).transposed().to_quaternion()
def q_dirs(q):
    m = q.to_matrix()
    return m.col[0].copy(), m.col[1].copy()

# ───────── 共通の構え ─────────
# 剣は下段:握りは腰の右前、切っ先は前下がり、刃は下向き(胸の座標系=胴のひねりに付いてくる)
CARRY_G = Vector((1.1, -2.2, 4.95))
CARRY_B, CARRY_E = ortho((0.78, -0.22, -0.58), (-0.6, 0.0, -0.8))
def carry_world(dx=0.0, dz=0.0):
    M = chest_M()
    return M @ (CARRY_G + Vector((dx, 0, dz))), M.to_3x3() @ CARRY_B, M.to_3x3() @ CARRY_E

STANCE = dict(arm_lower_L=72, arm_fwd_L=12, elbow_L=35, arm_lower_R=70,
              chest_left=-8, neck_left=8, pelvis_fwd=4)
STANCE_PEL = (0.0, 0.0, -0.35)
FOOT_IDLE = {"L": (HEEL_X + 0.45, FOOT_Y["L"] + 0.08, 4.0), "R": (HEEL_X - 0.45, FOOT_Y["R"] - 0.08, -12.0)}
def idle_feet():
    return {S: foot_targets(x, y, yaw, 0.0) for S, (x, y, yaw) in FOOT_IDLE.items()}

# ───────── 待機:2秒ループ。足は固定、膝と胸でゆっくり呼吸 ─────────
new_action("Idle")
for f in range(0, 61, 2):
    b = 0.5 - 0.5 * math.cos(2 * math.pi * f / 60)
    sem = dict(STANCE, chest_left=-8 + 1.5 * b, neck_left=8 - 1.5 * b, arm_fwd_L=12 + 3 * b, elbow_L=35 + 4 * b)
    body_pose(sem, (0, 0, STANCE_PEL[2] - 0.07 * b))
    pose_ik("Idle", sem, (0, 0, STANCE_PEL[2] - 0.07 * b), idle_feet(), carry_world(0.0, -0.06 * b))
    key_all(f)

# ───────── 歩き:32フレームで2歩。0 で左かかと着地、16 で右かかと着地(ゲームの足音と一致) ─────────
new_action("Walk")
T = 32
C = 2 * STEP_LEN              # 1周期(2歩)で進む距離
BETA = 0.6                    # 接地している割合
Q_HEEL, Q_TOE = 0.08, BETA - 0.16
P_STRIKE, P_OFF, LIFT = 12.0, -22.0, 0.75
X0 = -A_OFS.x + C * BETA / 2  # 接地の中間で足首が股関節の真下に来る
WALK_FOOT_Y = {"L": FOOT_Y["L"] - 0.06, "R": FOOT_Y["R"] + 0.06}

def walk_foot(S, q):
    y = WALK_FOOT_Y[S]
    if q <= BETA:
        hx = X0 - C * q
        if q < Q_HEEL: pitch = P_STRIKE * (1 - smooth(q / Q_HEEL))
        elif q > Q_TOE: pitch = P_OFF * smooth((q - Q_TOE) / (BETA - Q_TOE))
        else: pitch = 0.0
        return foot_targets(hx, y, 0.0, pitch)
    s = (q - BETA) / (1 - BETA)
    H0, T0 = foot_targets(X0 - C * BETA, y, 0.0, P_OFF)
    H1, T1 = foot_targets(X0, y, 0.0, P_STRIKE)
    m0, m1 = (H0 + T0) / 2, (H1 + T1) / 2
    v = Vector((-C * (1 - BETA), 0, 0))          # 地面に対する足の速さをそのままつなぐ
    mid = hermite(m0, m1, v, v, s) + Vector((0, 0, LIFT * math.sin(math.pi * s ** 0.85)))
    pitch = lerp(P_OFF, P_STRIKE, smooth(s * 1.15))
    return foot_targets(0, 0, 0.0, pitch, mid=mid)

def walk_body(p):
    c = math.cos(2 * math.pi * p)
    ca = math.cos(2 * math.pi * (p - 0.07))   # 腕は脚より少し遅れる
    pl, ch = -6 * c, -8 + 9 * math.cos(2 * math.pi * (p - 0.04))
    sem = dict(STANCE, pelvis_left=pl, chest_left=ch, neck_left=-(pl + ch) * 0.9, pelvis_fwd=7,
               pelvis_rollL=2.5 * math.cos(2 * math.pi * (p - 0.8)),
               arm_fwd_L=8 - 22 * ca, elbow_L=30 + 16 * (1 - ca) / 2)
    pel = (0.0, 0.12 * math.cos(2 * math.pi * (p - 0.3)), -0.25 - 0.13 * math.cos(4 * math.pi * (p - 0.06)))
    return sem, pel, ca

# 1回目:各フレームで脚が届く骨盤の高さを求め、なめらかにしてから解く
zs = []
for f in range(T):
    p = f / T
    sem, pel, _ = walk_body(p)
    feet = {"L": walk_foot("L", p), "R": walk_foot("R", (p + 0.5) % 1.0)}
    body_pose(sem, pel)
    zs.append(pel[2] - reach_drop(feet))
zs_s = [sum(zs[(f + k) % T] * w for k, w in ((-2, 1), (-1, 2), (0, 3), (1, 2), (2, 1))) / 9 for f in range(T)]
zs_s = [min(a, b + 0.02) for a, b in zip(zs_s, zs)]
for f in range(T + 1):
    p = (f % T) / T
    sem, pel, ca = walk_body(p)
    pel = (pel[0], pel[1], zs_s[f % T])
    feet = {"L": walk_foot("L", p), "R": walk_foot("R", (p + 0.5) % 1.0)}
    body_pose(sem, pel)
    pose_ik("Walk", sem, pel, feet, carry_world(0.3 * ca, 0.08 * (1 - ca) / 2), ground_slack=0.995)
    key_all(f)
print("WALK pelvis z", [round(z, 2) for z in zs_s])

# ───────── 走り:20フレーム(0.67秒)で2歩。両足が浮く瞬間がある。0 で左足着地 ─────────
# ゲームの走り 16.7m/s に合わせ、1歩 = 16.7 × 22/30 / 2 ≒ 6.1m(Unity の runStepLength と同じ)
new_action("Run")
LEG_LIMITS = True
RT = 20
RUN_SPEED = 16.7
R_STEP = RUN_SPEED * RT / 30.0 / 2.0
RC = 2 * R_STEP                 # 1周期で進む距離
R_BETA = 0.30                   # 接地している割合(0.5 未満なので両足が浮く時間がある)
R_STRIKE, R_OFF, R_LIFT = 8.0, -24.0, 1.0
R_X0 = -A_OFS.x + RC * R_BETA / 2
RUN_FOOT_Y = {"L": FOOT_Y["L"] - 0.04, "R": FOOT_Y["R"] + 0.04}
print("RUN step", round(R_STEP, 2), "stance sweep", round(RC * R_BETA, 2), "leg", round(LEG_LEN, 2))

def run_foot(S, q):
    y = RUN_FOOT_Y[S]
    if q <= R_BETA:
        hx = R_X0 - RC * q
        u = q / R_BETA
        pitch = R_STRIKE * (1 - smooth(u / 0.2)) if u < 0.2 else (R_OFF * smooth((u - 0.6) / 0.4) if u > 0.6 else 0.0)
        return foot_targets(hx, y, 0.0, pitch)
    s_ = (q - R_BETA) / (1 - R_BETA)
    H0, T0 = foot_targets(R_X0 - RC * R_BETA, y, 0.0, R_OFF)
    H1, T1 = foot_targets(R_X0, y, 0.0, R_STRIKE)
    m0, m1 = (H0 + T0) / 2, (H1 + T1) / 2
    v = Vector((-RC * (1 - R_BETA), 0, 0))
    # 蹴り上げ:前半はかかとが後ろで高く上がり、後半で前へ振り出す
    mid = hermite(m0, m1, v * 0.12, v * 0.25, s_) + Vector((0, 0, R_LIFT * math.sin(math.pi * s_ ** 0.8)))
    pitch = lerp(R_OFF, R_STRIKE, smooth(s_ * 1.2))
    return foot_targets(0, 0, 0.0, pitch, mid=mid)

def run_body(p):
    c = math.cos(2 * math.pi * p)
    ca = math.cos(2 * math.pi * (p - 0.05))
    pl, ch = -9 * c, -8 + 14 * math.cos(2 * math.pi * (p - 0.03))
    sem = dict(STANCE, pelvis_left=pl, chest_left=ch, neck_left=-(pl + ch) * 0.9, pelvis_fwd=14,
               pelvis_rollL=3.5 * math.cos(2 * math.pi * (p - 0.8)),
               arm_fwd_L=6 - 40 * ca, elbow_L=70 + 15 * (1 - ca) / 2)
    # 上下:着地の中ほどで一番低く、両足が浮いている間に高い
    pel = (0.25, 0.12 * math.cos(2 * math.pi * (p - 0.25)), -0.38 - 0.16 * math.cos(4 * math.pi * (p - R_BETA / 2)))
    return sem, pel, ca

zs = []
for f in range(RT):
    p = f / RT
    sem, pel, _ = run_body(p)
    feet = {"L": run_foot("L", p), "R": run_foot("R", (p + 0.5) % 1.0)}
    body_pose(sem, pel)
    zs.append(pel[2] - reach_drop(feet, 0.97))
zs_s = [sum(zs[(f + k) % RT] * w for k, w in ((-1, 1), (0, 2), (1, 1))) / 4 for f in range(RT)]
zs_s = [min(a, b + 0.02) for a, b in zip(zs_s, zs)]
# 準備運転:周期の終わりの数フレームを先に解いて、IK の初期値を走りの姿勢にしておく(静止姿勢から解くと脚がねじれて固まる)
for f in range(RT - 6, RT):
    p = f / RT
    sem, pel, ca = run_body(p)
    pel = (pel[0], pel[1], zs_s[f])
    feet = {"L": run_foot("L", p), "R": run_foot("R", (p + 0.5) % 1.0)}
    body_pose(sem, pel)
    pose_ik("RunWarm", sem, pel, feet, carry_world(0.4 * ca - 0.3, 0.15 + 0.1 * (1 - ca) / 2), ground_slack=0.98)
for f in range(RT + 1):
    p = (f % RT) / RT
    sem, pel, ca = run_body(p)
    pel = (pel[0], pel[1], zs_s[f % RT])
    feet = {"L": run_foot("L", p), "R": run_foot("R", (p + 0.5) % 1.0)}
    body_pose(sem, pel)
    pose_ik("Run", sem, pel, feet, carry_world(0.4 * ca - 0.3, 0.15 + 0.1 * (1 - ca) / 2), ground_slack=0.98)
    key_all(f)
print("RUN pelvis z", [round(z, 2) for z in zs_s])
LEG_LIMITS = False

# ───────── 斬撃(袈裟斬り):0 構え → 8 溜め → 16 振りかぶり → 18〜21 振り下ろし → 25 振り抜き → 40 構え ─────────
# ゲームの windup は 0〜16、strike は 16〜21、recovery は 21〜40 を時間に合わせて再生する
new_action("Attack")
body_pose(STANCE, STANCE_PEL); vl.update()
G0, B0, E0 = carry_world()
BODY_KEYS = {
    0:  (STANCE, STANCE_PEL),
    8:  (dict(STANCE, chest_left=-22, pelvis_left=-8, pelvis_fwd=2, neck_left=26, arm_fwd_L=28, elbow_L=60), (-0.25, -0.1, -0.55)),
    16: (dict(STANCE, chest_left=-38, pelvis_left=-14, pelvis_fwd=4, neck_left=46, arm_fwd_L=58, arm_lower_L=68, elbow_L=55), (0.35, 0.1, -0.65)),
    18: (dict(STANCE, chest_left=-6, pelvis_left=-3, pelvis_fwd=8, neck_left=9, arm_fwd_L=28, elbow_L=50), (0.6, 0.2, -0.75)),
    21: (dict(STANCE, chest_left=32, pelvis_left=12, pelvis_fwd=12, neck_left=-38, arm_fwd_L=-20, arm_lower_L=62, elbow_L=40), (0.9, 0.3, -0.95)),
    25: (dict(STANCE, chest_left=38, pelvis_left=15, pelvis_fwd=13, neck_left=-46, arm_fwd_L=-28, arm_lower_L=62, elbow_L=38), (0.95, 0.32, -1.0)),
    40: (STANCE, STANCE_PEL),
}
SWORD_KEYS = {   # 握り位置, 刃の向き, 刃筋(刃が向いている側)
    0:    (G0, B0, E0),
    5:    (Vector((1.2, -2.8, 7.0)), (0.3, -0.2, 0.95), (0.9, 0.1, -0.3)),
    9:    (Vector((0.2, -2.9, 9.0)), (-0.3, -0.2, 0.93), (0.9, 0.2, 0.3)),
    16:   (Vector((-0.7, -1.9, 10.1)), (-0.5, -0.3, 0.8), (0.5, 0.5, 0.5)),
    18:   (Vector((2.2, -1.5, 9.6)), (0.5, 0.3, 0.8), (0.6, 0.3, -0.5)),
    19.5: (Vector((3.6, -0.6, 7.8)), (0.9, 0.4, 0.1), (0.1, 0.3, -1.0)),
    21:   (Vector((3.3, 0.9, 5.8)), (0.35, 0.6, -0.72), (-0.6, -0.3, -0.5)),
    25:   (Vector((2.4, 1.7, 4.9)), (-0.05, 0.55, -0.83), (-0.7, -0.5, -0.3)),
    40:   (G0, B0, E0),
}
SEM_NAMES = sorted({k for s, _ in BODY_KEYS.values() for k in s})
sk = sorted(SWORD_KEYS.items())
QK, prevq = [], None
for fr, (g, b, e) in sk:
    q = frame_q(b, e)
    if prevq is not None and q.dot(prevq) < 0: q.negate()
    QK.append((fr, q)); prevq = q
GK = [(fr, v[0]) for fr, v in sk]

AL, BL = (FOOT_IDLE["L"][0], FOOT_IDLE["L"][1]), (FOOT_IDLE["L"][0] + 1.75, FOOT_IDLE["L"][1] + 0.05)
def attack_foot_L(f):
    yaw = FOOT_IDLE["L"][2]
    def swing(a, b, s, lift, p0, p1):
        Ha, Ta = foot_targets(a[0], a[1], yaw, p0)
        Hb, Tb = foot_targets(b[0], b[1], yaw, p1)
        mid = lerp((Ha + Ta) / 2, (Hb + Tb) / 2, smooth(s)) + Vector((0, 0, lift * math.sin(math.pi * s)))
        return foot_targets(0, 0, yaw, lerp(p0, p1, smooth(s)), mid=mid)
    if f <= 9: return foot_targets(*AL, yaw, 0.0)
    if f < 15: return swing(AL, BL, (f - 9) / 6, 0.45, 0.0, 8.0)
    if f < 17: return foot_targets(*BL, yaw, 8.0 * (1 - smooth((f - 15) / 2)))
    if f < 28: return foot_targets(*BL, yaw, 0.0)
    if f < 37: return swing(BL, AL, (f - 28) / 9, 0.35, 0.0, 0.0)
    return foot_targets(*AL, yaw, 0.0)
def attack_foot_R(f):
    x, y, yaw = FOOT_IDLE["R"]
    pitch = -14 * (smooth((f - 16) / 6) if f < 26 else 1 - smooth((f - 26) / 8))
    return foot_targets(x, y, yaw, pitch)

for f in range(41):
    sem = {k: catmull([(fr, s.get(k, STANCE.get(k, 0.0))) for fr, (s, _) in BODY_KEYS.items()], f) for k in SEM_NAMES}
    pel = catmull([(fr, Vector(p)) for fr, (_, p) in BODY_KEYS.items()], f)
    b, e = q_dirs(qslerp(QK, f))
    feet = {"L": attack_foot_L(f), "R": attack_foot_R(f)}
    pose_ik("Attack", sem, tuple(pel), feet, (catmull(GK, f), b, e), curlL=FIST if 6 < f < 34 else RELAX)
    key_all(f)

# ───────── 左手の斬撃:右の斬撃を左右反転(左腕で振る。右手の剣は下段に構えたまま) ─────────
# 胴のひねりは構え(STANCE)からの変化だけを反転するので、最初と最後は待機と同じ姿勢になる
new_action("AttackL")
body_pose(STANCE, STANCE_PEL, FIST); vl.update()
GL0, BL0, EL0 = hand_frame("L")
TWIST = ("chest_left", "neck_left", "pelvis_left", "pelvis_rollL")
def mirror_sem(sm):
    out = {}
    for k in SEM_NAMES:
        if k.endswith("_L") or k.endswith("_R"): continue   # 腕はIKで動かす
        base = STANCE.get(k, 0.0)
        v = sm.get(k, base)
        out[k] = 2 * base - v if k in TWIST else v
    return out
BODY_KEYS_L = {fr: (mirror_sem(sm), (p[0], -p[1], p[2])) for fr, (sm, p) in BODY_KEYS.items()}
SEM_NAMES_L = sorted({k for s, _ in BODY_KEYS_L.values() for k in s})
SWORD_KEYS_L = {fr: ((GL0, BL0, EL0) if fr in (0, 40) else (mir(Vector(g)), mir(Vector(b)), mir(Vector(e))))
                for fr, (g, b, e) in SWORD_KEYS.items()}
skl = sorted(SWORD_KEYS_L.items())
QKL, prevq = [], None
for fr, (g, b, e) in skl:
    q = frame_q(b, e)
    if prevq is not None and q.dot(prevq) < 0: q.negate()
    QKL.append((fr, q)); prevq = q
GKL = [(fr, v[0]) for fr, v in skl]
STATE.clear()
for f in range(41):
    sem = dict(STANCE)
    sem.update({k: catmull([(fr, s.get(k, STANCE.get(k, 0.0))) for fr, (s, _) in BODY_KEYS_L.items()], f) for k in SEM_NAMES_L})
    pel = catmull([(fr, Vector(p)) for fr, (_, p) in BODY_KEYS_L.items()], f)
    b, e = q_dirs(qslerp(QKL, f))
    feet = {"L": attack_foot_L(f), "R": attack_foot_R(f)}
    body_pose(sem, tuple(pel), FIST)
    pose_ik("AttackL", sem, tuple(pel), feet, carry_world(), curlL=FIST, swordL=(catmull(GKL, f), b, e))
    key_all(f)

# ───────── 射撃の構え:右腕を前に水平(足は待機と同じ) ─────────
new_action("Aim")
for f in (0, 10):
    sem = dict(STANCE, chest_left=-12, neck_left=12, arm_fwd_L=15)
    body_pose(sem, STANCE_PEL)
    M = chest_M()
    bA, eA = ortho((1, 0, 0.05), (0, 0, -1))
    pose_ik("Aim", sem, STANCE_PEL, idle_feet(), (M @ Vector((3.0, -1.75, 7.5)), M.to_3x3() @ bA, M.to_3x3() @ eA))
    key_all(f)

# ───────── ダッシュ:前傾で浮く。脚は後ろへ流し、剣は後ろへ引きずる ─────────
new_action("Dash")
for f in (0, 10):
    sem = dict(STANCE, pelvis_fwd=12, chest_left=-4, neck_left=4, arm_fwd_L=-30, elbow_L=50,
               hip_fwd_L=36, knee_L=45, toe_up_L=-30, hip_fwd_R=16, knee_R=30, toe_up_R=-35)
    body_pose(sem, (0, 0, 0.1))
    pose_ik("Dash", sem, (0, 0, 0.1), None, carry_world(-0.5, 0.4))
    key_all(f)

# 毎フレーム打っているので補間は直線にする(ベジェの行き過ぎを防ぐ)
def all_fcurves(a):
    try:
        return list(a.fcurves)
    except AttributeError:
        out = []
        for layer in a.layers:
            for strip in layer.strips:
                for cb in strip.channelbags: out.extend(cb.fcurves)
        return out
for a in ACTIONS.values():
    for fc in all_fcurves(a):
        for kp in fc.keyframe_points: kp.interpolation = 'LINEAR'
for name, a in ACTIONS.items():
    print("ACTION", name, a.frame_range[:], len(all_fcurves(a)), "IK誤差(最大)", {k: round(v, 3) for k, v in WORST.items() if k.startswith(name)})
mod.show_viewport = True

# ───────── 確認用レンダリング ─────────
if not NO_RENDER:
    os.makedirs(RENDER_DIR, exist_ok=True)
    for fn in os.listdir(RENDER_DIR): os.remove(os.path.join(RENDER_DIR, fn))
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.render.resolution_x = 640; scene.render.resolution_y = 720
    for o in scene.objects:
        if o.type == 'MESH' and o != body: o.hide_render = True
    # 剣(仮):握りから切っ先まで SWORD_GRIP、柄頭側に残り
    import bmesh
    def box(name, p0, p1, w, color):
        me = bpy.data.meshes.new(name); bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0); bm.to_mesh(me); bm.free()
        ob = bpy.data.objects.new(name, me); scene.collection.objects.link(ob)
        d = p1 - p0
        ob.matrix_world = Matrix.LocRotScale((p0 + p1) / 2, Vector((1, 0, 0)).rotation_difference(d.normalized()), Vector((d.length, w, w * 0.4)))
        ob.color = color
        return ob
    sword_parts = [box("chk_blade", GRIP_REST, GRIP_REST + BLADE_REST * SWORD_GRIP, 0.3, (0.9, 0.9, 1.0, 1)),
                   box("chk_hilt", GRIP_REST - BLADE_REST * (SWORD_LEN - SWORD_GRIP), GRIP_REST, 0.22, (0.2, 0.1, 0.1, 1))]
    rest_mw = {o: o.matrix_world.copy() for o in sword_parts}
    left_blade = box("chk_blade_L", Vector((0, 0, 0)), Vector((1, 0, 0)), 0.3, (1.0, 0.75, 0.3, 1))
    floor = bpy.data.objects.new("chk_floor", bpy.data.meshes.new("chk_floor"))
    bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=30); bm.to_mesh(floor.data); bm.free()
    scene.collection.objects.link(floor); floor.location = (0, 0, G - 0.01)
    scene.display.shading.color_type = 'OBJECT'
    body.color = (0.6, 0.65, 0.7, 1); floor.color = (0.25, 0.25, 0.27, 1)
    cam_data = bpy.data.cameras.new("chkcam"); cam_data.type = 'ORTHO'; cam_data.ortho_scale = 19
    cam = bpy.data.objects.new("chkcam", cam_data); scene.collection.objects.link(cam); scene.camera = cam
    def shot(action, frame, view):
        arm.animation_data.action = ACTIONS[action]
        scene.frame_set(frame)
        W = P["R_武器"].matrix @ P["R_武器"].bone.matrix_local.inverted()
        for o in sword_parts: o.matrix_world = W @ rest_mw[o]
        g, b, _ = hand_frame("L")   # 左手の刃(確認用:握りから刃の向きへ)
        d = b * 4.5
        left_blade.matrix_world = Matrix.LocRotScale(g + d / 2, Vector((1, 0, 0)).rotation_difference(b), Vector((4.5, 0.3, 0.12)))
        left_blade.hide_render = action != "AttackL"
        if view == "side":   # 左側面(+Yから見る)
            cam.location = (1, 40, 4.6); cam.rotation_euler = (math.radians(90), 0, math.radians(180))
        elif view == "q":
            cam.location = (30, -26, 10); cam.rotation_euler = (math.radians(78), 0, math.radians(49))
        elif view == "back":   # 真後ろから(ゲームのカメラと同じ向き)
            cam.location = (-40, 0, 6); cam.rotation_euler = (math.radians(85), 0, math.radians(-90))
        elif view == "q2":   # 左前から
            cam.location = (30, 26, 10); cam.rotation_euler = (math.radians(78), 0, math.radians(131))
        elif view == "top":
            cam.location = (1, 0, 40); cam.rotation_euler = (0, 0, math.radians(-90))
        else:                # 正面(+Xから見る)
            cam.location = (40, 0, 4.6); cam.rotation_euler = (math.radians(90), 0, math.radians(90))
        scene.render.filepath = f"{RENDER_DIR}{action}_{frame:02d}_{view}.png"
        bpy.ops.render.render(write_still=True)
    shot("Idle", 0, "side"); shot("Idle", 0, "front"); shot("Idle", 0, "q")
    for f in range(0, 32, 4): shot("Walk", f, "side")
    shot("Walk", 0, "front"); shot("Walk", 8, "front")
    for f in range(0, 20, 3): shot("Run", f, "side")
    for f in range(0, 20, 2): shot("Run", f, "back")
    shot("Run", 0, "front")
    if "--only-run" in sys.argv: raise SystemExit
    for f in (0, 8, 12, 16, 18, 19, 20, 21, 25, 32, 40): shot("Attack", f, "q")
    for f in (16, 21): shot("Attack", f, "side"); shot("Attack", f, "front")
    for f in (0, 9, 16, 19, 21, 25, 40): shot("AttackL", f, "q2")
    for f in (16, 21): shot("AttackL", f, "front")
    shot("Dash", 0, "side"); shot("Aim", 0, "front")
    for o in sword_parts + [left_blade, floor, cam]: bpy.data.objects.remove(o, do_unlink=True)
    for o in scene.objects:
        if o.type == 'MESH': o.hide_render = False

arm.animation_data.action = ACTIONS["Idle"]
scene.frame_set(0)

if DO_EXPORT:
    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND, copy=True)
    print("SAVED", OUT_BLEND)
    # Blenderの正面(-Y)を向けてから書き出すと、Unityで +Z 向きになる
    arm.rotation_euler = (0, 0, math.radians(-90))
    vl.update()
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True); body.select_set(True); vl.objects.active = arm
    bpy.ops.export_scene.fbx(filepath=OUT_FBX, use_selection=True, object_types={'ARMATURE', 'MESH'},
        apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', add_leaf_bones=False,
        use_armature_deform_only=False, bake_anim=True, bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
        mesh_smooth_type='FACE')
    print("EXPORTED", OUT_FBX)
