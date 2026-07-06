using System.Diagnostics;

namespace OtelDemo.Orders;

public class ActivityConfig
{
    public static readonly ActivitySource Source = new("OtelDemo.Orders");
}
