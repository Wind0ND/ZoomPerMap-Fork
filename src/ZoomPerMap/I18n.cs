using System;
using System.CodeDom.Compiler;
using StardewModdingAPI;

namespace ZoomPerMap;

[GeneratedCode("TextTemplatingFileGenerator", "1.0.0")]
internal static class I18n
{
	public static class Keys
	{
		public const string Settings_DefaultOutdoorZoom = "settings.default-outdoor-zoom";

		public const string Settings_DefaultIndoorZoom = "settings.default-indoor-zoom";

		public const string Settings_DefaultMineZoom = "settings.default-mine-zoom";

		public const string Settings_DefaultEventZoom = "settings.default-event-zoom";
	}

	private static ITranslationHelper? Translations;

	public static void Init(ITranslationHelper translations)
	{
		Translations = translations;
	}

	public static string Settings_DefaultOutdoorZoom()
	{
		return (string)(GetByKey("settings.default-outdoor-zoom"));
	}

	public static string Settings_DefaultIndoorZoom()
	{
		return (string)(GetByKey("settings.default-indoor-zoom"));
	}

	public static string Settings_DefaultMineZoom()
	{
		return (string)(GetByKey("settings.default-mine-zoom"));
	}

	public static string Settings_DefaultEventZoom()
	{
		return (string)(GetByKey("settings.default-event-zoom"));
	}

	public static Translation GetByKey(string key, object? tokens = null)
	{
		if (Translations == null)
		{
			throw new InvalidOperationException("You must call I18n.Init from the mod's entry method before reading translations.");
		}
		return Translations.Get(key, tokens);
	}
}
