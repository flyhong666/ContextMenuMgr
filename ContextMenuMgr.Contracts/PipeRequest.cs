namespace ContextMenuMgr.Contracts;

/// <summary>
/// Represents the pipe Request.
/// </summary>
public sealed record PipeRequest
{
    /// <summary>
    /// Gets or sets the command.
    /// </summary>
    public PipeCommand Command { get; init; }

    /// <summary>
    /// Gets or sets the item Id.
    /// </summary>
    public string? ItemId { get; init; }

    /// <summary>
    /// Gets or sets the enable.
    /// </summary>
    public bool? Enable { get; init; }

    /// <summary>
    /// Gets or sets the item payload used when a command targets a scene-only item.
    /// </summary>
    public ContextMenuEntry? Item { get; init; }

    /// <summary>
    /// Gets or sets the shell Attribute.
    /// </summary>
    public ContextMenuShellAttribute? ShellAttribute { get; init; }

    /// <summary>
    /// Gets or sets the text Value.
    /// </summary>
    public string? TextValue { get; init; }

    /// <summary>
    /// Gets or sets the rule storage kind.
    /// </summary>
    public string? RuleStorageKind { get; init; }

    /// <summary>
    /// Gets or sets the rule path.
    /// </summary>
    public string? RulePath { get; init; }

    /// <summary>
    /// Gets or sets the INI section for rule values.
    /// </summary>
    public string? RuleSection { get; init; }

    /// <summary>
    /// Gets or sets the rule key name.
    /// </summary>
    public string? RuleKeyName { get; init; }

    /// <summary>
    /// Gets or sets the rule registry value kind.
    /// </summary>
    public string? RuleValueKind { get; init; }

    /// <summary>
    /// Gets or sets the rule value. Null means delete/reset.
    /// </summary>
    public string? RuleValue { get; init; }

    /// <summary>
    /// Gets or sets the interactive user SID for HKCU registry writes requested by the frontend.
    /// </summary>
    public string? RuleUserSid { get; init; }

    /// <summary>
    /// Gets or sets the definition Xml.
    /// </summary>
    public string? DefinitionXml { get; init; }

    /// <summary>
    /// Gets or sets the UI culture name.
    /// </summary>
    public string? CultureName { get; init; }

    /// <summary>
    /// Gets or sets the scene Kind.
    /// </summary>
    public ContextMenuSceneKind? SceneKind { get; init; }

    /// <summary>
    /// Gets or sets the scope Value.
    /// </summary>
    public string? ScopeValue { get; init; }

    /// <summary>
    /// Gets or sets the decision.
    /// </summary>
    public ContextMenuDecision? Decision { get; init; }
}
