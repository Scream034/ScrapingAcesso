namespace ScraperAcesso.Components.Menu;

/// <summary>
/// Represents a menu item that executes an action.
/// </summary>
public record class ActionMenuItem(in string Description, in Func<Task> Action) : IMenuItem;