using System.Diagnostics;

namespace OtelDemo.Inventory;

public class ActivityConfig
{
    public static readonly ActivitySource Source = new("OtelDemo.Inventory");
}
