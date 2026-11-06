using System;
using DecidedlyShared.APIs;
using DecidedlyShared.Logging;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;

namespace ZoomPerMap;

public class ModEntry : Mod
{
    private Logger logger;
    private static Logger StaticLogger;
    private ModConfig config;
    private static ModConfig StaticConfig;

    // [patch] state used by the continuous zoom tracker
    private string? trackedLocationName;
    private float lastKnownDesiredBaseZoom;
    private int saveCountdown;

    /// <summary>The upstream mod this fork backports from.</summary>
    private const string UpstreamId = "DecidedlyHuman.ZoomPerMap";

    /// <summary>
    /// Highest upstream version that was actually published when this fork was written (Nexus shows 1.0.4).
    /// If the author ships anything newer, this fork switches itself off instead of
    /// fighting with it over <see cref="Game1.options"/>.
    /// </summary>
    private static readonly ISemanticVersion AuthorLatestKnown = new SemanticVersion(1, 0, 4);

    public override void Entry(IModHelper helper)
    {
        if (this.BailIfUpstreamIsNewer(helper))
            return;

        I18n.Init(helper.Translation);
        this.config = helper.ReadConfig<ModConfig>();
        StaticConfig = this.config;
        this.logger = new Logger(this.Monitor);
        StaticLogger = this.logger;

        helper.Events.Display.MenuChanged += this.DisplayOnMenuChanged;
        helper.Events.Player.Warped += this.PlayerOnWarped;
        helper.Events.GameLoop.GameLaunched += this.GameLoopOnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += this.GameLoopOnSaveLoaded;
        helper.Events.GameLoop.UpdateTicked += this.GameLoopOnUpdateTicked; // [patch]
        helper.Events.GameLoop.ReturnedToTitle += this.GameLoopOnReturnedToTitle; // [patch]

        Harmony harmony = new Harmony(this.ModManifest.UniqueID);
        harmony.Patch(
            AccessTools.Method(typeof(Event), nameof(Event.exitEvent)),
            postfix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.ExitEvent_Postfix)));
    }

    // [patch] self-disable once the author has published something newer than this fork knows about
    private bool BailIfUpstreamIsNewer(IModHelper helper)
    {
        IModInfo? upstream = helper.ModRegistry.Get(UpstreamId);

        if (upstream is null)
            return false;

        if (!upstream.Manifest.Version.IsNewerThan(AuthorLatestKnown))
        {
            this.Monitor.Log(
                $"Both this fork and {UpstreamId} v{upstream.Manifest.Version} are installed. " +
                "Running both will fight over the zoom level - remove one of them.",
                LogLevel.Warn);
            return false;
        }

        this.Monitor.Log(
            $"Disabling this fork: the official {UpstreamId} v{upstream.Manifest.Version} is newer than " +
            $"v{AuthorLatestKnown}, which is what this fork was written against. Use the official mod instead.",
            LogLevel.Alert);

        return true;
    }

    private void GameLoopOnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        Game1.options.desiredBaseZoomLevel = GetZoomForLocation(Game1.currentLocation);
        this.SyncZoomTracker(); // [patch]
    }

    private void GameLoopOnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        IGenericModConfigMenuApi? gmcm;

        if (!this.Helper.ModRegistry.IsLoaded("spacechase0.GenericModConfigMenu"))
            return;

        gmcm = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");

        if (gmcm is null)
            return;

        gmcm.Register(
            this.ModManifest,
            () =>
            {
                this.config = new ModConfig();
                StaticConfig = this.config; // [patch] keep the static copy in sync
            },
            () => this.Helper.WriteConfig(this.config));

        gmcm.AddNumberOption(
            this.ModManifest,
            () => this.config.defaultOutdoorZoomLevel,
            (val) => this.config.defaultOutdoorZoomLevel = val,
            () => I18n.Settings_DefaultOutdoorZoom());

        gmcm.AddNumberOption(
            this.ModManifest,
            () => this.config.defaultIndoorZoomLevel,
            (val) => this.config.defaultIndoorZoomLevel = val,
            () => I18n.Settings_DefaultIndoorZoom());

        gmcm.AddNumberOption(
            this.ModManifest,
            () => this.config.defaultMineZoomLevel,
            (val) => this.config.defaultMineZoomLevel = val,
            () => I18n.Settings_DefaultMineZoom());

        gmcm.AddNumberOption(
            this.ModManifest,
            () => this.config.eventZoomLevel,
            (val) => this.config.eventZoomLevel = val,
            () => I18n.Settings_DefaultEventZoom());
    }

    public static void ExitEvent_Postfix(Event __instance)
    {
        try
        {
            Game1.options.desiredBaseZoomLevel = GetZoomForLocation(Game1.player.currentLocation);
        }
        catch (Exception e)
        {
            StaticLogger.Error("Caught an exception in Event.exitMethod postfix.");
            StaticLogger.Exception(e);
        }
    }

    private void PlayerOnWarped(object? sender, WarpedEventArgs e)
    {
        // [patch] only react to the local player's warps
        if (!e.IsLocalPlayer)
            return;

        if (Game1.eventUp)
            Game1.options.desiredBaseZoomLevel = StaticConfig.eventZoomLevel;
        else
            Game1.options.desiredBaseZoomLevel = GetZoomForLocation(e.NewLocation);

        this.SyncZoomTracker(); // [patch]
    }

    // [patch] remembers a zoom change even if the player never opens the game menu
    private void GameLoopOnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (this.saveCountdown > 0 && --this.saveCountdown == 0)
        {
            this.Helper.WriteConfig(this.config);
        }

        if (!Context.IsWorldReady || Game1.currentLocation is null || !e.IsMultipleOf(6u))
            return;

        if (Game1.eventUp)
        {
            this.SyncZoomTracker();
            return;
        }

        string locationName = Game1.currentLocation.Name;
        float desiredZoom = Game1.options.desiredBaseZoomLevel;

        if (this.trackedLocationName != locationName)
        {
            this.SyncZoomTracker();
        }
        else if (Math.Abs(desiredZoom - this.lastKnownDesiredBaseZoom) > 0.0001f)
        {
            this.RememberCurrentLocationZoom(desiredZoom);
            this.SyncZoomTracker();
        }
    }

    // [patch]
    private void GameLoopOnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        if (this.saveCountdown > 0)
        {
            this.saveCountdown = 0;
            this.Helper.WriteConfig(this.config);
        }
    }

    private void DisplayOnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        // [patch] guard against firing outside a save, and defer to the tracker during events
        if (e.OldMenu is not GameMenu || !Context.IsWorldReady || Game1.currentLocation is null)
            return;

        if (Game1.eventUp)
        {
            this.SyncZoomTracker();
            return;
        }

        this.RememberCurrentLocationZoom(Game1.options.desiredBaseZoomLevel);
        this.SyncZoomTracker();
    }

    // [patch]
    private void SyncZoomTracker()
    {
        if (!Context.IsWorldReady || Game1.currentLocation is null)
            return;

        this.trackedLocationName = Game1.currentLocation.Name;
        this.lastKnownDesiredBaseZoom = Game1.options.desiredBaseZoomLevel;
    }

    // [patch]
    private void RememberCurrentLocationZoom(float zoom)
    {
        if (!Context.IsWorldReady || Game1.currentLocation is null)
            return;

        string locationName = Game1.currentLocation.Name;

        if (this.config.zoomLevels.TryGetValue(locationName, out float stored) &&
            Math.Abs(stored - zoom) <= 0.0001f)
        {
            return;
        }

        this.config.zoomLevels[locationName] = zoom;
        this.saveCountdown = 90;
    }

    private static float GetZoomForLocation(GameLocation location)
    {
        if (StaticConfig.zoomLevels.TryGetValue(location.Name, out float zoomLevel))
            return zoomLevel;

        if (location is MineShaft || location is VolcanoDungeon)
            return StaticConfig.defaultMineZoomLevel;

        return location.IsOutdoors ? StaticConfig.defaultOutdoorZoomLevel : StaticConfig.defaultIndoorZoomLevel;
    }
}
