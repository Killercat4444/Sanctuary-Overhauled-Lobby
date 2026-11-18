using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public static partial class LobbyPresetRuntime {
 static Type OptionalApi(string name) { return AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Sanctuary.ModApi."+name,false)).FirstOrDefault(t=>t!=null); }
 static bool ApiAllowsEditing() {
  var api=OptionalApi("Lobby");return api==null || (bool)api.GetProperty("CanChangeSelection",StaticAll).GetValue(null,null);
 }
 static bool IsLocalPlayer(object player) {
  return (bool)FindType("EM.Network.LobbyManager").GetMethod("IsLocalPlayer",StaticAll).Invoke(null,new[]{player});
 }
 static bool SafeAiToken(string value) { return !string.IsNullOrEmpty(value) && Regex.IsMatch(value,@"\A[A-Za-z0-9_.-]+\z"); }
 static bool IsDycomFaction(string name) {
  int value=Convert.ToInt32(ParseFaction(FindType("EM.Core.Faction"),name));
  var api=OptionalApi("Factions");
  if(api!=null)foreach(var entry in (IEnumerable)api.GetProperty("Current",StaticAll).GetValue(null,null)) {
   if(Convert.ToInt32(entry.GetType().GetProperty("Value").GetValue(entry,null))!=value)continue;
   var mod=entry.GetType().GetProperty("Mod").GetValue(entry,null);
   return Convert.ToString(mod.GetType().GetProperty("Id").GetValue(mod,null))=="com.dycom.faction";
  }
  return string.Equals(name,"Dycom",StringComparison.OrdinalIgnoreCase) || string.Equals(name,"DycomMech",StringComparison.OrdinalIgnoreCase);
 }
 static void CaptureAiChoice(Seat seat) {
  seat.AiSpecified=true;seat.AiMod=null;seat.AiKey=null;
  // Capture is read-only. Retired DYCOM picks become the faction's native AI;
  // other mod and native AI options retain their registered selection.
 }
 static void ValidateAiChoice(Seat seat) {
  if(!seat.AiSpecified || seat.AiMod==null)return;
  if(seat.Host || !SafeAiToken(seat.AiMod) || !SafeAiToken(seat.AiKey))throw new InvalidDataException("Invalid AI override in preset.");
  if(seat.AiMod=="com.dycom.faction") {
   // Retired choices no longer exist in Ais.Find. Clearing their override uses
   // AI-Dycom on DYCOM factions and the faction default on other factions.
   // Migration changes this in-memory snapshot, never the user's INI at load.
   seat.AiMod=null;seat.AiKey=null;return;
  }
  var ais=OptionalApi("Ais");
  if(ais==null || ais.GetMethod("Find",StaticAll).Invoke(null,new object[]{seat.AiMod,seat.AiKey})==null)
   throw new InvalidOperationException("Enable the preset's AI mod before applying or saving its profile.");
 }
 static void ApplyAiChoices(Preset preset) {
  foreach(var seat in preset.Seats) if(!seat.Host && seat.AiSpecified) ValidateAiChoice(seat);
 }
}

