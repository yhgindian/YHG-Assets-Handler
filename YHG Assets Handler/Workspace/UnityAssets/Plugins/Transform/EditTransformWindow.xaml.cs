using AssetsHandler.Controls;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using YHG_Assets_Handler.Utils.Unity;
using YHG_Assets_Handler.Views.Windows.Unity;

namespace YHG_Assets_Handler.Workspace.UnityAssets.Plugins.Transform
{
    public class ChildTransformItem
    {
        public int FileId { get; set; }
        public long PathId { get; set; }
        public string DisplayText => $"FileID: {FileId}, PathID: {PathId}";
    }

    public partial class EditTransformWindow : DarkWindow
    {
        private readonly UnityAssetWorkspace _workspace;
        private readonly AssetContainer _cont;
        private readonly AssetTypeValueField _baseField;

        private int _gameObjectFileId;
        private long _gameObjectPathId;

        private int _fatherFileId;
        private long _fatherPathId;

        public ObservableCollection<ChildTransformItem> ChildrenList { get; } = new();

        public EditTransformWindow(UnityAssetWorkspace workspace, AssetContainer cont)
        {
            InitializeComponent();

            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _cont = cont ?? throw new ArgumentNullException(nameof(cont));

            lstChildren.ItemsSource = ChildrenList;

            // Safe fetch + Null handling
            var baseField = _workspace.GetBaseField(_cont);
            if (baseField == null)
            {
                NotificationManager.Warning(
                    "Failed to deserialize TypeTree BaseField for this Transform.\nMake sure classdata.tpk is loaded and the asset data is valid.",
                    "TypeTree Error");

                Loaded += (s, e) => Close();
                _baseField = null!;
                return;
            }

            _baseField = baseField;

            LoadTransformData();
            RegisterEventHandlers();
        }

        private void LoadTransformData()
        {
            if (_baseField == null) return;

            // 1. m_GameObject (PPtr<GameObject>)
            var goField = _baseField["m_GameObject"];
            _gameObjectFileId = goField["m_FileID"].AsInt;
            _gameObjectPathId = goField["m_PathID"].AsLong;
            UpdateGameObjectDisplay();

            // 2. m_Father (PPtr<Transform>)
            var fatherField = _baseField["m_Father"];
            _fatherFileId = fatherField["m_FileID"].AsInt;
            _fatherPathId = fatherField["m_PathID"].AsLong;
            UpdateFatherDisplay();

            // 3. m_Children (Array of PPtr<Transform>)
            var childrenArray = _baseField["m_Children"]["Array"];
            ChildrenList.Clear();
            for (int i = 0; i < childrenArray.Children.Count; i++)
            {
                var child = childrenArray[i];
                ChildrenList.Add(new ChildTransformItem
                {
                    FileId = child["m_FileID"].AsInt,
                    PathId = child["m_PathID"].AsLong
                });
            }

            // 4. m_LocalPosition
            var pos = _baseField["m_LocalPosition"];
            txtPositionX.Text = pos["x"].AsFloat.ToString(CultureInfo.InvariantCulture);
            txtPositionY.Text = pos["y"].AsFloat.ToString(CultureInfo.InvariantCulture);
            txtPositionZ.Text = pos["z"].AsFloat.ToString(CultureInfo.InvariantCulture);

            // 5. m_LocalRotation (Quaternion: x, y, z, w)
            var rot = _baseField["m_LocalRotation"];
            txtRotationX.Text = rot["x"].AsFloat.ToString(CultureInfo.InvariantCulture);
            txtRotationY.Text = rot["y"].AsFloat.ToString(CultureInfo.InvariantCulture);
            txtRotationZ.Text = rot["z"].AsFloat.ToString(CultureInfo.InvariantCulture);
            txtRotationW.Text = rot["w"].AsFloat.ToString(CultureInfo.InvariantCulture);

            // 6. m_LocalScale
            var scale = _baseField["m_LocalScale"];
            txtScaleX.Text = scale["x"].AsFloat.ToString(CultureInfo.InvariantCulture);
            txtScaleY.Text = scale["y"].AsFloat.ToString(CultureInfo.InvariantCulture);
            txtScaleZ.Text = scale["z"].AsFloat.ToString(CultureInfo.InvariantCulture);
        }

        private void RegisterEventHandlers()
        {
            btnGameObject.Click += BtnGameObject_Click;
            btnFather.Click += BtnFather_Click;

            btnAddChild.Click += BtnAddChild_Click;
            btnEditChild.Click += BtnEditChild_Click;
            btnRemoveChild.Click += BtnRemoveChild_Click;

            btnOk.Click += BtnOk_Click;
        }

        private void UpdateGameObjectDisplay()
        {
            txtGameObject.Text = $"FileID: {_gameObjectFileId}, PathID: {_gameObjectPathId}";
        }

        private void UpdateFatherDisplay()
        {
            txtFather.Text = $"FileID: {_fatherFileId}, PathID: {_fatherPathId}";
        }

        // Helper to ensure window focus & prevent minimizing / dropping Z-order
        private void ShowPickerModal(UnityPPtrPickerView picker)
        {
            picker.Owner = this;
            picker.ShowDialog();

            this.Activate();
            this.Focus();
        }

        // PPtr Pickers
        private void BtnGameObject_Click(object sender, RoutedEventArgs e)
        {
            var picker = new UnityPPtrPickerView(_workspace, _cont.FileInstance, _gameObjectFileId, _gameObjectPathId, AssetClassID.GameObject);
            picker.ViewModel.OnApply = (f, p) =>
            {
                _gameObjectFileId = f.FileId;
                _gameObjectPathId = p.PathId;
                UpdateGameObjectDisplay();
            };

            ShowPickerModal(picker);
        }

        private void BtnFather_Click(object sender, RoutedEventArgs e)
        {
            var picker = new UnityPPtrPickerView(_workspace, _cont.FileInstance, _fatherFileId, _fatherPathId, AssetClassID.Transform);
            picker.ViewModel.OnApply = (f, p) =>
            {
                _fatherFileId = f.FileId;
                _fatherPathId = p.PathId;
                UpdateFatherDisplay();
            };

            ShowPickerModal(picker);
        }

        private void BtnAddChild_Click(object sender, RoutedEventArgs e)
        {
            var picker = new UnityPPtrPickerView(_workspace, _cont.FileInstance, allowed: AssetClassID.Transform);
            picker.ViewModel.OnApply = (f, p) =>
            {
                ChildrenList.Add(new ChildTransformItem
                {
                    FileId = f.FileId,
                    PathId = p.PathId,
                });
            };

            ShowPickerModal(picker);
        }

        private void BtnEditChild_Click(object sender, RoutedEventArgs e)
        {
            if (lstChildren.SelectedItem is not ChildTransformItem selected)
            {
                NotificationManager.Warning("Please select a child item from the list.", "Notice");
                return;
            }

            var picker = new UnityPPtrPickerView(_workspace, _cont.FileInstance, selected.FileId, selected.PathId, AssetClassID.Transform);
            picker.ViewModel.OnApply = (f, p) =>
            {
                selected.FileId = f.FileId;
                selected.PathId = p.PathId;
                lstChildren.Items.Refresh();
            };

            ShowPickerModal(picker);
        }

        private void BtnRemoveChild_Click(object sender, RoutedEventArgs e)
        {
            if (lstChildren.SelectedItem is ChildTransformItem selected)
            {
                ChildrenList.Remove(selected);
            }
        }

        // Save & Apply
        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (_baseField == null)
            {
                DialogResult = false;
                Close();
                return;
            }

            if (!ValidateFloatInputs(out float posX, out float posY, out float posZ,
                                     out float rotX, out float rotY, out float rotZ, out float rotW,
                                     out float scaleX, out float scaleY, out float scaleZ))
            {
                return;
            }

            // 1. Assign m_GameObject
            _baseField["m_GameObject"]["m_FileID"].AsInt = _gameObjectFileId;
            _baseField["m_GameObject"]["m_PathID"].AsLong = _gameObjectPathId;

            // 2. Assign m_Father
            _baseField["m_Father"]["m_FileID"].AsInt = _fatherFileId;
            _baseField["m_Father"]["m_PathID"].AsLong = _fatherPathId;

            // 3. Assign m_Children
            var childrenArray = _baseField["m_Children"]["Array"];
            childrenArray.Children.Clear();
            for (int i = 0; i < ChildrenList.Count; i++)
            {
                var newChildField = childrenArray.GetEmptyArrayField();
                newChildField["m_FileID"].AsInt = ChildrenList[i].FileId;
                newChildField["m_PathID"].AsLong = ChildrenList[i].PathId;
                childrenArray.Children.Add(newChildField);
            }

            // 4. Assign Vectors
            var pos = _baseField["m_LocalPosition"];
            pos["x"].AsFloat = posX;
            pos["y"].AsFloat = posY;
            pos["z"].AsFloat = posZ;

            var rot = _baseField["m_LocalRotation"];
            rot["x"].AsFloat = rotX;
            rot["y"].AsFloat = rotY;
            rot["z"].AsFloat = rotZ;
            rot["w"].AsFloat = rotW;

            var scale = _baseField["m_LocalScale"];
            scale["x"].AsFloat = scaleX;
            scale["y"].AsFloat = scaleY;
            scale["z"].AsFloat = scaleZ;

            // 5. Write back to asset
            byte[] rawBytes = _baseField.WriteToByteArray();
            _workspace.AddOrUpdateAsset(_cont, rawBytes);

            DialogResult = true;
            Close();
        }

        private bool ValidateFloatInputs(out float px, out float py, out float pz,
                                         out float rx, out float ry, out float rz, out float rw,
                                         out float sx, out float sy, out float sz)
        {
            px = py = pz = rx = ry = rz = rw = sx = sy = sz = 0f;

            bool ok = float.TryParse(txtPositionX.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out px) &&
                      float.TryParse(txtPositionY.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out py) &&
                      float.TryParse(txtPositionZ.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out pz) &&
                      float.TryParse(txtRotationX.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out rx) &&
                      float.TryParse(txtRotationY.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out ry) &&
                      float.TryParse(txtRotationZ.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out rz) &&
                      float.TryParse(txtRotationW.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out rw) &&
                      float.TryParse(txtScaleX.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out sx) &&
                      float.TryParse(txtScaleY.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out sy) &&
                      float.TryParse(txtScaleZ.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out sz);

            if (!ok)
            {
                NotificationManager.Warning("Please enter valid floating-point numbers for Position, Rotation, and Scale.", "Invalid Input");
                return false;
            }

            return true;
        }
    }
}