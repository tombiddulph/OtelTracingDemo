using System.Diagnostics;

namespace OtelDemo.Kiwis;

public class ActivityConfig
{
    public static ActivitySource Source { get; } = new("OtelDemo.Kiwis");
}