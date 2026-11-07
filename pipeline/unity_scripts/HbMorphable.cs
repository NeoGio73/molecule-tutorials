// HbMorphable.cs — anything in the scene that interpolates between the T (t=0) and R (t=1) states.
using UnityEngine;

namespace HbTutorial
{
    public abstract class HbMorphable : MonoBehaviour
    {
        /// <summary>Set the T->R interpolation parameter, 0 = T state, 1 = R state.</summary>
        public abstract void SetT(float t);
    }
}
