namespace PluginAbstractions;

/// <summary>Maps a view model type to the UserControl that renders it.</summary>
public sealed record ViewRegistration(Type ViewModelType, Type ViewType);
