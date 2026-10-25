// The 5 switches on the hand display (FeriPlayerBody), in order. Each one runs on a timer (durations) and then
// flips itself back off:
//   1 FLASH  - spawns a flashlight in front of you that drops to the floor and shines ahead; [E] picks it up into your
//              hand. Either way it lasts the FLASH timer and flickers in its last second.
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

        [Tooltip("Seconds each ability lasts: FLASH, XRAY, NOCLIP, SLOW, BHOP.")]
        public float[] durations = { 5f, 5f, 5f, 5f, 30f };

        [Header("FLASH")]
        [Tooltip("Length of the spawned flashlight (metres).")]
        public float flashlightLength = 0.28f;
        [Tooltip("How far in front of the player it spawns (metres).")]
        public float spawnDistance = 1.2f;
        [Tooltip("The player's own flashlight starts off and F does nothing: light only comes from the FLASH switch.")]
        public bool flashlightOnlyFromAbility = true;

        [Header("XRAY")]
        public Color xrayColor = new Color(1f, 0.25f, 0.15f, 1f);

        [Header("SLOW")]
        [Range(0.05f, 1f)] public float slowScale = 0.3f;

        FeriPlayer player;
        FeriPlayerBody body;
        float[] until = { -1f, -1f, -1f, -1f, -1f };
        GameObject handLight;   // the spawned flashlight
        float dropFloor, dropSpeed;
        bool holding;   // picked up: the player's own flashlight is on
        PS1Flashlight beam;
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
            DropFlashlight();
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

        /// <summary>Seconds left on ability i, 0 when it's off.</summary>
        public float Remaining(int i) => i >= 0 && i < until.Length && until[i] > 0f ? Mathf.Max(0f, until[i] - Time.time) : 0f;

        void OnSwitch(int i, bool on)
        {
            if (i >= 0 && i < until.Length)
                until[i] = on ? Time.time + (durations != null && i < durations.Length ? durations[i] : 5f) : -1f;
            switch (i)
            {
                case Flash: if (on) SpawnFlashlight(); else DropFlashlight(); break;
                case XRay: SetXRay(on); break;
                case NoClip: player.noclip = on; break;
                case Slow: SetSlow(on); break;
                case BHop: player.bhop = on; break;
            }
        }

        void Update()
        {
            // timers run out -> the switch flips back off (which switches the ability off)
            for (int i = 0; i < until.Length; i++)
                if (until[i] > 0f && Time.time >= until[i])
                {
                    until[i] = -1f;
                    if (body != null && IsOn(i)) body.SetSwitch(i, false);
                    else OnSwitch(i, false);
                }
            if (beam != null) beam.flicker = Remaining(Flash) < 1f ? 0.6f : 0f;
            if (holding && player.flashlight != null) player.flashlight.flicker = Remaining(Flash) < 1f ? 0.6f : 0f;
            if (handLight != null && handLight.transform.position.y > dropFloor)
            {
                dropSpeed += 9.81f * Time.deltaTime;
                Vector3 p = handLight.transform.position;
                p.y = Mathf.Max(dropFloor, p.y - dropSpeed * Time.deltaTime);
                handLight.transform.position = p;
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

        // ------------------------------------------------------------------ FLASH
        // A real flashlight object (PlayerBody/Resources/AbilityFlashlight, built by PlayerBodySetup) spawns in front of
        // the player, drops onto the floor and shines the way you were looking. It carries its own PS1Flashlight.
        void SpawnFlashlight()
        {
            DropFlashlight();
            var prefab = Resources.Load<GameObject>("AbilityFlashlight");
            Transform cam = player.cameraPivot;
            if (prefab == null || cam == null) return;

            Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = transform.forward;
            fwd.Normalize();
            Vector3 pos = cam.position + fwd * spawnDistance;
            // don't spawn it inside a wall
            if (Physics.Raycast(cam.position, fwd, out RaycastHit wall, spawnDistance + 0.3f, ~0, QueryTriggerInteraction.Ignore))
                pos = wall.point - fwd * 0.3f;

            handLight = Instantiate(prefab, pos, Quaternion.LookRotation(Vector3.ProjectOnPlane(cam.forward, Vector3.up).sqrMagnitude > 1e-4f ? fwd : transform.forward));
            handLight.name = "SpawnedFlashlight";
            handLight.transform.localScale = Vector3.one * flashlightLength;   // the prefab is 1 m long, lens at +Z
            bool ps1 = FindAnyObjectByType<PS1Lighting>() != null;
            var part = handLight.GetComponent<FeriPlayerBodyPart>();
            foreach (var r in handLight.GetComponentsInChildren<Renderer>())
            {
                if (part != null) r.sharedMaterial = ps1 && part.ps1Material != null ? part.ps1Material : part.litMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var col = handLight.AddComponent<CapsuleCollider>();
            col.direction = 2; col.height = 1.2f; col.radius = 0.35f;   // a bit bigger than the model, easier to aim at
            // no Rigidbody: thin and fast, it tunnelled through the floor. Fall by hand onto the floor found by a ray.
            col.enabled = false;   // don't let the ray hit itself
            dropFloor = Physics.Raycast(pos, Vector3.down, out RaycastHit floor, 20f, ~0, QueryTriggerInteraction.Ignore)
                ? floor.point.y + 0.12f * flashlightLength : pos.y - 20f;   // model radius ~0.12 of its length
            col.enabled = true;
            dropSpeed = 0f;

            var src = player.flashlight;
            var b = new GameObject("Beam").transform;
            b.SetParent(handLight.transform, false);
            b.localPosition = new Vector3(0, 0, 0.5f);
            beam = b.gameObject.AddComponent<PS1Flashlight>();   // becomes PS1Flashlight.Active
            if (src != null)
            {
                beam.color = src.color; beam.intensity = src.intensity; beam.range = src.range;
                beam.outerAngle = src.outerAngle; beam.innerAngle = src.innerAngle;
            }
            beam.on = true;
            handLight.AddComponent<FeriFlashlightPickup>().owner = this;
        }

        /// <summary>The spawned flashlight goes into the player's hand for the rest of the FLASH timer.</summary>
        public void PickUpFlashlight()
        {
            if (handLight != null) Destroy(handLight);
            handLight = null;
            beam = null;
            var f = player.flashlight;
            if (f == null) return;
            f.enabled = false; f.enabled = true;   // make it PS1Flashlight.Active again (the spawned beam took over)
            f.on = true;
            f.flicker = 0f;
            holding = true;
        }

        void DropFlashlight()
        {
            if (handLight != null) Destroy(handLight);
            handLight = null;
            beam = null;
            if (holding && player.flashlight != null) { player.flashlight.on = false; player.flashlight.flicker = 0f; }
            holding = false;
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
