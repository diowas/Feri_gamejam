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
        public float standHeight = 1.75f;
        public float crouchHeight = 1.1f;
        public float eyeOffset = 0.12f;
        [Tooltip("Pocasno nihanje kamere med hojo.")]
        public float headBob = 0.035f;
        public bool inputEnabled = true;
        public PS1Flashlight flashlight;

        public static readonly System.Collections.Generic.List<FeriPlayer> All = new System.Collections.Generic.List<FeriPlayer>();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        CharacterController cc;
        float pitch, yaw, vy, bobT;
        FeriInteractable hover;
        GUIStyle promptStyle;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            if (cameraPivot == null && Camera.main != null) cameraPivot = Camera.main.transform;
            if (flashlight == null) flashlight = GetComponentInChildren<PS1Flashlight>();
            yaw = transform.eulerAngles.y;
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

            if (active)
            {
                Vector2 look = FeriInput.Look() * mouseSensitivity;
                yaw += look.x;
                pitch = Mathf.Clamp(pitch - look.y, -85f, 85f);
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
            Vector3 wish = (transform.right * mv.x + transform.forward * mv.y) * speed;
            if (cc.isGrounded && vy < 0) vy = -2f;
            vy -= gravity * Time.deltaTime;
            cc.Move((wish + Vector3.up * vy) * Time.deltaTime);

            if (cameraPivot != null)
            {
                float moving = (cc.isGrounded && mv.sqrMagnitude > 0.01f) ? 1f : 0f;
                bobT += Time.deltaTime * speed * 2.2f * moving;
                float bob = Mathf.Sin(bobT) * headBob * moving;
                cameraPivot.localPosition = new Vector3(0, cc.height - eyeOffset + bob, 0);
                cameraPivot.localRotation = Quaternion.Euler(pitch, 0, 0);
            }

            if (active && FeriInput.Flashlight() && flashlight != null) flashlight.Toggle();

            hover = null;
            if (cameraPivot != null)
            {
                Ray ray = new Ray(cameraPivot.position, cameraPivot.forward);
                if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, ~0, QueryTriggerInteraction.Collide))
                    hover = hit.collider.GetComponentInParent<FeriInteractable>();
            }
            if (active && hover != null && FeriInput.Interact()) hover.Interact(this);
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
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
                    promptStyle.alignment = TextAnchor.MiddleCenter;
                }
                promptStyle.fontSize = Mathf.RoundToInt(Screen.height / 36f);
                GUI.color = new Color(0, 0, 0, 0.8f);
                GUI.Label(new Rect(cx - 199, cy + s * 6 + 1, 400, 40), hover.Prompt, promptStyle);
                GUI.color = new Color(1f, 0.9f, 0.7f, 1f);
                GUI.Label(new Rect(cx - 200, cy + s * 6, 400, 40), hover.Prompt, promptStyle);
            }
            GUI.color = Color.white;
        }
    }
}
