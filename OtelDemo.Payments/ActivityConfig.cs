using System.Diagnostics;

namespace OtelDemo.Payments;

public class ActivityConfig
{
    public static readonly ActivitySource Source = new("OtelDemo.Payments");
}
