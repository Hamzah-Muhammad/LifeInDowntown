using System.Collections.Generic;
using UnityEngine;

namespace LifeInDowntown.World
{
    // Anything Interactor offers a "[E] ..." prompt for besides doors (docs/DESIGN.md 3M).
    public abstract class Interactable : MonoBehaviour
    {
        public float radius = 1.6f;
        public static readonly List<Interactable> All = new List<Interactable>();

        public abstract string Prompt { get; }
        public virtual bool Available => true;
        public abstract void Interact();

        protected virtual void OnEnable() => All.Add(this);
        protected virtual void OnDisable() => All.Remove(this);
    }
}
