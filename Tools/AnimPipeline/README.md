# Simitone Custom 3D Skeletal Animation Pipeline (`Tools/AnimPipeline`)

Offline authoring and retargeting toolkit targeting The Sims 1 / FreeSO `adult.skel` (29 bones) and outputting hot-reloadable `.simanim.json` v1 clips.

## 1. Extract `adult.skel` Reference (one-time)
```powershell
& "C:\Program Files\Python311\python.exe" Tools\AnimPipeline\dump_adult_skel.py
```
Reads `adult-skeleton.cmx.bcf` from `Animation.far` and writes `adult_skel_ref.json` (29 exact FreeSO right-handed rest transforms + visual T-pose reference).

## 2. Build / Open Blender 4.5 IK Rig
```powershell
& "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" --factory-startup --background --python Tools\AnimPipeline\blender_rig_setup.py
```
Open `Tools/AnimPipeline/adult_rig.blend` in Blender 4.5 to inspect or tweak any Action in Pose Mode (unmute `LegIK_L`/`LegIK_R` constraints to pose legs with IK).

## 3. Generate & Export the Cadet Academy Custom Pack
```powershell
& "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" --factory-startup --background Tools\AnimPipeline\adult_rig.blend --python Tools\AnimPipeline\generate_cadet_pack.py
```
Bakes all 7 Cadet Actions into `adult_rig.blend` and exports `Tools/Conversations/Assets/Animations/cadet_*.simanim.json`.

## 4. Export Any Custom Blender Action
```powershell
& "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe" --factory-startup --background Tools\AnimPipeline\adult_rig.blend --python Tools\AnimPipeline\blender_export_simanim.py -- --action MyAction --out Tools\Conversations\Assets\Animations\my_clip.simanim.json --fps 30
```

## 5. Retarget License-Clean CC-BY BVH Mocap (100STYLE / ACCAD / CMU)
```powershell
& "C:\Program Files\Python311\python.exe" Tools\AnimPipeline\bvh_to_simanim.py path\to\run.bvh Tools\Conversations\Assets\Animations\my_run.simanim.json --name my_run --loop --in-place
```

## 6. Run Pipeline Verification Suite
```powershell
& "C:\Program Files\Python311\python.exe" Tools\AnimPipeline\verify_pipeline.py
```
