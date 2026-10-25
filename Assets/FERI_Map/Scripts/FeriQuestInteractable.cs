using UnityEngine;

namespace FeriMap
{
    // A generic [E] thing for the escape quest (FeriEscape builds them at runtime): its prompt and what it does are
    // set from code. An empty prompt means there's nothing to do with it right now.
    public class FeriQuestInteractable : FeriInteractable
    {
        public System.Func<string> prompt;
        public System.Action action;

        public override string Prompt => prompt != null ? prompt() : "";

        public override void Interact(FeriPlayer player)
        {
            if (action != null && !string.IsNullOrEmpty(Prompt)) action();
        }
    }
}
