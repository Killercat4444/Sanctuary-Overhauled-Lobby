using System;
using System.Linq;
using EM.Core;
using EM.Network;
using EM.Network.Lobby;

public static partial class LobbyPresetRuntime {
 static byte waitingArmy;
 static string waitingAction;
 static Seat nativeHost;
 static LobbyPlayer AtArmy(int army) { return LobbyManager.CurrentState.GetPlayerAtSlot(army); }
 static LobbyPlayer LocalHost() { return LobbyManager.CurrentState.GetPlayers(true,true).FirstOrDefault(p=>LobbyManager.IsLocalPlayer(p)); }
 static void Begin(int number) {
  if(!InLobby() || !IsHost() || !ApiAllowsEditing() || pending!=null || LobbyBulkAi.IsRunning)return;
  var preset=LoadPresets().FirstOrDefault(p=>p.Number==number);
  if(preset==null){Chat("No preset is assigned to Ctrl+"+number+".");return;}
  var state=LobbyManager.CurrentState;
  var local=LocalHost();
  if(local==null || local.type!=PlayerType.Player){Chat("Assign yourself as a player before applying a preset.");return;}
  if(preset.Seats.Count(s=>s.Host)>1){Chat("Preset has more than one host entry.");return;}
  var host=preset.Seats.FirstOrDefault(s=>s.Host);
  int hostArmy=host==null?(int)local.armyID:(int)host.Army;
  if(hostArmy<1 || hostArmy>state.MaxPlayers){Chat("Preset host army is outside this map's "+state.MaxPlayers+" available armies.");return;}
  var armies=preset.Seats.Select(s=>(int)s.Army).ToArray();
  if(armies.Distinct().Count()!=armies.Length){Chat("Preset assigns more than one member to the same army.");return;}
  foreach(var seat in preset.Seats){
   if(seat.Army<1 || seat.Army>state.MaxPlayers){Chat("Preset '"+preset.Name+"' needs army "+seat.Army+", but this map supports "+state.MaxPlayers+" armies.");return;}
   seat.ResolvedFaction=ResolveSeatFaction(typeof(Faction),seat,false);ValidateAiChoice(seat);
   if(seat.AiMod!=null)throw new InvalidOperationException("Only native AI selections are supported by this game version.");
   var occupant=AtArmy(seat.Army);
   if(occupant.IsHumanPlayer && !LobbyManager.IsLocalPlayer(occupant)){Chat("Army "+seat.Army+" belongs to another human; preset was not applied.");return;}
   if(!seat.Host && seat.Army==hostArmy){Chat("An AI entry conflicts with the host army.");return;}
  }
  if(!SanctuaryLobbyState.SetModeFromPreset(preset.Mode)){Chat("Preset mode could not be selected; no seats changed.");return;}
  // Validate the complete plan before sending any request. Native AI requests
  // address one-based army IDs; legacy Slot remains saved metadata only.
  removals.Clear();additions.Clear();updates.Clear();
  foreach(var p in state.GetPlayers(true,true))if(p.type==PlayerType.AI &&
   (preset.ClearOtherAI || p.armyID==hostArmy))removals.Enqueue(p.armyID);
  foreach(var seat in preset.Seats.Where(s=>!s.Host))additions.Enqueue((byte)seat.Army);
  nativeHost=host;pending=preset;phase=-1;waitingAction=null;
  deadline=DateTime.UtcNow.AddSeconds(30);SetSelectorInteractable(false);
  Chat("Applying preset '"+preset.Name+"' by native army ID...");
 }
 static void ContinueApply() {
  if(!InLobby() || !IsHost()){Fail("Preset cancelled because the host lobby is no longer editable.");return;}
  if(DateTime.UtcNow>deadline){Fail("Timed out waiting for native lobby acknowledgement at "+waitingAction+" army "+waitingArmy+".");return;}
  if(phase==-1){if(!SanctuaryLobbyState.PresetModeReady(pending.Mode))return;phase=0;}
  if(!ApiAllowsEditing()){Fail("Preset cancelled because the host lobby is no longer editable.");return;}
  if(LocalHost()==null){Fail("Local host is no longer in the lobby.");return;}
  if(waitingAction!=null){
   var p=AtArmy(waitingArmy);
   if(waitingAction=="remove" && p.type==PlayerType.AI)return;
   if(waitingAction=="add" && p.type!=PlayerType.AI)return;
   if(waitingAction=="host" && LocalHost().armyID!=waitingArmy)return;
   waitingAction=null;
  }
  if(phase==0){
   if(removals.Count>0){waitingArmy=removals.Dequeue();waitingAction="remove";Send("SendRemoveAI",waitingArmy);return;}
   phase=1;
  }
  if(phase==1){
   if(nativeHost!=null && LocalHost().armyID!=nativeHost.Army){
    var target=AtArmy(nativeHost.Army);if(target!=null&&target.IsHumanPlayer&&!LobbyManager.IsLocalPlayer(target)){Fail("Another human now occupies the host destination; preset stopped.");return;}
    waitingArmy=(byte)nativeHost.Army;waitingAction="host";
    Send("SendSetReady",false);Send("SendSetArmyID",waitingArmy);return;
   }
   phase=2;
  }
  if(phase==2){
   if(additions.Count>0){
    waitingArmy=additions.Dequeue();var p=AtArmy(waitingArmy);
    if(p.type==PlayerType.AI)return;
    if(p.IsHumanPlayer){Fail("Another human occupies army "+waitingArmy+"; preset stopped.");return;}
    waitingAction="add";Send("SendAddAI",waitingArmy);return;
   }
   foreach(var seat in pending.Seats)updates.Enqueue(seat);phase=3;
  }
  if(phase==3){
   if(updates.Count>0){
    var seat=updates.Dequeue();var p=seat.Host?LocalHost():AtArmy(seat.Army);
    var faction=(Faction)seat.ResolvedFaction;
    if(seat.Host){Send("SendSetReady",false);Send("SendSetTeam",seat.Team);}
    else if(p.type==PlayerType.AI)Send("SendUpdateAI",(byte)seat.Army,seat.Team,faction);
    else {Fail("AI disappeared from army "+seat.Army+"; preset stopped.");return;}
    if(!SanctuaryLobbyState.SetPresetChoice(seat.Army,Convert.ToInt32(faction),IsRandomChoice(seat.Faction),IsRandomChoice(seat.Commander))){Fail("Preset choices could not be synchronized for army "+seat.Army+".");return;}
    return;
   }
   phase=4;
  }
  if(phase==4){
   foreach(var seat in pending.Seats){
    var p=seat.Host?LocalHost():AtArmy(seat.Army);
    if(p.armyID!=seat.Army || p.team!=seat.Team || (int)p.faction!=Convert.ToInt32(seat.ResolvedFaction))return;
   }
   ApplyAiChoices(pending);string name=pending.Name;pending=null;SetSelectorInteractable(true);ResetSelector();Chat("Preset '"+name+"' applied.");
  }
 }
}
