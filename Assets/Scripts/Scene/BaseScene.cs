using Core;
using UnityEngine;

namespace Scene
{
    public class BaseScene : MonoBehaviour
    {
        internal GameManager _gm;

        public virtual void Awake()
        {
            _gm = GameManager.Instance;
        }

        public virtual void Start()
        {
        }

        public virtual void OnEnable()
        {
        }

        public virtual void OnDisable()
        {
        }

        public virtual void OnDestroy()
        {
        }
    }
}