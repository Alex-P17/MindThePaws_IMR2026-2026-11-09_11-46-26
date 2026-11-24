"""
Mind the Paws — generator de căței corgi chibi pentru Blender (4.2 LTS … 5.x).

Construiește:
  * Corgi_Base      — baza comună: corgi chibi grăsuț și pufos (corp dolofan, cap mare, obrajori, botic, urechi ascuțite,
                       lăbuțe scurte și grăsuțe, coadă pufoasă), ~0.35 m înălțime, originea la bază (Z = 0),
                       privește spre -Y (în Unity devine +Z).
  * Corgi_Mochi     — „Zoomie”: bandă cozy cu urechiușe de iepuraș arcuite, jucăușe (interior pastelat).
  * Corgi_Gogoasa   — „Hoțomanul”: colac/gogoașă pe măsura corpului, glazură pastelată caldă, sprinkles 3D pe partea de sus.
  * Corgi_Fasolica  — „Lipicioasa”: corp „boabă de fasole”, glugă moale verde-păstaie pe creștet și ceafă (fața liberă),
                       frunzuliță în vârf, eșarfă la gât cu o păstaie mică pe post de breloc.

Corpul și capul sunt o singură suprafață organică (metaballs topite unele în altele, convertite în mesh), cu tranziții moi
între cap, obrajori, botic și trunchi; culorile (blană caisie / crem cald) sunt atribuite pe zone, iar ponderile
pe oase sunt netezite (gâtul se îndoaie lin între spine și head). Ochii, năsucul și obrăjorii sunt așezați exact pe
suprafață (raycast). Peste tot: Shade Smooth + Subdivision Surface.

Armature: root, spine, head, ear.L/R, tail, leg.FL/FR/BL/BR + oase de accesorii (acc_*).
Animații (piste NLA, exportate ca „take”-uri separate în FBX):
  Idle, Sleep_Lay (toți) + Run_Zoomie (Mochi), Trotting_Decoy (Gogoașă), Clingy_Nod (Fasolică).

Rulare:
  * în Blender: Scripting → Open → build_corgis.py → Run Script
    sau:  blender --python build_corgis.py        (deschide Blender cu cei 4 căței construiți)
  * din linia de comandă (export FBX în Assets/Resources/MindThePaws/Models):
      blender --background --factory-startup --python build_corgis.py -- --export
      opțional: --out "C:/alt/folder"   --save "C:/cale/corgis.blend"   --preview "C:/cale/preview.png"
                --icons "C:/.../Assets/Resources/MindThePaws/Icons"   (portretele din poza Polaroid de la final)

Unity: corgi_mochi.fbx / corgi_gogoasa.fbx / corgi_fasolica.fbx din Assets/Resources/MindThePaws/Models sunt preluate
automat de joc (AutoAssetLoader), importate ca animație Legacy cu clipuri în buclă (CorgiModelImport.cs) și animate de
NativeAnim.cs fără conflict cu CozyAnimator.
"""

import bpy
import bmesh
import math
import os
import random
import sys
from mathutils import Matrix, Vector, Euler, Quaternion
from mathutils.bvhtree import BVHTree

EXPORT = False          # True = exportă FBX-urile când rulezi scriptul din Blender
FPS = 30
SUBDIV_LEVELS = 1       # Subdivision Surface în Blender (viewport + render)
EXPORT_SUBDIV = True    # exportă mesh-ul subdivizat (mai rotund în Unity; ~4× triunghiuri)
METABALL_RES = 0.0105   # rezoluția suprafeței organice (m); mai mic = mai fin, dar mai multe triunghiuri
EXPORT_MAX_TRIS = 12000 # buget de triunghiuri per cățel în FBX (Quest 3); 0 = fără reducere

# ============================================================================ argumente

def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    opts = {"export": EXPORT, "out": None, "save": None, "preview": None, "keep": False, "icons": None}
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--export":
            opts["export"] = True
        elif a == "--out" and i + 1 < len(argv):
            opts["out"] = argv[i + 1]; i += 1
        elif a == "--save" and i + 1 < len(argv):
            opts["save"] = argv[i + 1]; i += 1
        elif a == "--preview" and i + 1 < len(argv):
            opts["preview"] = argv[i + 1]; i += 1
        elif a == "--icons" and i + 1 < len(argv):
            opts["icons"] = argv[i + 1]; i += 1
        elif a == "--keep":
            opts["keep"] = True
        i += 1
    return opts


def default_export_dir():
    """Assets/MindThePaws/BlenderScripts → Assets/Resources/MindThePaws/Models"""
    here = None
    try:
        here = os.path.dirname(os.path.abspath(__file__))
    except NameError:
        pass
    if not here or not os.path.isdir(here):
        here = os.path.dirname(bpy.data.filepath) if bpy.data.filepath else os.getcwd()
    cand = os.path.normpath(os.path.join(here, "..", "..", "Resources", "MindThePaws", "Models"))
    return cand if os.path.isdir(os.path.dirname(cand)) else here

# ============================================================================ utilitare

def M(loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    """Matrice locație · rotație (grade, XYZ) · scară."""
    r = Euler([math.radians(a) for a in rot], "XYZ").to_matrix().to_4x4()
    return Matrix.Translation(Vector(loc)) @ r @ Matrix.Diagonal((scale[0], scale[1], scale[2], 1.0))


def MQ(loc, quat, scale):
    """Matrice locație · rotație (cuaternion) · scară."""
    return Matrix.Translation(Vector(loc)) @ quat.to_matrix().to_4x4() @ Matrix.Diagonal((scale[0], scale[1], scale[2], 1.0))


def look_quat(normal, up=Vector((0, 0, 1))):
    """Rotație cu axa locală Z de-a lungul normalei (pentru piese „lipite” de suprafață: ochi, obrăjori)."""
    n = Vector(normal).normalized()
    return n.to_track_quat("Z", "Y" if abs(n.dot(up)) < 0.95 else "X")


_mats = {}

def mat(name, color, rough=0.6, sheen=0.0):
    """Material Principled (culori liniare). Unity îl importă ca material Lit cu aceeași culoare de bază."""
    if name in _mats:
        return _mats[name]
    m = bpy.data.materials.get("MTP_" + name) or bpy.data.materials.new("MTP_" + name)
    m.diffuse_color = (color[0], color[1], color[2], 1.0)
    try:
        if not getattr(m, "node_tree", None):
            m.use_nodes = True     # în Blender 5.x materialele noi au deja noduri
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        if bsdf:
            bsdf.inputs["Base Color"].default_value = (color[0], color[1], color[2], 1.0)
            bsdf.inputs["Roughness"].default_value = rough
            if sheen > 0 and "Sheen Weight" in bsdf.inputs:
                bsdf.inputs["Sheen Weight"].default_value = sheen
    except Exception:
        pass
    _mats[name] = m
    return m


def palette():
    return {
        "fur": mat("Fur", (0.95, 0.46, 0.13), 0.85, 0.5),          # blăniță portocaliu-miere / caisiu cald
        "cream": mat("Cream", (1.0, 0.88, 0.7), 0.85, 0.5),        # crem cald (burtică, bot, lăbuțe)
        "pinkInner": mat("EarPink", (1.0, 0.6, 0.6), 0.75),
        "eye": mat("Eye", (0.025, 0.015, 0.02), 0.08),
        "shine": mat("Shine", (1.0, 1.0, 1.0), 0.05),
        "nose": mat("Nose", (0.05, 0.03, 0.035), 0.25),
        "blush": mat("Blush", (1.0, 0.45, 0.42), 0.8),             # roz-piersică
        "tongue": mat("Tongue", (0.95, 0.3, 0.36), 0.5),
    }

# ============================================================================ construcția mesh-ului

class Builder:
    """Adună toate piesele într-un singur bmesh.
    - piesele rigide (ochi, urechi, accesorii) sunt legate 100% de un os;
    - suprafața organică (corp + cap) are ponderi netezite între oase."""

    def __init__(self):
        self.bm = bmesh.new()
        self.deform = self.bm.verts.layers.deform.verify()
        self.tone = self.bm.verts.layers.float.new("mtp_tone")   # creat de la început: un strat nou invalidează vertecșii
        self.mats = []
        self.groups = []
        self.bvh = None

    def _mi(self, m):
        if m not in self.mats:
            self.mats.append(m)
        return self.mats.index(m)

    def _gi(self, bone):
        if bone not in self.groups:
            self.groups.append(bone)
        return self.groups.index(bone)

    def _finish(self, verts, m, bone):
        gi = self._gi(bone)
        mi = self._mi(m)
        faces = set()
        for v in verts:
            if not v.is_valid:
                continue
            v[self.deform].clear()
            v[self.deform][gi] = 1.0
            faces.update(v.link_faces)
        for f in faces:
            f.material_index = mi
            f.smooth = True
        return verts

    # ---------------------------------------------------------------- primitive rigide

    def sphere(self, center, radii, m, bone, rot=(0, 0, 0), segs=24, rings=14):
        r = bmesh.ops.create_uvsphere(self.bm, u_segments=segs, v_segments=rings, radius=1.0, matrix=M(center, rot, radii))
        return self._finish(r["verts"], m, bone)

    def sphere_q(self, center, quat, radii, m, bone, segs=20, rings=12):
        r = bmesh.ops.create_uvsphere(self.bm, u_segments=segs, v_segments=rings, radius=1.0, matrix=MQ(center, quat, radii))
        return self._finish(r["verts"], m, bone)

    def cone(self, base, r1, r2, depth, m, bone, rot=(0, 0, 0), segs=16):
        mtx = M(base, rot) @ Matrix.Translation((0, 0, depth * 0.5))
        r = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=segs,
                                  radius1=r1, radius2=r2, depth=depth, matrix=mtx)
        return self._finish(r["verts"], m, bone)

    def cone_dir(self, base, direction, r1, r2, depth, m, bone, segs=20, squash=1.0):
        """Con orientat după o direcție (urechi); squash < 1 îl turtește din față în spate."""
        q = Vector(direction).normalized().to_track_quat("Z", "Y")
        mtx = MQ(base, q, (1.0, squash, 1.0)) @ Matrix.Translation((0, 0, depth * 0.5))
        r = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=segs,
                                  radius1=r1, radius2=r2, depth=depth, matrix=mtx)
        return self._finish(r["verts"], m, bone)

    def torus(self, center, major, minor, m, bone, rot=(0, 0, 0), scale=(1, 1, 1), seg=36, ring=14, top_only=False, arc=None):
        """Tor în planul XY (în jurul axei Z). top_only = doar jumătatea de sus a secțiunii (glazura);
        arc = (de la, până la) în grade = doar o porțiune din inel (ex. o bandă peste creștet)."""
        mtx = M(center, rot, scale)
        rows = []
        ring_range = range(ring + 1) if top_only else range(ring)
        closed = arc is None
        count = seg if closed else seg + 1
        for i in range(count):
            a = i / seg * math.tau if closed else math.radians(arc[0] + (arc[1] - arc[0]) * i / seg)
            row = []
            for j in ring_range:
                b = (-0.2 + j / ring * (math.pi + 0.4)) if top_only else (j / ring * math.tau)
                x = (major + minor * math.cos(b)) * math.cos(a)
                y = (major + minor * math.cos(b)) * math.sin(a)
                z = minor * math.sin(b)
                row.append(self.bm.verts.new(mtx @ Vector((x, y, z))))
            rows.append(row)
        verts = [v for row in rows for v in row]
        n = len(rows[0])
        for i in range(len(rows) if closed else len(rows) - 1):
            a, b = rows[i], rows[(i + 1) % len(rows)]
            jr = n - 1 if top_only else n
            for j in range(jr):
                j2 = (j + 1) % n
                try:
                    self.bm.faces.new((a[j], b[j], b[j2], a[j2]))
                except ValueError:
                    pass
        return self._finish(verts, m, bone)

    def loft(self, pts, widths, ratio, m, bone, ring=14, front=Vector((0, -1, 0)), loop=False, normals=None):
        """Tub neted de-a lungul unei curbe (secțiune elipsă: `w` pe lateral, `w * ratio` față-spate).
        Deschis: închis la bază și ascuțit la vârf. loop=True: inel închis (bandă, eșarfă).
        normals (opțional): pentru fiecare punct, direcția „turtită” a secțiunii (ex. normala suprafeței pe care stă)."""
        n = len(pts)
        rings = []
        for i, pnt in enumerate(pts):
            if loop:
                tan = (pts[(i + 1) % n] - pts[(i - 1) % n]).normalized()
            else:
                tan = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
            if normals is not None:
                side = Vector(normals[i]).normalized()
                side = (side - tan * side.dot(tan)).normalized()
            else:
                side = tan.cross(front)
                if side.length < 1e-5:
                    side = Vector((1, 0, 0))
                side.normalize()
            fwd = side.cross(tan).normalized()
            w = widths[i]
            ringv = []
            for k in range(ring):
                a = k / ring * math.tau
                ringv.append(self.bm.verts.new(pnt + side * math.cos(a) * w + fwd * math.sin(a) * w * ratio))
            rings.append(ringv)
        verts = [v for r in rings for v in r]
        spans = n if loop else n - 1
        for i in range(spans):
            r0, r1 = rings[i], rings[(i + 1) % n]
            for k in range(ring):
                k2 = (k + 1) % ring
                self.bm.faces.new((r0[k], r0[k2], r1[k2], r1[k]))
        if not loop:
            tip_dir = (pts[-1] - pts[-2]).normalized()
            tip = self.bm.verts.new(pts[-1] + tip_dir * widths[-1] * 0.8)
            base = self.bm.verts.new(pts[0] - (pts[1] - pts[0]).normalized() * widths[0] * 0.3)
            for k in range(ring):
                k2 = (k + 1) % ring
                self.bm.faces.new((rings[-1][k], rings[-1][k2], tip))
                self.bm.faces.new((rings[0][k2], rings[0][k], base))
            verts += [tip, base]
        return self._finish(verts, m, bone)

    def panel(self, grid, thickness, m, bone):
        """Pânză (eșarfă / bandană): o grilă de puncte rows × cols, cu grosime (solidify)."""
        rows = [[self.bm.verts.new(p) for p in row] for row in grid]
        faces = []
        for i in range(len(rows) - 1):
            for j in range(len(rows[i]) - 1):
                quad = (rows[i][j], rows[i][j + 1], rows[i + 1][j + 1], rows[i + 1][j])
                if len(set(quad)) == 4:
                    faces.append(self.bm.faces.new(quad))
        verts = [v for row in rows for v in row]
        bmesh.ops.remove_doubles(self.bm, verts=verts, dist=1e-5)
        faces = [f for f in faces if f.is_valid]
        res = bmesh.ops.solidify(self.bm, geom=faces, thickness=thickness)
        all_v = set(v for v in verts if v.is_valid)
        for g in res["geom"]:
            if isinstance(g, bmesh.types.BMVert):
                all_v.add(g)
        return self._finish(list(all_v), m, bone)

    # ---------------------------------------------------------------- suprafețe organice

    def organic(self, elements, mats, set_bvh=True):
        """Suprafață organică din metaballs topite unele în altele, convertită în mesh.
        elements = [(tag, centru, semi-axe, os[, rotație în grade])]:
          * tag "neg"      → element negativ (scobește, ex. deschiderea glugii pentru față);
          * os "paint:..." → element doar de culoare (fără volum), ex. sprâncene, dunga de pe frunte;
          * celelalte tag-uri → materialul zonei (ex. "fur" / "cream").
        Granițele de culoare sunt tăiate pe linia de nivel a unui câmp netezit (fără trepte), iar ponderile pe oase
        sunt o medie netezită (softmax) a elementelor din jur."""
        def parts(e):
            rot = e[4] if len(e) > 4 else (0, 0, 0)
            return e[0], Vector(e[1]), Vector(e[2]), e[3], rot

        shape = [parts(e) for e in elements if not parts(e)[3].startswith("paint:")]
        solid = [e for e in shape if e[0] != "neg"]
        tone_el = [parts(e) for e in elements if parts(e)[0] != "neg"]
        tags = sorted(set(e[0] for e in tone_el))
        first = tags[0]
        second = tags[1] if len(tags) > 1 else tags[0]

        mb = bpy.data.metaballs.new("TmpOrganic")
        mb.resolution = METABALL_RES
        mb.render_resolution = METABALL_RES
        mb.threshold = 0.6
        for tag, c, semi, bone, rot in shape:
            e = mb.elements.new(type="ELLIPSOID")
            e.co = c
            e.radius = 1.0
            e.size_x, e.size_y, e.size_z = (s / 0.574 for s in semi)
            if any(rot):
                e.rotation = Euler([math.radians(a) for a in rot], "XYZ").to_quaternion()
            if tag == "neg":
                e.use_negative = True
                e.stiffness = 4.0
        ob = bpy.data.objects.new("TmpOrganic", mb)
        bpy.context.scene.collection.objects.link(ob)
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
        bpy.data.objects.remove(ob, do_unlink=True)
        bpy.data.metaballs.remove(mb)

        self.bm.verts.ensure_lookup_table()
        start = len(self.bm.verts)
        self.bm.from_mesh(me)
        bpy.data.meshes.remove(me)
        self.bm.verts.ensure_lookup_table()
        new_verts = [self.bm.verts[i] for i in range(start, len(self.bm.verts))]
        bmesh.ops.remove_doubles(self.bm, verts=new_verts, dist=1e-5)
        self.bm.verts.ensure_lookup_table()
        new_verts = [v for v in self.bm.verts[start:] if v.is_valid] if start < len(self.bm.verts) else []

        def ndist(p, c, semi, rot):
            d = Vector(p) - c
            if any(rot):
                d = Euler([math.radians(a) for a in rot], "XYZ").to_matrix().inverted() @ d
            return math.sqrt((d.x / semi.x) ** 2 + (d.y / semi.y) ** 2 + (d.z / semi.z) ** 2)

        lay = self.tone

        def tone(co):
            tot = 0.0
            cr = 0.0
            for tag, c, semi, bone, rot in tone_el:
                w = math.exp(-7.0 * ndist(co, c, semi, rot))
                tot += w
                if tag == second:
                    cr += w
            return cr / tot - 0.5 if tot > 0 else -0.5

        iso = set()
        if second != first:
            org_faces = set()
            for v in new_verts:
                org_faces.update(v.link_faces)
            bmesh.ops.triangulate(self.bm, faces=list(org_faces))
            for v in new_verts:
                v[lay] = tone(v.co)
            edges = set()
            for v in new_verts:
                edges.update(v.link_edges)
            for e in list(edges):
                if not e.is_valid:
                    continue
                a, b2 = e.verts
                va, vb = a[lay], b2[lay]
                if (va < 0.0) != (vb < 0.0) and abs(va - vb) > 1e-7:
                    t = va / (va - vb)
                    if t < 0.02 or t > 0.98:
                        continue
                    ne, nv = bmesh.utils.edge_split(e, a, t)
                    nv[lay] = 0.0
                    iso.add(nv)
            touched = set()
            for v in iso:
                touched.update(v.link_faces)
            for f in list(touched):
                if not f.is_valid:
                    continue
                on = [v for v in f.verts if v in iso]
                if len(on) == 2:
                    try:
                        bmesh.utils.face_split(f, on[0], on[1])
                    except (ValueError, RuntimeError):
                        pass
        else:
            for v in new_verts:
                v[lay] = 0.5
        new_verts = [v for v in new_verts if v.is_valid] + list(iso)

        # ponderi netezite pe oase
        for v in new_verts:
            acc = {}
            for tag, c, semi, bone, rot in solid:
                w = math.exp(-9.0 * ndist(v.co, c, semi, rot))
                acc[bone] = acc.get(bone, 0.0) + w
            best = sorted(acc.items(), key=lambda kv: -kv[1])[:3]
            total = sum(w for _, w in best) or 1.0
            dv = v[self.deform]
            dv.clear()
            for bone, w in best:
                if w / total > 0.02:
                    dv[self._gi(bone)] = w / total

        faces = set()
        for v in new_verts:
            faces.update(v.link_faces)
        for f in faces:
            vals = [v[lay] for v in f.verts if v not in iso]
            avg = sum(vals) / len(vals) if vals else tone(f.calc_center_median())
            f.material_index = self._mi(mats[second if avg > 0.0 else first])
            f.smooth = True
        self.bm.normal_update()
        if set_bvh:
            self.bvh = BVHTree.FromBMesh(self.bm)

    def surface(self, origin, direction, fallback=0.1):
        """Primul punct de pe suprafața organică dinspre interior (origin) spre direction: (punct, normală)."""
        d = Vector(direction).normalized()
        if self.bvh is not None:
            hit = self.bvh.ray_cast(Vector(origin), d, 1.0)
            if hit[0] is not None:
                n = hit[1] if hit[1].dot(d) > 0 else -hit[1]
                return hit[0], n.normalized()
        return Vector(origin) + d * fallback, d

    def to_object(self, name, collection):
        self.bm.verts.layers.float.remove(self.tone)
        self.bm.normal_update()
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        for m in self.mats:
            me.materials.append(m)
        obj = bpy.data.objects.new(name, me)
        collection.objects.link(obj)
        for g in self.groups:             # aceeași ordine ca indicii din stratul de deformare
            obj.vertex_groups.new(name=g)
        return obj

# ============================================================================ corpul de bază

BASE_BONES = [
    # nume,      cap,                       coadă,                     părinte
    ("root",     (0.0, 0.0, 0.0),           (0.0, 0.0, 0.06),          None),
    ("spine",    (0.0, 0.13, 0.13),         (0.0, -0.07, 0.15),        "root"),
    ("head",     (0.0, -0.09, 0.19),        (0.0, -0.115, 0.32),       "spine"),
    ("ear.L",    (0.062, -0.1, 0.31),       (0.085, -0.1, 0.38),       "head"),
    ("ear.R",    (-0.062, -0.1, 0.31),      (-0.085, -0.1, 0.38),      "head"),
    ("tail",     (0.0, 0.17, 0.16),         (0.0, 0.23, 0.21),         "spine"),
    ("leg.FL",   (0.066, -0.075, 0.11),     (0.066, -0.075, 0.0),      "spine"),
    ("leg.FR",   (-0.066, -0.075, 0.11),    (-0.066, -0.075, 0.0),     "spine"),
    ("leg.BL",   (0.072, 0.105, 0.11),      (0.072, 0.105, 0.0),       "spine"),
    ("leg.BR",   (-0.072, 0.105, 0.11),     (-0.072, 0.105, 0.0),      "spine"),
]

HEAD_C = Vector((0.0, -0.11, 0.24))


def body_elements(variant):
    """Elementele metaball (tag, centru, semi-axe, os[, rotație]). Silueta chibi grăsuță e aceeași pentru toți;
    variantele schimbă doar puțin trunchiul (Gogoașă mai rotundă, Fasolică „boabă de fasole”)."""
    body_c, body_s = (0.0, 0.035, 0.118), (0.138, 0.15, 0.112)       # trunchi scurt, lat, rotunjit
    rump_c, rump_s = (0.0, 0.112, 0.122), (0.128, 0.085, 0.108)      # funduleț plin
    if variant == "Gogoasa":
        body_s, rump_s = (0.145, 0.15, 0.115), (0.132, 0.085, 0.11)
    elif variant == "Fasolica":
        body_c, body_s = (0.0, 0.045, 0.116), (0.13, 0.165, 0.108)
        rump_c = (0.0, 0.13, 0.122)
    hc = HEAD_C
    el = [
        ("fur", body_c, body_s, "spine"),
        ("fur", rump_c, rump_s, "spine"),
        ("fur", (0.0, -0.055, 0.175), (0.1, 0.075, 0.09), "spine"),                    # gât pufos, lat
        ("fur", tuple(hc), (0.123, 0.108, 0.105), "head"),                               # cap mare, dolofan
        ("fur", (0.0, -0.095, 0.305), (0.085, 0.06, 0.035), "head"),                     # creștet rotunjit
        ("cream", (0.074, -0.158, 0.198), (0.056, 0.048, 0.048), "head"),                # obrăjori pufoși
        ("cream", (-0.074, -0.158, 0.198), (0.056, 0.048, 0.048), "head"),
        ("cream", (0.0, -0.198, 0.196), (0.054, 0.045, 0.036), "head"),                  # botic
        ("cream", (0.0, -0.2, 0.27), (0.016, 0.03, 0.034), "paint:head"),                # dunga de pe frunte
        ("cream", (0.04, -0.19, 0.297), (0.013, 0.03, 0.009), "paint:head"),             # sprâncene-bulină
        ("cream", (-0.04, -0.19, 0.297), (0.013, 0.03, 0.009), "paint:head"),
        ("cream", (0.0, -0.105, 0.145), (0.088, 0.06, 0.078), "spine"),                  # piept pufos
        # sub bărbie și pe gât: crem continuu de la botic până la piept (doar culoare, fără volum,
        # ca să nu rămână nicio fâșie portocalie sub cap)
        ("cream", (0.0, -0.165, 0.168), (0.085, 0.07, 0.045), "paint:head"),             # bărbia / sub botic
        ("cream", (0.0, -0.135, 0.17), (0.08, 0.06, 0.055), "paint:spine"),              # gâtul, în față
        ("cream", (0.0, -0.12, 0.155), (0.085, 0.055, 0.05), "paint:spine"),             # legătura gât–piept
        ("cream", (0.0, -0.01, 0.085), (0.112, 0.125, 0.078), "spine"),                  # burtică plină
        ("fur", (0.0, 0.205, 0.165), (0.05, 0.05, 0.048), "tail"),                       # coadă pufoasă
        ("cream", (0.0, 0.235, 0.19), (0.032, 0.032, 0.032), "tail"),
    ]
    for name, x, y in (("leg.FL", 0.07, -0.072), ("leg.FR", -0.07, -0.072), ("leg.BL", 0.076, 0.108), ("leg.BR", -0.076, 0.108)):
        el.append(("fur", (x, y, 0.062), (0.044, 0.044, 0.048), name))                 # lăbuțe scurte, grăsuțe
        el.append(("cream", (x, y - 0.008, 0.022), (0.044, 0.05, 0.026), name))        # „șosete” crem
    return el


def build_base(b, p, variant, ears=True):
    b.organic(body_elements(variant), {"fur": p["fur"], "cream": p["cream"]})
    hc = HEAD_C

    # urechi mari, moi, rotunjite la vârf (tub neted, nu con → fără dungi pe umbrire), cu interior roz
    if ears:
        for sx, bone in ((1, "ear.L"), (-1, "ear.R")):
            base, n = b.surface(hc, Vector((sx * 0.55, -0.02, 1.0)))
            up = Vector((sx * 0.36, 0.03, 1.0)).normalized()
            start = base - up * 0.02
            pts = [start + up * (0.098 * t) + Vector((0, -0.01 * t * t, 0)) for t in (i / 6 for i in range(7))]
            widths = [0.046, 0.048, 0.044, 0.036, 0.026, 0.016, 0.007]
            b.loft(pts, widths, 0.42, p["fur"], bone, ring=18)
            ipts = []
            for i in range(1, 6):
                tan = (pts[i + 1] - pts[i - 1]).normalized()
                side = tan.cross(Vector((0, -1, 0))).normalized()
                fwd = side.cross(tan).normalized()
                ipts.append(pts[i] + fwd * widths[i] * 0.42 * 0.78)
            b.loft(ipts, [w * 0.6 for w in widths[1:6]], 0.2, p["pinkInner"], bone, ring=14)

    # ochi lucioși cu reflexie albă
    for sx in (-1, 1):
        pt, n = b.surface(hc, Vector((sx * 0.4, -1.0, 0.1)))
        q = look_quat(n)
        b.sphere_q(pt - n * 0.003, q, (0.017, 0.021, 0.011), p["eye"], "head")
        side = Vector((1, 0, 0)) * (-sx)
        b.sphere_q(pt + n * 0.008 + Vector((0, 0, 0.007)) + side * 0.004, q, (0.0058, 0.0058, 0.003), p["shine"], "head", segs=12, rings=8)
        b.sphere_q(pt + n * 0.008 + Vector((0, 0, -0.006)) - side * 0.004, q, (0.0026, 0.0026, 0.0015), p["shine"], "head", segs=10, rings=6)
        # obrăjori roz-piersică, mari și turtiți pe obraz
        pt, n = b.surface(hc, Vector((sx * 0.88, -0.6, -0.3)))
        b.sphere_q(pt - n * 0.0015, look_quat(n), (0.027, 0.018, 0.0035), p["blush"], "head", segs=18, rings=10)

    # năsuc lucios
    muzzle = Vector((0.0, -0.196, 0.196))
    pt, n = b.surface(muzzle, Vector((0.0, -1.0, 0.45)))
    b.sphere_q(pt - n * 0.002, look_quat(n), (0.018, 0.013, 0.011), p["nose"], "head", segs=16, rings=10)
    b.sphere_q(pt + n * 0.008 + Vector((0.004, 0, 0.004)), look_quat(n), (0.004, 0.003, 0.002), p["shine"], "head", segs=8, rings=6)
    # zâmbet „ω” sub năsuc
    for sx in (-1, 1):
        pts, nrm = [], []
        for i in range(6):
            t = i / 5
            d = Vector((sx * 0.3 * t, -1.0, -0.1 - 0.55 * math.sin(math.pi * t * 0.9) * 0.45))
            hp, hn = b.surface(muzzle + Vector((0, 0, 0.004)), d)
            pts.append(hp + hn * 0.0015)
            nrm.append(hn)
        b.loft(pts, [0.0022] * 6, 1.0, p["nose"], "head", ring=8)

# ============================================================================ accesorii

def head_arc(b, y_off, a0, a1, count, lift):
    """Puncte pe suprafața capului, pe un arc în planul XZ (peste creștet), plus normalele lor."""
    pts, nrm = [], []
    for i in range(count):
        a = math.radians(a0 + (a1 - a0) * i / (count - 1))
        d = Vector((math.cos(a), 0.0, math.sin(a)))
        hp, hn = b.surface(HEAD_C + Vector((0, y_off, 0)), d)
        pts.append(hp + hn * lift)
        nrm.append(hn)
    return pts, nrm


def build_mochi(b, p):
    """Bandă pastelată moale pe cap + două urechiușe de iepuraș arcuite (exterior roz, interior roz-crem),
    lângă urechile naturale, fără să le acopere."""
    band = mat("MochiBand", (1.0, 0.68, 0.76), 0.9, 0.6)
    bunny = mat("BunnyPink", (1.0, 0.62, 0.72), 0.9, 0.6)
    inner = mat("BunnyCream", (1.0, 0.88, 0.8), 0.85, 0.5)
    pts, nrm = head_arc(b, 0.03, -12, 192, 26, 0.007)
    # banda: turtită pe cap (subțire pe normală, lată față-spate)
    b.loft(pts, [0.011] * len(pts), 2.0, band, "acc_band", ring=16, normals=nrm)
    for i in (0, len(pts) - 1):
        b.sphere_q(pts[i], look_quat(nrm[i]), (0.018, 0.022, 0.011), band, "acc_band", segs=14, rings=10)

    for sx, bone in ((1, "acc_bunny.L"), (-1, "acc_bunny.R")):
        idx = int(round((90 - sx * 9 + 12) / 204 * (len(pts) - 1)))      # aproape de mijloc, între urechile de corgi
        root = pts[idx] - nrm[idx] * 0.004
        curve = []
        for i in range(9):
            t = i / 8
            curve.append(root + Vector((sx * (0.004 * t + 0.026 * t ** 3), 0.028 * t * t, 0.135 * t)))
        widths = [0.015, 0.021, 0.025, 0.027, 0.027, 0.025, 0.021, 0.015, 0.007]
        b.loft(curve, widths, 0.45, bunny, bone, ring=18)
        ipts = []
        for i in range(1, 8):
            tan = (curve[i + 1] - curve[i - 1]).normalized()
            side = tan.cross(Vector((0, -1, 0))).normalized()
            fwd = side.cross(tan).normalized()
            ipts.append(curve[i] + fwd * widths[i] * 0.45 * 0.8)
        b.loft(ipts, [w * 0.58 for w in widths[1:8]], 0.2, inner, bone, ring=14)


def build_gogoasa(b, p):
    """Colac/gogoașă rotunjit pe măsura corpului: aluat auriu, glazură roz caldă care „curge” pe margini,
    sprinkles 3D mici și colorate așezate pe glazură."""
    dough = mat("Dough", (0.9, 0.52, 0.22), 0.75)
    glaze = mat("GlazePink", (1.0, 0.5, 0.62), 0.28)
    colors = [mat("SprinkleBlue", (0.25, 0.6, 1.0), 0.35), mat("SprinkleYellow", (1.0, 0.82, 0.15), 0.35),
              mat("SprinkleMint", (0.3, 0.88, 0.55), 0.35), mat("SprinkleWhite", (1.0, 1.0, 1.0), 0.35),
              mat("SprinkleLilac", (0.68, 0.42, 1.0), 0.35), mat("SprinkleCoral", (1.0, 0.45, 0.3), 0.35)]
    center = Vector((0.0, 0.035, 0.112))
    wx = (b.surface(center, Vector((1, 0, 0)))[0] - center).length
    ly = max((b.surface(center, Vector((0, 1, 0)))[0] - center).length,
             (b.surface(center, Vector((0, -1, 0)))[0] - center).length * 0.92)
    minor = 0.052
    major = wx + minor - 0.016
    sy = max(1.0, (ly + minor - 0.016) / major)

    # aluatul
    b.torus(center, major, minor, dough, "acc_donut", scale=(1.0, sy, 1.0), seg=44, ring=16)
    # glazura: acoperă partea de sus, cu „picături” care curg pe exterior
    gm = minor + 0.006
    seg, ring = 48, 18
    rows = []
    for i in range(seg):
        a = i / seg * math.tau
        drip = max(0.0, math.sin(6 * a + 0.4) + 0.6 * math.sin(11 * a + 1.3) - 0.35)
        b_lo = -0.25 - 0.55 * min(1.0, drip) ** 1.5           # pe exterior (b≈0) coboară în picături
        b_hi = math.pi + 0.2                                   # pe interior acoperă puțin peste creastă
        row = []
        for j in range(ring + 1):
            bb = b_lo + (b_hi - b_lo) * j / ring
            x = (major + gm * math.cos(bb)) * math.cos(a)
            y = (major + gm * math.cos(bb)) * math.sin(a) * sy
            z = gm * math.sin(bb)
            row.append(b.bm.verts.new(center + Vector((x, y, z + 0.002))))
        rows.append(row)
    gverts = [v for r in rows for v in r]
    for i in range(seg):
        r0, r1 = rows[i], rows[(i + 1) % seg]
        for j in range(ring):
            b.bm.faces.new((r0[j], r1[j], r1[j + 1], r0[j + 1]))
    gfaces = set(f for v in gverts for f in v.link_faces)
    res = bmesh.ops.solidify(b.bm, geom=list(gfaces), thickness=0.003)
    for g in res["geom"]:
        if isinstance(g, bmesh.types.BMVert):
            gverts.append(g)
    b._finish(gverts, glaze, "acc_donut")

    # sprinkles: unghiul de aur pe creasta glazurii, culcați pe suprafață, orientări variate
    rnd = random.Random(11)
    golden = math.radians(137.508)
    for i in range(72):
        a = i * golden
        bsec = math.radians(30 + 120 * ((i * 0.618034) % 1.0))
        rr = major + gm * math.cos(bsec)
        pos = center + Vector((rr * math.cos(a), rr * math.sin(a) * sy, gm * math.sin(bsec) + 0.004))
        nrm = Vector((math.cos(bsec) * math.cos(a), math.cos(bsec) * math.sin(a), math.sin(bsec))).normalized()
        ref = Vector((math.cos(a + rnd.uniform(0, math.pi)), math.sin(a + rnd.uniform(0, math.pi)), 0.3))
        tangent = nrm.cross(ref)
        if tangent.length < 1e-3:
            tangent = Vector((1, 0, 0))
        q = tangent.normalized().to_track_quat("Z", "Y")
        b.sphere_q(pos, q, (0.0036, 0.0036, 0.0105), colors[i % len(colors)], "acc_donut", segs=8, rings=6)


def build_fasolica(b, p):
    """Glugă moale verde-păstaie (tricot) care îmbracă creștetul, lateralele și ceafa, cu deschidere rotundă pentru
    față, două urechiușe-păstaie rotunjite contopite în glugă și o mlădiță în vârf; bandană triunghiulară moale
    la gât, cu o păstaie mică atârnată."""
    pod = mat("PodGreen", (0.36, 0.66, 0.2), 0.95, 0.7)
    leaf = mat("LeafGreen", (0.42, 0.78, 0.2), 0.7)
    pea = mat("Pea", (0.5, 0.84, 0.28), 0.6)
    scarf = mat("ScarfTeal", (0.18, 0.62, 0.68), 0.85, 0.3)
    hc = HEAD_C

    # gluga = o a doua suprafață organică: învelește capul, se continuă pe ceafă, urechi-păstaie topite în ea,
    # iar un element negativ scobește deschiderea pentru față (cu margine rotunjită, ca un tiv moale)
    hood = [
        ("hood", tuple(hc + Vector((0, 0.012, 0.014))), (0.141, 0.124, 0.122), "acc_hood"),
        ("hood", (0.0, -0.055, 0.2), (0.11, 0.08, 0.075), "acc_hood"),                       # ceafă
        ("hood", (0.094, -0.13, 0.19), (0.05, 0.06, 0.06), "acc_hood"),                      # clapete pe obraji
        ("hood", (-0.094, -0.13, 0.19), (0.05, 0.06, 0.06), "acc_hood"),
        ("hood", tuple(hc + Vector((0.088, 0.01, 0.125))), (0.042, 0.034, 0.085), "acc_hood", (0, 24, 0)),    # urechi-păstaie
        ("hood", tuple(hc + Vector((-0.088, 0.01, 0.125))), (0.042, 0.034, 0.085), "acc_hood", (0, -24, 0)),
        ("neg", tuple(hc + Vector((0, -0.115, -0.012))), (0.092, 0.075, 0.088), "acc_hood"),               # fața liberă
    ]
    b.organic(hood, {"hood": pod}, set_bvh=False)

    # mlădiță în vârf: tulpiniță + două frunzulițe
    top = hc + Vector((0, 0.012, 0.014 + 0.122))
    stem = [top + Vector((0, 0.002 * t, 0.032 * t)) for t in (i / 4 for i in range(5))]
    b.loft(stem, [0.006, 0.0055, 0.005, 0.0045, 0.004], 1.0, leaf, "acc_leaf", ring=10)
    tip = stem[-1]
    for sx, ang in ((1, -32), (-1, 32)):
        b.sphere((tip.x + sx * 0.022, tip.y, tip.z + 0.006), (0.026, 0.012, 0.0045), leaf, "acc_leaf", rot=(0, ang, 0), segs=16, rings=8)

    # bandană: o bandă moale pe gât + un triunghi drapat pe piept, lipite de corp (raycast) și cu grosime
    neck_c = Vector((0.0, -0.06, 0.165))
    ring_pts, ring_n = [], []
    for i in range(32):
        a = i / 32 * math.tau
        d = Vector((math.cos(a), math.sin(a), 0.42 * math.sin(a))).normalized()   # în față coboară spre piept
        hp, hn = b.surface(neck_c, d)
        ring_pts.append(hp + hn * 0.006)
        ring_n.append(hn)
    b.loft(ring_pts, [0.006] * 32, 2.6, scarf, "acc_scarf", ring=12, loop=True, normals=ring_n)
    # triunghiul: de la bandă (stânga-dreapta, în față) până la vârful de pe piept
    left = ring_pts[int(32 * 0.75) - 5]
    right = ring_pts[int(32 * 0.75) + 5]
    mid_top = ring_pts[int(32 * 0.75)]
    tipp = mid_top + Vector((0, -0.012, -0.085))
    chest_c = Vector((0.0, -0.04, 0.13))
    rows_n, cols_n = 7, 9
    grid = []
    for r in range(rows_n):
        t = r / (rows_n - 1)
        l = left.lerp(tipp, t)
        rgt = right.lerp(tipp, t)
        row = []
        for c in range(cols_n):
            q = l.lerp(rgt, c / (cols_n - 1))
            sag = math.sin(math.pi * c / (cols_n - 1)) * 0.012 * (1 - t)          # un mic pliu drapat
            q = q + Vector((0, 0, -sag))
            hp, hn = b.surface(chest_c, q - chest_c)
            row.append(hp + hn * (0.005 + 0.002 * math.sin(math.pi * c / (cols_n - 1))))
        grid.append(row)
    b.panel(grid, 0.004, scarf, "acc_scarf")
    tip_pt = grid[-1][cols_n // 2]

    # păstaia-breloc atârnată la vârful bandanei
    hang = [tip_pt + Vector((0.005 * math.sin(t * 2.4), -0.006, -0.008 - 0.036 * t)) for t in (i / 6 for i in range(7))]
    b.loft(hang, [0.005, 0.01, 0.012, 0.013, 0.011, 0.008, 0.004], 0.62, pod, "acc_pod", ring=14)
    for k in range(3):
        c = hang[2 + k]
        b.sphere((c.x, c.y - 0.008, c.z), (0.0075, 0.006, 0.0075), pea, "acc_pod", segs=12, rings=8)


VARIANTS = {
    # nume:      (accesorii, oase-accesorii (nume, cap, coadă, părinte), animație specială, fișier export)
    "Base": (None, [], None, None),
    "Mochi": (build_mochi, [
        ("acc_band", (0.0, -0.08, 0.34), (0.0, -0.08, 0.38), "head"),
        ("acc_bunny.L", (0.04, -0.08, 0.35), (0.07, -0.06, 0.47), "acc_band"),
        ("acc_bunny.R", (-0.04, -0.08, 0.35), (-0.07, -0.06, 0.47), "acc_band"),
    ], "Run_Zoomie", "corgi_mochi"),
    "Gogoasa": (build_gogoasa, [
        ("acc_donut", (0.0, 0.035, 0.112), (0.0, 0.035, 0.17), "spine"),
    ], "Trotting_Decoy", "corgi_gogoasa"),
    "Fasolica": (build_fasolica, [
        ("acc_hood", (0.0, -0.1, 0.3), (0.0, -0.1, 0.37), "head"),
        ("acc_leaf", (0.0, -0.098, 0.376), (0.0, -0.096, 0.41), "acc_hood"),
        ("acc_scarf", (0.0, -0.06, 0.165), (0.0, -0.15, 0.13), "spine"),
        ("acc_pod", (0.0, -0.2, 0.09), (0.0, -0.21, 0.04), "acc_scarf"),
    ], "Clingy_Nod", "corgi_fasolica"),
}

# ============================================================================ armature

def ensure_object_mode():
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")


def build_armature(name, collection, extra_bones):
    arm_data = bpy.data.armatures.new(name + "_Rig")
    arm_data.display_type = "STICK"
    arm = bpy.data.objects.new(name, arm_data)
    collection.objects.link(arm)
    ensure_object_mode()
    for o in bpy.context.selected_objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm_data.edit_bones
    for bname, head, tail, parent in BASE_BONES + list(extra_bones):
        bone = eb.new(bname)
        bone.head = Vector(head)
        bone.tail = Vector(tail)
        bone.roll = 0.0
        if parent:
            bone.parent = eb[parent]
            bone.use_connect = False
        # accesoriile nu deformează nimic în afară de ele însele; toate oasele deformează (piese rigide)
        bone.use_deform = True
    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
    return arm

# ============================================================================ animații

def key(arm, frame, bone, rot=None, loc=None, scale=None):
    pb = arm.pose.bones.get(bone)
    if pb is None:
        return
    if rot is not None:
        pb.rotation_euler = Euler([math.radians(a) for a in rot], "XYZ")
        pb.keyframe_insert("rotation_euler", frame=frame)
    if loc is not None:
        pb.location = Vector(loc)
        pb.keyframe_insert("location", frame=frame)
    if scale is not None:
        pb.scale = Vector(scale)
        pb.keyframe_insert("scale", frame=frame)


def reset_pose(arm):
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)


def make_action(arm, clip, length, keyer):
    """Creează o acțiune, o cheiază cu keyer(arm, frames) și o pune pe o pistă NLA numită ca clipul."""
    reset_pose(arm)
    act = bpy.data.actions.new(f"{arm.name}|{clip}")
    act.use_fake_user = True
    if arm.animation_data is None:
        arm.animation_data_create()
    arm.animation_data.action = act
    keyer(arm, length)
    try:
        for fc in act.fcurves:
            for kp in fc.keyframe_points:
                kp.interpolation = "BEZIER"
    except Exception:
        pass   # Blender 5.x: curbele stau în canalele sloturilor; interpolarea implicită e oricum Bézier
    arm.animation_data.action = None
    track = arm.animation_data.nla_tracks.new()
    track.name = clip
    strip = track.strips.new(clip, 1, act)
    strip.name = clip
    try:
        if hasattr(strip, "action_slot") and len(act.slots) > 0:
            strip.action_slot = act.slots[0]
    except Exception:
        pass
    # pistele stau dezactivate în scenă (poza de repaus rămâne curată); exportul FBX le activează temporar
    track.mute = True
    reset_pose(arm)
    return act


def loop_keys(n, steps):
    """Cadrele 1..n împărțite în `steps` pași egali (inclusiv cadrul final = primul, pentru buclă)."""
    return [1 + round(i * (n - 1) / steps) for i in range(steps + 1)]


def anim_idle(arm, n):
    fr = loop_keys(n, 4)
    for i, f in enumerate(fr):
        ph = i % 4
        breathe = (0, 0.004, 0, 0.004)[ph] if i < 4 else 0
        key(arm, f, "spine", loc=(0, 0, breathe), scale=(1, 1, 1 + breathe * 4))
        key(arm, f, "head", rot=((0, 3, 0, -2)[ph] if i < 4 else 0, 0, (0, 4, 0, -4)[ph] if i < 4 else 0))
        key(arm, f, "tail", rot=(0, 0, (0, 18, 0, -18)[ph] if i < 4 else 0))
        key(arm, f, "ear.L", rot=(0, 0, (0, 0, 6, 0)[ph] if i < 4 else 0))
        key(arm, f, "ear.R", rot=(0, 0, (0, 0, -6, 0)[ph] if i < 4 else 0))
        for b in ("acc_bunny.L", "acc_bunny.R", "acc_leaf"):
            key(arm, f, b, rot=((0, 5, 0, -5)[ph] if i < 4 else 0, 0, 0))


def anim_run(arm, n, swing=38, bob=0.018, tail=45):
    """Galop jucăuș (Run_Zoomie): picioarele din față și din spate alternează, spatele țopăie."""
    fr = loop_keys(n, 4)
    for i, f in enumerate(fr):
        ph = i % 4
        s = (1, 0, -1, 0)[ph] if i < 4 else 1
        c = (0, 1, 0, -1)[ph] if i < 4 else 0
        key(arm, f, "leg.FL", rot=(s * swing, 0, 0))
        key(arm, f, "leg.FR", rot=(s * swing * 0.8, 0, 0))
        key(arm, f, "leg.BL", rot=(-s * swing, 0, 0))
        key(arm, f, "leg.BR", rot=(-s * swing * 0.8, 0, 0))
        key(arm, f, "spine", loc=(0, 0, abs(c) * bob), rot=(c * 5, 0, 0))
        key(arm, f, "head", rot=(-c * 6, 0, 0))
        key(arm, f, "tail", rot=(0, 0, s * tail))
        key(arm, f, "ear.L", rot=(-abs(c) * 14, 0, 0))
        key(arm, f, "ear.R", rot=(-abs(c) * 14, 0, 0))
        key(arm, f, "acc_bunny.L", rot=(-abs(c) * 30 + 8, 0, s * 8))
        key(arm, f, "acc_bunny.R", rot=(-abs(c) * 30 + 8, 0, -s * 8))


def anim_trot(arm, n):
    """Trap mândru (Trotting_Decoy): perechi diagonale, capul sus, gogoașa se leagănă."""
    fr = loop_keys(n, 4)
    for i, f in enumerate(fr):
        ph = i % 4
        s = (1, 0, -1, 0)[ph] if i < 4 else 1
        c = (0, 1, 0, -1)[ph] if i < 4 else 0
        key(arm, f, "leg.FL", rot=(s * 26, 0, 0))
        key(arm, f, "leg.BR", rot=(s * 26, 0, 0))
        key(arm, f, "leg.FR", rot=(-s * 26, 0, 0))
        key(arm, f, "leg.BL", rot=(-s * 26, 0, 0))
        key(arm, f, "spine", loc=(0, 0, abs(c) * 0.01), rot=(0, c * 3, 0))
        key(arm, f, "head", rot=(-6, 0, c * 5))
        key(arm, f, "tail", rot=(0, 0, s * 25))
        key(arm, f, "acc_donut", rot=(c * 3, s * 4, 0), loc=(0, 0, abs(c) * 0.006))


def anim_nod(arm, n):
    """Clingy_Nod: dă din cap cerând atenție, coada și frunza se leagănă."""
    fr = loop_keys(n, 4)
    for i, f in enumerate(fr):
        ph = i % 4
        nod = (0, 14, 0, 14)[ph] if i < 4 else 0
        key(arm, f, "head", rot=(nod, 0, (0, 6, 0, -6)[ph] if i < 4 else 0))
        key(arm, f, "tail", rot=(0, 0, (30, -30, 30, -30)[ph] if i < 4 else 30))
        key(arm, f, "acc_leaf", rot=(0, (0, 12, 0, -12)[ph] if i < 4 else 0, 0))
        key(arm, f, "acc_pod", rot=((0, -6, 0, -6)[ph] if i < 4 else 0, 0, 0))
        key(arm, f, "spine", loc=(0, 0, (0, -0.004, 0, -0.004)[ph] if i < 4 else 0))
        key(arm, f, "ear.L", rot=(0, 0, (0, 8, 0, 8)[ph] if i < 4 else 0))
        key(arm, f, "ear.R", rot=(0, 0, (0, -8, 0, -8)[ph] if i < 4 else 0))


def anim_sleep(arm, n):
    """Sleep_Lay: întins pe o parte, lăbuțele strânse, capul lăsat, respirație lentă (pentru Pătura Moale)."""
    fr = loop_keys(n, 2)
    for i, f in enumerate(fr):
        breathe = 0.03 if i == 1 else 0.0
        key(arm, f, "root", rot=(0, 0, 72), loc=(0, 0.05, 0))
        key(arm, f, "spine", scale=(1 + breathe, 1, 1 + breathe))
        key(arm, f, "head", rot=(22, 0, -10))
        key(arm, f, "leg.FL", rot=(-50, 0, 0))
        key(arm, f, "leg.FR", rot=(-45, 0, 0))
        key(arm, f, "leg.BL", rot=(50, 0, 0))
        key(arm, f, "leg.BR", rot=(45, 0, 0))
        key(arm, f, "tail", rot=(0, 0, 35))
        key(arm, f, "ear.L", rot=(20, 0, 10))
        key(arm, f, "ear.R", rot=(20, 0, -10))
        for b in ("acc_bunny.L", "acc_bunny.R"):
            key(arm, f, b, rot=(35, 0, 0))


SPECIALS = {
    "Run_Zoomie": (16, anim_run),
    "Trotting_Decoy": (24, anim_trot),
    "Clingy_Nod": (40, anim_nod),
}

# ============================================================================ construcție

def clear_scene():
    ensure_object_mode()
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for c in list(bpy.data.collections):
        if c.name.startswith("Corgi_"):
            bpy.data.collections.remove(c)
    for a in list(bpy.data.actions):
        if "|" in a.name:
            bpy.data.actions.remove(a)
    _mats.clear()


def build_variant(variant, x_offset):
    builder_fn, extra, special, _ = VARIANTS[variant]
    name = "Corgi_" + variant
    coll = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(coll)

    p = palette()
    b = Builder()
    build_base(b, p, variant, ears=(variant != "Fasolica"))
    if builder_fn:
        builder_fn(b, p)
    mesh = b.to_object(name + "_Mesh", coll)
    if SUBDIV_LEVELS > 0:
        sub = mesh.modifiers.new("Subdivision", "SUBSURF")
        sub.levels = SUBDIV_LEVELS
        sub.render_levels = SUBDIV_LEVELS
        sub.quality = 3

    arm = build_armature(name, coll, extra)
    mesh.parent = arm
    mod = mesh.modifiers.new("Armature", "ARMATURE")
    mod.object = arm

    # animații
    make_action(arm, "Idle", 60, anim_idle)
    if special:
        length, fn = SPECIALS[special]
        make_action(arm, special, length, fn)
    make_action(arm, "Sleep_Lay", 60, anim_sleep)

    arm.location = (x_offset, 0, 0)
    return arm, mesh


def eval_tris(obj):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(dg)
    me = ev.to_mesh()
    n = sum(len(poly.vertices) - 2 for poly in me.polygons)
    ev.to_mesh_clear()
    return n


def export_fbx(arm, mesh, path):
    ensure_object_mode()
    # Subdivision rămâne pentru aspectul moale; pentru Quest reducem temporar la EXPORT_MAX_TRIS (Decimate „collapse”
    # păstrează forma rotundă, granițele de culoare și ponderile pe oase)
    dec = None
    if EXPORT_SUBDIV and EXPORT_MAX_TRIS > 0:
        n = eval_tris(mesh)
        if n > EXPORT_MAX_TRIS:
            dec = mesh.modifiers.new("ExportDecimate", "DECIMATE")
            dec.decimate_type = "COLLAPSE"
            dec.ratio = EXPORT_MAX_TRIS / n
            dec.use_collapse_triangulate = True
    old = arm.location.copy()
    arm.location = (0, 0, 0)
    for o in bpy.context.selected_objects:
        o.select_set(False)
    arm.select_set(True)
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = arm
    tracks = list(arm.animation_data.nla_tracks) if arm.animation_data else []
    for t in tracks:
        t.mute = False          # exportorul FBX sare peste pistele dezactivate
    bpy.context.view_layer.update()
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,          # fără rotația de -90° pe X în Unity
        use_mesh_modifiers=EXPORT_SUBDIV,    # aplică Subdivision (armătura rămâne skin, nu se aplică)
        add_leaf_bones=False,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=True,       # fiecare pistă NLA = un clip (Idle, Run_Zoomie, Sleep_Lay ...)
        bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.0,
        path_mode="AUTO",
        mesh_smooth_type="FACE",
    )
    for t in tracks:
        t.mute = True
    if dec is not None:
        mesh.modifiers.remove(dec)
    reset_pose(arm)          # exportorul lasă oasele în ultima animație evaluată (ex. Sleep_Lay)
    arm.location = old
    print("[MindThePaws] exportat:", path)


def render_preview(path, arms, focus=None):
    """Previzualizare rapidă (Workbench): toți cățeii din față-lateral, sau un prim-plan pe un singur cățel (focus)."""
    scene = bpy.context.scene
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    scene.collection.objects.link(cam)
    xs = [a.location.x for a in arms]
    cx = (min(xs) + max(xs)) * 0.5
    if focus is not None:
        cx = focus.location.x
        cam.location = (cx + 0.45, -1.05, 0.42)
        target = Vector((cx, -0.02, 0.19))
        cam_data.lens = 50
    else:
        cam.location = (cx + 0.5, -2.9, 0.75)
        target = Vector((cx, 0.0, 0.17))
        cam_data.lens = 42
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.engine = "BLENDER_WORKBENCH"
    try:
        scene.display.shading.light = "STUDIO"
        scene.display.shading.color_type = "MATERIAL"
        scene.display.shading.show_cavity = True
    except Exception:
        pass
    scene.render.resolution_x = 1400 if focus is None else 900
    scene.render.resolution_y = 560 if focus is None else 900
    scene.render.film_transparent = False
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("[MindThePaws] previzualizare:", path)


# ============================================================================ iconițe / portrete (Cycles, fundal transparent)

def setup_icon_render(size=512, samples=48):
    """Cycles cu lumini moi de studio, fundal transparent, culori „Standard” (pastelurile rămân vii)."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    try:
        scene.cycles.use_denoising = True
    except Exception:
        pass
    scene.render.film_transparent = True
    scene.render.resolution_x = size
    scene.render.resolution_y = size
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    try:
        scene.view_settings.view_transform = "Standard"
        scene.view_settings.look = "None"
        scene.view_settings.exposure = -0.7
    except Exception:
        pass
    world = scene.world or bpy.data.worlds.new("IconWorld")
    scene.world = world
    try:
        if not getattr(world, "node_tree", None):
            world.use_nodes = True
        bg = world.node_tree.nodes.get("Background")
        if bg:
            bg.inputs["Color"].default_value = (1.0, 0.96, 0.95, 1.0)
            bg.inputs["Strength"].default_value = 0.35
    except Exception:
        pass


def icon_lights(target, scale):
    """Trei lumini moi (cheie caldă, umplere rece, contur) în jurul țintei; întoarce obiectele, ca să fie șterse."""
    made = []
    for name, off, energy, color, size in (
            ("IconKey", (-1.2, -1.6, 1.6), 90.0, (1.0, 0.95, 0.9), 1.4),
            ("IconFill", (1.6, -1.2, 0.8), 35.0, (0.9, 0.95, 1.0), 1.6),
            ("IconRim", (0.4, 1.6, 1.4), 60.0, (1.0, 0.92, 0.95), 1.0)):
        ld = bpy.data.lights.new(name, "AREA")
        ld.energy = energy * scale * scale
        ld.color = color
        ld.size = size * scale
        lo = bpy.data.objects.new(name, ld)
        bpy.context.scene.collection.objects.link(lo)
        lo.location = target + Vector(off) * scale
        lo.rotation_euler = (target - lo.location).to_track_quat("-Z", "Y").to_euler()
        made.append(lo)
    return made


def render_icon(path, cam_pos, target, lens=50):
    scene = bpy.context.scene
    cam = bpy.data.objects.new("IconCam", bpy.data.cameras.new("IconCam"))
    scene.collection.objects.link(cam)
    cam.location = cam_pos
    cam.rotation_euler = (Vector(target) - Vector(cam_pos)).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = lens
    scene.camera = cam
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)
    print("[MindThePaws] iconiță:", path)


def render_portraits(built, out_dir):
    """Portret 3/4 din față pentru fiecare cățel (tot corpul), fundal transparent → poza Polaroid din joc."""
    os.makedirs(out_dir, exist_ok=True)
    setup_icon_render(512, 48)
    for v in ("Mochi", "Gogoasa", "Fasolica"):
        arm, mesh = built[v]
        for other, (a2, m2) in built.items():
            m2.hide_render = other != v
        x = arm.location.x
        target = Vector((x, -0.02, 0.2))
        lights = icon_lights(target, 0.9)
        render_icon(os.path.join(out_dir, VARIANTS[v][3] + ".png"), Vector((x + 0.42, -1.05, 0.42)), target, lens=50)
        for l in lights:
            bpy.data.objects.remove(l, do_unlink=True)
    for a2, m2 in built.values():
        m2.hide_render = False


def main():
    opts = parse_args()
    clear_scene()
    bpy.context.scene.render.fps = FPS
    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end = 60
    built = {}
    for i, v in enumerate(["Base", "Mochi", "Gogoasa", "Fasolica"]):
        built[v] = build_variant(v, i * 0.6)
    if opts["export"]:
        out = opts["out"] or default_export_dir()
        os.makedirs(out, exist_ok=True)
        for v in ("Mochi", "Gogoasa", "Fasolica"):
            arm, mesh = built[v]
            export_fbx(arm, mesh, os.path.join(out, VARIANTS[v][3] + ".fbx"))
    if opts["icons"]:
        render_portraits(built, opts["icons"])
    if opts["preview"]:
        arms = [a for a, _ in built.values()]
        render_preview(opts["preview"], arms)
        root, ext = os.path.splitext(opts["preview"])
        for v in ("Mochi", "Gogoasa", "Fasolica"):
            render_preview(root + "_" + v.lower() + ext, arms, focus=built[v][0])
    if opts["save"]:
        bpy.ops.wm.save_as_mainfile(filepath=opts["save"])
    print("[MindThePaws] gata: Corgi_Base, Corgi_Mochi, Corgi_Gogoasa, Corgi_Fasolica")


if __name__ == "__main__":
    main()
