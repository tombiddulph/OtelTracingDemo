using System.Diagnostics;

namespace OtelDemo.Audit;

public class ActivityConfig
{
    public static readonly ActivitySource Source = new("OtelDemo.Audit");
}
