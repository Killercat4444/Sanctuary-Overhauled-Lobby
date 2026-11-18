using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal static class UnitRestrictionsUiRules {
 internal static bool IsPicker(string mod,string key){return mod=="com.sanctuary.overhauled-lobby"&&key=="units";}
 internal static bool CanEdit(bool inLobby,bool host,bool editable,bool locked,bool bulk,bool preset,bool apiHost,bool apiEditable){return inLobby&&host&&editable&&!locked&&!bulk&&!preset&&apiHost&&apiEditable;}
 internal static string PortraitPath(string unit){return !string.IsNullOrEmpty(unit)&&unit.Length<=128&&Regex.IsMatch(unit,@"\A[A-Za-z0-9_-]+\z")?"UI/Sprites/Icons/Units/"+unit+".sansprite":null;}
}

// Owned companion options are synchronized by Mod API and consumed by our Lua
// enforcement. ModManager supplies only its generic units picker presentation.
internal static class RestrictionTypeResolver {
 internal static Type Resolve(IEnumerable<Assembly> assemblies,string name,string mvid,Func<Type,bool> active,out string reason){
  reason=null;var found=new List<Type>();bool exists=false;
  foreach(var assembly in assemblies){Type type;try{type=assembly.GetType(name,false);}catch{continue;}if(type==null)continue;exists=true;if(!active(type))continue;
   if(type.Module.ModuleVersionId.ToString()!=mvid){reason="Unsupported loaded contract: "+name;return null;}found.Add(type);
  }
  if(found.Count==1)return found[0];reason=found.Count>1?"Multiple active loaded contracts: "+name:exists?"Waiting for initialization: "+name:"Waiting for loaded type: "+name;return null;
 }
}
internal static class LobbyUnitRestrictions {
 internal const string ModId="com.sanctuary.overhauled-lobby";
 const string LegacyModId="sanctuarymods.unitrestrictions";
 internal const string ManagerMvid="49237414-c505-481d-83c9-c1cb13ac9a68";
 internal const string ApiMvid="f3920172-941a-4b70-ae35-68ef791cc8e3";
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 static readonly string[] keys={"noLand","noAir","noNaval","noExperimental"};
 static readonly string[] labels={"No Land","No Air","No Naval","No Experimentals"};
 static Type pageType,apiType;
 static Harmony patches;
 static string failure;
 static DateTime nextProbe;
 static DateTime nextResolve;
 static string resolutionReason;
 static bool opening;
 static object FindSelected(string id){var type=SanctuaryLobbyState.FindType("Sanctuary.ModApi.Lobby");var selected=type==null?null:type.GetProperty("Selection",All).GetValue(null,null) as IEnumerable;return selected==null?null:selected.Cast<object>().FirstOrDefault(s=>Convert.ToString(Property(s,"Id"))==id);}
 static IReadOnlyDictionary<string,string> Values(){return Property(FindSelected(ModId),"Options") as IReadOnlyDictionary<string,string>;}
 static string Value(string key){var values=Values();string value;return values!=null&&values.TryGetValue(key,out value)?value:"";}
 static string[] UnitIds(){var type=SanctuaryLobbyState.FindType("Sanctuary.ModApi.ModOption");return type==null?new string[0]:((IEnumerable)type.GetMethod("SplitUnits",All).Invoke(null,new object[]{Value("units")})).Cast<string>().ToArray();}
 static Array SelectedLocals(){var type=SanctuaryLobbyState.FindType("Sanctuary.ModApi.Lobby");var selected=(IEnumerable)type.GetProperty("Selection",All).GetValue(null,null);var locals=selected.Cast<object>().Select(s=>Property(s,"Local")).Where(m=>m!=null).ToArray();var array=Array.CreateInstance(SanctuaryLobbyState.FindType("Sanctuary.ModApi.ModInfo"),locals.Length);for(int i=0;i<locals.Length;i++)array.SetValue(locals[i],i);return array;}
 static bool PickerOpen(){if(pageType==null)return false;var page=pageType.GetField("_current",All).GetValue(null);var panel=Field(page,"_lobbyPanel") as GameObject;return panel!=null&&panel.activeSelf&&ThisPicker(page);}
 internal static bool RequiresSync {get{try{return opening||PickerOpen()||keys.Any(k=>Value(k)=="true")||UnitIds().Length>0;}catch{return true;}}}
internal static string StartBlockReason(){try{if(!RequiresSync)return null;if(FindSelected(LegacyModId)!=null)return "Disable the separate Unit Restrictions mod before using integrated restrictions.";if(FindSelected(ModId)==null)return "Integrated Unit Restrictions require Overhauled Lobby gameplay features.";var type=SanctuaryLobbyState.FindType("Sanctuary.ModApi.Lobby");if(type==null)return "Restriction synchronization is unavailable.";if((bool)type.GetProperty("Settling",All).GetValue(null,null))return "Wait for Unit Restrictions to synchronize.";return SanctuaryProtocolRules.NormalizeBlockReason(Convert.ToString(type.GetProperty("StartBlockedReason",All).GetValue(null,null)));}catch{return "Unit Restrictions synchronization could not be verified.";}}
internal static string Summary {get{try{var parts=new List<string>();for(int i=0;i<keys.Length;i++)if(Value(keys[i])=="true")parts.Add(labels[i]);var ids=UnitIds();var names=new Dictionary<string,string>(StringComparer.Ordinal);if(ids.Length>0){var catalog=SanctuaryLobbyState.FindType("Sanctuary.ModApi.UnitCatalog");var entries=(IEnumerable)catalog.GetMethod("Buildable",All).Invoke(null,new object[]{SelectedLocals()});foreach(var entry in entries){string id=Convert.ToString(Property(entry,"Id"));names[id]=Convert.ToString(Property(entry,"Name"));}}foreach(var id in ids){string name;parts.Add(names.TryGetValue(id,out name)&&!string.IsNullOrWhiteSpace(name)?name+" ("+id+")":id);}return "Unit Restrictions: "+(parts.Count==0?"None":string.Join(", ",parts.ToArray()));}catch{return "Unit Restrictions: unavailable";}}}
 internal static bool Available {get{string reason;return Ready(out reason);}}
 internal static bool CanOpen {get{string reason;return Ready(out reason)&&SanctuaryLobbyState.InLobby&&SanctuaryLobbyState.CanEdit;}}
 internal static string Reason {get{string reason;if(!Ready(out reason))return reason;return CanOpen?null:"Unit Restrictions is available in the lobby before the match starts.";}}
 internal static void Tick(){if(DateTime.UtcNow<nextProbe)return;nextProbe=DateTime.UtcNow.AddSeconds(2);string reason;Ready(out reason);}
 internal static void Reset(){opening=false;nextProbe=nextResolve=DateTime.MinValue;resolutionReason=null;}
 internal static void Shutdown(){
  try{if(patches!=null)patches.UnpatchSelf();}
  finally{patches=null;pageType=null;apiType=null;failure=null;Reset();}
 }
 static object Field(object o,string name){return o==null?null:o.GetType().GetField(name,All).GetValue(o);}
 static object Property(object o,string name){return o==null?null:o.GetType().GetProperty(name,All).GetValue(o,null);}
 static object Call(object o,string name,params object[] args){return o.GetType().GetMethod(name,All).Invoke(o,args);}
 static bool ApiBool(string name){return apiType!=null&&(bool)apiType.GetProperty(name,All).GetValue(null,null);}
 static object Selected(){var selection=apiType.GetProperty("Selection",All).GetValue(null,null) as IEnumerable;return selection==null?null:selection.Cast<object>().FirstOrDefault(s=>Convert.ToString(Property(s,"Id"))==ModId);}
 static object[] Definitions(object selected){var options=Property(selected,"OptionDefinitions") as IEnumerable;return options==null?new object[0]:options.Cast<object>().ToArray();}
 static bool HasOption(object selected,string key){return Definitions(selected).Any(o=>Convert.ToString(Property(o,"Key"))==key);}
 static bool Ready(out string reason){
  reason=null;
  try{
   if(failure!=null){reason=failure;return false;}
   if(pageType==null){
    if(DateTime.UtcNow<nextResolve){reason=resolutionReason;return false;}nextResolve=DateTime.UtcNow.AddSeconds(2);
    // ModLoader renames UI assemblies to Name-<UtcNow.Ticks> before loading
    // bytes. Locate already-loaded TYPES, never load another disk assembly.
    var loaded=AppDomain.CurrentDomain.GetAssemblies();
    var page=RestrictionTypeResolver.Resolve(loaded,"SanctuaryHud.ModsPage",ManagerMvid,PageActive,out resolutionReason);
    if(page==null){reason=resolutionReason;return false;}
    var api=RestrictionTypeResolver.Resolve(loaded,"Sanctuary.ModApi.Lobby",ApiMvid,t=>true,out resolutionReason);
    if(api==null){reason=resolutionReason;return false;}
    pageType=page;apiType=api;
    var fill=pageType.GetMethod("FillUnitPickerRows",All);
    var closure=pageType.GetNestedType("<>c__DisplayClass188_0",All);
    var paint=closure.GetMethod("<FillUnitPickerRows>g__Paint|12",All);
    patches=new Harmony("com.sanctuary.overhauledlobby.unitrestrictions.ui");
    patches.Patch(fill,postfix:new HarmonyMethod(typeof(LobbyUnitRestrictions).GetMethod("PickerBuilt",All)));
    patches.Patch(paint,postfix:new HarmonyMethod(typeof(LobbyUnitRestrictions).GetMethod("CellBuilt",All)));
   }
   var selected=Selected();
   if(selected!=null&&(!HasOption(selected,"units")||keys.Any(k=>!HasOption(selected,k)))){reason="The installed Overhauled Lobby gameplay features restriction options are incompatible.";return false;}
   if(!PageActive(pageType)){Shutdown();nextResolve=DateTime.UtcNow.AddSeconds(2);resolutionReason=reason="Waiting for the Mod Manager lobby menu to initialize.";return false;}
   return true;
  }catch(Exception ex){
   string detail=ex.GetBaseException().Message;
   // Both UI patches belong to this adapter. Never retain a half-installed
   // pair if a private contract or Harmony installation fails.
   try{Shutdown();}catch(Exception cleanup){detail+="; patch cleanup failed: "+cleanup.GetBaseException().Message;}
   failure="Unit Restrictions integration is unavailable: "+detail;reason=failure;return false;
  }
 }
 static bool PageActive(Type type){var field=type.GetField("_current",All);if(field==null)return true;var current=field.GetValue(null);if(current==null)return false;var unity=current as UnityEngine.Object;return ReferenceEquals(unity,null)||unity!=null;}
 internal static bool Open(out string reason){
  reason=LobbyMergedOptions.BlockReason();if(reason!=null)return false;
  if(!Ready(out reason))return false;
  if(!SanctuaryLobbyState.InLobby||!SanctuaryLobbyState.CanEdit){reason="Open Unit Restrictions before the match starts.";return false;}
  try{
   if(Selected()==null){if(!SanctuaryLobbyState.IsHost||!ApiBool("CanChangeSelection")){reason="Only the host can enable integrated restrictions.";return false;}opening=true;if(!(bool)apiType.GetMethod("SetSelected",All).Invoke(null,new object[]{ModId,true})){opening=false;reason="Overhauled Lobby gameplay features could not be selected.";return false;}}
   if(Selected()==null||!HasOption(Selected(),"units")){opening=false;reason="Overhauled Lobby gameplay features with integrated restrictions must be installed.";return false;}
   var page=pageType.GetField("_current",All).GetValue(null);
   // Exact UI-only transition performed by native PickerButton's callback.
   pageType.GetField("_pickerMod",All).SetValue(page,ModId);
   pageType.GetField("_pickerKey",All).SetValue(page,"units");
   pageType.GetField("_lobbyScrollToTop",All).SetValue(page,true);
   var version=pageType.GetField("_pickerVersion",All);version.SetValue(page,(int)version.GetValue(page)+1);
   var panel=Field(page,"_lobbyPanel") as GameObject;
   if(panel==null||!panel.activeSelf)Call(page,"ToggleLobbyPanel");else Call(page,"RebuildLobbyPanel");
   panel=Field(page,"_lobbyPanel") as GameObject;if(panel==null||!panel.activeSelf){opening=false;reason="The integrated restrictions menu could not open.";return false;}
   panel.transform.SetAsLastSibling();opening=false;return true;
  }catch(Exception ex){opening=false;reason="The integrated restrictions menu could not open: "+ex.GetBaseException().Message;return false;}
 }
 static bool ThisPicker(object page){return page!=null&&UnitRestrictionsUiRules.IsPicker(Convert.ToString(Field(page,"_pickerMod")),Convert.ToString(Field(page,"_pickerKey")));}
 static bool Editable(){return UnitRestrictionsUiRules.CanEdit(SanctuaryLobbyState.InLobby,SanctuaryLobbyState.IsHost,SanctuaryLobbyState.CanEdit,EM.Network.LobbyManager.isLocked,LobbyBulkAi.IsRunning,LobbyPresetRuntime.IsApplying,ApiBool("IsHost"),ApiBool("CanChangeSelection"));}
 static bool Current(object page,string key){return Convert.ToString(Call(page,"CurrentOption",ModId,key))=="true";}
 static void Set(object page,string key,bool value){
  try{if(!ThisPicker(page)||!Editable()||!HasOption(Selected(),key))return;apiType.GetMethod("SetOption",All,null,new[]{typeof(string),typeof(string),typeof(string)},null).Invoke(null,new object[]{ModId,key,value?"true":"false"});}catch(Exception ex){SanctuaryLobbyState.Warn("Unit Restrictions option was not changed: "+ex.GetBaseException().Message);}
 }
 static void ClearSelected(object page){
  try{
   if(!ThisPicker(page)||!Editable())return;
   var definitions=Definitions(Selected());var expected=keys.Concat(new[]{"units"}).ToArray();
   if(definitions.Length!=expected.Length||expected.Any(k=>definitions.Count(o=>Convert.ToString(Property(o,"Key"))==k)!=1)||definitions.Any(o=>Convert.ToString(Property(o,"Default"))!=(Convert.ToString(Property(o,"Key"))=="units"?"":"false")))return;
   // Mod API replaces this companion's option dictionary in one operation and
   // schedules its ordinary synchronized options revision. It preserves all
   // other mods, selection requirements and settings. Defaults are all off.
   if(!(bool)apiType.GetMethod("ResetOptions",All,null,new[]{typeof(string)},null).Invoke(null,new object[]{ModId}))SanctuaryLobbyState.Warn("Unit Restrictions could not be cleared; wait for host synchronization.");
  }catch(Exception ex){SanctuaryLobbyState.Warn("Unit Restrictions were not cleared: "+ex.GetBaseException().Message);}
 }
 static void PickerBuilt(object __instance,bool __result){
  if(!__result||!ThisPicker(__instance))return;
  try{
   var parent=Field(__instance,"_lobbyList") as Transform;if(parent==null)return;
   var updaters=Field(__instance,"_lobbyUpdaters") as IList<Action>;if(updaters==null)return;
   if(parent.Find("Restriction content")!=null)return;
   parent=LobbyRestrictionPresentation.InsetContent(parent);
   // Remove this native navigation row only inside our picker. The dialog's
   // existing bottom Close button still returns directly to the lobby.
   foreach(Transform child in parent)if(child.GetComponentsInChildren<TMPro.TMP_Text>(true).Any(t=>RestrictionPresentationRules.HideNativeRow(t.text)))child.gameObject.SetActive(false);
   var toolbar=new GameObject("Restriction actions",typeof(RectTransform),typeof(LayoutElement));toolbar.transform.SetParent(parent,false);toolbar.transform.SetSiblingIndex(1);var toolbarSize=toolbar.GetComponent<LayoutElement>();toolbarSize.minHeight=toolbarSize.preferredHeight=36;toolbarSize.flexibleHeight=0;
   var clear=Call(__instance,"Box",toolbar.transform,"Clear Selected",null,(Action)(()=>ClearSelected(__instance)),36f,16f,Color.white,true);
   ((RectTransform)toolbar.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,36);
   var clearBack=Field(clear,"Back") as Image;if(clearBack!=null){var r=clearBack.rectTransform;r.anchorMin=r.anchorMax=new Vector2(1,1);r.pivot=new Vector2(1,1);r.anchoredPosition=Vector2.zero;r.sizeDelta=new Vector2(170,36);var fit=parent.GetComponent<LobbyRestrictionContentLayout>();fit.ClearButton=r;fit.Refresh();}
   Action refreshClear=()=>{if(ThisPicker(__instance))LobbyRestrictionPresentation.Category(clear,false,Editable(),"Clear Selected");};refreshClear();updaters.Add(refreshClear);
   var container=new GameObject("Restriction categories",typeof(RectTransform),typeof(LayoutElement),typeof(GridLayoutGroup),typeof(LobbyRestrictionCategoryGrid));container.transform.SetParent(parent,false);container.transform.SetSiblingIndex(2);
   var selected=Selected();
   for(int i=0;i<keys.Length;i++){
    string key=keys[i];if(!HasOption(selected,key))continue;
    string label=labels[i];var box=Call(__instance,"Box",container.transform,label,null,(Action)(()=>Set(__instance,key,!Current(__instance,key))),RestrictionPresentationRules.RowHeight,16f,Color.white,true);
    Action update=()=>{if(!ThisPicker(__instance))return;try{LobbyRestrictionPresentation.Category(box,Current(__instance,key),Editable(),label);}catch{}};
    update();updaters.Add(update);
   }
   container.GetComponent<LobbyRestrictionCategoryGrid>().Refresh();
   // Native rows derive their height from a switch template, and allow layout
   // shrinking. Pin unit-card rows to a uniform 40-point height plus gutters.
   foreach(Transform row in parent){var columns=row.GetComponentsInChildren<VerticalLayoutGroup>(true);int max=0;foreach(var col in columns){int count=col.transform.Cast<Transform>().Count(c=>c.name=="Box");max=Math.Max(max,count);if(count>0)col.spacing=8;}
    if(max>0){float h=max*RestrictionPresentationRules.RowHeight+(max-1)*8;var size=row.GetComponent<LayoutElement>();if(size!=null){size.minHeight=size.preferredHeight=h;size.flexibleHeight=0;}((RectTransform)row).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,h);}
   }
  }catch(Exception ex){SanctuaryLobbyState.Warn("Unit Restrictions shortcuts unavailable: "+ex.GetBaseException().Message);}
 }
 internal static string PortraitPath(string unit){return UnitRestrictionsUiRules.PortraitPath(unit);}
 static void CellBuilt(object __instance,object __0,IReadOnlyCollection<string> __1){
  try{
   var page=Field(__instance,"<>4__this");if(!ThisPicker(page)||__0==null||__1==null)return;
   var back=Field(__0,"Back") as Image;var glow=Field(__0,"Icon") as Image;var symbol=Field(__0,"Symbol") as Image;var text=Field(__0,"Text") as TMPro.TMP_Text;bool cell=(bool)Field(__0,"IsCell");string unit=cell&&__1.Count==1?__1.First():null;
   Action update=()=>{if(ThisPicker(page))LobbyRestrictionPresentation.Card(back,glow,symbol,text,unit,cell);};
   update();var updaters=Field(page,"_lobbyUpdaters") as IList<Action>;if(updaters!=null)updaters.Add(update);
  }catch{/* A changed private contract leaves the native picker intact. */}
 }
}
