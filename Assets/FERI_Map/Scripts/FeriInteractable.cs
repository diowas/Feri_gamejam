using UnityEngine;

namespace FeriMap
{

    public abstract class FeriInteractable : MonoBehaviour
    {
        public virtual string Prompt { get { return "[E]"; } }
        public abstract void Interact(FeriPlayer player);
    }
}
