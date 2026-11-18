using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

// The lobby armyID is one-based. The client Lua runtime stores ClientArmy
// instances at Armies[armyID]; army.name is a map marker such as "Army_1".
public sealed class MatchColorAssignment {
 public int ArmyId;
 public Color Color;
 public MatchColorAssignment(int armyId,Color color){ArmyId=armyId;Color=color;}
}

public static class InMatchColorBridge {
 static readonly BindingFlags All=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static readonly TimeSpan RetryDelay=TimeSpan.FromSeconds(1);
 static DateTime nextAttempt;
 static DateTime nextWarning;
 static bool applied;
 public static bool ContractAvailable(){
  try{
   var client=AccessTools.TypeByName("EM.Lua.Client.ClientLuaInterface");
   var shared=client==null?null:client.GetField("UnmanagedData",All);
   var nested=client==null?null:client.GetNestedType("Unmanaged",All);
   var state=nested==null?null:nested.GetField("luaState",All);
   var jit=AccessTools.TypeByName("LuaJIT");
   return shared!=null&&state!=null&&jit!=null&&jit.GetMethod("luaL_dostring",All)!=null&&jit.GetMethod("lua_gettop",All)!=null&&jit.GetMethod("lua_settop",All)!=null&&jit.GetMethod("lua_tostring",All)!=null;
  }catch{return false;}
 }
 static bool Finite01(float value){return !float.IsNaN(value)&&!float.IsInfinity(value)&&value>=0f&&value<=1f;}
 static string LuaNumber(float value){return value.ToString("R",CultureInfo.InvariantCulture);}

 // Builds a fail-closed script from synchronized army IDs. Duplicate IDs are
 // rejected: a snapshot assigning two owners to one army is not authoritative.
 public static bool TryBuildScript(IEnumerable<MatchColorAssignment> assignments,out string script,out string error){
  script=null;error=null;
  if(assignments==null){error="Color assignments are unavailable.";return false;}
  var list=assignments.ToList();
  if(list.Count==0){error="Color assignments are empty.";return false;}
  if(list.Count>32){error="Color assignment count exceeds the supported lobby bound.";return false;}
  var seen=new HashSet<int>();
  foreach(var item in list){
   if(item==null){error="A color assignment is null.";return false;}
   if(item.ArmyId<1||item.ArmyId>255){error="Army ID is outside the one-based lobby range.";return false;}
   if(!seen.Add(item.ArmyId)){error="Duplicate army ID in synchronized color assignments.";return false;}
   var c=item.Color;if(!Finite01(c.r)||!Finite01(c.g)||!Finite01(c.b)||!Finite01(c.a)){error="Color contains an invalid RGBA component.";return false;}
  }
  list.Sort((a,b)=>a.ArmyId.CompareTo(b.ArmyId));
  var bld=new StringBuilder();
  bld.AppendLine("if type(Armies) ~= \"table\" then error(\"Overhauled Lobby: Armies not ready\") end");
  bld.AppendLine("if type(EngineClasses) ~= \"table\" or type(EngineClasses.float4) ~= \"function\" then error(\"Overhauled Lobby: float4 unavailable\") end");
  // Use <= rather than negated > comparisons: math.abs(NaN) > epsilon is
  // false in LuaJIT, which could otherwise turn an invalid native readback
  // into apparent success.
  bld.AppendLine("local function __ol_matches(actual, r, g, b, a)");
  bld.AppendLine(" if not actual then return false end");
  bld.AppendLine(" local values, expected = {actual.x, actual.y, actual.z, actual.w}, {r, g, b, a}");
  bld.AppendLine(" for i = 1, 4 do local value = values[i]; if type(value) ~= \"number\" or value ~= value or not (math.abs(value-expected[i]) <= 0.00001) then return false end end");
  bld.AppendLine(" return true end");
  bld.AppendLine("local __ol_armies, __ol_commanders, __ol_icons = {}, {}, {}");
  // Validate every occupied army before writing any color. Empty/observer seats
  // are absent from the captured assignments. A real starting commander is
  // identified by Sanctuary's COMMAND template tag (also used by installed
  // mod commanders), rather than accepting an arbitrary early-spawned unit.
  foreach(var item in list){
   string id=item.ArmyId.ToString(CultureInfo.InvariantCulture);
   bld.Append("do local army = Armies[").Append(id).AppendLine("]");
   bld.Append(" if not army or army.id ~= ").Append(id).AppendLine(" or type(army.SetColor) ~= \"function\" then error(\"Overhauled Lobby: army not ready\") end");
   // RegisterUnit constructs the unit before adding it to army.units, and
   // ClientUnit:SimCreateThread waits one tick before isInitialized=true so the
   // renderer can catch up. Do not make the one-shot application permanent
   // until the starting commander for every occupied army has crossed that
   // real client lifecycle boundary.
   bld.AppendLine(" if type(army.units) ~= \"table\" or next(army.units) == nil then error(\"Overhauled Lobby: starting unit not registered\") end");
   bld.AppendLine(" local commander = nil");
   bld.AppendLine(" for _, unit in pairs(army.units) do for _, tag in pairs(unit and unit.tp and unit.tp.tags or {}) do if tag == \"COMMAND\" then commander = unit; break end end if commander then break end end");
   bld.AppendLine(" if not commander then error(\"Overhauled Lobby: starting commander not registered\") end");
   bld.AppendLine(" if commander.isInitialized ~= true then error(\"Overhauled Lobby: starting commander renderer not initialized\") end");
   // StrategicIcon color is a separate native value. Capture every current
   // icon and its alpha before any army is written; in-progress/ghost icons use
   // alpha 0.30 and must not become opaque when their player RGB changes.
   bld.AppendLine(" local icons, commanderIcon = {}, commander.icons and commander.icons.StrategicIcon");
   bld.AppendLine(" if not commanderIcon then error(\"Overhauled Lobby: starting commander strategic icon not ready\") end");
   bld.AppendLine(" for _, unit in pairs(army.units) do");
   bld.AppendLine("  local icon = unit and unit.icons and unit.icons.StrategicIcon");
   bld.AppendLine("  if icon then");
   bld.AppendLine("   if type(icon.GetColor) ~= \"function\" or type(icon.SetColor) ~= \"function\" then error(\"Overhauled Lobby: strategic icon contract unavailable\") end");
   bld.AppendLine("   local current = icon:GetColor(); local alpha = current and current.w");
   bld.AppendLine("   if type(alpha) ~= \"number\" or alpha ~= alpha or alpha < 0 or alpha > 1 then error(\"Overhauled Lobby: strategic icon alpha invalid\") end");
   bld.AppendLine("   icons[#icons + 1] = { icon = icon, alpha = alpha }");
   bld.AppendLine("  end");
   bld.AppendLine(" end");
   bld.Append(" __ol_armies[").Append(id).Append("] = army; __ol_commanders[").Append(id).Append("] = commander; __ol_icons[").Append(id).AppendLine("] = icons end");
  }
  bld.AppendLine("local __ol_applied = 0");
  foreach(var item in list){
   string id=item.ArmyId.ToString(CultureInfo.InvariantCulture),r=LuaNumber(item.Color.r),g=LuaNumber(item.Color.g),bl=LuaNumber(item.Color.b),a=LuaNumber(item.Color.a);
   bld.Append("do local army, commander, icons = __ol_armies[").Append(id).Append("], __ol_commanders[").Append(id).Append("], __ol_icons[").Append(id).AppendLine("]");
   bld.Append(" local color = EngineClasses.float4(").Append(r).Append(',').Append(g).Append(',').Append(bl).Append(',').Append(a).AppendLine(")");
   bld.AppendLine(" army:SetColor(color)");
   // ClientArmy:SetColor currently forwards to ClientUnit:SetColor, whose
   // implementation updates the mesh renderer only. Strategic icons are
   // colored once in ClientUnit:SetUpIcon and otherwise retain their original
   // slot color, so update and read back those existing icons explicitly.
   bld.AppendLine(" for _, record in ipairs(icons) do");
   bld.AppendLine("  local iconColor = EngineClasses.float4(color.x, color.y, color.z, record.alpha)");
   bld.AppendLine("  record.icon:SetColor(iconColor)");
   bld.AppendLine("  local ic = record.icon:GetColor(); if not __ol_matches(ic, iconColor.x, iconColor.y, iconColor.z, record.alpha) then error(\"Overhauled Lobby: strategic icon propagation failed\") end");
   bld.AppendLine(" end");
   // SetColor calls current units and the player-list dirty hook. Reaching this
   // assertion proves those calls returned and rejects army-state no-ops.
   // It does not prove that the renderer accepted every unit write.
   bld.Append(" local actual = army.color; if not __ol_matches(actual, ").Append(r).Append(',').Append(g).Append(',').Append(bl).Append(',').Append(a).AppendLine(") then error(\"Overhauled Lobby: color propagation failed\") end");
   bld.AppendLine(" __ol_applied = __ol_applied + 1 end");
  }
  bld.Append("if __ol_applied ~= ").Append(list.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" then error(\"Overhauled Lobby: incomplete color propagation\") end");
  script=bld.ToString();return true;
 }

 // Kept deterministic so compiled-code tests can exercise retry/reset behavior.
 public static bool ShouldAttempt(DateTime utcNow){return !applied&&utcNow>=nextAttempt;}
 public static void RecordAttempt(DateTime utcNow,bool success){nextAttempt=utcNow+RetryDelay;if(success)applied=true;}
 public static bool AppliedForSession {get{return applied;}}

 static string BoundedError(string error){
  error=string.IsNullOrEmpty(error)?"Lua execution failed without an error message.":error;
  error=error.Replace('\r',' ').Replace('\n',' ');
  return error.Length>512?error.Substring(0,512):error;
 }
 // Executes in the caller's Lua context without retaining errors or return values
 // on its shared stack. Tests use an isolated state, never the game's live state.
 public static bool TryExecute(lua_State state,string script,out string error){
  error=null;
  if(state.Handle==UIntPtr.Zero){error="Client Lua state is not ready.";return false;}
  if(string.IsNullOrEmpty(script)){error="Color script is empty.";return false;}
  int top=LuaJIT.lua_gettop(state);
  try{
   int result=LuaJIT.luaL_dostring(state,script);
   if(result==0)return true;
   error=BoundedError(LuaJIT.lua_tostring(state,-1));return false;
  }finally{LuaJIT.lua_settop(state,top);}
 }
 static void WarnDeferred(DateTime now,string error){
  if(now<nextWarning)return;
  nextWarning=now+TimeSpan.FromSeconds(30);
  if(SanctuaryOverhauledLobbyPlugin.Log!=null)SanctuaryOverhauledLobbyPlugin.Log.LogWarning("Deferred in-match color application: "+BoundedError(error));
 }

 public static void ApplyIfReady(){
  var now=DateTime.UtcNow;if(!ShouldAttempt(now))return;
  string script=SanctuaryLobbyState.MatchColorScript();
  if(string.IsNullOrEmpty(script)){RecordAttempt(now,false);return;}
  bool success=false;
  try{
   lua_State state=EM.Lua.Client.ClientLuaInterface.Data.luaState;
   string error;success=TryExecute(state,script,out error);
   if(!success)WarnDeferred(now,error);
   if(success&&SanctuaryOverhauledLobbyPlugin.Log!=null)SanctuaryOverhauledLobbyPlugin.Log.LogInfo("Applied synchronized Overhauled Lobby army colors in client Lua; renderer verification requires an in-game check.");
  }catch(Exception ex){WarnDeferred(now,SanctuaryLobbyState.Root(ex));}
  finally{RecordAttempt(now,success);}
 }
 public static void Reset(){applied=false;nextAttempt=nextWarning=DateTime.MinValue;}
}

[HarmonyPatch]
static class InMatchColorPatch {
 static MethodBase TargetMethod(){var t=AccessTools.TypeByName("EM.Lua.Client.ClientLuaInterface");return t==null?null:AccessTools.Method(t,"RenderUpdate");}
 static void Postfix(){InMatchColorBridge.ApplyIfReady();}
}
