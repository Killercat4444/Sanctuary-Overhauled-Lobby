using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EM.Core;
using EM.Network;
using EM.Network.Lobby;

// The controller is deliberately independent of Unity and transport. The native
// adapter below supplies authoritative identity and the received client roster.
internal sealed class BulkAiSnapshot {
 internal bool Editable;
 internal object Session;
 internal ulong SessionId, Host, Local;
 internal string Map, Humans;
 internal int Capacity, Room;
 internal int[] Types, ClientTypes;
 internal object[] Actors;
}

internal sealed class BulkAiBatch {
 internal bool IsRunning {get;private set;}
 internal string Status {get;private set;}
 internal bool IsAdding(int army){return IsRunning&&filling&&waiting==army&&army>0;}
 readonly Queue<int> plan=new Queue<int>();
 BulkAiSnapshot baseline;
 bool filling;
 bool acknowledging;
 object waitingActor;
 int waiting, completed, total;
 DateTime end, acknowledgementEnd;
 internal bool CanStart(BulkAiSnapshot s,bool fill){return !IsRunning&&Valid(s)&&s.Types.SequenceEqual(s.ClientTypes)&&Targets(s,fill).Any();}
 static bool Valid(BulkAiSnapshot s){return s!=null&&s.Editable&&s.Session!=null&&s.Host!=0&&s.Local==s.Host&&s.Capacity>0&&s.Capacity<=255&&s.Types!=null&&s.ClientTypes!=null&&s.Actors!=null&&s.Types.Length==s.Capacity+1&&s.ClientTypes.Length==s.Types.Length&&s.Actors.Length==s.Types.Length;}
 static IEnumerable<int> Targets(BulkAiSnapshot s,bool fill){
  int room=Math.Max(0,s.Room);
  for(int i=1;i<=s.Capacity;i++)if(s.Types[i]==(fill?0:3)&&s.ClientTypes[i]==s.Types[i]&&(!fill||room-- >0))yield return i;
 }
 internal bool Start(BulkAiSnapshot s,bool fill,DateTime now){
  if(IsRunning)return false;
  if(!Valid(s)){Status="AI changes require an editable host lobby.";return false;}
  // Start only from a converged roster; never treat a stale client as an ack.
  if(!s.Types.SequenceEqual(s.ClientTypes)){Status="Wait for the lobby roster to synchronize.";return false;}
  plan.Clear();foreach(int slot in Targets(s,fill))plan.Enqueue(slot);
  if(plan.Count==0){Status=fill?"No open playable slots are available.":"There are no AI players to remove.";return false;}
  baseline=s;filling=fill;waiting=completed=0;waitingActor=null;total=plan.Count;
  end=now.AddSeconds(60);IsRunning=true;Status=fill?"Filling open slots with AI...":"Removing AI players...";return true;
 }
 internal void Reset(){IsRunning=false;waiting=0;waitingActor=null;baseline=null;plan.Clear();Status=null;}
 internal void Stop(string reason){IsRunning=false;waiting=0;waitingActor=null;baseline=null;plan.Clear();Status=reason;}
 bool SameContext(BulkAiSnapshot s){return Valid(s)&&ReferenceEquals(s.Session,baseline.Session)&&s.SessionId==baseline.SessionId&&s.Host==baseline.Host&&s.Local==baseline.Local&&s.Capacity==baseline.Capacity&&s.Map==baseline.Map&&s.Humans==baseline.Humans;}
 internal void Tick(BulkAiSnapshot s,DateTime now,Action<bool,byte> send,Func<int,object,bool> added=null){
  if(!IsRunning||acknowledging)return;
  if(!SameContext(s)){Stop("AI batch stopped: lobby authority, roster, map, or editability changed.");return;}
  if(now>=end){Stop("AI batch timed out; no requests were retried.");return;}
  for(int i=1;i<=s.Capacity;i++)if(i!=waiting&&(s.Types[i]!=baseline.Types[i]||!ReferenceEquals(s.Actors[i],baseline.Actors[i]))){Stop("AI batch stopped: a slot occupant changed.");return;}
  if(waiting!=0){
   int t=s.Types[waiting];
   if(filling){if(t!=0&&t!=3){Stop("AI batch stopped: the requested slot is no longer open.");return;}}
   else if(t!=0&&(t!=3||!ReferenceEquals(s.Actors[waiting],baseline.Actors[waiting]))){Stop("AI batch stopped: the AI occupant changed.");return;}
   if(filling&&waitingActor!=null&&(t!=3||!ReferenceEquals(waitingActor,s.Actors[waiting]))){Stop("AI batch stopped: the newly added AI occupant changed.");return;}
   if(filling&&t==3&&waitingActor==null)waitingActor=s.Actors[waiting];
   int expected=filling?3:0;
   if(t!=expected||s.ClientTypes[waiting]!=expected){if(now>=acknowledgementEnd)Stop("AI batch timed out waiting for the native roster; no requests were retried.");return;}
   // Only the newly acknowledged authoritative actor receives Random intent.
   // A failed intent write must not advance or issue another native add.
   if(filling&&added!=null){acknowledging=true;try{if(s.Actors[waiting]==null||!added(waiting,s.Actors[waiting])){Stop("AI batch stopped: Random choices could not be synchronized.");return;}}catch{Stop("AI batch stopped: Random choices could not be synchronized.");return;}finally{acknowledging=false;}if(!IsRunning)return;}
   completed++;baseline=s;waiting=0;
  }
  if(plan.Count==0){Stop((filling?"Added ":"Removed ")+completed+" AI player"+(completed==1?".":"s."));return;}
  int next=plan.Peek();
  if(s.Types[next]!=(filling?0:3)||s.ClientTypes[next]!=s.Types[next]||(filling&&s.Room<1)){Stop("AI batch stopped: the next slot is unavailable.");return;}
  plan.Dequeue();waiting=next;waitingActor=null;acknowledgementEnd=now.AddSeconds(5);
  Status=(filling?"Adding AI ":"Removing AI ")+(completed+1)+" of "+total+"...";
  // Set waiting before invoking transport: reentrant UI events cannot resend.
  try{send(filling,(byte)next);}catch{Stop("AI batch stopped: the native request failed; it was not retried.");}
 }
}

internal static class LobbyBulkAi {
 const BindingFlags Flags=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 static readonly FieldInfo HostState=typeof(LobbyManager).GetField("hostState",Flags),Locked=typeof(LobbyManager).GetField("isLocked",Flags),SessionId=typeof(LobbyManager).GetField("currentSessionID",Flags),LocalId=typeof(LobbyManager).GetField("localPlayerID",Flags);
 static readonly MethodInfo Add=typeof(LobbyManager).GetMethod("SendAddAI",Flags,null,new[]{typeof(byte)},null),Remove=typeof(LobbyManager).GetMethod("SendRemoveAI",Flags,null,new[]{typeof(byte)},null);
 static readonly BulkAiBatch batch=new BulkAiBatch();
 internal static bool IsRunning {get{return batch.IsRunning;}}
 internal static bool IsPendingRandom(int army){return batch.IsAdding(army);}
 internal static string Status {get{return batch.Status;}}
 internal static bool CanFill {get{return batch.CanStart(Capture(),true);}}
 internal static bool CanRemove {get{return batch.CanStart(Capture(),false);}}
 internal static bool StartFill(){return batch.Start(Capture(),true,DateTime.UtcNow);}
 internal static bool StartRemove(){return batch.Start(Capture(),false,DateTime.UtcNow);}
 internal static void Reset(){batch.Reset();}
 internal static void Tick(){if(batch.IsRunning)batch.Tick(Capture(),DateTime.UtcNow,Send,SanctuaryLobbyState.SetAddedAiRandom);}
 static void Send(bool add,byte slot){
  // Always use the game's synchronized request path; CreateDefault is owned by
  // native HandleOnHost(AddAI). Never construct or locally insert an AI.
  (add?Add:Remove).Invoke(null,new object[]{slot});
 }
 static BulkAiSnapshot Capture(){
  try{
   if(HostState==null||Locked==null||SessionId==null||LocalId==null||Add==null||Remove==null||!SanctuaryLobbyState.InLobby||!SanctuaryLobbyState.IsHost||!SanctuaryLobbyState.CanEdit||LobbyPresetRuntime.IsApplying||(bool)Locked.GetValue(null))return null;
   var state=HostState.GetValue(null) as LobbyState;var client=LobbyManager.CurrentState;
   if(state==null||client==null||state.MaxPlayers!=client.MaxPlayers||state.hostID!=client.hostID||state.mapPath!=client.mapPath)return null;
   var local=(PlayerID)LocalId.GetValue(null);var host=state.GetPlayer(local);
   if(local!=state.hostID||host==null||!host.IsHumanPlayer)return null;
   int n=state.MaxPlayers;if(n<1||n>255)return null;
   var players=state.GetPlayers(true,true).ToArray();
   if(players.Where(p=>p.type!=PlayerType.Empty&&p.armyID>0&&p.type!=PlayerType.Observer).GroupBy(p=>p.armyID).Any(g=>g.Count()>1))return null;
   var s=new BulkAiSnapshot{Editable=true,Session=state,SessionId=((PlayerID)SessionId.GetValue(null)).value,Host=state.hostID.value,Local=local.value,Map=state.mapPath,Capacity=n,Room=state.UnassignedCapacity-players.Length,Types=new int[n+1],ClientTypes=new int[n+1],Actors=new object[n+1]};
   s.Humans=string.Join(";",players.Where(p=>p.IsHumanPlayer).OrderBy(p=>p.id.value).Select(p=>p.id.value+":"+(int)p.type+":"+p.armyID).ToArray());
   // This build has no per-slot Closed flag/type. Only native Empty slots in
   // 1..MaxPlayers are playable; unknown/null slots are always unavailable.
   for(int i=1;i<=n;i++){var p=state.GetPlayerAtSlot(i);var c=client.GetPlayerAtSlot(i);s.Types[i]=p==null?-1:(int)p.type;s.ClientTypes[i]=c==null?-1:(int)c.type;s.Actors[i]=p==null||p.type==PlayerType.Empty?null:p;}
   return s;
  }catch{return null;}
 }
}
