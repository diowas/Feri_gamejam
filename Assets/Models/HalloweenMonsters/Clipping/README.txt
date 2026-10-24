CLIPPING  –  "it's not a bug, it's a feature"
================================================

Lore pitch: the ceiling mesh had a hole in it. Something grew through.
It's always clipping halfway into the ceiling tiles, and every one of its eyes
is on you. You only really see it out of the corner of your eye.

FILES
  Clipping.obj / .mtl       ~6,700 tris, ~1.2 m wide, hangs ~0.6 m down. Origin = the ceiling plane.
                            17 separate eye objects (eye_00 ... eye_14, eye_stalk_0/1), irises face -Y at rest.
  Clipping_tex.png          256x256 atlas: 8 eye types (double pupil, goat, cat slit, blind, ...), flesh, ooze
  Clipping_emission.png     eyeshine: only the pupils glow, like animal eyes in a torch beam
  CeilingWatcher.cs         behaviour script
  Clipping_preview.png      left/middle: lit by a flashlight, right: what it looks like in the dark

UNITY
  Same import as NULLREF:
  1. Drop the folder into Assets/.
  2. Textures: Filter Mode = Point, Compression = None, Mip Maps OFF.
  3. Material: Albedo = Clipping_tex.png, Smoothness 0, Emission ON with map = Clipping_emission.png
     (colour white - the script drives the brightness). Render Face = Both on URP Lit.
     Remap it in the .obj's Materials tab.
  4. Model: Normals = Import. (Read/Write is NOT needed for this one.)
  5. Drag it into the scene and place it so its origin sits exactly at ceiling height.
     The top of the blob disappears into the ceiling - that's the point.
  6. Add CeilingWatcher to the root. Check the hierarchy: each eye_xx should be its own child.
     Press Play - every eye turns to follow you.
  7. Optional: drag your NULLREF object into "Hunter". If you stand under the Watcher for
     1.5 s, every eye snaps onto you, it flares, and NULLREF teleports behind you.

BLENDER
  File > Import > Wavefront (.obj); every eye comes in as its own object.
  The eyes all look straight down in the file - the script aims them at runtime.
  To pose them for a screenshot, set each eye's origin to its geometry
  (Object > Set Origin > Origin to Geometry) and rotate it.

TUNING
  - Place several around the level, some in places the player walks under, some in dark
    corners where they're only visible as glowing pupils.
  - laggardChance higher = more eyes drift late after you move, which is very unsettling.
  - Want them only visible in peripheral vision? Set Direct Glow to 0.
