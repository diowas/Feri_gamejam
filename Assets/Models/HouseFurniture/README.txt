HOUSE FURNITURE  -  for the Prologue house
==========================================

PS1-style low-poly furniture with one shared 256x256 atlas (point filtered, no mips).
Real-world scale in metres, +Y up, front faces +Z, origin on the floor.

FILES
  Bed.obj            ~420 tris  1.06 x 2.06 m, headboard 1.08 m. Wool blanket, dirty pillow.
  Table.obj          ~180 tris  1.40 x 0.80 m, 0.76 m high. Plank top.
  Chair.obj          ~150 tris  seat 0.46 m, back 0.98 m. Faces +Z (back at -Z).
  CookingStove.obj   ~530 tris  0.95 x 0.71 m, cast iron wood stove with glowing firebox grate,
                                oven door, towel rail, warming shelf, pot on the stove.
                                Parts: "stove", "pipe" (stovepipe up to 2.4 m), "pot".
  Fireplace.obj      ~290 tris  2.0 m wide. Fieldstone, wooden mantel, hearth slab, logs, embers.
                                ORIGIN = BACK EDGE: snap it flat against a wall, it opens toward +Z.
                                Parts: "fireplace", "hearth", "chimney" (to 2.6 m), "logs", "fire".
  Candle.obj         ~130 tris  candle in an iron holder - for the table.
  HouseFurniture_tex.png / _emission.png   shared atlas (only the stove grate + embers glow)
  Fire_tex.png       4-frame flame flipbook with cutout alpha
  HouseFireFlicker.cs    flickers a light + plays the flame flipbook
  Editor/HouseFurnitureSetup.cs   import settings + prefab builder

UNITY
  1. Folder lives at Assets/Models/HouseFurniture.
  2. Menu: Tools > House Furniture > Build Prefabs.
     -> HouseFurniture/Prefabs: Bed, Table, Chair, CookingStove, Fireplace, Candle, DiningSet
        (DiningSet = table + 4 chairs + candle; one chair pulled out as if someone just got up).
     Materials are made for URP (also works with Built-in / HDRP). Every prefab has a BoxCollider.
     Fireplace, stove and candle each have a "FireLight" point light with HouseFireFlicker on the root.
  3. Drag prefabs into the Prologue house.

TUNING
  - Ceiling lower/higher? Select the "chimney" child of the Fireplace or the "pipe" child of the
    CookingStove and scale its Y until it meets the ceiling.
  - Fireplace light has Soft shadows on (looks great, costs a bit). Set Shadow Type to None if needed.
  - HouseFireFlicker: Base Intensity / Flicker / Speed for the light, Fps for the flame animation.
  - Want the furniture static for baked lighting? Mark Bed/Table/Chair Static. Leave the
    FireLight objects as Realtime so they keep flickering.

BLENDER
  File > Import > Wavefront (.obj). Each part comes in as its own object.
