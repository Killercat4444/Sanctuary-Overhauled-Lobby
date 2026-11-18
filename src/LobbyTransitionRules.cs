using System;
using System.Collections.Generic;
using System.Linq;
using EM.Core;
using EM.Network;
using EM.Network.Lobby;

internal static class LobbyTransitionRules {
 internal static bool CanChange(LobbyState state,PlayerID sender,PlayerType requested,out string reason){
  reason=null;
  if(state==null||requested!=PlayerType.Player&&requested!=PlayerType.Observer){reason="The requested player transition is unavailable.";return false;}
  var members=state.GetPlayers(true,true).ToArray();var player=state.GetPlayer(sender);
  if(player==null||!player.IsHumanPlayer||members.Count(p=>p.id==sender)!=1){reason="The player is not a unique human member of this lobby.";return false;}
  if(player.type==requested)return true;
  if(state.MaxPlayers<1||state.MaxPlayers>64||state.MaxObservers<0||state.MaxObservers>64||members.Length>state.MaxPlayers+state.MaxObservers){reason="The lobby capacity is inconsistent; no player was moved.";return false;}
  if(requested==PlayerType.Observer&&state.ObserverCount>=state.MaxObservers){reason=state.MaxObservers==0?"Observer slots are disabled (0 allowed).":"Observer slots are full ("+state.ObserverCount+"/"+state.MaxObservers+").";return false;}
  if(requested==PlayerType.Player&&state.EmptySeatsCount<=0){reason="No player slot is available; you remain an observer.";return false;}
  return true;
 }
 internal static bool Apply(LobbyState state,PlayerID sender,PlayerType requested,bool hostAuthority,bool editable,out bool changed,out string reason){
  changed=false;reason=null;if(!hostAuthority||!editable){reason="The lobby is locked or is not accepting player changes.";return false;}
  if(!CanChange(state,sender,requested,out reason))return false;
  var player=state.GetPlayer(sender);if(player.type==requested)return true;
  // Never remove/re-add a member: the native failed observer branch loses them.
  // Preserve the exact member object, PlayerID, name, faction, team and hostID.
  player.type=requested;player.armyID=0;
  foreach(var member in state.GetPlayers(true,true))member.isReady=false;
  changed=true;return true;
 }
 internal static bool SafeClient(bool localHost,bool confirmedHost){return localHost||confirmedHost;}
}

internal static class LobbyTeamRules {
 internal static bool Apply(LobbyState state,IDictionary<int,int> teams,out bool changed,out string reason){
  changed=false;reason=null;if(state==null||teams==null){reason="Team assignments are unavailable.";return false;}
  var assigned=state.GetPlayers(true,true).Where(p=>(p.type==PlayerType.Player||p.type==PlayerType.AI)&&p.armyID>0).ToArray();
  if(assigned.GroupBy(p=>p.armyID).Any(g=>g.Count()!=1)||assigned.Any(p=>!teams.ContainsKey(p.armyID)||teams[p.armyID]<1||teams[p.armyID]>64)){reason="Spawn positions are unavailable or the roster has conflicting assignments.";return false;}
  changed=assigned.Any(p=>p.team!=teams[p.armyID]);if(!changed)return true;
  foreach(var player in assigned)player.team=(byte)teams[player.armyID];
  foreach(var player in state.GetPlayers(true,true))player.isReady=false;
  return true;
 }
}

public static partial class SanctuaryLobbyState {
 static bool hostTypeGuardConfirmed;static int pendingType=-1;static DateTime typeDeadline;
 static PlayerID confirmedTypeHost,confirmedTypeSession;
 static void ClearTransitionCapability(){hostTypeGuardConfirmed=false;confirmedTypeHost=confirmedTypeSession=default(PlayerID);pendingType=-1;pendingJoinArmy=0;}
 static void InitializeTransitionGuard(){LobbyManager.OnLobbyEntered+=delegate(LobbyState unused){ClearTransitionCapability();};LobbyManager.OnLobbyClosed+=ClearTransitionCapability;LobbyManager.OnConnectionLost+=ClearTransitionCapability;LobbyManager.OnKicked+=ClearTransitionCapability;}
 static void ConfirmTransitionHost(){var state=AuthoritativeState;if(state==null)return;confirmedTypeHost=state.hostID;confirmedTypeSession=LobbyManager.currentSessionID;hostTypeGuardConfirmed=true;}
 internal static bool SafeTypeChanges {get{var state=AuthoritativeState;return IsHost||hostTypeGuardConfirmed&&state!=null&&state.hostID==confirmedTypeHost&&LobbyManager.currentSessionID==confirmedTypeSession;}}
 internal static bool CanChangeType(PlayerType type,out string reason){
  reason=null;if(!PrivateContracts||!CanEdit||LobbyManager.isLocked){reason="The lobby is locked or busy; player changes are unavailable.";return false;}
  if(!SafeTypeChanges){reason="Safe player changes require the host to run Overhauled Lobby "+SanctuaryOverhauledLobbyPlugin.PluginVersion+". No request was sent.";return false;}
  var player=LocalPlayer() as LobbyPlayer;if(player==null){reason="Your lobby member is unavailable.";return false;}
  return LobbyTransitionRules.CanChange(AuthoritativeState,player.id,type,out reason);
 }
 static bool ApplyHumanType(PlayerID sender,PlayerType requested){
  bool changed;string reason;var state=AuthoritativeState;
  if(!LobbyTransitionRules.Apply(state,sender,requested,IsHost&&LobbyManager.isHostRunning,CanEdit&&!LobbyManager.isLocked,out changed,out reason)){var player=state==null?null:state.GetPlayer(sender);if(player!=null)Chat(player.name+": "+reason);return false;}
  if(changed){Revision++;BroadcastState();BroadcastSnapshot();}
  return true;
 }
 static bool HandleNativeTypeChange(PlayerID sender,Unity.Collections.NativeArray<byte> data){
  // Exact native contract: one byte type 4, followed by one byte PlayerType.
  // Guarded by the installed Trebuchet/Mod API identities before this is called.
  if(data.Length!=2||data[1]!=1&&data[1]!=2)return false;
  ApplyHumanType(sender,(PlayerType)data[1]);return false;
 }
 static void TrackTypeRequest(){
  if(pendingType<0)return;var player=LocalPlayer();
  if(player!=null&&PlayerType(player)==pendingType){pendingType=-1;return;}
  if(DateTime.UtcNow<=typeDeadline)return;pendingType=-1;
  Chat("Player change was refused or timed out. Your existing lobby membership was retained; check available slots.");
 }
}
