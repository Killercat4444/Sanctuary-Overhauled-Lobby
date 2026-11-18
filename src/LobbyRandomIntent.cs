using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using EM.Core;
using EM.Network;
using EM.Network.Lobby;

// Intent only. Never put the match's randomly resolved outcome into this wire type.
public sealed class OverhaulChoice {
 public string owner,player,faction,commander;public int type,army,value;
 internal OverhaulChoice Copy(){return new OverhaulChoice{owner=owner,player=player,faction=faction,commander=commander,type=type,army=army,value=value};}
}

internal static class RandomChoiceRules {
 internal const string Random="Random";
 internal static bool IsRandom(OverhaulChoice c){return c!=null&&(c.faction==Random||c.commander==Random);}
 internal static bool ValidIntent(string faction,string commander,SanctuaryLobbyState.FactionChoice[] catalog,out int placeholder,out string error){
  placeholder=0;error=null;
  if(string.IsNullOrEmpty(faction)||string.IsNullOrEmpty(commander)||faction.Length>256||commander.Length>256||catalog==null||catalog.Length==0||catalog.Any(c=>c==null||string.IsNullOrEmpty(c.FactionId)||string.IsNullOrEmpty(c.Identity)||c.Value<0||c.Value>255)||catalog.GroupBy(c=>c.Identity,StringComparer.Ordinal).Any(g=>g.Count()!=1)||catalog.GroupBy(c=>c.Value).Any(g=>g.Count()!=1)){error="The faction/commander catalog is unavailable or ambiguous.";return false;}
  if(faction==Random){if(commander!=Random){error="Choose a faction before choosing an explicit commander.";return false;}placeholder=catalog.OrderBy(c=>c.Value).First().Value;return true;}
  var factionChoices=catalog.Where(c=>c.FactionId==faction).OrderBy(c=>c.Identity,StringComparer.Ordinal).ToArray();
  if(factionChoices.Length==0){error="The selected faction is not available in the active mod set.";return false;}
  var choice=commander==Random?factionChoices[0]:factionChoices.SingleOrDefault(c=>c.Identity==commander);
  if(choice==null){error="The commander does not belong to the selected faction.";return false;}
  placeholder=choice.Value;return true;
 }
 internal static bool ValidWire(OverhaulChoice[] choices){
  if(choices==null||choices.Length>64)return false;var owners=new HashSet<string>(StringComparer.Ordinal);var humans=new HashSet<string>(StringComparer.Ordinal);var armies=new HashSet<int>();
  foreach(var c in choices){ulong id;
   if(c==null||string.IsNullOrEmpty(c.owner)||c.owner.Length>64||!owners.Add(c.owner)||c.type!=1&&c.type!=3||c.army<0||c.army>64||c.value<0||c.value>255||string.IsNullOrEmpty(c.faction)||c.faction.Length>256||string.IsNullOrEmpty(c.commander)||c.commander.Length>256||c.faction==Random&&c.commander!=Random)return false;
   if(c.type==1&&(string.IsNullOrEmpty(c.player)||c.player.Length>20||!ulong.TryParse(c.player,NumberStyles.None,CultureInfo.InvariantCulture,out id)||id==0||c.player!=id.ToString(CultureInfo.InvariantCulture)||!humans.Add(c.player)||c.owner!="H:"+c.player))return false;
   if(c.type==3){long counter;if(c.army==0||c.player!=""||!c.owner.StartsWith("A:",StringComparison.Ordinal)||!long.TryParse(c.owner.Substring(2),NumberStyles.None,CultureInfo.InvariantCulture,out counter)||counter<1||c.owner!="A:"+counter.ToString(CultureInfo.InvariantCulture))return false;}
   if(c.army>0&&!armies.Add(c.army))return false;
  }return true;
 }
 internal static bool ValidPlaceholder(string faction,string commander,int value,SanctuaryLobbyState.FactionChoice[] catalog){
  int ignored;string error;if(!ValidIntent(faction,commander,catalog,out ignored,out error))return false;
  return catalog.Any(c=>c.Value==value&&(faction==Random||c.FactionId==faction)&&(commander==Random||c.Identity==commander));
 }
 internal static bool ValidChoiceRequest(OverhaulRequest q,ulong sender,int revision,bool capabilityAcknowledged){
  if(!capabilityAcknowledged||q==null||sender==0||q.player!=sender.ToString(CultureInfo.InvariantCulture)||q.revision!=revision||q.color!=-1)return false;
  if(q.kind==2)return q.faction==null&&q.commander==null;
  return q.kind==1&&!string.IsNullOrEmpty(q.faction)&&q.faction.Length<=256&&!string.IsNullOrEmpty(q.commander)&&q.commander.Length<=256;
 }
 internal static bool Same(OverhaulChoice a,OverhaulChoice b){return a!=null&&b!=null&&a.owner==b.owner&&a.player==b.player&&a.type==b.type&&a.army==b.army&&a.value==b.value&&a.faction==b.faction&&a.commander==b.commander;}
 internal static bool TryResolve(OverhaulChoice[] intents,SanctuaryLobbyState.FactionChoice[] catalog,Func<int,int> draw,out Dictionary<string,int> result,out string error){
  result=null;error=null;if(!ValidWire(intents)||catalog==null||draw==null){error="Random choice state is invalid.";return false;}
  var pending=new Dictionary<string,int>(StringComparer.Ordinal);
  foreach(var intent in intents.OrderBy(c=>c.owner,StringComparer.Ordinal)){
   int placeholder;if(!ValidIntent(intent.faction,intent.commander,catalog,out placeholder,out error))return false;
   string faction=intent.faction;
   if(faction==Random){var pool=catalog.Select(c=>c.FactionId).Distinct(StringComparer.Ordinal).OrderBy(c=>c,StringComparer.Ordinal).ToArray();int n=draw(pool.Length);if(n<0||n>=pool.Length){error="Random faction source failed.";return false;}faction=pool[n];}
   var commanders=catalog.Where(c=>c.FactionId==faction).OrderBy(c=>c.Identity,StringComparer.Ordinal).ToArray();
   var selected=commanders.FirstOrDefault(c=>c.Identity==intent.commander);
   if(intent.commander==Random){int n=draw(commanders.Length);if(n<0||n>=commanders.Length){error="Random commander source failed.";return false;}selected=commanders[n];}
   if(selected==null||selected.Value<0||selected.Value>255){error="No valid commander can be launched for the selected faction.";return false;}
   pending.Add(intent.owner,selected.Value);
  }
  result=pending;return true;
 }
 internal static string Fingerprint(IEnumerable<OverhaulChoice> choices){return string.Join("|",choices.OrderBy(c=>c.owner,StringComparer.Ordinal).Select(c=>c.owner+":"+c.type+":"+c.army+":"+c.value+":"+c.faction+":"+c.commander).ToArray());}
}

internal sealed class RandomLaunchCache {
 string fingerprint;Dictionary<string,int> values;
 internal void Clear(){fingerprint=null;values=null;}
 internal bool Resolve(string key,OverhaulChoice[] intents,SanctuaryLobbyState.FactionChoice[] catalog,Func<int,int> draw,out string error){
  error=null;if(key==fingerprint&&values!=null)return true;
  Dictionary<string,int> candidate;if(!RandomChoiceRules.TryResolve(intents,catalog,draw,out candidate,out error))return false;
  fingerprint=key;values=candidate;return true;
 }
 internal bool TryGet(string key,out int value){value=0;return values!=null&&values.TryGetValue(key,out value);}
}

public static partial class SanctuaryLobbyState {
 static readonly Dictionary<string,OverhaulChoice> lobbyChoices=new Dictionary<string,OverhaulChoice>(StringComparer.Ordinal);
 static readonly Dictionary<object,string> randomAiKeys=new Dictionary<object,string>(ReferenceComparer.Instance);
 static readonly Dictionary<ulong,int> choiceAcknowledgements=new Dictionary<ulong,int>();
 static readonly RandomLaunchCache randomLaunch=new RandomLaunchCache();
 static long nextRandomAi;internal static int ChoiceRevision;
 static OverhaulChoice pendingChoice;static bool choiceHostConfirmed;static PlayerID choiceSession,choiceHost;
 static DateTime nextChoiceSend,pendingChoiceDeadline;static string launchKey;
 static string connectionNonce=Guid.NewGuid().ToString("N"),remoteHostNonce,snapshotBlockReason;
 internal static string PresentationKey {get{return ChoiceRevision+"/"+(pendingChoice==null?"":pendingChoice.faction+"/"+pendingChoice.commander);}}
 static string HumanChoiceKey(ulong id){return "H:"+id.ToString(CultureInfo.InvariantCulture);}
 static string AuthoritativeChoiceKey(LobbyPlayer p){if(p.type!=EM.Core.PlayerType.AI)return HumanChoiceKey(Id(p));string key;if(!randomAiKeys.TryGetValue(p,out key)){key="A:"+(++nextRandomAi).ToString(CultureInfo.InvariantCulture);randomAiKeys[p]=key;}return key;}
 static OverhaulChoice ExplicitChoice(LobbyPlayer p,string key){
  var choice=Choices().FirstOrDefault(c=>c.Value==(int)p.faction);if(choice==null)return null;
  return new OverhaulChoice{owner=key,player=p.type==EM.Core.PlayerType.AI?"":Id(p).ToString(CultureInfo.InvariantCulture),type=(int)p.type,army=p.armyID,value=(int)p.faction,faction=choice.FactionId,commander=choice.Identity};
 }
 static void RefreshLobbyChoices(){
  if(!IsHost||!PrivateContracts||!CanEdit||(int)LobbyManager.lobbyGameStatus!=0)return;var roster=AuthoritativeState;if(roster==null)return;
  var live=new HashSet<string>(StringComparer.Ordinal);var objects=new HashSet<object>(ReferenceComparer.Instance);bool changed=false;
  foreach(var p in roster.GetPlayers(true,true).Where(p=>p.type==EM.Core.PlayerType.Player||p.type==EM.Core.PlayerType.AI)){
   objects.Add(p);string key=AuthoritativeChoiceKey(p);live.Add(key);OverhaulChoice old;var explicitChoice=ExplicitChoice(p,key);
   if(!lobbyChoices.TryGetValue(key,out old)){
    if(explicitChoice==null)continue;
    if(p.type==EM.Core.PlayerType.AI&&LobbyBulkAi.IsPendingRandom(p.armyID)){explicitChoice.faction=explicitChoice.commander=RandomChoiceRules.Random;}
    lobbyChoices[key]=explicitChoice;changed=true;
   }else if(old.value!=(int)p.faction&&explicitChoice!=null){lobbyChoices[key]=explicitChoice;changed=true;}
   else if(old.army!=p.armyID){old.army=p.armyID;changed=true;}
  }
  foreach(var key in lobbyChoices.Keys.Where(k=>!live.Contains(k)).ToArray()){lobbyChoices.Remove(key);changed=true;}
  foreach(var p in randomAiKeys.Keys.Where(p=>!objects.Contains(p)).ToArray())randomAiKeys.Remove(p);
  foreach(var id in choiceAcknowledgements.Keys.Where(id=>!live.Contains(HumanChoiceKey(id))).ToArray())choiceAcknowledgements.Remove(id);
  if(changed){ChoiceRevision++;launchKey=null;}
 }
 static OverhaulChoice StoredChoice(LobbyPlayer p){
  if(p==null)return null;var values=lobbyChoices.Values;
  return p.type==EM.Core.PlayerType.AI?values.FirstOrDefault(c=>c.type==3&&c.army==p.armyID):values.FirstOrDefault(c=>c.type==1&&c.player==Id(p).ToString(CultureInfo.InvariantCulture));
 }
 internal static OverhaulChoice PresentationChoice(object player){
  var p=player as LobbyPlayer;if(p==null)return null;
  if(pendingChoice!=null&&p.type==EM.Core.PlayerType.Player&&pendingChoice.player==Id(p).ToString(CultureInfo.InvariantCulture))return pendingChoice.Copy();
  if(IsHost&&p.type==EM.Core.PlayerType.AI&&LobbyBulkAi.IsPendingRandom(p.armyID))return new OverhaulChoice{faction="Random",commander="Random"};
  var saved=StoredChoice(p);if(saved!=null)return saved.Copy();
  if(choiceHostConfirmed&&!IsHost)return null; // Unknown new row: conceal until the complete host snapshot arrives.
  return ExplicitChoice(p,p.type==EM.Core.PlayerType.AI?"A:untracked":HumanChoiceKey(Id(p)));
 }
 internal static bool TryGetLobbyChoice(object player,out string faction,out string commander){var choice=PresentationChoice(player);faction=choice==null?null:choice.faction;commander=choice==null?null:choice.commander;return choice!=null;}
 static bool HasRandomIntent(object player){var p=player as LobbyPlayer;return p!=null&&RandomChoiceRules.IsRandom(StoredChoice(p));}
 internal static bool SetLobbyChoice(int army,string faction,string commander){
  if(!CanEdit||LobbyManager.isLocked||LobbyBulkAi.IsRunning||LobbyPresetRuntime.IsApplying)return false;
  var player=EditableRowPlayer(army);if(player==null||player.type!=EM.Core.PlayerType.Player&&player.type!=EM.Core.PlayerType.AI)return false;
  int placeholder;string error;if(!RandomChoiceRules.ValidIntent(faction,commander,Choices(),out placeholder,out error)){Chat(error);return false;}
  if(IsHost){if(!LobbyManager.IsLocalPlayer(player)&&player.type!=EM.Core.PlayerType.AI)return false;return CommitLobbyChoice(player,faction,commander,placeholder);}
  if(!LobbyManager.IsLocalPlayer(player))return false;
  if(!choiceHostConfirmed||!hostCapabilityAcknowledged||!SafeTypeChanges){
   if(faction=="Random"||commander=="Random"){Chat("Concealed Random requires the host to run the same Lobby version. Choose explicit faction/commander choices for this native lobby.");return false;}
   SendLobby("SendSetReady",false);SendLobby("SendSetFaction",(Faction)placeholder);return true;
  }
  SendLobby("SendSetReady",false);
  pendingChoice=new OverhaulChoice{owner=HumanChoiceKey(Id(player)),player=Id(player).ToString(CultureInfo.InvariantCulture),type=1,army=player.armyID,value=placeholder,faction=faction,commander=commander};pendingChoiceDeadline=DateTime.UtcNow.AddSeconds(12);nextChoiceSend=DateTime.MinValue;SendChoiceRequest();return true;
 }
 static bool CommitLobbyChoice(LobbyPlayer player,string faction,string commander,int placeholder){
  if(!IsHost||!PrivateContracts||!CanEdit||LobbyManager.isLocked||player==null)return false;
  if(!RandomChoiceRules.ValidPlaceholder(faction,commander,placeholder,Choices())){Chat("The choice or its placeholder changed; try again.");return false;}
  RefreshLobbyChoices();string key=AuthoritativeChoiceKey(player);var choice=new OverhaulChoice{owner=key,player=player.type==EM.Core.PlayerType.AI?"":Id(player).ToString(CultureInfo.InvariantCulture),type=(int)player.type,army=player.armyID,value=placeholder,faction=faction,commander=commander};
  OverhaulChoice old;if(lobbyChoices.TryGetValue(key,out old)&&RandomChoiceRules.Same(old,choice))return true;
  lobbyChoices[key]=choice;player.faction=(Faction)placeholder;
  foreach(var p in AuthoritativeState.GetPlayers(true,true))p.isReady=false;
  OverhaulEntry color;if(entries.TryGetValue(Id(player),out color)&&player.type==EM.Core.PlayerType.Player)color.commander=Choices().First(c=>c.Value==placeholder).Identity;
  ChoiceRevision++;Revision++;launchKey=null;BroadcastSnapshot();BroadcastState();return true;
 }
 internal static bool SetAddedAiRandom(int army,object actor){
  var p=actor as LobbyPlayer;var state=AuthoritativeState;if(!IsHost||!PrivateContracts||!CanEdit||LobbyManager.isLocked||state==null||p==null||p.type!=EM.Core.PlayerType.AI||!ReferenceEquals(state.GetPlayerAtSlot(army),p))return false;
  int placeholder;string error;if(!RandomChoiceRules.ValidIntent("Random","Random",Choices(),out placeholder,out error))return false;
  return CommitLobbyChoice(p,"Random","Random",placeholder);
 }
 internal static bool SetPresetChoice(int army,int placeholderValue,bool randomFaction,bool randomCommander){
  if(!IsHost||!CanEdit||!PrivateContracts||LobbyManager.isLocked)return false;var p=EditableRowPlayer(army);if(p==null||!LobbyManager.IsLocalPlayer(p)&&p.type!=EM.Core.PlayerType.AI)return false;
  var choice=Choices().FirstOrDefault(c=>c.Value==placeholderValue);if(choice==null||randomFaction&&!randomCommander)return false;
  string faction=randomFaction?"Random":choice.FactionId,commander=randomCommander?"Random":choice.Identity;
  // This is a validated neutral placeholder, not a random outcome. Preserve
  // the preset's frozen byte so its native acknowledgement can converge.
  return CommitLobbyChoice(p,faction,commander,placeholderValue);
 }
 static OverhaulChoice[] ChoiceSnapshot(){RefreshLobbyChoices();return lobbyChoices.Values.OrderBy(c=>c.owner,StringComparer.Ordinal).Select(c=>c.Copy()).ToArray();}
 static bool AcceptChoiceSnapshot(OverhaulSnapshot snapshot){
  if(snapshot.choiceRevision<ChoiceRevision||!RandomChoiceRules.ValidWire(snapshot.choices))return false;
  var roster=AuthoritativeState;if(roster==null)return false;
  var players=roster.GetPlayers(true,true).Where(p=>p.type==EM.Core.PlayerType.Player||p.type==EM.Core.PlayerType.AI).ToArray();var catalog=Choices();
  if(players.Length!=snapshot.choices.Length||snapshot.choices.Any(c=>!RandomChoiceRules.ValidPlaceholder(c.faction,c.commander,c.value,catalog))||players.Any(p=>!snapshot.choices.Any(c=>c.type==(int)p.type&&c.army==p.armyID&&c.value==(int)p.faction&&(p.type==EM.Core.PlayerType.AI||c.player==Id(p).ToString(CultureInfo.InvariantCulture)))))return false;
  if(snapshot.choiceRevision==ChoiceRevision&&choiceHostConfirmed&&RandomChoiceRules.Fingerprint(lobbyChoices.Values)!=RandomChoiceRules.Fingerprint(snapshot.choices))return false;
  lobbyChoices.Clear();foreach(var c in snapshot.choices)lobbyChoices.Add(c.owner,c.Copy());ChoiceRevision=snapshot.choiceRevision;
  choiceHostConfirmed=true;choiceHost=roster.hostID;choiceSession=LobbyManager.currentSessionID;
  if(pendingChoice!=null){OverhaulChoice echoed;if(lobbyChoices.TryGetValue(pendingChoice.owner,out echoed)&&echoed.faction==pendingChoice.faction&&echoed.commander==pendingChoice.commander)pendingChoice=null;}
  return true;
 }
 static void SendChoiceAcknowledgement(){
  if(IsHost||pendingChoice!=null||!choiceHostConfirmed||!hostCapabilityAcknowledged||!CanEdit||(int)LobbyManager.lobbyGameStatus!=0)return;var p=LocalPlayer();if(p==null)return;
  var catalog=Choices();if(lobbyChoices.Values.Any(c=>!RandomChoiceRules.ValidPlaceholder(c.faction,c.commander,c.value,catalog)))return;
  protocolSend.Invoke(null,new object[]{RequestType,new OverhaulRequest{nonce=connectionNonce,kind=2,player=Id(p).ToString(CultureInfo.InvariantCulture),revision=ChoiceRevision}});
 }
 static void SendChoiceRequest(){
  if(pendingChoice==null||IsHost||!choiceHostConfirmed||!hostCapabilityAcknowledged||DateTime.UtcNow<nextChoiceSend)return;
  nextChoiceSend=DateTime.UtcNow.AddSeconds(1);
  try{protocolSend.Invoke(null,new object[]{RequestType,new OverhaulRequest{nonce=connectionNonce,kind=1,player=pendingChoice.player,faction=pendingChoice.faction,commander=pendingChoice.commander,revision=ChoiceRevision}});}catch(Exception ex){Warn("Random intent request failed: "+Root(ex));}
 }
 static bool HandleChoiceRequest(ulong sender,OverhaulRequest q){
  OverhaulHello h;bool exact=hellos.TryGetValue(sender,out h)&&SanctuaryProtocolRules.ExactHello(h,SanctuaryOverhauledLobbyPlugin.PluginVersion)&&q!=null&&q.nonce==h.nonce;
  if(!RandomChoiceRules.ValidChoiceRequest(q,sender,ChoiceRevision,exact)){if(exact)BroadcastSnapshot();return false;}
  var p=PlayerById(sender) as LobbyPlayer;if(p==null||!p.IsHumanPlayer)return false;
  if(q.kind==2){if(q.revision==ChoiceRevision)choiceAcknowledgements[sender]=q.revision;return true;}
  choiceAcknowledgements.Remove(sender);p.isReady=false;
  if(!CanEdit||LobbyManager.isLocked||LobbyBulkAi.IsRunning||LobbyPresetRuntime.IsApplying||q.revision!=ChoiceRevision||p.type!=EM.Core.PlayerType.Player){BroadcastSnapshot();return false;}
  int placeholder;string error;if(!RandomChoiceRules.ValidIntent(q.faction,q.commander,Choices(),out placeholder,out error)){BroadcastSnapshot();return false;}
  bool accepted=CommitLobbyChoice(p,q.faction,q.commander,placeholder);BroadcastSnapshot();return accepted;
 }
 static void TickRandomChoices(){
  if(IsHost){RefreshLobbyChoices();return;}
  var roster=AuthoritativeState;if(choiceHostConfirmed&&(roster==null||choiceHost!=roster.hostID||choiceSession!=LobbyManager.currentSessionID)){ClearRandomChoices();Revision=0;hostCapabilityAcknowledged=false;nextHello=DateTime.MinValue;}
  if(pendingChoice!=null&&DateTime.UtcNow>=pendingChoiceDeadline){pendingChoice=null;Chat("The host did not accept that faction/commander choice. The host's current choice is shown; choose again after synchronization.");SendLobby("SendSetReady",false);}
  SendChoiceRequest();
 }
 static void ClearRandomChoices(){lobbyChoices.Clear();randomAiKeys.Clear();choiceAcknowledgements.Clear();randomLaunch.Clear();nextRandomAi=0;ChoiceRevision=0;pendingChoice=null;choiceHostConfirmed=false;choiceSession=choiceHost=default(PlayerID);launchKey=null;nextChoiceSend=pendingChoiceDeadline=DateTime.MinValue;remoteHostNonce=null;snapshotBlockReason=null;}
 static void PrepareSnapshotContext(string hostNonce){
  var roster=AuthoritativeState;if(roster==null)return;
  if(choiceHostConfirmed&&(choiceHost!=roster.hostID||choiceSession!=LobbyManager.currentSessionID||remoteHostNonce!=hostNonce)){
   ClearRandomChoices();ClearTransitionCapability();ClearMatchColors();entries.Clear();seatColors.Clear();occupiedColorOwners.Clear();aiOwnerKeys.Clear();Revision=0;hostCapabilityAcknowledged=false;lastSentPreference=null;lastSentPreferenceRevision=-1;nextHello=DateTime.MinValue;
  }
  remoteHostNonce=hostNonce;
 }
 static string RandomStartBlockReason(){
  if(!IsHost)return pendingChoice==null?null:"Wait for Random intent to synchronize.";
  RefreshLobbyChoices();if(!lobbyChoices.Values.Any(RandomChoiceRules.IsRandom))return null;
  if(!PrivateContracts)return "Concealed Random is unavailable because the launch compatibility guard failed. Choose explicit faction and commander selections.";
  var roster=AuthoritativeState;if(roster==null)return "Random choice roster is unavailable.";
  foreach(var p in roster.GetPlayers(true,true).Where(p=>p.IsHumanPlayer&&!LobbyManager.IsLocalPlayer(p))){
   OverhaulHello hello;ulong id=Id(p);if(!hellos.TryGetValue(id,out hello))return p.name+" does not have this Lobby version installed. Concealed Random requires matching clients, even in Vanilla Lobby. Choose explicit choices or ask them to install it.";
   if(!SanctuaryProtocolRules.ExactHello(hello,SanctuaryOverhauledLobbyPlugin.PluginVersion))return p.name+" has a different Lobby version or copy; concealed Random cannot start.";
   int ack;if(!choiceAcknowledgements.TryGetValue(id,out ack)||ack!=ChoiceRevision)return p.name+" has not acknowledged the current Random choices. Wait for synchronization.";
  }
  var catalog=Choices();foreach(var c in lobbyChoices.Values){int placeholder;string error;if(!RandomChoiceRules.ValidIntent(c.faction,c.commander,catalog,out placeholder,out error))return error;}
  return ApiStartReason();
 }
 static int SecureDraw(int count){
  if(count<1)throw new ArgumentOutOfRangeException("count");uint maximum=uint.MaxValue-(uint.MaxValue%(uint)count);var bytes=new byte[4];
  using(var rng=RandomNumberGenerator.Create()){uint sample;do{rng.GetBytes(bytes);sample=BitConverter.ToUInt32(bytes,0);}while(sample>=maximum);return (int)(sample%(uint)count);}
 }
 internal static bool PrepareRandomLaunch(PlayerID sender){
  if(!IsHost)return false;
  if(!PrivateContracts){if(lobbyChoices.Values.Any(RandomChoiceRules.IsRandom)){Chat("Random launch guard is unavailable; choose explicit selections.");return false;}return true;}var roster=AuthoritativeState;
  if(roster==null||sender!=roster.hostID||LobbyManager.currentSessionID==PlayerID.None){Chat("Launch refused: host/session authority is unavailable.");return false;}
  RefreshLobbyChoices();if(!lobbyChoices.Values.Any(RandomChoiceRules.IsRandom)){launchKey=null;return true;}
  string error=StartBlockReason();if(error!=null){Chat(error);return false;}
  if((int)LobbyManager.lobbyGameStatus!=0||!CanEdit||LobbyManager.isLocked){Chat("Random launch refused because the lobby is not editable.");return false;}
  var catalog=Choices();var intents=ChoiceSnapshot().Where(c=>c.army>0).ToArray();
  var rosterPlayers=roster.GetPlayers(true,true).Where(p=>(p.type==EM.Core.PlayerType.Player||p.type==EM.Core.PlayerType.AI)&&p.armyID>0).ToArray();
  if(intents.Length!=rosterPlayers.Length||rosterPlayers.Any(p=>!intents.Any(c=>c.owner==AuthoritativeChoiceKey(p)&&c.army==p.armyID&&c.value==(int)p.faction))){Chat("Random launch refused: roster and intent snapshot differ.");return false;}
  string key=LobbyManager.currentSessionID+"/"+roster.hostID+"/"+Mode+"/"+roster.mapPath+"/"+ChoiceRevision+"/"+RandomChoiceRules.Fingerprint(intents)+"/"+string.Join(";",catalog.OrderBy(c=>c.Identity,StringComparer.Ordinal).Select(c=>c.Identity+"="+c.Value).ToArray());
  if(!randomLaunch.Resolve(key,intents,catalog,SecureDraw,out error)){Chat(error);return false;}launchKey=key;return true;
 }
 internal static Faction LaunchFactionFor(LobbyPlayer player){
  if(player==null)throw new InvalidOperationException("Launch player is unavailable.");
  var intent=StoredChoice(player);if(!RandomChoiceRules.IsRandom(intent))return player.faction;
  int value;if(!PrivateContracts||!IsHost||launchKey==null||!randomLaunch.TryGet(intent.owner,out value)||!Choices().Any(c=>c.Value==value))throw new InvalidOperationException("Random launch was not prepared; refusing an unverified faction.");
  return (Faction)value;
 }
}
