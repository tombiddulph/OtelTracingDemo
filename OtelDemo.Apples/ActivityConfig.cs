using System.Diagnostics;

namespace OtelDemo.Apples;

public class ActivityConfig
{
    public static ActivitySource Source { get; } = new("OtelDemo.Apples");
}