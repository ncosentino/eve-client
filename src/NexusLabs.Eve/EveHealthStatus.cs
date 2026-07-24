namespace NexusLabs.Eve;

/// <summary>
/// Describes a ready eve deployment.
/// </summary>
public sealed record EveHealthStatus
{
    internal EveHealthStatus(bool ok, string status, string workflowId)
    {
        Ok = ok;
        Status = status;
        WorkflowId = workflowId;
    }

    /// <summary>
    /// Gets whether the health response reports success.
    /// </summary>
    public bool Ok { get; }

    /// <summary>
    /// Gets the server readiness status.
    /// </summary>
    public string Status { get; }

    /// <summary>
    /// Gets the workflow identifier used by the deployment.
    /// </summary>
    public string WorkflowId { get; }
}
