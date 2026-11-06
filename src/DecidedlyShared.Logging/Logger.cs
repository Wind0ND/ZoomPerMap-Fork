using System;
using System.Diagnostics;
using StardewModdingAPI;
using StardewValley;

namespace DecidedlyShared.Logging;

public class Logger
{
	private readonly IMonitor monitor;

	private ITranslationHelper translationHelper;

	public Logger(IMonitor monitor, ITranslationHelper translationHelper = null)
	{
		this.monitor = monitor;
		this.translationHelper = translationHelper;
	}

	public void Log(string logMessage, LogLevel logLevel = (LogLevel)2, bool displayInHud = false)
	{
		monitor.Log(logMessage, logLevel);
		if (Context.IsWorldReady && displayInHud)
		{
			HUDMessage val = new HUDMessage(logMessage, 2);
			if (!Game1.doesHUDMessageExist(logMessage))
			{
				Game1.addHUDMessage(val);
			}
		}
	}

	public void Error(string logMessage, bool displayInHud = false)
	{
		Log(logMessage, (LogLevel)4, displayInHud);
	}

	public void Warn(string logMessage, bool displayInHud = false)
	{
		Log(logMessage, (LogLevel)3, displayInHud);
	}

	[Conditional("DEBUG")]
	public void Debug(string logMessage)
	{
		Log(logMessage, (LogLevel)2);
	}

	public void Exception(Exception e, string? message = null)
	{
		if (message != null)
		{
			monitor.Log(message, (LogLevel)4);
		}
		monitor.Log("Exception: " + e.Message, (LogLevel)4);
		monitor.Log("Stack trace: \n" + e.StackTrace, (LogLevel)4);
	}
}
