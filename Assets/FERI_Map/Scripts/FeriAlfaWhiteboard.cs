// Dressing for the blackout and the escape (FeriBlackout calls Setup once): the whiteboards at the front of Alfa (their
// texture has been wiped clean) have red marker on them that faintly glows in the dark.
//   middle board: THEY'RE WATCHING
//   left board:   IF YOU WANT TO ESCAPE, GET TO THE FIFTH FLOOR. YOU HAVE 15 MINUTES.  (reading it starts FeriEscape)
using UnityEngine;

namespace FeriMap
{
    public static class FeriAlfaWhiteboard
    {
        // just in front of the whiteboards at the front of Alfa (they face the seats, towards -z)
        static readonly Vector3 MiddleBoard = new Vector3(7.25f, 1.2f, 38.455f);
        public static readonly Vector3 LeftBoard = new Vector3(3.25f, 1.18f, 38.455f);

        static readonly Color Ink = new Color(0.62f, 0.03f, 0.03f), Glow = new Color(0.3f, 0.03f, 0.03f);

        public static void Setup()
        {
            // a quad faces -z already: towards the seats; slightly crooked
            FeriScrawl.Decal("WhiteboardWriting", new[] { "THEY'RE WATCHING" }, MiddleBoard, Quaternion.Euler(0f, 0f, -2.5f),
                0.2f, 3, Ink, Glow, true, 66, "whiteboard");
            FeriScrawl.Decal("EscapeWriting", new[] { "IF YOU WANT TO ESCAPE,", "GET TO THE FIFTH FLOOR.", "YOU HAVE 15 MINUTES." },
                LeftBoard, Quaternion.Euler(0f, 0f, 1.5f), 0.13f, 2, Ink, Glow, true, 15, "whiteboard");
        }
    }
}
