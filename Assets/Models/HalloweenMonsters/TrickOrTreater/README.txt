TRICK-OR-TREATER  -  the main killer.   "it's not a bug, it's a feature"
==========================================================================

Lore pitch: someone came to the faculty Halloween party in an orange bedsheet ghost costume
with a jack-o'-lantern face drawn on in marker. It never took it off. It's 3.3 m tall, on huge
legs whose knees bend backwards. A long bare neck rises out of the sheet and hangs a huge hooded
head down in front of it - a real face pushing through the drawing, two orange pinpricks in the
eye holes, real teeth where the mouth tore through. One arm is long. The other is far too long,
with an elbow too many, dragging a kitchen knife just above the floor. The other hand holds a
blood-soaked kids' trick-or-treat pillowcase.
Things have punched through it: broken ribs burst out of its chest, bone spikes run along its
neck, its back is full of knives someone stabbed into it, a wooden stake goes straight through
its torso, and an extra grey hand is clawing its way out of a tear in its side. Look under the sheet and
there is no body. Only the missing texture.

FILES
  TrickOrTreater.obj / .mtl       ~9,600 tris, 3.3 m tall, 15 rigid parts (PS1-style segmented model), pivot at the feet, faces +Z
  TrickOrTreater_tex.png          256x256 atlas
  TrickOrTreater_emission.png     the pinprick eyes
  TrickOrTreater.cs               AI + procedural animation (builds its own skeleton at runtime)
  TrickOrTreater_preview.png      walking / chasing / face in the dark / looking up under the sheet

UNITY SETUP
  1. Drop the folder into Assets/.
  2. Model import: Normals = Import. (Read/Write is NOT needed.)
  3. Texture import (both PNGs): Filter Mode = Point, Compression = None, Generate Mip Maps OFF.
  4. Material: Albedo = TrickOrTreater_tex.png, Smoothness 0, Emission ON with map =
     TrickOrTreater_emission.png, colour white (the script drives the brightness).
     Render Face = Both (URP Lit) - the sheet must be visible from inside too.
     Remap it in the .obj's Materials tab, Apply.
  5. Drag TrickOrTreater.obj into the scene, on the floor of your level.
  6. Select the ROOT object and Add Component > Trick Or Treater. A NavMeshAgent is added for you.
     Don't move/rotate the child parts - the script finds them by name ("head", "arm_upper_L", ...).
  7. Bake a NavMesh: Window > Package Manager > install "AI Navigation" if it isn't already,
     then add a "NavMesh Surface" component to your level root and click Bake.
  8. Press Play. It wanders on its own. For a fixed route, drag empty GameObjects into "Waypoints".

  RECOMMENDED
  - Disable On Kill: drag in your player's movement and mouse-look scripts, so the player
    is locked in place during the kill and the camera is forced to look up into its face.
  - Player Root: if your camera is NOT a child of the player object, drag the player in here.
  - Size: it's 3.3 m tall. If your ceilings are lower, scale the ROOT object down (e.g. 0.85) - the rig
    scales with it. Lower the NavMeshAgent's Height/Radius to match.
  - Doorways: the agent height is 3.4 m, so the NavMesh won't route it through
    doors lower than that. Lower Agent Height on the NavMeshAgent (and re-bake) if it gets stuck.
  - Chase Speed: keep it just under your player's sprint speed (default 4.8). Escapes should be possible, barely.
  - Audio: add two AudioSources (Spatial Blend = 1, 3D). One looping whisper -> "Voice"
    (record yourself whispering "trick... or treat..."), one empty -> "Sfx". Add footstep clips
    (wet bare-foot slaps), a "Spot Sting" and a "Kill Scream".
  - On Killed Player: hook your death / game-over screen here. If left empty it reloads the scene.

BEHAVIOUR
  Patrol       walks the halls at 1.3 m/s, neck twitching at the wrong angle
  Investigate  hears you running (anything over 3.5 m/s within 11 m) and comes to look
  Spotting     when it sees you it FREEZES for ~1 s and its whole neck snaps sideways...
  Chase        ...then runs at you on its backwards knees, the huge knife arm sweeping low, sack hand reaching. Animation drops to
               6 fps so its limbs snap between poses; bones briefly come apart. It keeps
               "knowing" where you are for 1.5 s after you break line of sight.
  Search       lost you for 4 s: goes to where it last saw you, head sweeping, pokes around 7 s
  Kill         within 2 m (long arms): it appears right in your face, your camera is pulled up to its
               eyes, FOV narrows, eyes strobe, bones go haywire - then death event / reload.
  The pillowcase swings on its own like a pendulum.

WORKS WITH THE OTHER MONSTERS
  - CeilingWatcher: drag the Trick-or-Treater into its "Hunter" slot. When the eyes spot you,
    the killer comes to investigate where you're standing.
  - FloatingEyes: on its "On Vanished" event, add the Trick-or-Treater > AlertAtPlayer().
    Staring at the eyes too long now brings the killer.
  - Any script can call Alert(position) - e.g. a door slamming or a dropped bottle.

BLENDER
  File > Import > Wavefront (.obj). It comes in as 15 separate parts in the standing pose.
  It is not rigged in Blender - the skeleton is built by the Unity script at runtime.

TROUBLESHOOTING
  - Warning "part 'xyz' not found": the script must be on the root object that has the parts as children.
  - "SetDestination can only be called on an active agent...": no NavMesh baked under it.
  - It sees through walls: make sure walls have colliders and are included in "Sight Blockers".
  - Never sees you: the player's own collider might block the line - set "Player Root" to the player object.
