using System;
using System.Collections.Generic;
using System.Text;

public interface IStatusLine
{
    bool IsAlive { get; }   // After Clear() or MaxLines trim
    void Update(string message, StatusType? type = null);
    void SetType(StatusType type);
    void SetProgress(double percent, string? message = null);
    void Complete(string? message = null, StatusType type = StatusType.Success);
    IProgress<double> AsProgress(string? message = null);
}