NULLREF  –  "it's not a bug, it's a feature"   (pumpkin scarecrow edition)
==========================================================================

Lore pitch: something in the faculty failed to load. It never got a walk animation,
so it slides, frozen in a T-pose like a scarecrow. Someone sliced the top off its
pumpkin head and never put it back right - the lid floats, crooked, still hanging
on by strings of pulp. Look into the carved holes and something human looks back.

FILES
  NullRef.obj / .mtl       ~7,700 tris, 2.7 m tall, Y-up, faces +Z, pivot at the feet, smooth normals
  NullRef_tex.png          256x256 atlas (skin, burlap, carved face, pumpkin, straw, vine, rope, slice, error)
  NullRef_emission.png     glow map: carved holes + the inside of the sliced pumpkin
  NullRefEntity.cs         behaviour script
  NullRef_preview.png      software-rendered preview

UNITY IMPORT
  1. Drop the folder into Assets/.
  2. Model import: Read/Write Enabled = ON (script deforms the mesh), Normals = Import.
  3. Both textures: Filter Mode = Point, Compression = None, Generate Mip Maps = OFF.
  4. Material: Albedo = NullRef_tex.png. Enable Emission, map = NullRef_emission.png,
     colour = orange. Turn on double-sided / Cull Off if your shader allows
     (the rags, leaves and pulp strings are single-sided planes).
  5. Add NullRefEntity to the root, put it on the "Ignore Raycast" layer.
     Optional: NavMeshAgent + baked NavMesh.
  6. For the full effect: add an orange Point Light (range ~3, no shadows) as a child,
     placed inside the pumpkin, drag it into "Head Glow" and the model's renderer into
     "Glow Renderer". It flickers like a candle, cuts out/flares on glitches and burns
     brighter as it gets closer. In your dark PS1 corridors this is the thing players
     will see first.

BEHAVIOUR (NullRefEntity)
  - Freezes while on-screen and in line of sight; slides toward you while you aren't looking.
  - Moves at 8 "fps" (stepRate) - choppy on purpose.
  - Turns to face you in 22.5 degree snaps, even while frozen.
  - Vertex wobble while watched; stronger the closer it is.
  - Random vertex explosions (PS1 stretched-polygon bug) + optional glitch sound.
  - Unseen for 7 s and far away? 40% chance it appears behind you.
  - Touch range -> onCaughtPlayer event (hook up a jumpscare); default reloads the scene.

TUNING IDEAS
  - Scarier: lower stepRate to 4, raise teleportChance.
  - Light cue: since the glow dies on every glitch, a corridor that suddenly goes dark is the warning.
  - Sell the joke: show a fake "NullReferenceException" console overlay on the catch screen.
