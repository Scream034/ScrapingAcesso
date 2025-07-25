namespace ScraperAcesso.Components.Menu;

using ScraperAcesso.Components.Log;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Log.Internal;

/// <summary>
/// Manages a single level of a hierarchical console menu.
/// </summary>
public class ConsoleMenuManager
{
    private readonly string _title;
    private readonly List<IMenuItem> _items = new();
    private readonly ConsoleMenuManager? _parent; // Link to the parent menu

    /// <summary>
    /// Creates a new menu.
    /// </summary>
    /// <param name="title">The title to be displayed at the top of the menu.</param>
    /// <param name="parent">The parent menu. If null, this is the root menu.</param>
    public ConsoleMenuManager(in string title, in ConsoleMenuManager? parent = null)
    {
        _title = title;
        _parent = parent;

        // If this is a sub-menu, automatically add a "Back" option
        if (_parent != null)
        {
            // The action is an empty task, the loop will handle returning.
            _items.Add(new ActionMenuItem("Back", () => Task.CompletedTask));
        }
    }

    /// <summary>
    /// Adds a new action item to the menu.
    /// </summary>
    public void AddAction(string description, Func<Task> action)
    {
        _items.Add(new ActionMenuItem(description, action));
    }

    /// <summary>
    /// Adds a new sub-menu and returns it for configuration.
    /// </summary>
    public ConsoleMenuManager AddSubMenu(string description)
    {
        var subMenu = new ConsoleMenuManager(description, this);
        _items.Add(new SubMenuItem(description, subMenu));
        return subMenu;
    }

    /// <summary>
    /// Sets the final exit option for the root menu.
    /// </summary>
    public void SetExitOption(string description = "Exit")
    {
        if (_parent != null)
        {
            Log.Warning("SetExitOption should only be called on the root menu.");
            return;
        }
        _items.Add(new ActionMenuItem(description, () => Task.CompletedTask));
    }

    /// <summary>
    /// Runs the main loop for this menu level.
    /// </summary>
    public async Task RunAsync()
    {
        if (_items.Count == 0)
        {
            Log.Error($"Menu '{_title}' has no items.");
            return;
        }

        (string Message, LogLevel Level)? feedbackMessage = null;

        while (true)
        {
            Display(feedbackMessage);
            feedbackMessage = null;

            int choice = GetUserInput();
            int selectionIndex = choice - 1;

            if (selectionIndex < 0 || selectionIndex >= _items.Count)
            {
                feedbackMessage = ("Invalid input. Please select a valid option.", LogLevel.Warning);
                continue;
            }

            var selectedItem = _items[selectionIndex];

            // Check if it's the exit/back option
            // For root menu, it's the last item. For sub-menu, it's the first.
            bool isExitOrBack = (_parent == null && selectionIndex == _items.Count - 1) ||
                                (_parent != null && selectionIndex == 0);

            if (isExitOrBack)
            {
                // Exit the loop and return to the parent menu's loop (or exit the app)
                return;
            }

            // Handle the selected item based on its type
            switch (selectedItem)
            {
                case ActionMenuItem actionItem:
                    await ExecuteActionItemAsync(actionItem);
                    break;
                case SubMenuItem subMenuItem:
                    // Recursively run the sub-menu's loop
                    await subMenuItem.SubMenu.RunAsync();
                    break;
            }
        }
    }

    private async Task ExecuteActionItemAsync(ActionMenuItem item)
    {
        Console.Clear();
        Log.Print($"Executing: '{item.Description}'");
        Log.Print("--------------------------------------------------");
        try
        {
            await item.Action.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error($"An error occurred in '{item.Description}': {ex.Message}");
            Log.Error($"Details: {ex}");
        }
        Log.Print("--------------------------------------------------");
        Log.Print("Action finished. Press Enter to continue...");
        Console.ReadLine();
    }

    private void Display((string Message, LogLevel Level)? feedback)
    {
        Console.Clear();
        if (feedback.HasValue)
        {
            PrintFeedbackMessage(feedback.Value.Message, feedback.Value.Level);
        }

        Console.WriteLine($"\n========== {_title.ToUpper()} ==========");
        for (int i = 0; i < _items.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {_items[i].Description}");
        }
        Console.WriteLine("======================================");
        Console.Write("Select an option: ");
    }

    private void PrintFeedbackMessage(string message, LogLevel level)
    {
        var originalColor = Console.ForegroundColor;
        switch (level)
        {
            case LogLevel.Warning: Console.ForegroundColor = ConsoleColor.Yellow; break;
            case LogLevel.Error: Console.ForegroundColor = ConsoleColor.Red; break;
            default: Console.ForegroundColor = ConsoleColor.Cyan; break;
        }
        Console.WriteLine($"\n[!] {message}");
        Console.ResetColor();
    }

    private int GetUserInput()
    {
        string? input = Console.ReadLine();
        return int.TryParse(input, out int choice) ? choice : -1;
    }
}