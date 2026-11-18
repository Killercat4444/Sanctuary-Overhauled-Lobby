using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EM.Core;
using EM.Network;
using EM.Network.Lobby;
using HarmonyLib;

// Resolve synchronized Random intent only while NetworkManager constructs the
// launch payload.  The lobby's authoritative state remains visibly Random.
[HarmonyPatch]
static class LobbyRandomLaunchPatch {
 static MethodBase TargetMethod(){
  var method=AccessTools.Method(typeof(EM.Network.NetworkManager),"HostStartGame",Type.EmptyTypes) as MethodInfo;
  if(method==null||!method.IsStatic||method.ReturnType!=typeof(void))
   throw new MissingMethodException("Unsupported launch contract: expected static void NetworkManager.HostStartGame().");
  return method;
 }

 static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions){
  if(instructions==null)throw new ArgumentNullException("instructions");
  var code=instructions.ToList();
  var source=AccessTools.Field(typeof(LobbyPlayer),"faction");
  var destination=AccessTools.Field(typeof(EM.Engine.PlayerInformationManaged),"faction");
  var resolver=AccessTools.Method(typeof(SanctuaryLobbyState),"LaunchFactionFor",new[]{typeof(LobbyPlayer)});
  if(source==null||source.FieldType!=typeof(Faction))
   throw new MissingFieldException("Unsupported launch contract: LobbyPlayer.faction is unavailable or has changed type.");
  if(destination==null||destination.FieldType!=typeof(Faction))
   throw new MissingFieldException("Unsupported launch contract: PlayerInformationManaged.faction is unavailable or has changed type.");
  if(resolver==null||!resolver.IsStatic||resolver.ReturnType!=typeof(Faction))
   throw new MissingMethodException("Unsupported launch contract: expected static Faction SanctuaryLobbyState.LaunchFactionFor(LobbyPlayer).");

  var sourceLoads=new List<int>();
  var exactCopies=new List<int>();
  for(int i=0;i<code.Count;i++){
   if(code[i].opcode!=OpCodes.Ldfld||!Equals(code[i].operand,source))continue;
   sourceLoads.Add(i);
   if(i>=2&&i+1<code.Count&&IsLoadLocalAddress(code[i-2].opcode)&&IsLoadLocal(code[i-1].opcode)&&
      code[i+1].opcode==OpCodes.Stfld&&Equals(code[i+1].operand,destination))exactCopies.Add(i);
  }
  if(sourceLoads.Count!=1||exactCopies.Count!=1)
   throw new InvalidOperationException("Unsupported launch contract: expected exactly one LobbyPlayer.faction copy into PlayerInformationManaged.faction; found "+sourceLoads.Count+" source load(s) and "+exactCopies.Count+" exact copy pattern(s).");

  // Mutate the existing instruction so Harmony labels and exception blocks are
  // retained. LobbyPlayer is already on the stack; the resolver returns the
  // same Faction stack shape consumed by the following stfld.
  int index=exactCopies[0];
  code[index].opcode=OpCodes.Call;
  code[index].operand=resolver;
  return code;
 }

 static bool IsLoadLocalAddress(OpCode opcode){return opcode==OpCodes.Ldloca||opcode==OpCodes.Ldloca_S;}
 static bool IsLoadLocal(OpCode opcode){
  return opcode==OpCodes.Ldloc||opcode==OpCodes.Ldloc_S||opcode==OpCodes.Ldloc_0||opcode==OpCodes.Ldloc_1||opcode==OpCodes.Ldloc_2||opcode==OpCodes.Ldloc_3;
 }
}

public static partial class SanctuaryLobbyState {
 static readonly Guid RandomLaunchModApiMvid=new Guid("f3920172-941a-4b70-ae35-68ef791cc8e3");

 // Mod API has already accepted native message 14 and locked its selection by
 // the time the private HandleOnHost prefix reaches this boundary.  Unwind that
 // lock only for the current authoritative host/session; a forged sender must
 // never be able to invoke HostStartFailed.
 internal static bool HandleGuardedRandomLaunch(PlayerID sender,int length){
  if(!IsHost)return false;
  var roster=AuthoritativeState;
  if(roster==null||LobbyManager.currentSessionID==PlayerID.None||sender!=roster.hostID)return false;
  Exception failure=null;
  try{if(length==1&&PrepareRandomLaunch(sender))return true;}catch(Exception ex){failure=ex;}
  launchKey=null;
  try{ReleaseFailedRandomStart();}catch(Exception cleanup){Warn("Random start recovery is unavailable: "+Root(cleanup));}
  if(failure!=null)Chat("Random launch preparation failed safely: "+Root(failure));
  return false;
 }

 static void ReleaseFailedRandomStart(){
  var api=FindType("Sanctuary.ModApi.Lobby");
  if(api.Assembly.ManifestModule.ModuleVersionId!=RandomLaunchModApiMvid)
   throw new InvalidOperationException("Unsupported Mod API start-recovery contract.");
  var methods=api.GetMethods(StaticAll).Where(m=>m.Name=="HostStartFailed"&&m.IsStatic&&m.IsAssembly&&m.ReturnType==typeof(void)&&m.GetParameters().Length==0).ToArray();
  if(methods.Length!=1)throw new MissingMethodException("Unsupported Mod API start-recovery signature.");
  methods[0].Invoke(null,null);
 }
}
