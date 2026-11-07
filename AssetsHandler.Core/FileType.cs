namespace AssetsHandler.Core
{
    public enum FileType
    {
        Unknown,

        // --- Core Workspace Engines ---
        UnityAsset,
        UnityBundle,
        UnityWebFile,
        UnityResourceFile,

        Ets2Pmd,
        Ets2Pmg,
        Ets2Sii,
    }

    public enum InspectorPanel : int
    {
        None = -1,
        Info,
        Preview
    }
}