using System;
using System.Collections.Generic;
using System.Text;

namespace AssetsHandler.Core.ProcessManager;

/// <summary>
/// Defines the UI hosting contract implemented by the application layer.
/// Decouples the Core engine from UI framework specifics.
/// </summary>
public interface IProcessHost
{
    /// <summary>Executes the action on the UI thread.</summary>
    void Post(Action action);

    /// <param name="auto">True if triggered automatically by the process manager; otherwise, false.</param>
    void Show(bool auto);

    /// <param name="onlyIfAuto">If true, prevents closing if the window was explicitly opened by the user.</param>
    void Hide(bool onlyIfAuto);

    void Toggle();
}