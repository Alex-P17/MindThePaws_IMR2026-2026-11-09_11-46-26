"""
Mind the Paws — exportă ceașca cu flori (Flower Teacup) pentru Unity / Quest 3.

Modelul original e foarte detaliat (~600k triunghiuri cu Subdivision) și are materiale procedurale
(gradienți, zgomot, atribute), pe care FBX nu le poate transporta. Scriptul:
  1. ia toate mesh-urile vizibile (fără fundalul „Backdrop”), cu modificatorii aplicați, și le unește într-un singur mesh;
  2. îl reduce la ~TARGET_TRIS triunghiuri (Decimate), îi face UV-uri (Smart UV Project);
  3. „coace” (bake, Cycles) culoarea materialelor procedurale de pe modelul original pe o textură (selected → active);
  4. exportă tea_cup.fbx cu textura inclusă în Assets/Resources/MindThePaws/Models (cheia „tea_cup” folosită de joc).
Fișierul .blend original NU este modificat (nu se salvează nimic peste el).

Rulare:
  blender --background "C:/.../phase4_materials.blend" --python export_teacup.py -- --out "C:/.../Assets/Resources/MindThePaws/Models"
  opțional: --tris 14000  --tex 1024  --preview "C:/cale/preview.png"
            --icon "C:/.../Assets/Resources/MindThePaws/Icons/mug.png"   (iconița din inventar, fundal transparent)
"""

import bpy
import bmesh
import math
import os
import sys
from mathutils import Vector

TARGET_TRIS = 14000
TEX_SIZE = 1024
SKIP = {"Backdrop"}


def args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    o = {"out": None, "tris": TARGET_TRIS, "tex": TEX_SIZE, "preview": None, "name": "tea_cup", "icon": None}
    i = 0
    while i < len(argv):
        k = argv[i]
        if k in ("--out", "--preview", "--name", "--icon") and i + 1 < len(argv):
            o[k[2:]] = argv[i + 1]; i += 1
        elif k == "--tris" and i + 1 < len(argv):
            o["tris"] = int(argv[i + 1]); i += 1
        elif k == "--tex" and i + 1 < len(argv):
            o["tex"] = int(argv[i + 1]); i += 1
        i += 1
    if not o["out"]:
        here = os.path.dirname(os.path.abspath(__file__))
        o["out"] = os.path.normpath(os.path.join(here, "..", "..", "Resources", "MindThePaws", "Models"))
    return o


def high_objects():
    return [o for o in bpy.context.scene.objects
            if o.type == "MESH" and o.name not in SKIP and o.visible_get() and not o.hide_render]


def build_low(highs, name):
    """Un singur mesh din toate piesele, cu modificatorii aplicați și transformările în coordonate lume."""
    dg = bpy.context.evaluated_depsgraph_get()
    bm = bmesh.new()
    for o in highs:
        ev = o.evaluated_get(dg)
        me = ev.to_mesh()
        tmp = bmesh.new()
        tmp.from_mesh(me)
        tmp.transform(o.matrix_world)
        me2 = bpy.data.meshes.new("tmp")
        tmp.to_mesh(me2)
        tmp.free()
        bm.from_mesh(me2)
        bpy.data.meshes.remove(me2)
        ev.to_mesh_clear()
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for poly in me.polygons:
        poly.use_smooth = True
    low = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(low)
    return low


def tri_count(obj):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(dg)
    me = ev.to_mesh()
    n = sum(len(p.vertices) - 2 for p in me.polygons)
    ev.to_mesh_clear()
    return n


def decimate(low, target):
    n = tri_count(low)
    if n <= target:
        return n
    mod = low.modifiers.new("Decimate", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = target / n
    mod.use_collapse_triangulate = True
    dg = bpy.context.evaluated_depsgraph_get()
    new_me = bpy.data.meshes.new_from_object(low.evaluated_get(dg))
    old = low.data
    low.modifiers.remove(mod)
    low.data = new_me
    bpy.data.meshes.remove(old)
    return tri_count(low)


def select_only(objs, active):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active


def unwrap(low):
    select_only([low], low)
    for uv in list(low.data.uv_layers):
        low.data.uv_layers.remove(uv)
    low.data.uv_layers.new(name="UVMap")
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.004, area_weight=0.0, scale_to_bounds=True)
    bpy.ops.object.mode_set(mode="OBJECT")


def bake(highs, low, size, out_dir, name):
    img = bpy.data.images.new(name + "_BaseColor", size, size, alpha=False)
    img.generated_color = (0.8, 0.8, 0.8, 1.0)
    mat = bpy.data.materials.new("M_" + name)
    if not getattr(mat, "node_tree", None):
        mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.35            # glazură de porțelan
    nt.nodes.active = tex
    low.data.materials.clear()
    low.data.materials.append(mat)

    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 4
    bk = scene.render.bake
    bk.use_selected_to_active = True
    bk.cage_extrusion = 0.0025
    bk.max_ray_distance = 0.006
    bk.margin = 6
    bk.use_pass_direct = False
    bk.use_pass_indirect = False
    bk.use_pass_color = True
    select_only(highs + [low], low)
    bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"}, use_selected_to_active=True,
                        cage_extrusion=0.0025, max_ray_distance=0.006, margin=6)
    path = os.path.join(out_dir, name + "_basecolor.png")
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return img, path


def center_on_base(low):
    """Originea la baza obiectului, centrată pe X/Y (cum o așteaptă jocul)."""
    me = low.data
    xs = [v.co.x for v in me.vertices]; ys = [v.co.y for v in me.vertices]; zs = [v.co.z for v in me.vertices]
    off = Vector(((min(xs) + max(xs)) * 0.5, (min(ys) + max(ys)) * 0.5, min(zs)))
    for v in me.vertices:
        v.co -= off


def export(low, path):
    select_only([low], low)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        use_mesh_modifiers=True, mesh_smooth_type="FACE",
        path_mode="COPY", embed_textures=True, bake_anim=False)


def preview(low, path):
    scene = bpy.context.scene
    for o in scene.objects:
        o.hide_render = o is not low
    cam = bpy.data.objects.new("PrevCam", bpy.data.cameras.new("PrevCam"))
    scene.collection.objects.link(cam)
    dims = low.dimensions
    cam.location = (dims.x * 1.6, -dims.y * 2.4, dims.z * 2.2)
    cam.rotation_euler = (Vector((0, 0, dims.z * 0.45)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.render.resolution_x = 900
    scene.render.resolution_y = 900
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


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


def make_icon(low, path):
    """Iconița ceștii pentru inventar: ușor de sus și din față, pe fundal transparent."""
    scene = bpy.context.scene
    for ob in scene.objects:
        if ob.type in ("MESH", "LIGHT") and ob is not low:
            ob.hide_render = True
    os.makedirs(os.path.dirname(path), exist_ok=True)
    setup_icon_render(512, 48)
    dims = low.dimensions
    target = Vector((0.0, 0.0, dims.z * 0.45))
    s_ = max(dims.x, dims.y, dims.z)
    lights = icon_lights(target, s_ * 2.2)
    render_icon(path, target + Vector((s_ * 1.0, -s_ * 1.9, s_ * 1.15)), target, lens=62)
    for l in lights:
        bpy.data.objects.remove(l, do_unlink=True)


def main():
    o = args()
    os.makedirs(o["out"], exist_ok=True)
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    highs = high_objects()
    print("[MindThePaws] piese:", len(highs), "triunghiuri original:", sum(tri_count(h) for h in highs))
    low = build_low(highs, o["name"])
    print("[MindThePaws] după reducere:", decimate(low, o["tris"]))
    unwrap(low)
    img, tex_path = bake(highs, low, o["tex"], o["out"], o["name"])
    center_on_base(low)
    fbx = os.path.join(o["out"], o["name"] + ".fbx")
    export(low, fbx)
    print("[MindThePaws] exportat:", fbx, "+", tex_path)
    if o["icon"]:
        make_icon(low, o["icon"])
    if o["preview"]:
        preview(low, o["preview"])


if __name__ == "__main__":
    main()
