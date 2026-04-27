using UnityEngine;

namespace AIRTS.Lockstep.Unity.Gameplay
{
    public static class RtsPrototypeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreatePrototypeController()
        {
            if (Object.FindObjectOfType<RtsPrototypeController>() != null)
            {
                return;
            }

            var root = new GameObject("AIRTS RTS Prototype");
            if (Object.FindObjectOfType<LockstepClientBehaviour>() == null)
            {
                root.AddComponent<LockstepClientBehaviour>();
            }

            root.AddComponent<RtsPrototypeController>();
        }
    }
}
