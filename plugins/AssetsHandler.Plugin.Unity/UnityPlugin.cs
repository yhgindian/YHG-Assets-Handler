using AssetsHandler.Controls;
using AssetsHandler.Core;
using AssetsHandler.Plugin.Unity.ViewModels;
using AssetsHandler.Plugin.Unity.Views;
using AssetsHandler.Plugin.Unity.Views.Windows.Tools;
using AssetsHandler.Plugin.Unity.Views.Windows.Unity;
using AssetsHandler.Plugin.Unity.Workspace.UnityAssets;
using Microsoft.Win32;

namespace AssetsHandler.Plugin.Unity
{
    /// <summary>Entry point: everything Unity-specific is registered here, the host knows none of it.</summary>
    public sealed class UnityPlugin : IPlugin
    {
        public PluginInfo Info { get; } = new()
        {
            Id = "yhg.unity",
            Name = "Unity Assets",
            Version = "1.0.0",
            Author = "YHGIndian",
            Credits = "AssetsTools.NET (nesrak1), AssimpNet, Fmod5Sharp, HelixToolkit, StbImageSharp, TagLibSharp",
            License = "MIT",
            Description = "Inspect and edit Unity AssetBundles (UnityFS) and serialized .assets files.",
            MinHostApiVersion = 1
        };

        public void Initialize(IPluginRegistry registry)
        {
            // ── Formats ── (one detector instance recognises all three keys; it is attached to a single definition)
            registry.RegisterFormat(new FormatDefinition
            {
                Key = UnityFormats.Asset,
                DisplayName = "Unity Serialized Assets",
                Detector = UnityFormats.Detector,
                Priority = 100,
                IconKind = "Cube",
                IconColor = "#00E6FF",
                ViewModelFactory = item => new UnityAssetsInspectorViewModel(item)
            });
            registry.RegisterFormat(new FormatDefinition
            {
                Key = UnityFormats.Bundle,
                DisplayName = "Unity AssetBundle (UnityFS)",
                IconKind = "Cube",
                IconColor = "#00E6FF",
                ViewModelFactory = item => new UnityBundleInspectorViewModel(item)
            });
            registry.RegisterFormat(new FormatDefinition
            {
                Key = UnityFormats.Web,
                DisplayName = "Unity Web File",
                IconKind = "Cube",
                IconColor = "#00E6FF"
            });

            // ── Views ──
            registry.RegisterView(typeof(UnityAssetsInspectorViewModel), typeof(UnityAssetsInspectorView));
            registry.RegisterView(typeof(UnityBundleInspectorViewModel), typeof(UnityBundleInspectorView));

            // ── Explorer ──
            registry.RegisterExplorerFilter(new SplitFileFilter());

            // ── Menus ──
            var assetOnly = new[] { UnityFormats.Asset };
            static bool HasAssets(MenuContext c) => c.GetViewModel<UnityAssetsInspectorViewModel>() != null;

            registry.RegisterMenu(new MenuContribution
            {
                Location = MenuLocation.View,
                Order = 10,
                Header = "Add Assets...",
                IconKind = "PlusBox",
                Gesture = "Ctrl+Shift+A",
                InputGestureText = "Ctrl+Shift+A",
                VisibleForFormats = assetOnly,
                CanExecute = HasAssets,
                Execute = ctx =>
                {
                    var vm = ctx.GetViewModel<UnityAssetsInspectorViewModel>();
                    if (vm == null) return;
                    var dialog = new AddAssetView(vm.workspace) { Owner = ctx.Owner };
                    if (dialog.ShowDialog() == true)
                        Status.Write($"Asset created successfully in {ctx.CurrentItem?.Name}", StatusType.Success);
                }
            });

            registry.RegisterMenu(new MenuContribution
            {
                Location = MenuLocation.View,
                Order = 20,
                Header = "File Infos...",
                IconKind = "InformationOutline",
                VisibleForFormats = assetOnly,
                CanExecute = HasAssets,
                Execute = ctx =>
                {
                    var vm = ctx.GetViewModel<UnityAssetsInspectorViewModel>();
                    if (vm != null) new UnityAssetFilesInfos(vm.workspace).ShowDialog();
                }
            });

            registry.RegisterMenu(new MenuContribution
            {
                Location = MenuLocation.View,
                Order = 30,
                Header = "Add Files To workspace",
                IconKind = "FileSync",
                VisibleForFormats = assetOnly,
                CanExecute = HasAssets,
                ExecuteAsync = async ctx =>
                {
                    var vm = ctx.GetViewModel<UnityAssetsInspectorViewModel>();
                    if (vm == null) return;

                    var ofd = new OpenFileDialog
                    {
                        Title = "Select Assets Files to Add to Workspace",
                        Filter = "Unity Assets Files (*.assets;*.*)|*.assets;*.*",
                        Multiselect = true
                    };
                    if (ofd.ShowDialog() != true || ofd.FileNames.Length == 0) return;

                    int loaded = await vm.LoadFilesAsync(ofd.FileNames, "Adding Files to Workspace");
                    Status.Write($"Added {loaded}/{ofd.FileNames.Length} file(s) to workspace.",
                        loaded > 0 ? StatusType.Success : StatusType.Warning);
                }
            });

            registry.RegisterMenu(new MenuContribution
            {
                Location = MenuLocation.View,
                Order = 40,
                Header = "Setup Mono Generator",
                IconKind = "FileCode",
                VisibleForFormats = assetOnly,
                CanExecute = HasAssets,
                Execute = ctx => ctx.GetViewModel<UnityAssetsInspectorViewModel>()?.workspace.SetMonoTempGenerators(string.Empty)
            });

            registry.RegisterMenu(new MenuContribution
            {
                Location = MenuLocation.View,
                Order = 50,
                Header = "Filter",
                IconKind = "Filter",
                Gesture = "Ctrl+Shift+F",
                InputGestureText = "Ctrl+Shift+F",
                VisibleForFormats = assetOnly,
                Execute = ctx =>
                {
                    var vm = ctx.GetViewModel<UnityAssetsInspectorViewModel>();
                    if (vm != null) vm.OpenFilter();
                    else NotificationManager.Info("Open an assets file first to use the filter.", "Filter", key: "filter-no-workspace");
                }
            });

            registry.RegisterMenu(new MenuContribution
            {
                Location = MenuLocation.View,
                Order = 60,
                Header = "Migration",
                IconKind = "FileArrowLeftRight",
                VisibleForFormats = assetOnly,
                CanExecute = HasAssets,
                ExecuteAsync = async ctx =>
                {
#if DEBUG
                    var vm = ctx.GetViewModel<UnityAssetsInspectorViewModel>();
                    if (vm != null) await vm.MigrateAssetFile();
#else
                    NotificationManager.Warning("This feature is not yet available in the public release.");
                    await Task.CompletedTask;
#endif
                }
            });

            registry.RegisterMenu(new MenuContribution
            {
                Location = MenuLocation.Tools,
                Order = 10,
                Header = "Unity Assets Merge & Split",
                IconKind = "FileTree",
                Execute = _ => WindowHelper.ShowSingle<UnityAssetsMergeSplitter>()
            });
        }
    }

    internal sealed class SplitFileFilter : IExplorerFilter
    {
        public bool ShouldHide(string filePath) => UnityAssetWorkspace.IsSplitFile(filePath);
    }
}
