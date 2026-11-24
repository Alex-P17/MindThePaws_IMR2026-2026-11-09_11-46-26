Pune aici modelele (.fbx / .blend / prefab; .glb doar cu pachetul glTFast).
Numele fișierului = cheia, fără extensie. Jocul le preia automat, le scalează și le scoate colliderele.

Comori: mug, keys, diary, glasses, glove, lantern
Personaje: corgi_mochi, corgi_gogoasa, corgi_fasolica, corgi_barker, spirit_cat, fairy_lumi
Mediu & mini-jocuri: paw, flower, star_jar, constellation_star, bakery_plate, cookie_star, cookie_paw, cookie_heart, kettle, tea_cup

Orientare: modelul trebuie să privească spre +Z (în Blender: Forward -Y, Up Z la export FBX, sau Apply Transform).
Pivotul nu contează: jocul îl mută singur la baza modelului.

Cățeii corgi (corgi_mochi / corgi_gogoasa / corgi_fasolica) se pot genera cu Assets/MindThePaws/BlenderScripts/build_corgis.py:
  blender --background --factory-startup --python Assets/MindThePaws/BlenderScripts/build_corgis.py -- --export
Se importă automat ca animație Legacy cu clipuri în buclă (Idle, Run_Zoomie, Trotting_Decoy, Clingy_Nod, Sleep_Lay).
