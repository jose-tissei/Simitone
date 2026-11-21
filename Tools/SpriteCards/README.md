# SpriteCards: fixed original camera, meshes for most objects, sprites for chosen ones

Simitone's free 3D camera builds object meshes from the original 2D sprites, and some come out
poor (magic portal, radio, spellbook 2.0). This keeps the engine's fixed, original camera
(Hybrid2D, toggled with **F12** in a lot) and lets you pick, per object, the original sprite
or the mesh. No billboard maths is needed because the camera always matches the sprite angle.

## Use
1. Close the game, build both projects, back up `FSO.LotView.dll`, then (from the original DLL):

       dotnet build Tools/SpriteCards -c Release -o Tools/SpriteCards/out
       dotnet build Tools/SpriteCardsPatch -c Release -o Tools/SpriteCardsPatch/out
       dotnet Tools/SpriteCardsPatch/out/SpriteCardsPatch.dll <appDir> Tools/SpriteCards/out/SpriteCards.dll <outDir>

   Copy `<outDir>\FSO.LotView.dll` and `SpriteCards.dll` into the app dir.
2. Create `<appDir>\Content\spritecards.txt` (see `spritecards.example.txt`).
3. Launch with `-3d`, enter a lot, press **F12** (Full3D -> Hybrid2D). To apply list edits, press
   F12 twice (rebuilds the lot). Object file names are logged to `Content\spritecards.log`.

## What the patch does
- `ObjectComponent.{DoDraw,Draw,Update,Preload}` call `SpriteCards.Apply(this, world)`, which sets
  the object's render mode (sprite or mesh) from the list and sets `WorldState.ForceImmediate`
  (objects must be drawn live, in order, to mix both kinds; the default path bakes them into a
  cached static buffer).
- `_2DWorldBatch.DrawImmediate(_2DStandaloneSprite)` only re-binds the sprite quad index buffer
  when `!FSOEnvironment.DirectX`. A mesh draw replaces that buffer, so later sprites were drawn
  with the wrong triangles (skewed, stretched). The patch makes it always re-bind.

## Notes
- Hybrid2D loads both sprite and mesh data; Full2D does not load meshes, Full3D does not load sprites.
- Release DLLs are patched in place (Mono.Cecil). Keep the originals.
