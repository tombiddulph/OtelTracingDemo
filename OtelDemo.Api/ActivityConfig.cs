using System.Diagnostics;

namespace OtelDemo.Api;

public class ActivityConfig
{
    public static readonly ActivitySource Source = new("OtelDemo.Api");
}