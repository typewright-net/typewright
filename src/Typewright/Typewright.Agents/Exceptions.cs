namespace Typewright.Agents;

/// <summary>
/// The agent has stopped (or was never able to process the message): thrown when posting
/// to a stopped agent, and used to fail requests that were still queued when the agent
/// stopped or aborted, so no caller waits forever.
/// </summary>
public sealed class AgentStoppedException : InvalidOperationException
{
    /// <summary>Creates the exception for the named agent.</summary>
    public AgentStoppedException(string agentName)
        : base($"The agent '{agentName}' has stopped; the message was not processed.")
    {
        AgentName = agentName;
    }

    /// <summary>The agent that stopped.</summary>
    public string AgentName { get; }
}

/// <summary>The bounded mailbox was full and its policy is to reject rather than wait.</summary>
public sealed class MailboxFullException : InvalidOperationException
{
    /// <summary>Creates the exception for the named agent.</summary>
    public MailboxFullException(string agentName)
        : base($"The mailbox of agent '{agentName}' is full.")
    {
        AgentName = agentName;
    }

    /// <summary>The agent whose mailbox was full.</summary>
    public string AgentName { get; }
}

/// <summary>A queued request was dropped by the mailbox's overflow policy to make room for another message.</summary>
public sealed class MessageDroppedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public MessageDroppedException()
        : base("The message was dropped by the mailbox's overflow policy.")
    {
    }
}

/// <summary>No handler was registered for the message's runtime type.</summary>
public sealed class UnhandledMessageException : InvalidOperationException
{
    /// <summary>Creates the exception for <paramref name="messageType"/>.</summary>
    public UnhandledMessageException(Type messageType, string? detail = null)
        : base($"No handler is registered for messages of type '{messageType.Name}'.{(detail is null ? "" : " " + detail)}")
    {
        MessageType = messageType;
    }

    /// <summary>The unhandled message's runtime type.</summary>
    public Type MessageType { get; }
}
