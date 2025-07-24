namespace ScraperAcesso.Components.Log.Internal;

using System;
using System.Reflection;
using System.Diagnostics;

public sealed class LogMessage
{
	public readonly LogLevel Level;
	public readonly string Value;
	public readonly StackFrame Frame;

	public LogMessage(in LogLevel level, in string value, in StackFrame frame)
	{
		Level = level;
		Value = value;
		Frame = frame;
	}

	public override string ToString()
	{
		MethodBase? method = Frame.GetMethod();
		string className = method?.DeclaringType?.Name ?? string.Empty;

		return $"[{Level} {DateTime.Now:HH:mm:ss} {className}->{method?.Name}] {Value}";
	}
}