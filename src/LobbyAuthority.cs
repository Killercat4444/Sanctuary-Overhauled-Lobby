using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EM.Core;
using EM.Network;
using EM.Network.Lobby;

internal static class LobbyAuthorityRules {
 // Called only after host/lock/contract checks. Validate the entire swap before
 // changing either occupant, then let the caller broadcast once.
 internal static bool Swap(LobbyState state,LobbyPlayer source,int destination,out string error){
  error=null;if(state==null||source==null||!state.GetPlayers(true,true).Contains(source)||source.type!=EM.Core.PlayerType.Player||source.armyID<1||source.armyID>state.MaxPlayers||destination<1||destination>state.MaxPlayers){error="The source or destination slot is unavailable.";return false;}
  var assigned=state.GetPlayers(true,true).Where(p=>p.type!=EM.Core.PlayerType.Empty&&p.armyID>0).ToArray();
  if(assigned.GroupBy(p=>p.armyID).Any(g=>g.Count()>1)){error="The roster has conflicting slot assignments.";return false;}
  if(destination==source.armyID)return true;var target=assigned.SingleOrDefault(p=>p.armyID==destination);
  if(target!=null&&target.type!=EM.Core.PlayerType.Player&&target.type!=EM.Core.PlayerType.AI){error="Only occupied player or AI slots can be swapped.";return false;}
  byte origin=source.armyID;source.armyID=(byte)destination;if(target!=null)target.armyID=origin;
  foreach(var p in state.GetPlayers(true,true))p.isReady=false;return true;
 }
}

public static partial class SanctuaryLobbyState {
 internal sealed class FactionChoice {internal int Value;internal string FactionId,FactionName,CommanderName,Identity;}
 static bool preferenceApplied;static int? requestedColor;static string requestedCommander;
 internal static LobbyState AuthoritativeState {get{if(IsHost&&PrivateContracts&&hostStateField!=null)return hostStateField.GetValue(null) as LobbyState;return LobbyManager.CurrentState;}}
 internal static bool ApiFlag(string name,bool fallback){try{return (bool)FindType("Sanctuary.ModApi.Lobby").GetProperty(name,StaticAll).GetValue(null,null);}catch{return fallback;}}
 internal static bool CanEdit {get{return InLobby&&!ApiFlag("MatchUnderway",true)&&!ApiFlag("Settling",true);}}
 static string ApiStartReason(){try{return SanctuaryProtocolRules.NormalizeBlockReason(Convert.ToString(FindType("Sanctuary.ModApi.Lobby").GetProperty("StartBlockedReason",StaticAll).GetValue(null,null)));}catch{return "Mod API compatibility status is unavailable.";}}
 static bool SyncSelected(){try{foreach(var item in (IEnumerable)FindType("Sanctuary.ModApi.Lobby").GetProperty("Selection",StaticAll).GetValue(null,null)){var prop=item.GetType().GetProperty("Id")??item.GetType().GetProperty("ModId");if(prop!=null&&Convert.ToString(prop.GetValue(item,null))==SyncModId)return true;}return false;}catch{return false;}}
 internal static FactionChoice[] Choices(){
  IEnumerable registered=null;try{registered=(IEnumerable)FindType("Sanctuary.ModApi.Factions").GetProperty("Current",StaticAll).GetValue(null,null);}catch(Exception ex){Warn("Commander catalog unavailable: "+Root(ex));}
  return ChoicesFrom(registered);
 }
 internal static FactionChoice[] ChoicesFrom(IEnumerable registered){
  var list=new List<FactionChoice>{
   new FactionChoice{Value=0,FactionId="stock:eda",FactionName="EDA",CommanderName="Default",Identity="stock:eda:uel0000"},
   new FactionChoice{Value=1,FactionId="stock:chosen",FactionName="Chosen",CommanderName="Default",Identity="stock:chosen:ucl0000"},
   new FactionChoice{Value=2,FactionId="stock:guard",FactionName="Guard",CommanderName="Default",Identity="stock:guard:ugl0000"}};
  var additions=new List<FactionChoice>();if(registered!=null)foreach(var item in registered){try{
   if(item==null)continue;
   var type=item.GetType();var faction=type.GetProperty("Faction").GetValue(item,null);var commander=type.GetProperty("Commander").GetValue(item,null);var mod=type.GetProperty("Mod").GetValue(item,null);
   string modId=TextProperty(mod,"Id"),key=TextProperty(faction,"Key"),unit=TextProperty(commander,"Unit");int value=Convert.ToInt32(type.GetProperty("Value").GetValue(item,null));
   // Mod API registers additive factions. Never rename stock slots, depend on
   // registration order, or infer a mod identity from a numeric faction byte.
   if(string.IsNullOrWhiteSpace(modId)||string.IsNullOrWhiteSpace(key)||string.IsNullOrWhiteSpace(unit)||value<3||value>255)continue;
   string factionName=TextProperty(faction,"Name"),commanderName=TextProperty(commander,"Name");
   additions.Add(new FactionChoice{Value=value,FactionId=modId+":"+key,FactionName=string.IsNullOrWhiteSpace(factionName)?key:factionName,CommanderName=string.IsNullOrWhiteSpace(commanderName)?unit:commanderName,Identity=modId+":"+key+":"+unit});
  }catch(Exception ex){Warn("Ignored malformed registered faction: "+Root(ex));}}
  // Ambiguous registrations are unusable: do not silently pick a different
  // commander/faction just because it was enumerated last.
  var unique=additions.Where(c=>additions.Count(x=>x.Value==c.Value)==1&&additions.Count(x=>x.Identity==c.Identity)==1);
  list.AddRange(unique);
  return list.OrderBy(c=>c.Value).ToArray();
 }
 static string TextProperty(object o,string name){if(o==null)return null;var p=o.GetType().GetProperty(name);return p==null?null:Convert.ToString(p.GetValue(o,null));}
 internal static bool ChangeType(PlayerType type){string reason;if(!CanChangeType(type,out reason)){Chat(reason);return false;}pendingJoinArmy=0;var player=LocalPlayer() as LobbyPlayer;if(IsHost)return ApplyHumanType(player.id,type);pendingType=(int)type;typeDeadline=DateTime.UtcNow.AddSeconds(5);SendLobby("SendSetType",(byte)type);return true;}
 internal static bool BecomeUnassigned(){if(!ChangeType(EM.Core.PlayerType.Player))return false;SendLobby("SendSetArmyID",(byte)0);return true;}
 // Army zero is not a native spawn slot. Only the unassigned local human may
 // use its row controls; never resolve zero to another occupant.
 static LobbyPlayer EditableRowPlayer(int army){
  var state=AuthoritativeState;if(state==null)return null;
  if(army>0)return state.GetPlayerAtSlot(army);
  var local=LocalPlayer() as LobbyPlayer;return local!=null&&local.armyID==0&&local.type==EM.Core.PlayerType.Player?local:null;
 }
 internal static bool SetTeam(int army,int team){
  if(!CanEdit||team<1||team>64)return false;var p=EditableRowPlayer(army);if(p==null)return false;
  if(LobbyManager.IsLocalPlayer(p)){SendLobby("SendSetReady",false);SendLobby("SendSetTeam",(byte)team);return true;}
  if(IsHost&&p.type==EM.Core.PlayerType.AI){SendLobby("SendUpdateAI",(byte)army,(byte)team,p.faction);return true;}return false;
 }
 internal static bool SetFaction(int army,int value){
  if(!CanEdit||!Choices().Any(c=>c.Value==value))return false;var p=EditableRowPlayer(army);if(p==null)return false;
  if(LobbyManager.IsLocalPlayer(p)){
   if(Mode==SanctuaryLobbyMode.Overhauled){requestedCommander=Choices().First(c=>c.Value==value).Identity;return SubmitLocalSelection();}
   SendLobby("SendSetReady",false);SendLobby("SendSetFaction",(Faction)value);return true;
  }
  if(IsHost&&p.type==EM.Core.PlayerType.AI){SendLobby("SendUpdateAI",(byte)army,p.team,(Faction)value);return true;}return false;
 }
 internal static int[] ColorsForArmy(int army){
  var p=EditableRowPlayer(army);ulong id=p==null?0:Id(p);var used=new HashSet<int>(entries.Where(e=>e.Key!=id).Select(e=>e.Value.color));foreach(var s in seatColors)if(s.Key!=army)used.Add(s.Value);
  return Enumerable.Range(0,Palette.Length).Where(i=>!used.Contains(i)).ToArray();
 }
 internal static bool SelectColor(int army,int color){
  if(!CanEdit||Mode!=SanctuaryLobbyMode.Overhauled||!PrivateContracts||!ColorsForArmy(army).Contains(color))return false;
  var p=EditableRowPlayer(army);if(p==null)return false;
  if(LobbyManager.IsLocalPlayer(p)){requestedColor=color;return SubmitLocalSelection();}
  if(IsHost&&p.type==EM.Core.PlayerType.AI){occupiedColorOwners[OwnerKey(p,3)]=color;seatColors[army]=color;foreach(var player in AuthoritativeState.GetPlayers(true,true))player.isReady=false;Revision++;BroadcastState();BroadcastSnapshot();return true;}return false;
 }
 static bool SubmitLocalSelection(){
  var p=LocalPlayer();if(p==null||PlayerType(p)!=1)return false;
  if(IsHost){ulong id=Id(p);var e=BuildPreference(id,PlayerName(p));if(!CommanderAllowed(e.commander))return false;entries[id]=e;ApplyCommander(p,e.commander);Ready(p,false);RefreshOccupiedSeatColors();Revision++;BroadcastState();BroadcastSnapshot();requestedColor=null;requestedCommander=null;return true;}
  lastSentPreference=null;nextHello=DateTime.MinValue;SendClientUpdate();return true;
 }
 static void ApplyJoinPreference(){
  if(preferenceApplied||!CanEdit)return;var p=LocalPlayer();if(p==null||PlayerType(p)!=1)return;
  preferenceApplied=true;var choice=Choices().FirstOrDefault(c=>c.Identity==PersonalPreferences.Commander)??Choices().FirstOrDefault(c=>c.FactionName.Equals(PersonalPreferences.Faction,StringComparison.OrdinalIgnoreCase));
  if(choice!=null){SendLobby("SendSetReady",false);SendLobby("SendSetFaction",(Faction)choice.Value);}
 }
}
