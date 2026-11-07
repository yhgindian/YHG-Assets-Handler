using AssetsHandler.Controls;
using AssetsTools.NET;
using AssetsTools.NET.Texture.TextureDecoders.CrnUnity;
using System;
using System.Windows;
using System.Windows.Controls;
using TagLib.Asf;

namespace YHG_Assets_Handler.Views.Windows.Unity
{
    public partial class AddExternalView : DarkWindow
    {
        public AssetsFileExternal? ResultExternal { get; private set; }

        public AddExternalView(AssetsFileExternal? external = null)
        {
            InitializeComponent();

            if (external != null)
            {
                Title = "Edit External";
                TxtFileName.Text = external.PathName;
                TxtOriginalFileName.Text = external.OriginalPathName;
                CmbExternalType.SelectedIndex = (int)external.Type;
                TxtGuid.Text = external.Guid.ToString();
            }
            else
            {
                Title = "Add External";
                CmbExternalType.SelectedIndex = 0;
            }

            UpdateFieldStates();
        }

        private void TxtFileName_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateFieldStates();
        }

        private void CmbExternalType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateFieldStates();
        }

        private void UpdateFieldStates()
        {
            if (TxtOriginalFileName == null || TxtGuid == null || CmbExternalType == null)
                return;

            // Unity "Resources/" prefix ho tabhi original file name enable hota hai
            bool hasOriginalName = TxtFileName.Text.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase);
            TxtOriginalFileName.IsEnabled = hasOriginalName;

            // Normal type nahi hai toh GUID enable hoti hai
            bool hasGuid = CmbExternalType.SelectedIndex > 0;
            TxtGuid.IsEnabled = hasGuid;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            string fileName = TxtFileName.Text.Trim();
            string originalFileName = TxtOriginalFileName.Text.Trim();
            var externalType = (AssetsFileExternalType)CmbExternalType.SelectedIndex;

            GUID128 guid = new GUID128();

            if (externalType != AssetsFileExternalType.Normal)
            {
                if (!GUID128.TryParse(TxtGuid.Text.Trim(), out guid))
                {
                    NotificationManager.Error("Invalid GUID provided! Format must be 32 hex characters.", "Validation Error");
                    MessageBox.Show("Invalid GUID provided! Format must be 32 hex characters.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            bool hasOriginalName = fileName.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase);

            ResultExternal = new AssetsFileExternal
            {
                VirtualAssetPathName = string.Empty,
                PathName = fileName,
                OriginalPathName = hasOriginalName ? originalFileName : fileName,
                Type = externalType,
                Guid = guid
            };

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            ResultExternal = null;
            DialogResult = false;
            Close();
        }
    }
}