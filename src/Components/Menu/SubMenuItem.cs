namespace ScraperAcesso.Components.Menu;

/// <summary>
/// Represents a menu item that opens a sub-menu.
/// </summary>
public record SubMenuItem(in string Description, in ConsoleMenuManager SubMenu) : IMenuItem;