using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

[BepInPlugin("com.sanctuary.overhauled-lobby", "Sanctuary Overhauled Lobby", "1.2.0")]
public sealed class SanctuaryOverhauledLobbyPlugin : BaseUnityPlugin
{
    public const string PluginId="com.sanctuary.overhauled-lobby";
    public const string PluginVersion="1.2.0";
    internal static ManualLogSource Log;
    Harmony harmony;bool active;
    void Awake() {
        Log=Logger;
        string legacy=System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory,"SanctuaryMods","LobbyPresets","LobbyPresets.dll");
        if(System.IO.File.Exists(legacy)){Logger.LogError("Overhauled Lobby refused to activate: standalone Lobby Presets is still discoverable. Preserve it outside active discovery during an approved closed-game installation.");enabled=false;return;}
        SanctuaryLobbyState.Initialize();harmony=new Harmony(PluginId);active=true;
        LobbyMergedOptions.Install(harmony);
        int patched=0;
        foreach(var type in new[]{typeof(OverhaulHostMessagePatch),typeof(OverhaulClientMessagePatch),typeof(OverhaulCanStartPatch),typeof(OverhaulStartPatch),typeof(InMatchColorPatch),typeof(OverhaulUiDataPatch),typeof(LobbyRandomLaunchPatch)}) {
            try { harmony.CreateClassProcessor(type).Patch();patched++; }
            catch(System.Exception ex) { SanctuaryLobbyState.PrivateContracts=false;Logger.LogError("Disabled guarded lobby features because patch "+type.Name+" failed: "+SanctuaryLobbyState.Root(ex)); }
        }
        Logger.LogInfo("Sanctuary Overhauled Lobby "+PluginVersion+" loaded; patches="+patched+"/7, private features="+SanctuaryLobbyState.PrivateContracts);
    }
    void Update() { if(active){LobbyPresetRuntime.Tick();SanctuaryLobbyState.Tick();LobbyBulkAi.Tick();LobbyUnitRestrictions.Tick();} }
    void LateUpdate() { if(active)SanctuaryLobbyUi.TickCanvas(); }
    void OnDestroy(){SanctuaryLobbyUi.RestoreNativeLayout();try{LobbyUnitRestrictions.Shutdown();}finally{if(harmony!=null)harmony.UnpatchSelf();SanctuaryLobbyState.ResetSession();}}
}

