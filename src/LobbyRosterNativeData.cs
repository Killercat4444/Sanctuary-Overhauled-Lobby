using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using EM.Core;
using EM.Network;
using EM.Network.Lobby;
using UnityEngine;

// Native catalogs and native-managed artwork; AI changes use only the game's
// checked settings request. No mod-specific paths or registration edits.
internal static class LobbyRosterNativeData {
 static readonly object visualGate=new object();
 static readonly Dictionary<ulong,object> avatarCache=new Dictionary<ulong,object>();
 static readonly Dictionary<ulong,System.DateTime> avatarPending=new Dictionary<ulong,System.DateTime>();
 static readonly Dictionary<ulong,int> avatarRequests=new Dictionary<ulong,int>();
 static readonly Dictionary<ulong,System.DateTime> avatarRetry=new Dictionary<ulong,System.DateTime>();
 static EventInfo avatarEvent;static System.Delegate avatarHandler;
 static readonly System.TimeSpan AvatarPendingTimeout=System.TimeSpan.FromSeconds(8);
 internal static string[] AiNames(){
  try{return LobbyAISettings.GetAvailableModNames()??new string[0];}catch{return new string[0];}
 }
 internal static string AiLabel(LobbyPlayer player){
  string name=player==null||player.aiSettings==null?null:player.aiSettings.modName;
  return string.IsNullOrEmpty(name)?"AI":"AI ("+name+")";
 }
 internal static bool SelectAi(LobbyPlayer expected,string modName){
  try{
   if(!SanctuaryLobbyState.InLobby||!SanctuaryLobbyState.IsHost||!SanctuaryLobbyState.CanEdit||LobbyManager.isLocked||LobbyBulkAi.IsRunning||LobbyPresetRuntime.IsApplying||expected==null||expected.type!=PlayerType.AI)return false;
   var state=LobbyManager.CurrentState;
   if(state==null||expected.armyID<1||expected.armyID>state.MaxPlayers)return false;
   var current=state.GetPlayerAtSlot(expected.armyID);
   // AI IDs can be None, so comparing IDs alone cannot identify an AI. A stale
   // popup must never change a replacement occupant after a roster refresh.
   if(!ReferenceEquals(current,expected)||current.id!=expected.id||current.type!=PlayerType.AI||!AiNames().Contains(modName))return false;
   var settings=current.aiSettings==null?LobbyAISettings.CreateDefault():current.aiSettings.Copy();
   settings.modName=modName;
   return LobbyManager.SetAISettings(current.armyID,settings);
  }catch{return false;}
 }
 internal static string CommanderUnit(SanctuaryLobbyState.FactionChoice choice){
  if(choice==null)return null;
  // Stock initialUnit values verified in common/systems/factions.lua. Mod
  // commander units are always resolved from the current exact registration.
  switch(choice.Identity){
   case "stock:eda:uel0000":return "uel0000";
   case "stock:chosen:ucl0000":return "ucl0000";
   case "stock:guard:ugl0000":return "ugl0000";
  }
  try{
   var entries=SanctuaryLobbyState.FindType("Sanctuary.ModApi.Factions").GetProperty("Current",SanctuaryLobbyState.StaticAll).GetValue(null,null) as IEnumerable;
   if(entries==null)return null;
   var matches=entries.Cast<object>().Where(e=>LobbyFactionIcons.MatchesIdentity(choice,e)).ToArray();
   if(matches.Length!=1)return null;
   var commander=matches[0].GetType().GetProperty("Commander").GetValue(matches[0],null);
   string unit=System.Convert.ToString(commander.GetType().GetProperty("Unit").GetValue(commander,null));
   return ValidUnit(unit)?unit:null;
  }catch{return null;}
 }
 static bool ValidUnit(string unit){return !string.IsNullOrEmpty(unit)&&unit.Length<=128&&Regex.IsMatch(unit,@"\A[A-Za-z0-9_-]+\z");}
 internal static Sprite CommanderPortrait(SanctuaryLobbyState.FactionChoice choice){
  string unit=CommanderUnit(choice);if(!ValidUnit(unit))return null;
  try{
   // Same virtual path and loader as client/ui/uiUtils.lua InitializeTextures
   // and AssetLoadingDelegates.LoadSprite. Data resolves the currently mounted
   // native/mod art pack. A miss is deliberately not cached: the selected mod
   // pack may still be mounting, so the next UI refresh retries safely.
   var entry=EM.Gamedata.Data.GetFileEntry("UI/Sprites/Icons/Units/"+unit+".sansprite");
   if(entry==null)return null;
   var sprite=EM.Gamedata.Load.LoadGamedataSprite(entry);
   return sprite==null?null:sprite.GetSprite();
  }catch{return null;}
 }

 // The game's supported avatar path is asynchronous: SteamLobbyBackend asks
 // Steam for the large friend image and publishes the Texture2D through
 // LobbyManager.OnLobbyAvatarTextureLoad. Keep the API object-typed here so
 // this adapter remains backend-neutral; NativeLobbyUi validates/casts it to
 // Texture2D on the Unity main thread.
 internal static object PlayerAvatar(LobbyPlayer player){
  if(player==null||(player.type!=PlayerType.Player&&player.type!=PlayerType.Observer))return null;
  ulong key=NativeId(player.id);if(key==0)return null;
  lock(visualGate){
   object cached;if(avatarCache.TryGetValue(key,out cached)&&cached!=null)return cached;
   var now=System.DateTime.UtcNow;System.DateTime pending;if(avatarPending.TryGetValue(key,out pending)){if(now<pending)return null;avatarPending.Remove(key);}
   System.DateTime retry;if(avatarRetry.TryGetValue(key,out retry)&&now<retry)return null;
   int requests;avatarRequests.TryGetValue(key,out requests);if(requests>=2||!EnsureAvatarSubscription())return null;
   avatarRequests[key]=requests+1;avatarPending[key]=now+AvatarPendingTimeout;
  }
  try{
   var backend=SanctuaryLobbyState.FindType("EM.Network.SteamLobbyBackend");
   var instance=backend.GetField("Instance",BindingFlags.Public|BindingFlags.Static).GetValue(null);
   var load=backend.GetMethod("LoadAvatar",BindingFlags.Public|BindingFlags.Instance);
   if(instance==null||load==null)throw new System.MissingMemberException("Native avatar loader unavailable.");
   load.Invoke(instance,new object[]{player.id});
  }catch{lock(visualGate){avatarPending.Remove(key);avatarRetry[key]=System.DateTime.UtcNow.AddSeconds(2);}}
  return null;
 }
 static ulong NativeId(object id){
  if(id==null)return 0;try{if(id is ulong)return (ulong)id;var field=id.GetType().GetField("value",BindingFlags.Public|BindingFlags.Instance);return field==null?0:System.Convert.ToUInt64(field.GetValue(id));}catch{return 0;}
 }
 static bool EnsureAvatarSubscription(){
  if(avatarHandler!=null)return true;
  try{
   avatarEvent=typeof(LobbyManager).GetEvent("OnLobbyAvatarTextureLoad",BindingFlags.Public|BindingFlags.Static);
   var invoke=avatarEvent==null?null:avatarEvent.EventHandlerType.GetMethod("Invoke");var parameters=invoke==null?null:invoke.GetParameters();
   if(parameters==null||parameters.Length!=3||parameters[0].ParameterType!=typeof(bool))return false;
   var method=typeof(LobbyRosterNativeData).GetMethod("AvatarLoaded",BindingFlags.NonPublic|BindingFlags.Static).MakeGenericMethod(parameters[1].ParameterType,parameters[2].ParameterType);
   avatarHandler=System.Delegate.CreateDelegate(avatarEvent.EventHandlerType,method);avatarEvent.AddEventHandler(null,avatarHandler);return true;
  }catch{avatarEvent=null;avatarHandler=null;return false;}
 }
 static void AvatarLoaded<TId,TTexture>(bool success,TId id,TTexture texture){
  ulong key=NativeId(id);if(key==0)return;
  lock(visualGate){
   // Native requests can finish out of order. Accept a successful completion
   // from either of our two bounded attempts even if an earlier failure already
   // cleared the current pending deadline.
   if(!avatarRequests.ContainsKey(key))return;
   if(success&&texture!=null){avatarCache[key]=texture;avatarPending.Remove(key);avatarRetry.Remove(key);}
   else{avatarPending.Remove(key);avatarRetry[key]=System.DateTime.UtcNow.AddSeconds(2);}
  }
 }
 internal static void ResetVisualCache(){
  lock(visualGate){
   if(avatarEvent!=null&&avatarHandler!=null)try{avatarEvent.RemoveEventHandler(null,avatarHandler);}catch{}
   avatarEvent=null;avatarHandler=null;avatarCache.Clear();avatarPending.Clear();avatarRequests.Clear();avatarRetry.Clear();
  }
 }
}
