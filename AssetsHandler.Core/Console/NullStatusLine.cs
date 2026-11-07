using System;
using System.Collections.Generic;
using System.Text;

public sealed class NullStatusLine : IStatusLine
{
    public static readonly NullStatusLine Instance = new();
    public bool IsAlive => false;
    public void Update(string message, StatusType? type = null) { }
    public void SetType(StatusType type) { }
    public void SetProgress(double percent, string? message = null) { }
    public void Complete(string? message = null, StatusType type = StatusType.Success) { }
    public IProgress<double> AsProgress(string? message = null) => new Progress<double>();
}
