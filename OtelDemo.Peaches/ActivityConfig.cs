using System.Diagnostics;

namespace OtelDemo.Peaches;

public class ActivityConfig
{
    public static ActivitySource Source { get; } = new("OtelDemo.Peaches");
}