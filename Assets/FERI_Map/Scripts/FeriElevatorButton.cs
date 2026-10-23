using UnityEngine;

namespace FeriMap
{

    public class FeriElevatorButton : FeriInteractable
    {
        public FeriElevator elevator;
        public int floorIndex;
        public bool isCallButton;
        public Renderer buttonRenderer;
        [Tooltip("Gumb sveti (izbrano nadstropje).")]
        public bool held;

        float glow;
        MaterialPropertyBlock mpb;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public override string Prompt
        {
            get
            {
                if (elevator == null) return "[E]";
                if (isCallButton) return "[E] Poklici dvigalo";
                return "[E] Nadstropje " + elevator.floors[floorIndex].label;
            }
        }

        public override void Interact(FeriPlayer player)
        {
            if (elevator != null) elevator.Press(this);
        }

        public void Blink(bool accepted)
        {
            glow = accepted ? 1.5f : -0.6f;
        }

        void Update()
        {
            if (buttonRenderer == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            Color c = held ? new Color(2.2f, 1.6f, 0.6f) : Color.white;
            if (glow > 0) { c = Color.Lerp(c, new Color(2.2f, 1.6f, 0.6f), Mathf.Clamp01(glow)); glow = Mathf.Max(0, glow - Time.deltaTime); }
            else if (glow < 0) { c = Color.Lerp(Color.white, new Color(2f, 0.3f, 0.3f), Mathf.Clamp01(-glow * 2f)); glow = Mathf.Min(0, glow + Time.deltaTime); }
            buttonRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(ColorId, c);
            buttonRenderer.SetPropertyBlock(mpb);
        }
    }
}
