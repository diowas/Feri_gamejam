// The 5 switches on the hand display (FeriPlayerBody), in order:
//   1 FLASH  - the flashlight comes on for flashDuration seconds (flickers at the end), then the switch flips back off
//   2 XRAY   - monsters glow through walls
//   3 NOCLIP - walk through walls and props; a ray keeps the feet on the floor (FeriPlayer.noclip)
//   4 SLOW   - every monster runs at slowScale (FeriSlow clock + NavMeshAgent speeds)
//   5 BHOP   - hold Space to hop automatically, every hop adds speed (FeriPlayer.bhop)
// FeriPlayer adds this component by itself.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace FeriMap
{
    [RequireComponent(typeof(FeriPlayer))]
    public class FeriAbilities : MonoBehaviour
    {
        public const int Flash = 0, XRay = 1, NoClip = 2, Slow = 3, BHop = 4;

        [Header("FLASH")]
        public float flashDuration = 5f;
        [Tooltip("The flashlight starts off and F does nothing: light only comes from the FLASH switch.")]
        public bool flashlightOnlyFromAbility = true;

        [Header("XRAY")]
        public Color xrayColor = new Color(1f, 0.25f, 0.15f, 1f);

        [Header("SLOW")]
        [Range(0.05f, 1f)] public float slowScale = 0.3f;

        FeriPlayer player;
        FeriPlayerBody body;
        float flashUntil = -1f;
        Material xrayMat;
        readonly HashSet<Renderer> xrayed = new HashSet<Renderer>();
        float nextXrayScan;
        readonly Dictionary<NavMeshAgent, Vector2> agentSpeed = new Dictionary<NavMeshAgent, Vector2>();   // x = script's speed, y = what we set
        float nextAgentScan;

        void Awake()
        {
            player = GetComponent<FeriPlayer>();
        }

        void OnEnable() { FeriPlayerBody.SwitchToggled += OnSwitch; }

        void OnDisable()
        {
            FeriPlayerBody.SwitchToggled -= OnSwitch;
            SetXRay(false);
            SetSlow(false);
        }

        void Start()
        {
            body = GetComponent<FeriPlayerBody>();
            if (flashlightOnlyFromAbility)
            {
                player.flashlightKey = false;
                if (player.flashlight != null) player.flashlight.on = false;
            }
        }

        bool IsOn(int i) => body != null && body.switches != null && i < body.switches.Length && body.switches[i];

        void OnSwitch(int i, bool on)
        {
            switch (i)
            {
                case Flash:
                    flashUntil = on ? Time.time + flashDuration : -1f;
                    if (player.flashlight != null) { player.flashlight.on = on; player.flashlight.flicker = 0f; }
                    break;
                case XRay: SetXRay(on); break;
                case NoClip: player.noclip = on; break;
                case Slow: SetSlow(on); break;
                case BHop: player.bhop = on; break;
            }
        }

        void Update()
        {
            // FLASH runs out
            if (flashUntil > 0f)
            {
                float left = flashUntil - Time.time;
                if (player.flashlight != null) player.flashlight.flicker = left < 1f ? 0.6f : 0f;
                if (left <= 0f)
                {
                    flashUntil = -1f;
                    if (player.flashlight != null) { player.flashlight.on = false; player.flashlight.flicker = 0f; }
                    if (body != null) body.SetSwitch(Flash, false);
                }
            }

            // monsters spawned after the switch went on
            if (IsOn(XRay) && Time.time > nextXrayScan) { nextXrayScan = Time.time + 0.5f; ApplyXRay(); }
        }

        void LateUpdate()
        {
            if (!IsOn(Slow)) return;
            if (Time.time > nextAgentScan)
            {
                nextAgentScan = Time.time + 1f;
                foreach (var a in FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None))
                    if (!agentSpeed.ContainsKey(a)) agentSpeed[a] = new Vector2(a.speed, -1f);
            }
            // monster scripts change agent.speed all the time (patrol / chase): pick up their value, scale it
            var keys = new List<NavMeshAgent>(agentSpeed.Keys);
            foreach (var a in keys)
            {
                if (a == null) { agentSpeed.Remove(a); continue; }
                Vector2 s = agentSpeed[a];
                if (!Mathf.Approximately(a.speed, s.y)) s.x = a.speed;
                s.y = s.x * slowScale;
                a.speed = s.y;
                agentSpeed[a] = s;
            }
        }

        // ------------------------------------------------------------------ SLOW
        void SetSlow(bool on)
        {
            FeriSlow.Scale = on ? slowScale : 1f;
            if (!on)
            {
                foreach (var kv in agentSpeed)
                    if (kv.Key != null && Mathf.Approximately(kv.Key.speed, kv.Value.y)) kv.Key.speed = kv.Value.x;
                agentSpeed.Clear();
            }
            nextAgentScan = 0f;
        }

        // ------------------------------------------------------------------ XRAY
        void SetXRay(bool on)
        {
            if (on) { ApplyXRay(); return; }
            foreach (var r in xrayed)
            {
                if (r == null) continue;
                var mats = new List<Material>(r.sharedMaterials);
                mats.RemoveAll(m => m == xrayMat);
                r.sharedMaterials = mats.ToArray();
            }
            xrayed.Clear();
        }

        void ApplyXRay()
        {
            if (xrayMat == null)
            {
                var shader = Resources.Load<Shader>("FERI_XRay");
                if (shader == null) shader = Shader.Find("FERI/XRay");
                if (shader == null) return;
                xrayMat = new Material(shader) { name = "XRay (runtime)" };
            }
            xrayMat.SetColor("_Color", xrayColor);
            foreach (var root in Monsters())
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                {
                    if (xrayed.Contains(r) || r is ParticleSystemRenderer) continue;
                    var mats = new List<Material>(r.sharedMaterials) { xrayMat };
                    r.sharedMaterials = mats.ToArray();
                    xrayed.Add(r);
                }
        }

        static IEnumerable<Component> Monsters()
        {
            foreach (var m in FindObjectsByType<TrickOrTreater>(FindObjectsSortMode.None)) yield return m;
            foreach (var m in FindObjectsByType<NullRefEntity>(FindObjectsSortMode.None)) yield return m;
            foreach (var m in FindObjectsByType<CeilingWatcher>(FindObjectsSortMode.None)) yield return m;
            foreach (var m in FindObjectsByType<FloatingEyes>(FindObjectsSortMode.None)) yield return m;
        }
    }
}
