using System.Diagnostics;

namespace OtelDemo.Pears;

public class ActivityConfig
{
    public static ActivitySource Source { get; } = new("OtelDemo.Pears");
}