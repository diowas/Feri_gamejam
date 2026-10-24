FLOATING EYES  –  "it's not a bug, it's a feature"
=====================================================

Lore pitch: something in the faculty lost its eyes, and they never stopped watching.
Torn-out eyeballs drift just below the ceiling, trailing their optic nerves. You see
them glowing in the dark at the end of a corridor. Look at them too long and they
blink out of existence... and come back behind you.

FILES
  FloatingEye_Human.obj         grey-blue, human
  FloatingEye_Goat.obj          yellow, horizontal goat pupil
  FloatingEye_Red.obj           red
  FloatingEye_DoublePupil.obj   two pupils (best for the "pair" in the dark)
      each ~1,400 tris, eye is 24 cm across, pivot = eye centre, iris faces +Z (forward)
  FloatingEyes_tex.png          shared texture
  FloatingEyes_emission.png     eyeshine (only the pupils glow)
  FloatingEyes.cs               behaviour script
  FloatingEyes_preview.png      left: lit, middle: a pair at the end of a corridor, right: a swarm in the dark

UNITY
  1. Drop the folder into Assets/. Textures: Point filter, no compression, no mip maps.
  2. One material for all four: Albedo = FloatingEyes_tex.png, Smoothness 0,
     Emission ON, map = FloatingEyes_emission.png, colour white. Remap it on each .obj.
  3. Make a "pair": GameObject > Create Empty, name it "Eyes_Pair", place it near the ceiling.
     Drag two FloatingEye_DoublePupil models in as CHILDREN, at local X = -0.2 and +0.2.
     Add FloatingEyes to "Eyes_Pair" (the empty, not the eyes).
  4. Singles or swarms: same thing with 1 child, or 3-5 children scattered loosely.
     Make a prefab and sprinkle them around the level.
  5. The script finds the ceiling with a raycast. If your ceiling has no collider,
     set Fallback Height to your ceiling height minus ~0.35.

BEHAVIOUR (FloatingEyes)
  - Every eye always turns toward you, with small twitches.
  - Drifts after you at ~6 m, just under the ceiling, bobbing. Freezes while you look at it.
  - Eyeshine is bright in your peripheral vision, dims when you look straight at it.
  - Stare directly at it for 1.5 s: it blinks shut, vanishes, and reappears behind you
    a few seconds later (onVanished / onReappeared events for sounds).
  - Walk right up to it and it retreats into the dark.

BLENDER
  Import each .obj separately. Material Preview to see textures, image node set to Closest.
