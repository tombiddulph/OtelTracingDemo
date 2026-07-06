namespace OtelDemo.Shared;

public record class OtelDemoMessage
{
    public string WorkflowId { get; set; } = Guid.CreateVersion7().ToString();
    public string OrderId { get; set; } = Guid.CreateVersion7().ToString();
    public string Scenario { get; set; } = DemoScenarios.LinearTrace;
    public decimal Amount { get; set; }
    public bool InventoryReserved { get; set; }
    public bool PaymentAuthorized { get; set; }
    public bool NotificationSent { get; set; }
    public bool UseLinks { get; set; }
    public bool StartNewTrace { get; set; }
    public bool Batch { get; set; }
    public FailureMode FailureMode { get; set; } = FailureMode.None;
    public int Step { get; set; }
    public string? ParentMessageId { get; set; }
}

public enum FailureMode
{
    None,
    ThrowOnce,
    AlwaysThrow,
    SlowProcessing,
    ExternalCallFailure
}

public static class DemoScenarios
{
    public const string OrderFlow = "order-flow";
    public const string LinearTrace = "linear-trace";
    public const string LinkedTrace = "linked-trace";
    public const string Batch = "batch";
    public const string Fanout = "fanout";
    public const string RetryThenSuccess = "retry-then-success";
    public const string DeadLetter = "dead-letter";
    public const string ExternalCallFailure = "external-call-failure";
}
