using Model;
using TMPro;

namespace UI
{
    public class AssetController : UIElement
    {
        public ButtonBehaviour ButtonEnable;
        public ButtonBehaviour ButtonDisable;

        public TextMeshProUGUI Name;
        private AssetModel _model;

        public void Init(AssetModel model)
        {
            _model = model;
            SetData();
        }

        private void SetData()
        {
            Name.text = _model.Name;
            if (_model.IsActive)
            {
                ButtonEnable.SetDisabled();
                ButtonDisable.SetEnabled();
            }
            else
            {
                ButtonEnable.SetEnabled();
                ButtonDisable.SetDisabled();
            }
        }
    }
}