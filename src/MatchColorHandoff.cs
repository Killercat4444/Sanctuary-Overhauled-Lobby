using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EM.Core;
using EM.Network;
using EM.Network.Lobby;

internal static class MatchColorRules {
 // Validate the final native roster against the already-authorized protocol
 // state, then deep-copy it. Never invent a default color for a missing owner.
 internal static bool TryCapture(LobbyState roster,IDictionary<ulong,OverhaulEntry> humans,IDictionary<int,int> seats,out Dictionary<ulong,OverhaulEntry> captured,out string error){
  captured=null;error=null;
  if(roster==null||humans==null||seats==null){error="The authoritative color state is unavailable.";return false;}
  var result=new Dictionary<ulong,OverhaulEntry>();var armies=new HashSet<int>();var colors=new HashSet<int>();
  foreach(var p in roster.GetPlayers(true,true)){
   if(p==null||(p.type!=PlayerType.Player&&p.type!=PlayerType.AI)||p.armyID==0)continue;
   if(p.armyID>roster.MaxPlayers||!armies.Add(p.armyID)){error="The final roster has invalid or duplicate army assignments.";return false;}
   OverhaulEntry entry;int color;ulong key;
   if(p.type==PlayerType.Player){
    key=SanctuaryLobbyState.Id(p.id);
    if(!humans.TryGetValue(key,out entry)||entry==null||entry.player!=key.ToString(CultureInfo.InvariantCulture)||entry.army!=p.armyID){error=p.name+" has not finished synchronizing their match color.";return false;}
    color=entry.color;
   }else{
    key=ulong.MaxValue-(ulong)p.armyID;
    if(!seats.TryGetValue(p.armyID,out color)){error="AI in slot "+p.armyID+" has no synchronized match color.";return false;}
    entry=new OverhaulEntry{commander="Random"};
   }
   if(color<0||color>=SanctuaryLobbyState.Palette.Length||!colors.Add(color)||result.ContainsKey(key)){error="The final match color assignments are invalid or duplicated.";return false;}
   result.Add(key,new OverhaulEntry{player=key.ToString(CultureInfo.InvariantCulture),name=p.name,army=p.armyID,color=color,commander=entry.commander});
  }
  if(result.Count==0||result.Count>SanctuaryLobbyState.Palette.Length){error="The final roster has no supported occupied color assignments.";return false;}
  captured=result;return true;
 }
}

public static partial class SanctuaryLobbyState {
 static bool matchColorsCaptured;
 static PlayerID matchColorSession,matchColorHost;
 static void InitializeMatchColorHandoff(){
  // The event argument is authoritative: native code invokes this event before
  // updating lobbyGameStatus and before EngineLoader.CreateClient().
  LobbyManager.OnLobbyStatusChanged+=HandleMatchColorPhase;
  LobbyManager.OnLobbyEntered+=delegate(LobbyState unused){ResetSession();};
  LobbyManager.OnLobbyClosed+=ResetSession;LobbyManager.OnConnectionLost+=ResetSession;LobbyManager.OnKicked+=ResetSession;
 }
 internal static void ClearMatchColors(){matchColorsCaptured=false;matchColorSession=matchColorHost=default(PlayerID);matchEntries.Clear();InMatchColorBridge.Reset();}
 static void HandleMatchColorPhase(LobbyManager.LobbyGameStatus phase){
  try{
   var roster=AuthoritativeState;
   string error;
   if(!AdvanceMatchColors((int)phase,LobbyManager.currentSessionID,roster,PrivateContracts&&InLobby&&(IsHost||SafeTypeChanges),out error)&&!string.IsNullOrEmpty(error))Warn("Match color handoff deferred: "+error);
  }catch(Exception ex){Warn("Match color handoff deferred: "+Root(ex));}
 }
 // Shared by event, Tick fallback and offline lifecycle tests. A successfully
 // captured match is immutable, even if later lobby packets or moves arrive.
 internal static bool AdvanceMatchColors(int phase,PlayerID session,LobbyState roster,bool authorized,out string error){
  error=null;
  if(phase==0||Mode!=SanctuaryLobbyMode.Overhauled){ClearMatchColors();return false;}
  if(phase!=1&&phase!=2){ClearMatchColors();error="Unsupported native match phase.";return false;}
  if(!authorized||session==PlayerID.None||roster==null){ClearMatchColors();error="No verified host/session color state is available.";return false;}
  if(matchColorsCaptured&&session==matchColorSession&&roster.hostID==matchColorHost)return true;
  ClearMatchColors();Dictionary<ulong,OverhaulEntry> captured;
  if(!MatchColorRules.TryCapture(roster,entries,seatColors,out captured,out error))return false;
  foreach(var pair in captured)matchEntries.Add(pair.Key,pair.Value);
  matchColorSession=session;matchColorHost=roster.hostID;matchColorsCaptured=true;
  if(SanctuaryOverhauledLobbyPlugin.Log!=null)SanctuaryOverhauledLobbyPlugin.Log.LogInfo("Captured synchronized match colors at native phase "+phase+", revision "+Revision+": "+string.Join(", ",captured.Values.OrderBy(x=>x.army).Select(x=>"army "+x.army+"="+ColorNames[x.color]).ToArray()));
  return true;
 }
}
