using Core;
using Scene;
using TMPro;
using UnityEngine;

namespace UI
{
    public class QuestionPanel : UIElement
    {
        [SerializeField] private TextMeshProUGUI _title;
        private int _sceneIndex;
        private MenuScene _scene;

        public override void Awake()
        {
            base.Awake();
            if (GameManager.Instance.CurrentScene is MenuScene scene) _scene = scene;
        }

        public void Init(int sceneIndex)
        {
            _sceneIndex = sceneIndex;
            _title.text = "Archviz Scene: " + (sceneIndex + 1);
            Show();
        }

        public void LoadPartial()
        {
            _scene.ChangeScene(_sceneIndex);
            Hide();
        }

        public void LoadFull()
        {
            _scene.FullLoadSceneByIndex(_sceneIndex);
            Hide();
        }
    }
}