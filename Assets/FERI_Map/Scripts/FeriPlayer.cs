using UnityEngine;

namespace FeriMap
{

    [RequireComponent(typeof(CharacterController))]
    public class FeriPlayer : MonoBehaviour
    {
        public Transform cameraPivot;
        public float walkSpeed = 2.6f;
        public float runSpeed = 4.8f;
        public float crouchSpeed = 1.4f;
        public float mouseSensitivity = 2f;
        public float gravity = 20f;
        public float interactDistance = 2.4f;
        [Tooltip("Fall below this height (off the map) and you die: the scene reloads, like being caught.")]
        public float fallDeathY = -30f;
        public float standHeight = 1.75f;
        public float crouchHeight = 1.1f;
        public float eyeOffset = 0.12f;
        [Tooltip("Pocasno nihanje kamere med hojo.")]
        public float headBob = 0.035f;
        [Tooltip("How far down you can look, degrees. FeriPlayerBody lowers it so the feet only just come into view.")]
        public float maxLookDown = 85f;
        public bool inputEnabled = true;
        [Tooltip("Off while the hand display is up: the mouse drives its cursor instead of the camera.")]
        public bool lookEnabled = true;
        public PS1Flashlight flashlight;
        [Tooltip("Off = the F key does nothing (the flashlight only comes from the FLASH ability).")]
        public bool flashlightKey = true;

        [Header("Abilities (set by FeriAbilities)")]
        [Tooltip("Walk through walls and props, but stay on the floor.")]
        public bool noclip;
        [Tooltip("Hold Space to hop automatically; every hop adds speed.")]
        public bool bhop;
        public float jumpSpeed = 4.6f;
        [Tooltip("Speed multiplier added per hop.")]
        public float bhopGain = 0.12f;
        public float bhopMaxBoost = 2f;
        [Tooltip("Noclip: highest step the player snaps up onto (metres).")]
        public float noclipStep = 0.45f;

        public Vector3 Velocity { get; private set; }
        public bool Grounded => cc.enabled ? cc.isGrounded : noclipGrounded;
        bool noclipGrounded;
        float hopBoost = 1f, groundTime;

        public static readonly System.Collections.Generic.List<FeriPlayer> All = new System.Collections.Generic.List<FeriPlayer>();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        CharacterController cc;
        float pitch, yaw, vy, bobT;
        FeriInteractable hover;
        public FeriInteractable Hover => hover;
        GUIStyle promptStyle;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            if (cameraPivot == null && Camera.main != null) cameraPivot = Camera.main.transform;
            if (flashlight == null) flashlight = GetComponentInChildren<PS1Flashlight>();
            yaw = transform.eulerAngles.y;
            if (GetComponent<FeriPlayerBody>() == null) gameObject.AddComponent<FeriPlayerBody>();   // first-person arms + legs
            if (GetComponent<FeriAbilities>() == null) gameObject.AddComponent<FeriAbilities>();       // the 5 hand-display switches
        }

        void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public void ShiftBy(Vector3 delta)
        {
            cc.enabled = false;
            transform.position += delta;
            cc.enabled = true;
        }

        public void TeleportTo(Vector3 position, float newYaw)
        {
            cc.enabled = false;
            transform.position = position;
            yaw = newYaw;
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            cc.enabled = true;
            vy = 0;
        }

        void Update()
        {
            if (transform.position.y < fallDeathY)
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                return;
            }
            if (FeriInput.Escape())
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (Cursor.lockState != CursorLockMode.Locked && FeriInput.Click())
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                return;
            }
            bool active = inputEnabled && Cursor.lockState == CursorLockMode.Locked;

            if (active && lookEnabled)
            {
                Vector2 look = FeriInput.Look() * mouseSensitivity;
                yaw += look.x;
                pitch = Mathf.Clamp(pitch - look.y, -85f, maxLookDown);
            }
            transform.rotation = Quaternion.Euler(0, yaw, 0);

            bool crouch = active && FeriInput.Crouch();
            float targetH = crouch ? crouchHeight : standHeight;
            if (!crouch && cc.height < standHeight - 0.01f)
            {

                if (Physics.SphereCast(transform.position + Vector3.up * (cc.height - cc.radius), cc.radius * 0.9f,
                    Vector3.up, out RaycastHit _, standHeight - cc.height + 0.05f, ~0, QueryTriggerInteraction.Ignore))
                    targetH = cc.height;
            }
            cc.height = Mathf.MoveTowards(cc.height, targetH, Time.deltaTime * 4f);
            cc.center = new Vector3(0, cc.height * 0.5f, 0);

            Vector2 mv = active ? FeriInput.Move() : Vector2.zero;
            float speed = crouch ? crouchSpeed : (FeriInput.Run() ? runSpeed : walkSpeed);
            bool grounded = Grounded;
            groundTime = grounded ? groundTime + Time.deltaTime : 0f;
            if (!bhop) hopBoost = 1f;
            else if (active && grounded && FeriInput.Jump()) { vy = jumpSpeed; hopBoost = Mathf.Min(hopBoost + bhopGain, bhopMaxBoost); grounded = false; }
            else if (grounded && groundTime > 0.2f) hopBoost = Mathf.MoveTowards(hopBoost, 1f, Time.deltaTime * 2f);   // lose the boost once you stop hopping
            Vector3 wish = (transform.right * mv.x + transform.forward * mv.y) * speed * hopBoost;
            if (grounded && vy < 0) vy = -2f;
            vy -= gravity * Time.deltaTime;
            Vector3 before = transform.position;
            if (noclip || !cc.enabled) NoclipMove(wish);
            else cc.Move((wish + Vector3.up * vy) * Time.deltaTime);
            Velocity = (transform.position - before) / Mathf.Max(Time.deltaTime, 1e-5f);

            if (cameraPivot != null)
            {
                float moving = (Grounded && mv.sqrMagnitude > 0.01f) ? 1f : 0f;
                bobT += Time.deltaTime * speed * 2.2f * moving;
                float bob = Mathf.Sin(bobT) * headBob * moving;
                cameraPivot.localPosition = new Vector3(0, cc.height - eyeOffset + bob, 0);
                cameraPivot.localRotation = Quaternion.Euler(pitch, 0, 0);
            }

            if (active && flashlightKey && FeriInput.Flashlight() && flashlight != null) flashlight.Toggle();

            hover = null;
            if (cameraPivot != null)
            {
                Ray ray = new Ray(cameraPivot.position, cameraPivot.forward);
                if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, ~0, QueryTriggerInteraction.Collide))
                    hover = hit.collider.GetComponentInParent<FeriInteractable>();
            }
            if (active && lookEnabled && hover != null && FeriInput.Interact()) hover.Interact(this);
        }

        // Noclip: the controller is off, so walls and props don't block; a short ray keeps the feet on the floor.
        void NoclipMove(Vector3 wish)
        {
            float dt = Time.deltaTime;
            cc.enabled = false;
            Vector3 pos = transform.position + wish * dt;
            float fall = Mathf.Max(0f, -vy * dt);
            noclipGrounded = false;
            if (vy <= 0f && Physics.Raycast(pos + Vector3.up * noclipStep, Vector3.down, out RaycastHit hit, noclipStep + fall + 0.02f, ~0, QueryTriggerInteraction.Ignore))
            {
                pos.y = hit.point.y;
                vy = 0f;
                noclipGrounded = true;
            }
            else pos.y += vy * dt;
            transform.position = pos;

            // switched off while inside a wall: keep ghosting until the capsule is free again
            if (!noclip)
            {
                Vector3 a = pos + Vector3.up * (cc.radius + 0.05f), b = pos + Vector3.up * Mathf.Max(cc.height - cc.radius, cc.radius + 0.06f);
                if (!Physics.CheckCapsule(a, b, cc.radius * 0.95f, ~0, QueryTriggerInteraction.Ignore)) cc.enabled = true;
            }
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !lookEnabled) return;
            GUI.depth = 0;
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            float s = Mathf.Max(2f, Screen.height / 360f);
            GUI.color = hover != null ? new Color(1f, 0.85f, 0.3f, 0.9f) : new Color(1, 1, 1, 0.35f);
            GUI.DrawTexture(new Rect(cx - s * 0.5f, cy - s * 0.5f, s, s), Texture2D.whiteTexture);
            if (hover != null)
            {
                if (promptStyle == null)
                {
                    promptStyle = new GUIStyle(GUI.skin.label);
                    promptStyle.alignment = TextAnchor.UpperCenter;
                    promptStyle.wordWrap = false;   // a long prompt was wrapped and clipped top and bottom
                    promptStyle.clipping = TextClipping.Overflow;
                }
                promptStyle.fontSize = Mathf.RoundToInt(Screen.height / 36f);
                GUI.color = new Color(0, 0, 0, 0.8f);
                float ph = promptStyle.fontSize * 2f;
                GUI.Label(new Rect(1, cy + s * 6 + 1, Screen.width, ph), hover.Prompt, promptStyle);
                GUI.color = new Color(1f, 0.9f, 0.7f, 1f);
                GUI.Label(new Rect(0, cy + s * 6, Screen.width, ph), hover.Prompt, promptStyle);
            }
            GUI.color = Color.white;
        }
    }
}
