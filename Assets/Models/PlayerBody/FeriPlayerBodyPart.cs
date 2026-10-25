using UnityEngine;

namespace FeriMap
{
    // Sits on the PlayerArms / PlayerLegs prefabs (built by PlayerBodySetup) and holds both materials:
    // FERI/PS1 for scenes lit by PS1Lighting, a normal Lit material everywhere else (e.g. the Prologue house).
    public class FeriPlayerBodyPart : MonoBehaviour
    {
        public Material ps1Material;
        public Material litMaterial;
    }
}
