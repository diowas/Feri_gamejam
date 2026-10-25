using UnityEngine;

namespace FeriMap
{
    // Sits on each flashlight the FLASH ability spawns. It drops to `floor` (hover height above the ground) and bobs there
    // for good; [E] puts it in your right palm with a fresh battery (FeriAbilities.PickUpFlashlight).
    public class FeriFlashlightPickup : FeriInteractable
    {
        public FeriAbilities owner;
        public float floor;

        float speed, phase;
        bool landed;

        public override string Prompt => "[E] Pick up flashlight";

        void Start() { phase = Random.value * 10f; }

        void Update()
        {
            // no Rigidbody: it tunnelled through the floor
            Vector3 p = transform.position;
            if (!landed)
            {
                speed += 9.81f * Time.deltaTime;
                p.y = Mathf.Max(floor, p.y - speed * Time.deltaTime);
                landed = p.y <= floor;
            }
            else
            {
                p.y = floor + Mathf.Sin(Time.time * 2.5f + phase) * 0.06f;   // floats and bobs
                transform.Rotate(0f, 20f * Time.deltaTime, 0f, Space.World);    // slow spin, like a pickup
            }
            transform.position = p;
        }

        public override void Interact(FeriPlayer player)
        {
            if (owner != null) owner.PickUpFlashlight(gameObject);
        }
    }
}
