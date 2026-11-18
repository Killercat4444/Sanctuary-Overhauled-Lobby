using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using EM.Core;
using EM.Network;
using EM.Network.Lobby;

// Custom controls use a dedicated UGUI overlay with top-left local units.
// Native controls retain their callbacks; temporary layout is fully restored.
public static class SanctuaryLobbyUi {
 const float RosterLeftInset=8,ReadyColumnCenter=877,ReadyColumnWidth=64;
 static readonly HashSet<int> portraitDiagnostics=new HashSet<int>();
 public sealed class LobbyGlyph : MaskableGraphic {
  internal int Kind;internal bool Ready,Up;
  static void Line(VertexHelper mesh,Vector2 a,Vector2 b,float thickness,Color32 color){var normal=new Vector2(-(b-a).y,(b-a).x).normalized*thickness/2;int n=mesh.currentVertCount;mesh.AddVert(a-normal,color,Vector2.zero);mesh.AddVert(a+normal,color,Vector2.zero);mesh.AddVert(b+normal,color,Vector2.zero);mesh.AddVert(b-normal,color,Vector2.zero);mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);}
  protected override void OnPopulateMesh(VertexHelper mesh){mesh.Clear();var r=rectTransform.rect;Vector2 c=r.center;float s=Mathf.Min(r.width,r.height)/24;Color32 tint=color;
   if(Kind==1){float direction=Up?-1:1;Line(mesh,c+new Vector2(-5,2*direction)*s,c+new Vector2(0,-3*direction)*s,1.6f*s,tint);Line(mesh,c+new Vector2(0,-3*direction)*s,c+new Vector2(5,2*direction)*s,1.6f*s,tint);return;}
   if(Kind==3){var points=new[]{new Vector2(-8,-5),new Vector2(-10,5),new Vector2(-4,1),new Vector2(0,8),new Vector2(4,1),new Vector2(10,5),new Vector2(8,-5),new Vector2(-8,-5)};for(int i=1;i<points.Length;i++)Line(mesh,c+points[i-1]*s,c+points[i]*s,1.7f*s,tint);return;}
   if(Kind==4){mesh.AddVert(c+new Vector2(-5,-7)*s,tint,Vector2.zero);mesh.AddVert(c+new Vector2(-5,7)*s,tint,Vector2.zero);mesh.AddVert(c+new Vector2(7,0)*s,tint,Vector2.zero);mesh.AddTriangle(0,1,2);return;}
   const int segments=32;for(int i=0;i<segments;i++){float a=i*Mathf.PI*2/segments,b=(i+1)*Mathf.PI*2/segments;float outer=Kind==2?(i%4<2?10:8):10,inner=Kind==2?4.5f:8;int n=mesh.currentVertCount;mesh.AddVert(c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*outer*s,tint,Vector2.zero);mesh.AddVert(c+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*outer*s,tint,Vector2.zero);mesh.AddVert(c+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*inner*s,tint,Vector2.zero);mesh.AddVert(c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*inner*s,tint,Vector2.zero);mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);}
   if(Kind==0&&Ready){Line(mesh,c+new Vector2(-5,0)*s,c+new Vector2(-1,-4)*s,2*s,tint);Line(mesh,c+new Vector2(-1,-4)*s,c+new Vector2(6,5)*s,2*s,tint);}
  }
 }
 sealed class NativePlacement {
  internal RectTransform Rect;internal Transform Parent;internal int Sibling;
  internal CanvasGroup MirrorGroup;internal Transform[] Ancestors;
  internal Vector2 Min,Max,Pivot,Size;internal Vector3 Position,Scale;internal Quaternion Rotation;
  internal NativePlacement(RectTransform rect){Rect=rect;Parent=rect.parent;Sibling=rect.GetSiblingIndex();Min=rect.anchorMin;Max=rect.anchorMax;Pivot=rect.pivot;Size=rect.sizeDelta;Position=rect.anchoredPosition3D;Scale=rect.localScale;Rotation=rect.localRotation;var ancestors=new List<Transform>();for(var p=Parent;p!=null;p=p.parent)ancestors.Add(p);Ancestors=ancestors.ToArray();}
  internal void Mirror(){
   if(MirrorGroup==null)return;bool active=true,interactable=true,blocks=true,readGroups=true;float alpha=1;
   foreach(var ancestor in Ancestors){
    if(ancestor==null){active=false;break;}if(!ancestor.gameObject.activeSelf)active=false;
    var parentCanvas=ancestor.GetComponent<Canvas>();if(parentCanvas!=null&&!parentCanvas.enabled)active=false;
    if(readGroups)foreach(var group in ancestor.GetComponents<CanvasGroup>()){if(!group.enabled)continue;if(group!=nativeFade)alpha*=group.alpha;interactable&=group.interactable;blocks&=group.blocksRaycasts;if(group.ignoreParentGroups)readGroups=false;}
   }
   // Only the new holder is changed. Native activeSelf/enabled/interactable
   // remain owned by UpdateButtons and native UI callbacks.
   MirrorGroup.alpha=alpha;MirrorGroup.interactable=interactable;MirrorGroup.blocksRaycasts=blocks;MirrorGroup.gameObject.SetActive(active&&alpha>0);
  }
  internal void Restore(){
   if(Rect==null)return;
   // A destroyed native hierarchy must not be resurrected at the scene root.
   // These controls survived only because the overlay temporarily owned them.
   if(Parent==null){Rect.gameObject.SetActive(false);UnityEngine.Object.Destroy(Rect.gameObject);return;}
   Rect.SetParent(Parent,false);Rect.SetSiblingIndex(Sibling);Rect.anchorMin=Min;Rect.anchorMax=Max;Rect.pivot=Pivot;Rect.sizeDelta=Size;Rect.anchoredPosition3D=Position;Rect.localScale=Scale;Rect.localRotation=Rotation;
  }
 }
 static readonly List<NativePlacement> nativePlacements=new List<NativePlacement>();
 static readonly Dictionary<RectTransform,NativePlacement> originalPlacements=new Dictionary<RectTransform,NativePlacement>();
 sealed class NativeButtonLayout {
  internal Component Manager;internal System.Reflection.FieldInfo AutoFit;internal object OriginalAutoFit;
  internal readonly Dictionary<Behaviour,bool> Drivers=new Dictionary<Behaviour,bool>();
  internal readonly Dictionary<HorizontalOrVerticalLayoutGroup,bool> ExpandWidths=new Dictionary<HorizontalOrVerticalLayoutGroup,bool>();
  internal readonly Dictionary<TMP_Text,TextAlignmentOptions> Alignments=new Dictionary<TMP_Text,TextAlignmentOptions>();
  internal readonly Dictionary<TMP_Text,Vector4> Margins=new Dictionary<TMP_Text,Vector4>();
  internal readonly Dictionary<TMP_Text,float> FontSizes=new Dictionary<TMP_Text,float>();
  internal readonly List<RectTransform> States=new List<RectTransform>(),Icons=new List<RectTransform>();
  internal NativeButtonLayout(Component manager){
   Manager=manager;AutoFit=manager.GetType().GetField("autoFitContent",SanctuaryLobbyState.InstanceAll);if(AutoFit!=null)OriginalAutoFit=AutoFit.GetValue(manager);
   foreach(var rect in manager.GetComponentsInChildren<RectTransform>(true))TrackNative(rect);
   foreach(var b in manager.GetComponentsInChildren<Behaviour>(true))if(b is LayoutGroup||b is ContentSizeFitter){Drivers[b]=b.enabled;var layout=b as HorizontalOrVerticalLayoutGroup;if(layout!=null)ExpandWidths[layout]=layout.childForceExpandWidth;}
   foreach(string name in new[]{"normalCG","highlightCG","disabledCG"}){var f=manager.GetType().GetField(name,SanctuaryLobbyState.InstanceAll);var c=f==null?null:f.GetValue(manager) as Component;if(c!=null){var r=c.transform as RectTransform;TrackNative(r);States.Add(r);}}
   foreach(var text in manager.GetComponentsInChildren<TMP_Text>(true)){TrackNative(text.rectTransform);Alignments[text]=text.alignment;Margins[text]=text.margin;FontSizes[text]=text.fontSize;}
   foreach(string name in new[]{"normalImageObj","highlightImageObj","disabledImageObj"}){var f=manager.GetType().GetField(name,SanctuaryLobbyState.InstanceAll);var image=f==null?null:f.GetValue(manager) as Image;if(image!=null){var r=image.transform.parent as RectTransform;if(r!=null&&r!=manager.transform){TrackNative(r);Icons.Add(r);}}}
  }
  internal void Apply(){
   if(Manager==null)return;if(AutoFit!=null)AutoFit.SetValue(Manager,false);foreach(var b in Drivers)if(b.Key!=null)b.Key.enabled=false;
   var rect=Manager.transform as RectTransform;var holder=rect.parent as RectTransform;Place(rect,new Rect(0,0,holder.rect.width,holder.rect.height));
   foreach(var state in States)if(state!=null)Stretch(state);
   foreach(var text in Alignments)if(text.Key!=null){var label=text.Key;label.fontSize=16;label.alignment=TextAlignmentOptions.Center;label.margin=Vector4.zero;var icon=Icons.FirstOrDefault(i=>i!=null&&i.parent==label.transform.parent&&i.gameObject.activeSelf);float iconWidth=icon==null?0:26;float labelWidth=Mathf.Min(Mathf.Max(1,rect.rect.width-16-iconWidth),label.preferredWidth+2),start=(rect.rect.width-labelWidth-iconWidth)/2;Place(label.rectTransform,new Rect(start+iconWidth,0,labelWidth,rect.rect.height));if(icon!=null)Place(icon,new Rect(start,(rect.rect.height-20)/2,20,20));}
  }
  internal void Restore(){if(Manager!=null&&AutoFit!=null)AutoFit.SetValue(Manager,OriginalAutoFit);foreach(var layout in ExpandWidths)if(layout.Key!=null)layout.Key.childForceExpandWidth=layout.Value;foreach(var b in Drivers)if(b.Key!=null)b.Key.enabled=b.Value;foreach(var text in Alignments)if(text.Key!=null){text.Key.alignment=text.Value;text.Key.margin=Margins[text.Key];text.Key.fontSize=FontSizes[text.Key];}}
 }
 static readonly List<NativeButtonLayout> nativeButtonLayouts=new List<NativeButtonLayout>();
 sealed class NativeChatText {
  internal TMP_Text Text;internal float Size,Line,Paragraph;internal bool Auto;
  internal NativeChatText(TMP_Text text){Text=text;Size=text.fontSize;Line=text.lineSpacing;Paragraph=text.paragraphSpacing;Auto=text.enableAutoSizing;text.enableAutoSizing=false;text.fontSize=11;text.lineSpacing=0;text.paragraphSpacing=0;}
  internal void Restore(){if(Text==null)return;Text.fontSize=Size;Text.lineSpacing=Line;Text.paragraphSpacing=Paragraph;Text.enableAutoSizing=Auto;}
 }
 static readonly List<NativeChatText> nativeChatTexts=new List<NativeChatText>();
 static void CompactChatText(TMP_Text text){if(text!=null&&!nativeChatTexts.Any(t=>t.Text==text))nativeChatTexts.Add(new NativeChatText(text));}
 static readonly Dictionary<Sprite,Rect> artworkBounds=new Dictionary<Sprite,Rect>();
 sealed class AvatarBinding {internal ulong Id;internal int Army;internal PlayerType Type;internal RectTransform Holder;internal RawImage Raw;internal Image Image;}
 static readonly List<AvatarBinding> avatars=new List<AvatarBinding>();
 sealed class PortraitBinding {internal Button Button;internal string Faction,Commander;internal LobbyPlayer Player;}
 static readonly List<PortraitBinding> rowPortraits=new List<PortraitBinding>(),popupPortraits=new List<PortraitBinding>();
 static LobbyPlayer[] shownAiActors=new LobbyPlayer[0];
 static RectTransform headerCard,chatCard,footerCard,rulesCard,rulesContent;
 static Button fillAi,removeAi,observeButton;static TextMeshProUGUI bulkStatus,rulesText,modsTabText;static int rulesTab;
 sealed class HiddenGroup {
  internal CanvasGroup Group;internal bool Added,Interactable,Blocks;internal float Alpha;
  internal void Restore(){if(Group==null)return;if(Added)UnityEngine.Object.Destroy(Group);else{Group.alpha=Alpha;Group.interactable=Interactable;Group.blocksRaycasts=Blocks;}}
 }
 sealed class Option {internal string Text;internal Action Select;internal Color? Color;internal Sprite Icon;internal SanctuaryLobbyState.FactionChoice Portrait;internal Option(string text,Action select,Color? color=null,Sprite icon=null,SanctuaryLobbyState.FactionChoice portrait=null){Text=text;Select=select;Color=color;Icon=icon;Portrait=portrait;}}
 static GameObject root,popup,modal,rosterRows,markers;
 static RectTransform rootRect,rosterViewport,rowContent,mapRect,mapContent,settingsContent;
 static Canvas canvas;static TMP_FontAsset font;static TMP_InputField presetName;
 static TextMeshProUGUI modeText,teamText,presetText,messageText,rosterTitle,mapTitle,mapDetails,mapPending;
 static CanvasGroup overlayFade,nativeFade;static bool ticking;
 static Button changeMap;static readonly Dictionary<Graphic,bool> hiddenGraphics=new Dictionary<Graphic,bool>();static readonly Dictionary<CanvasGroup,bool> ignoredGroups=new Dictionary<CanvasGroup,bool>();
 static Button apply,update,save,rename,delete,modeButton,teamButton,unitRestrictionsButton;
 static TextMeshProUGUI unitRestrictionsReason;
 static Image mapImage;static Component nativeLobby;static readonly List<HiddenGroup> hidden=new List<HiddenGroup>();
 static string rosterKey,markerKey,mapKey,catalogKey,lastError,shownStartReason;static LobbyMapData map;static int selectedPreset,attachedFrame,diagnostics;
 static string shownChoicePresentation;
 static DateTime nextRefresh,nextRetry;static bool committed;static float scrollPosition=1;static int attachedWidth,attachedHeight;
 static readonly Color Ink=new Color(15/255f,29/255f,43/255f,.96f),Accent=new Color(.52f,.76f,.90f),Muted=new Color(.71f,.78f,.82f),Neutral=new Color(.32f,.35f,.38f,1),Border=new Color(36/255f,90/255f,131/255f,.95f);
 public static bool IsActive {get{return root!=null&&committed&&root.activeInHierarchy;}}
 static bool LobbyVisible(){try{
  if(!SanctuaryLobbyState.PrivateContracts||!SanctuaryLobbyState.InLobby)return false;
  var t=SanctuaryLobbyState.FindType("EM.UI.InterfaceManager");var i=t.GetField("Instance",SanctuaryLobbyState.StaticAll).GetValue(null);
  if(i==null||Convert.ToInt32(t.GetField("currentWindow",SanctuaryLobbyState.InstanceAll).GetValue(i))!=6)return false;
  foreach(string name in new[]{"loadingModalWindow","simpleMessageModalWindow","exitModalWindow","questionModalWindow"}){
   var f=t.GetField(name,SanctuaryLobbyState.InstanceAll);if(f==null)return false;var modalWindow=f.GetValue(i) as Component;if(modalWindow!=null&&modalWindow.gameObject.activeInHierarchy&&!(name=="loadingModalWindow"&&ClosingLoadingModal()))return false;
  }
  var lobby=EM.UI.LobbyInterface.Instance;var picker=lobby==null?null:lobby.transform.Find("Mods Lobby Panel");return picker==null||!picker.gameObject.activeInHierarchy;
 }catch{return false;}}

 public static void TickCanvas(){
  if(ticking)return;ticking=true;try{TickCanvasCore();}finally{ticking=false;}
 }
 static int NativeWindow(){try{var t=SanctuaryLobbyState.FindType("EM.UI.InterfaceManager");var i=t.GetField("Instance",SanctuaryLobbyState.StaticAll).GetValue(null);return i==null?-1:Convert.ToInt32(t.GetField("currentWindow",SanctuaryLobbyState.InstanceAll).GetValue(i));}catch{return -1;}}
 static bool ClosingLoadingModal(){try{
  var type=SanctuaryLobbyState.FindType("EM.UI.InterfaceManager");var instance=type.GetField("Instance",SanctuaryLobbyState.StaticAll).GetValue(null);var field=type.GetField("loadingModalWindow",SanctuaryLobbyState.InstanceAll);var modal=instance==null||field==null?null:field.GetValue(instance) as Component;if(modal==null)return false;
  var open=modal.GetType().GetField("isOn",SanctuaryLobbyState.InstanceAll);var modalCanvas=modal.GetComponent<Canvas>();bool known=open!=null&&open.FieldType==typeof(bool);bool above=modalCanvas!=null&&modalCanvas.enabled&&modalCanvas.overrideSorting&&modalCanvas.sortingLayerID==0&&modalCanvas.sortingOrder>250;
  return LobbyUiGeometry.ClosingModalAllowsPresentation(modal.gameObject.activeInHierarchy,known,known&&(bool)open.GetValue(modal),above);
 }catch{return false;}}
 static void MirrorOverlay(bool interactive){
  if(overlayFade==null)return;overlayFade.alpha=nativeFade==null?1:Mathf.Clamp01(nativeFade.alpha);bool closing=ClosingLoadingModal();overlayFade.interactable=LobbyUiGeometry.OverlayInputAllowed(interactive,nativeFade==null||nativeFade.interactable,closing,overlayFade.alpha);overlayFade.blocksRaycasts=LobbyUiGeometry.OverlayInputAllowed(interactive,nativeFade==null||nativeFade.blocksRaycasts,closing,overlayFade.alpha);
  // Beam.OnSubmit does not consult CanvasGroup.interactable. A retained frame
  // must relinquish keyboard focus as well as pointer input during loading.
  var events=EventSystem.current;var selected=events==null?null:events.currentSelectedGameObject;
  if(!overlayFade.interactable&&selected!=null&&root!=null&&selected.transform.IsChildOf(root.transform))events.SetSelectedGameObject(null);
 }
 static void TickCanvasCore(){
  try{
   if(!LobbyVisible()){
    // TransitionTo changes currentWindow before Beam finishes fading the lobby.
    // Keep the modded outgoing frame until its actual native animation is hidden.
    if(LobbyUiGeometry.RetainOutgoing(committed,NativeWindow(),nativeLobby!=null&&nativeLobby.gameObject.activeInHierarchy,nativeFade!=null,nativeFade==null?0:nativeFade.alpha)){ClosePopup();CloseModal();RefreshChoicePresentation();MirrorOverlay(false);return;}
    RestoreNativeLayout();return;
   }
   var lobby=EM.UI.LobbyInterface.Instance;if(lobby==null||!lobby.gameObject.activeInHierarchy){RestoreNativeLayout();return;}
   if(nativeLobby!=null&&nativeLobby!=lobby)RestoreNativeLayout();
   if(root!=null&&(attachedWidth!=Screen.width||attachedHeight!=Screen.height))RestoreNativeLayout();
   if(root==null){if(DateTime.UtcNow<nextRetry)return;Attach(lobby);}
   MirrorOverlay(true);
   foreach(var placement in nativePlacements)placement.Mirror();
   foreach(var button in nativeButtonLayouts)button.Apply();
   if(DateTime.UtcNow<nextRefresh)return;nextRefresh=DateTime.UtcNow.AddMilliseconds(150);
   bool dirty=Refresh();if(committed){SuppressChildGroups();HideNativeDecoration();}if(dirty||!committed)Canvas.ForceUpdateCanvases();
   if(!committed){ValidateRenderState();HideNativePanels();LayoutNativeControls();committed=true;Log("UGUI candidate committed; visual acceptance still requires the user test.");}
   if(diagnostics<3&&Time.frameCount-attachedFrame>diagnostics*60){ValidateRenderState();LogDiagnostics();diagnostics++;}
  }catch(Exception ex){string message=SanctuaryLobbyState.Root(ex);RestoreNativeLayout();nextRetry=DateTime.UtcNow.AddSeconds(5);if(message!=lastError){lastError=message;Log("UGUI candidate restored native lobby: "+message);}}
 }

 static void Attach(Component lobby){
  nativeLobby=lobby;font=lobby.GetComponentsInChildren<TextMeshProUGUI>(true).Select(t=>t.font).FirstOrDefault(f=>f!=null);
  if(font==null||EventSystem.current==null)throw new InvalidOperationException("Native font or EventSystem is not ready.");
  root=new GameObject("Sanctuary Overhauled Lobby "+SanctuaryOverhauledLobbyPlugin.PluginVersion,typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
  rootRect=root.GetComponent<RectTransform>();canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=250;
  overlayFade=root.AddComponent<CanvasGroup>();nativeFade=lobby.GetComponent<CanvasGroup>();MirrorOverlay(true);
  var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1672,941);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
  Canvas.ForceUpdateCanvases();var layout=LobbyUiGeometry.Layout(rootRect.rect.width,rootRect.rect.height);float right=500,left=1076;
  headerCard=Panel(rootRect,"Native lobby header",LayoutRect(layout[0]),Ink);chatCard=Panel(rootRect,"Native lobby chat",LayoutRect(layout[3]),Ink);footerCard=Panel(rootRect,"Native lobby footer",LayoutRect(layout[5]),Ink);
  Label(chatCard,"CHAT",new Rect(14,6,left-28,25),18,Accent);
  var roster=Panel(rootRect,"Compact Roster",LayoutRect(layout[1]),Ink);
  Header(roster,"SLOT",13,50);Header(roster,"PLAYER",78,221);Header(roster,"FACTION",323,161);Header(roster,"COMMANDER",503,221);Header(roster,"TEAM",751,84);Label(roster,"READY",new Rect(RosterLeftInset+ReadyColumnCenter-ReadyColumnWidth/2,1,ReadyColumnWidth,25),13,Accent,TextAlignmentOptions.Center);
  rowContent=Scroll(roster,new Rect(RosterLeftInset,29,left-16,363),out rosterViewport);
  rosterViewport.parent.GetComponent<ScrollRect>().onValueChanged.AddListener(v=>{scrollPosition=v.y;ClosePopup();});
  fillAi=Button(roster,"FILL AI",new Rect(12,402,142,30),()=>{LobbyBulkAi.StartFill();Invalidate();});removeAi=Button(roster,"REMOVE AI",new Rect(164,402,142,30),()=>{LobbyBulkAi.StartRemove();Invalidate();});bulkStatus=Label(roster,"",new Rect(322,402,left-334,30),12,Muted);
  var info=Panel(rootRect,"Map",LayoutRect(layout[2]),Ink);
  mapRect=Panel(info,"Native map preview",new Rect(14,39,470,396),new Color(.05f,.09f,.13f,1));mapContent=Panel(mapRect,"Fitted map image",new Rect(0,0,470,396),Color.white);mapContent.gameObject.AddComponent<RectMask2D>();mapImage=mapContent.GetComponent<Image>();mapImage.preserveAspect=false;mapImage.raycastTarget=false;
  mapImage.color=Color.clear;mapPending=Label(mapRect,"Map preview loading...",new Rect(12,150,right-51,48),16,Muted,TextAlignmentOptions.Center);
  mapTitle=Label(info,"MAP",new Rect(12,7,right-138,26),18,Accent);
  mapDetails=Label(info,"",new Rect(12,34,right-24,20),11,Muted);mapDetails.gameObject.SetActive(false);
  changeMap=Button(info,"CHANGE MAP",new Rect(right-120,7,108,26),OpenNativeMapSettings);
  var settings=Panel(rootRect,"Match Settings",LayoutRect(layout[4]),Ink);
  Label(settings,"MATCH SETTINGS",new Rect(12,6,right-24,26),18,Accent);
  RectTransform unused;settingsContent=Scroll(settings,new Rect(6,38,right-12,266),out unused,true);SetHeight(settingsContent,398);
  BuildSettings(right-32);
  rulesCard=Panel(rootRect,"Lobby rules and mods",LayoutRect(layout[6]),Ink);
  Button(rulesCard,"LOBBY RULES",new Rect(10,4,160,29),()=>{rulesTab=0;Invalidate();});Button(rulesCard,"OBSERVERS",new Rect(180,4,155,29),()=>{rulesTab=1;Invalidate();});var modsTab=Button(rulesCard,"MODS",new Rect(345,4,180,29),()=>{rulesTab=2;Invalidate();});modsTabText=modsTab.GetComponentInChildren<TextMeshProUGUI>();
  RectTransform rulesViewport;rulesContent=Scroll(rulesCard,new Rect(12,39,left-24,85),out rulesViewport);rulesText=Label(rulesContent,"",new Rect(0,0,left-40,84),14,Muted);rulesText.enableWordWrapping=true;
  observeButton=Button(rulesContent,"BECOME OBSERVER",new Rect(left-232,0,192,29),()=>{SanctuaryLobbyState.ChangeType(PlayerType.Observer);Invalidate();});
  attachedFrame=Time.frameCount;attachedWidth=Screen.width;attachedHeight=Screen.height;diagnostics=0;rosterKey=markerKey=mapKey=null;nextRefresh=DateTime.MinValue;
  Log("UGUI attached: native lobby='"+PathOf(lobby.transform)+"'; screen="+Screen.width+"x"+Screen.height+"; scale="+canvas.scaleFactor+". Validating before this frame renders.");
 }

 static Rect LayoutRect(float[] r){return new Rect(r[0],r[1],r[2],r[3]);}
 static void Header(Transform p,string text,float x,float width){Label(p,text,new Rect(x,1,width,25),13,Accent);}
 static Component NativeField(string name){var f=nativeLobby.GetType().GetField(name,SanctuaryLobbyState.InstanceAll);return f==null?null:f.GetValue(nativeLobby) as Component;}
 static void PlaceNative(Component component,RectTransform holder,Rect bounds){
  var rect=component==null?null:component.transform as RectTransform;if(rect==null)throw new InvalidOperationException("Native lobby control is not ready for layout.");
  if(nativePlacements.Any(p=>p.Rect==rect))return;
  TrackNative(rect);var placement=nativePlacements.First(p=>p.Rect==rect);var wrapper=Element(holder,"Native visibility holder").GetComponent<RectTransform>();Place(wrapper,bounds);placement.MirrorGroup=wrapper.gameObject.AddComponent<CanvasGroup>();placement.Mirror();
  rect.SetParent(wrapper,false);rect.localScale=Vector3.one;rect.localRotation=Quaternion.identity;Place(rect,new Rect(0,0,bounds.width,bounds.height));
  foreach(var group in rect.GetComponentsInChildren<CanvasGroup>(true)){if(!ignoredGroups.ContainsKey(group))ignoredGroups.Add(group,group.ignoreParentGroups);group.ignoreParentGroups=false;}
 }
 static void TrackNative(RectTransform rect){if(rect!=null&&!nativePlacements.Any(p=>p.Rect==rect)){NativePlacement saved;if(!originalPlacements.TryGetValue(rect,out saved))saved=new NativePlacement(rect);nativePlacements.Add(saved);}}
 static void LayoutNativeControls(){
  foreach(var rect in nativeLobby.GetComponentsInChildren<RectTransform>(true))originalPlacements[rect]=new NativePlacement(rect);
  // Capture the exact launcher before moving its native sibling settings button.
  var settings=NativeField("settingsButton");var mods=settings==null||settings.transform.parent==null?null:settings.transform.parent.Find("Mods Button");
  var title=NativeField("lobbyNameTransition");if(title==null)title=NativeField("lobbyNameText");
  PlaceNative(title,headerCard,new Rect(14,5,headerCard.rect.width-28,36));
  var titleText=NativeField("lobbyNameText");if(titleText!=null&&titleText!=title&&!titleText.transform.IsChildOf(title.transform))PlaceNative(titleText,headerCard,new Rect(14,5,headerCard.rect.width-28,36));
  var output=NativeField("chatOutput") as ScrollRect;if(output==null)throw new InvalidOperationException("Native chat scroll contract unavailable.");
  CompactChatText(NativeField("chatOutputText") as TMP_Text);
  float outputHeight=chatCard.rect.height-74;PlaceNative(output,chatCard,new Rect(14,34,chatCard.rect.width-28,outputHeight));
  if(output.viewport!=null){TrackNative(output.viewport);Stretch(output.viewport);output.viewport.offsetMax=new Vector2(output.verticalScrollbar==null?0:-14,0);}
  if(output.content!=null){TrackNative(output.content);output.content.anchorMin=new Vector2(0,1);output.content.anchorMax=new Vector2(1,1);output.content.sizeDelta=new Vector2(0,output.content.sizeDelta.y);}
  // Scrollbars outside the native ScrollRect must also move, while descendants
  // retain the native relative anchors and layout behavior.
  if(output.verticalScrollbar!=null&&!output.verticalScrollbar.transform.IsChildOf(output.transform))PlaceNative(output.verticalScrollbar,chatCard,new Rect(chatCard.rect.width-24,34,10,outputHeight));
  if(output.horizontalScrollbar!=null&&!output.horizontalScrollbar.transform.IsChildOf(output.transform))PlaceNative(output.horizontalScrollbar,chatCard,new Rect(14,34+outputHeight-10,chatCard.rect.width-38,10));
  var chatInput=NativeField("chatInput");PlaceNative(chatInput,chatCard,new Rect(14,chatCard.rect.height-30,chatCard.rect.width-28,20));
  var input=chatInput as TMP_InputField;if(input!=null){CompactChatText(input.textComponent);CompactChatText(input.placeholder as TMP_Text);}
  PlaceNative(NativeField("leaveButton"),footerCard,new Rect(14,10,172,38));
  PlaceNative(settings,footerCard,new Rect(194,10,172,38));
  if(mods!=null)PlaceNative(mods,footerCard,new Rect(374,10,172,38));
  foreach(string field in new[]{"readyButton","notReadyButton","hostReadyButton","hostNotReadyButton"})PlaceNative(NativeField(field),footerCard,new Rect(1254,10,133,38));
  PlaceNative(NativeField("hostStartButton"),footerCard,new Rect(1401,10,177,38));
  // Beam's serialized Normal state can retain a 450px width beneath a 172px
  // root. Constrain all visual states without replacing its event receiver.
  foreach(string field in new[]{"readyButton","notReadyButton","hostReadyButton","hostNotReadyButton","hostStartButton"}){var presentation=new NativeButtonLayout(NativeField(field));nativeButtonLayouts.Add(presentation);presentation.Apply();}
 }
 static string NativeText(Component info,string field){var f=info.GetType().GetField(field,SanctuaryLobbyState.InstanceAll);var text=f==null?null:f.GetValue(info) as TMP_Text;return text==null?"":text.text;}
 static void OpenNativeMapSettings(){
  if(!SanctuaryLobbyState.IsHost||!SanctuaryLobbyState.CanEdit)return;
  var t=SanctuaryLobbyState.FindType("EM.UI.InterfaceManager");var i=t.GetField("Instance",SanctuaryLobbyState.StaticAll).GetValue(null);
  var method=t.GetMethod("OnLobbySettingsPressed",SanctuaryLobbyState.InstanceAll);if(i==null||method==null)throw new MissingMethodException("Native map settings contract is unavailable.");
  method.Invoke(i,null);
 }
 static void BuildSettings(float w){
  modeButton=Dropdown(settingsContent,"Mode: Vanilla Lobby",new Rect(8,0,w-16,32),()=>OpenChoices(modeButton.GetComponent<RectTransform>(),new[]{new Option("Vanilla Lobby",()=>ChangeMode(SanctuaryLobbyMode.Vanilla)),new Option("Overhauled Lobby",()=>ChangeMode(SanctuaryLobbyMode.Overhauled))}));modeText=modeButton.GetComponentInChildren<TextMeshProUGUI>();
  teamButton=Dropdown(settingsContent,"Auto Teams: Off",new Rect(8,39,w-16,32),()=>OpenChoices(teamButton.GetComponent<RectTransform>(),SanctuaryLobbyState.TeamNames.Select((n,i)=>new Option(n,()=>{if(SanctuaryLobbyState.IsHost)SanctuaryLobbyState.ApplyTeamPolicy((SanctuaryTeamPolicy)i);Invalidate();})).ToArray()));teamText=teamButton.GetComponentInChildren<TextMeshProUGUI>();
  Button(settingsContent,"PERSONAL SETTINGS",new Rect(8,78,w-16,30),ShowPersonal);
  Label(settingsContent,"LOBBY PRESETS",new Rect(8,115,w-16,23),18,Accent);
  Button choose=null;choose=Dropdown(settingsContent,"Choose layout",new Rect(8,144,w-16,31),()=>OpenChoices(choose.GetComponent<RectTransform>(),LobbyPresetRuntime.PresetEntries().Select(p=>new Option(p.Name+" ["+p.Mode+"]",()=>{selectedPreset=p.Number;presetName.SetTextWithoutNotify(p.Name);Invalidate();})).ToArray()));presetText=choose.GetComponentInChildren<TextMeshProUGUI>();
  float third=(w-32)/3;apply=Button(settingsContent,"LOAD",new Rect(8,183,third,30),()=>Message(LobbyPresetRuntime.ApplyPresetFromUi(selectedPreset)));update=Button(settingsContent,"SAVE",new Rect(16+third,183,third,30),()=>Message(LobbyPresetRuntime.UpdatePresetFromUi(selectedPreset)));delete=Button(settingsContent,"DELETE",new Rect(24+2*third,183,third,30),ConfirmDelete);
  presetName=Input(settingsContent,"New preset",new Rect(8,221,w*.48f-12,31));
  float actionWidth=(w*.52f-20)/2;save=Button(settingsContent,"SAVE AS",new Rect(w*.48f+4,221,actionWidth,31),()=>Message(LobbyPresetRuntime.CreatePresetFromUi(presetName.text)));rename=Button(settingsContent,"RENAME",new Rect(w*.48f+12+actionWidth,221,actionWidth,31),()=>Message(LobbyPresetRuntime.RenamePresetFromUi(selectedPreset,presetName.text)));
  messageText=Label(settingsContent,"",new Rect(8,260,w-16,42),12,Muted);messageText.enableWordWrapping=true;
  unitRestrictionsButton=Button(settingsContent,"Unit Restrictions",new Rect(8,310,w-16,32),OpenUnitRestrictions);
  unitRestrictionsReason=Label(settingsContent,"",new Rect(8,348,w-16,44),11,Muted);unitRestrictionsReason.enableWordWrapping=true;
 }
 static void OpenUnitRestrictions(){ClosePopup();string reason;if(!LobbyUnitRestrictions.Open(out reason)){unitRestrictionsReason.text=reason??"Unit Restrictions is unavailable.";Message(reason);return;}RestoreNativeLayout();}
 static void ChangeMode(SanctuaryLobbyMode mode){if(!SanctuaryLobbyState.SetMode(mode))Message("Mode change was not applied. Check the lobby warning.");Invalidate();}
 static void Message(string text){if(messageText!=null)messageText.text=text;Invalidate();}
 static void Invalidate(){rosterKey=null;nextRefresh=DateTime.MinValue;}
 static bool Refresh(){
  var state=LobbyManager.CurrentState;if(state==null)return false;
  string currentCatalog=LobbyFactionIcons.CatalogKey(SanctuaryLobbyState.Choices());if(currentCatalog!=catalogKey){catalogKey=currentCatalog;rosterKey=markerKey=null;}
  bool host=SanctuaryLobbyState.IsHost,editable=host&&SanctuaryLobbyState.CanEdit;
  changeMap.interactable=editable;modeButton.interactable=teamButton.interactable=editable;save.interactable=editable;apply.interactable=update.interactable=rename.interactable=delete.interactable=editable&&selectedPreset>0;
  unitRestrictionsButton.interactable=LobbyUnitRestrictions.CanOpen;unitRestrictionsReason.text=LobbyUnitRestrictions.Reason??"";
  modeText.text="Mode: "+(SanctuaryLobbyState.Mode==SanctuaryLobbyMode.Vanilla?"Vanilla Lobby":"Overhauled Lobby");teamText.text="Auto Teams: "+SanctuaryLobbyState.TeamNames[(int)SanctuaryLobbyState.TeamPolicy];
  var selected=LobbyPresetRuntime.PresetEntries().FirstOrDefault(p=>p.Number==selectedPreset);presetText.text=selected==null?"Choose layout":selected.Name;
  string reason=SanctuaryLobbyState.StartBlockReason();if(!string.IsNullOrEmpty(reason))messageText.text=reason;else if(!string.IsNullOrEmpty(shownStartReason)&&messageText.text==shownStartReason)messageText.text="";shownStartReason=reason;
  string currentMap=state.mapPath;if(currentMap!=mapKey){mapKey=currentMap;map=LobbyMapData.Read(AppDomain.CurrentDomain.BaseDirectory,currentMap);rosterKey=markerKey=null;}
  var info=(Component)nativeLobby.GetType().GetField("lobbyInfoPanel",SanctuaryLobbyState.InstanceAll).GetValue(nativeLobby);
  var nativeImage=info.GetType().GetField("mapImage",SanctuaryLobbyState.InstanceAll).GetValue(info) as Image;var sprite=nativeImage==null?null:nativeImage.sprite;
  mapPending.gameObject.SetActive(sprite==null);if(mapImage.sprite!=sprite){mapImage.sprite=sprite;mapImage.color=sprite==null?Color.clear:Color.white;if(sprite!=null){float[] fit=LobbyUiGeometry.Fit(mapRect.rect.width,mapRect.rect.height,sprite.rect.width/sprite.rect.height);Place(mapContent,new Rect(fit[0],fit[1],fit[2],fit[3]));}markerKey=null;}
  mapTitle.text=NativeText(info,"mapName");mapDetails.text=string.Join(" / ",new[]{NativeText(info,"hostName"),NativeText(info,"playersText"),NativeText(info,"observersText"),NativeText(info,"isLockedText"),NativeText(info,"versionText")}.Where(s=>!string.IsNullOrEmpty(s)).ToArray());
  string key=host+"|"+editable+"|"+SanctuaryLobbyState.CanEdit+"|"+LobbyManager.isLocked+"|"+SanctuaryLobbyState.SafeTypeChanges+"|"+SanctuaryLobbyState.Mode+"|"+SanctuaryLobbyState.TeamPolicy+"|"+state.mapPath+"|"+state.MaxPlayers+"|"+state.MaxObservers+"|"+state.ObserverCount+"|"+string.Join(";",state.GetPlayers(true,true).Select(p=>p.type+":"+p.id+":"+p.armyID+":"+p.faction+":"+p.team+":"+p.isReady+":"+p.name+":"+SanctuaryLobbyState.ColorIndex(SanctuaryLobbyState.Id(p),p.armyID)).ToArray());
  key+="|bulk:"+LobbyBulkAi.IsRunning+"|preset:"+LobbyPresetRuntime.IsApplying+"|ai:"+string.Join(";",state.GetPlayers(true,true).Where(p=>p.type==PlayerType.AI).Select(p=>p.armyID+":"+LobbyRosterNativeData.AiLabel(p)).ToArray());
  key+="|choice:"+SanctuaryLobbyState.ChoiceRevision+"|"+SanctuaryLobbyState.PresentationKey;
  // FullState may replace native AI objects without changing any displayed
  // values. Rebind their rows and dismiss stale menus; keep SelectAi's strict
  // reference check so a removed/replaced occupant is never edited by accident.
  var currentAiActors=state.GetPlayers(true,true).Where(p=>p.type==PlayerType.AI).OrderBy(p=>p.armyID).ToArray();bool actorsChanged=currentAiActors.Length!=shownAiActors.Length||Enumerable.Range(0,currentAiActors.Length).Any(i=>!ReferenceEquals(currentAiActors[i],shownAiActors[i]));
  bool changed=key!=rosterKey||actorsChanged;if(changed){rosterKey=key;shownAiActors=currentAiActors;ClosePopup();RebuildRoster(state);}
  string mk=state.mapPath+"|"+SanctuaryLobbyState.Mode+"|"+string.Join(";",state.GetPlayers(true,true).Select(p=>p.type+":"+p.id+":"+p.armyID+":"+p.faction+":"+SanctuaryLobbyState.ColorIndex(SanctuaryLobbyState.Id(p),p.armyID)).ToArray());
  mk+="|choice:"+SanctuaryLobbyState.ChoiceRevision+"|"+SanctuaryLobbyState.PresentationKey;
  if(mk!=markerKey){markerKey=mk;RebuildMarkers(state);changed=true;}shownChoicePresentation=ChoicePresentationKey();RefreshAvatars();RefreshPortraits();RefreshRules(state);fillAi.interactable=LobbyBulkAi.CanFill;removeAi.interactable=LobbyBulkAi.CanRemove;fillAi.gameObject.SetActive(host);removeAi.gameObject.SetActive(host);bulkStatus.text=LobbyBulkAi.Status??"";return changed;
 }
 static string ChoicePresentationKey(){return SanctuaryLobbyState.ChoiceRevision+"|"+SanctuaryLobbyState.PresentationKey;}
 static void RefreshChoicePresentation(){var state=LobbyManager.CurrentState;string key=ChoicePresentationKey();if(state==null||key==shownChoicePresentation)return;RebuildRoster(state);RebuildMarkers(state);RefreshAvatars();shownChoicePresentation=key;Canvas.ForceUpdateCanvases();}
 static void RefreshRules(LobbyState state){var snapshot=LobbyRulesAndMods.Capture(state);modsTabText.text="MODS ("+(snapshot.ModsAvailable?snapshot.SelectedModCount.ToString():"?")+")";var lines=rulesTab==1?snapshot.Observers:rulesTab==2?snapshot.Mods:snapshot.Rules;rulesText.text=string.Join(rulesTab==0?"    |    ":"\n",lines);float width=rulesTab==1?rulesCard.rect.width-258:rulesCard.rect.width-40;Place(rulesText.rectTransform,new Rect(0,0,width,84));float height=Mathf.Max(84,rulesText.preferredHeight+8);SetHeight(rulesContent,height);SetHeight(rulesText.rectTransform,height);observeButton.gameObject.SetActive(rulesTab==1);string reason;observeButton.interactable=SanctuaryLobbyState.CanChangeType(PlayerType.Observer,out reason)&&!LobbyBulkAi.IsRunning&&!LobbyPresetRuntime.IsApplying&&!LobbyManager.isLocked;}

 static void RebuildRoster(LobbyState state){
  avatars.Clear();
  rowPortraits.Clear();
  if(rosterRows!=null){rosterRows.SetActive(false);UnityEngine.Object.Destroy(rosterRows);}rosterRows=Element(rowContent,"Rows");Stretch(rosterRows.GetComponent<RectTransform>());
  float w=rowContent.rect.width;int row=0;for(int army=1;army<=state.MaxPlayers;army++)BuildRow(rosterRows.transform as RectTransform,state.GetPlayerAtSlot(army),army,row++*39,w);
  float undecidedY=row*39+12;var separator=Panel(rosterRows.transform,"Undecided separator",new Rect(0,undecidedY-7,w,1),Accent);separator.GetComponent<Image>().raycastTarget=false;
  var undecided=state.GetPlayers(true,true).Where(p=>p.type!=PlayerType.Empty&&p.type!=PlayerType.Observer&&p.armyID==0).ToArray();
  if(undecided.Length==0)BuildRow(rosterRows.transform as RectTransform,null,0,undecidedY,w);else for(int i=0;i<undecided.Length;i++)BuildRow(rosterRows.transform as RectTransform,undecided[i],0,undecidedY+i*39,w);
  SetHeight(rowContent,undecidedY+Math.Max(1,undecided.Length)*39);var scroll=rosterViewport.parent.GetComponent<ScrollRect>();scroll.verticalNormalizedPosition=scrollPosition;
 }
 static void BuildRow(RectTransform parent,LobbyPlayer p,int army,float y,float w){
  var row=Panel(parent,"Army "+army,new Rect(0,y,w,37),new Color(17/255f,30/255f,43/255f,.96f));bool occupied=p!=null&&p.type!=PlayerType.Empty;bool local=occupied&&LobbyManager.IsLocalPlayer(p);bool host=SanctuaryLobbyState.IsHost;bool editable=SanctuaryLobbyState.CanEdit&&!LobbyManager.isLocked&&!LobbyBulkAi.IsRunning&&!LobbyPresetRuntime.IsApplying;bool own=occupied&&((local&&p.type==PlayerType.Player)||(host&&p.type==PlayerType.AI));ulong id=occupied?SanctuaryLobbyState.Id(p):0;
  var slot=Button(row,army>0?army.ToString():"?",new Rect(5,3,42,32),null);slot.targetGraphic.color=occupied?SanctuaryLobbyState.ColorFor(id,army):Neutral;var slotColor=slot.targetGraphic.color;slot.GetComponentInChildren<TextMeshProUGUI>().color=LobbyUiGeometry.DarkLabel(slotColor.r,slotColor.g,slotColor.b)?new Color(.02f,.03f,.05f):Color.white;slot.interactable=army>0&&occupied&&own&&editable&&SanctuaryLobbyState.Mode==SanctuaryLobbyMode.Overhauled;slot.onClick.AddListener(()=>OpenChoices(slot.GetComponent<RectTransform>(),SanctuaryLobbyState.ColorsForArmy(army).Select(i=>new Option(SanctuaryLobbyState.ColorNames[i],()=>{SanctuaryLobbyState.SelectColor(army,i);Invalidate();},SanctuaryLobbyState.Palette[i])).ToArray()));
  var slotColors=slot.colors;slotColors.disabledColor=Color.white;slot.colors=slotColors;
  var name=Dropdown(row,occupied?PlayerDisplayName(p):army==0?"Undecided slot":"Open slot",new Rect(70,3,221,32),null);name.interactable=editable&&(!occupied||host&&p.type==PlayerType.AI||host&&p.IsHumanPlayer&&!local);
  if(occupied){AddAvatar(name.transform,p);var nameLabel=name.GetComponentInChildren<TextMeshProUGUI>();Place(nameLabel.rectTransform,new Rect(38,0,p.id==LobbyManager.CurrentState.hostID?138:158,32));if(p.id==LobbyManager.CurrentState.hostID)Glyph(name.transform,3,new Rect(178,6,20,20),Accent);}
  name.onClick.AddListener(()=>OpenPlayerChoices(name,p,army));
  var choice=occupied?SanctuaryLobbyState.PresentationChoice(p):null;var current=PresentationFaction(choice);var selectedCommander=PresentationCommander(choice);
  var faction=Dropdown(row,!occupied?"Random":choice==null?"Synchronizing...":choice.faction=="Random"?"Random":current==null?"Unavailable":current.FactionName,new Rect(315,3,161,32),null);faction.interactable=own&&editable&&choice!=null;SetButtonIcon(faction,LobbyFactionIcons.Get(current,nativeLobby));faction.onClick.AddListener(()=>OpenChoices(faction.GetComponent<RectTransform>(),FactionOptions(army)));
  var commander=Dropdown(row,!occupied?"Random":choice==null?"Synchronizing...":choice.faction=="Random"||choice.commander=="Random"?"Random":selectedCommander==null?"Unavailable":selectedCommander.CommanderName,new Rect(495,3,221,32),null);commander.interactable=own&&editable&&choice!=null;commander.onClick.AddListener(()=>OpenChoices(commander.GetComponent<RectTransform>(),CommanderOptions(choice,army)));
  BindPortrait(commander,selectedCommander,false,p);
  var team=Dropdown(row,occupied?p.team.ToString():"-",new Rect(743,3,84,32),null);team.interactable=own&&editable&&SanctuaryLobbyState.TeamPolicy==SanctuaryTeamPolicy.Off;team.onClick.AddListener(()=>OpenChoices(team.GetComponent<RectTransform>(),Enumerable.Range(1,Math.Max(2,LobbyManager.CurrentState.MaxPlayers)).Select(t=>new Option("Team "+t,()=>{SanctuaryLobbyState.SetTeam(army,t);Invalidate();})).ToArray()));
  if(occupied){var glyph=Glyph(row,0,new Rect(ReadyColumnCenter-11,8,22,22),p.type==PlayerType.AI||p.isReady?new Color(.35f,.85f,.61f):Muted);glyph.Ready=p.type==PlayerType.AI||p.isReady;}else Label(row,"-",new Rect(ReadyColumnCenter-ReadyColumnWidth/2,3,ReadyColumnWidth,32),16,Muted,TextAlignmentOptions.Center);
  if(!occupied){var join=Button(row,"JOIN",new Rect(932,3,w-940,32),()=>{if(army==0)SanctuaryLobbyState.BecomeUnassigned();else SanctuaryLobbyState.JoinSlot(army);Invalidate();});Glyph(join.transform,4,new Rect(8,8,16,16),Accent);join.interactable=editable;}
  else if(p.type==PlayerType.AI){var ai=Button(row,"AI SETTINGS",new Rect(932,3,w-940,32),()=>{ClosePopup();SanctuaryLobbyState.OpenAiSettings(p);});ai.GetComponentInChildren<TextMeshProUGUI>().fontSize=11;Place(ai.GetComponentInChildren<TextMeshProUGUI>().rectTransform,new Rect(23,0,w-965,32));Glyph(ai.transform,2,new Rect(5,8,16,16),Muted);ai.interactable=host&&editable;}
  else if(host&&p.type==PlayerType.Player&&!local&&army>0){var move=Button(row,"MOVE",new Rect(932,3,w-940,32),null);move.interactable=editable;move.onClick.AddListener(()=>OpenChoices(move.GetComponent<RectTransform>(),Enumerable.Range(1,LobbyManager.CurrentState.MaxPlayers).Where(a=>a!=army).Select(a=>new Option("Slot "+a+(SanctuaryLobbyState.PlayerType(SanctuaryLobbyState.PlayerAtSlot(a))==0?"":" (swap)"),()=>{SanctuaryLobbyState.Move(id,a);Invalidate();})).ToArray()));}
  else Label(row,"-",new Rect(932,3,w-940,32),16,Muted,TextAlignmentOptions.Center);
 }
 static SanctuaryLobbyState.FactionChoice PresentationFaction(OverhaulChoice choice){return choice==null||choice.faction=="Random"?null:SanctuaryLobbyState.Choices().FirstOrDefault(c=>c.FactionId==choice.faction);}
 static SanctuaryLobbyState.FactionChoice PresentationCommander(OverhaulChoice choice){return choice==null||choice.faction=="Random"||choice.commander=="Random"?null:SanctuaryLobbyState.Choices().FirstOrDefault(c=>c.FactionId==choice.faction&&c.Identity==choice.commander);}
 static void SelectLobbyChoice(int army,string faction,string commander){if(!SanctuaryLobbyState.SetLobbyChoice(army,faction,commander))Message("The selection could not be applied. Check the lobby warning.");Invalidate();}
 static Option[] FactionOptions(int army){var factions=SanctuaryLobbyState.Choices().GroupBy(f=>f.FactionId).Select(g=>g.First()).ToArray();return new[]{new Option("Random",()=>SelectLobbyChoice(army,"Random","Random"))}.Concat(factions.Select(f=>new Option(f.FactionName,()=>{var first=SanctuaryLobbyState.Choices().FirstOrDefault(c=>c.FactionId==f.FactionId);if(first!=null)SelectLobbyChoice(army,first.FactionId,first.Identity);},icon:LobbyFactionIcons.Get(f,nativeLobby)))).ToArray();}
 static Option[] CommanderOptions(OverhaulChoice current,int army){if(current==null)return new Option[0];var random=new Option("Random",()=>SelectLobbyChoice(army,current.faction,"Random"));if(current.faction=="Random")return new[]{random};var eligible=SanctuaryLobbyState.Choices().Where(f=>f.FactionId==current.faction).ToArray();return new[]{random}.Concat(eligible.Select(f=>new Option(f.CommanderName,()=>{var choice=SanctuaryLobbyState.Choices().FirstOrDefault(c=>c.FactionId==current.faction&&c.Identity==f.Identity);if(choice!=null)SelectLobbyChoice(army,choice.FactionId,choice.Identity);},portrait:f))).ToArray();}
 static string PlayerDisplayName(LobbyPlayer player){return player.type==PlayerType.AI?LobbyRosterNativeData.AiLabel(player):player.name;}
 static void OpenPlayerChoices(Button owner,LobbyPlayer player,int army){
  if(!SanctuaryLobbyState.CanEdit||LobbyManager.isLocked||LobbyBulkAi.IsRunning||LobbyPresetRuntime.IsApplying)return;var choices=new List<Option>();bool occupied=player!=null&&player.type!=PlayerType.Empty;
  if(!occupied){choices.Add(new Option("Join",()=>{if(army==0)SanctuaryLobbyState.BecomeUnassigned();else SanctuaryLobbyState.JoinSlot(army);Invalidate();}));if(army>0&&SanctuaryLobbyState.IsHost)choices.Add(new Option("Add AI",()=>{SanctuaryLobbyState.AddAi(army);Invalidate();}));}
  else if(SanctuaryLobbyState.IsHost&&player.type==PlayerType.AI){choices.AddRange(LobbyRosterNativeData.AiNames().Select(n=>new Option(n,()=>{LobbyRosterNativeData.SelectAi(player,n);Invalidate();})));choices.Add(new Option("Remove AI",()=>{SanctuaryLobbyState.RemoveAi(army);Invalidate();}));}
  else if(SanctuaryLobbyState.IsHost&&player.IsHumanPlayer&&!LobbyManager.IsLocalPlayer(player)){ulong id=SanctuaryLobbyState.Id(player);choices.Add(new Option("Kick player",()=>ShowPlayerActions(id)));if(army>0)choices.AddRange(Enumerable.Range(1,LobbyManager.CurrentState.MaxPlayers).Where(a=>a!=army).Select(a=>new Option("Move to slot "+a+(SanctuaryLobbyState.PlayerType(SanctuaryLobbyState.PlayerAtSlot(a))==0?"":" (swap)"),()=>{SanctuaryLobbyState.Move(id,a);Invalidate();})));}
  OpenChoices(owner.GetComponent<RectTransform>(),choices.ToArray());
 }

 static void AddAvatar(Transform parent,LobbyPlayer player){
  var holder=Element(parent,"Native player avatar").GetComponent<RectTransform>();Place(holder,new Rect(5,3,27,27));
  var raw=Element(holder,"Human avatar").AddComponent<RawImage>();Stretch(raw.rectTransform);raw.raycastTarget=false;raw.color=Color.gray;
  var image=Element(holder,"Native AI robot").AddComponent<Image>();Stretch(image.rectTransform);image.raycastTarget=false;image.preserveAspect=true;image.color=Color.clear;
  raw.gameObject.SetActive(player.type!=PlayerType.AI);image.gameObject.SetActive(player.type==PlayerType.AI);
  avatars.Add(new AvatarBinding{Id=SanctuaryLobbyState.Id(player),Army=player.armyID,Type=player.type,Holder=holder,Raw=raw,Image=image});
 }
 static void RefreshAvatars(){
  if(nativeLobby==null||avatars.Count==0)return;
  var rows=nativeLobby.GetComponentsInChildren<EM.UI.PlayerListItem>(true);
  foreach(var binding in avatars){
   if(binding.Type!=PlayerType.AI){var state=LobbyManager.CurrentState;var actor=state==null?null:state.GetPlayers(true,true).FirstOrDefault(p=>p.type==binding.Type&&SanctuaryLobbyState.Id(p)==binding.Id);var texture=LobbyRosterNativeData.PlayerAvatar(actor) as Texture2D;if(texture!=null){ApplyHumanAvatar(binding,texture,Color.white,new Rect(0,0,1,1));continue;}}
   foreach(var row in rows){var t=row.GetType();var playerField=t.GetField("player",SanctuaryLobbyState.InstanceAll);var player=playerField==null?null:playerField.GetValue(row) as LobbyPlayer;
    if(player==null||player.type!=binding.Type||(binding.Type==PlayerType.AI?player.armyID!=binding.Army:SanctuaryLobbyState.Id(player)!=binding.Id))continue;
    if(binding.Type==PlayerType.AI){var f=t.GetField("aiAvatarObj",SanctuaryLobbyState.InstanceAll);var go=f==null?null:f.GetValue(row) as GameObject;var source=go==null?null:go.GetComponentsInChildren<Image>(true).FirstOrDefault(i=>i.sprite!=null);if(source!=null){binding.Image.sprite=source.sprite;binding.Image.color=source.color;}}
    else{var f=t.GetField("avatarImage",SanctuaryLobbyState.InstanceAll);var source=f==null?null:f.GetValue(row) as RawImage;if(source!=null&&source.texture!=null)ApplyHumanAvatar(binding,source.texture,source.color,source.uvRect);}
    break;
   }
  }
 }
 static void ApplyHumanAvatar(AvatarBinding binding,Texture texture,Color color,Rect uv){binding.Raw.texture=texture;binding.Raw.color=color;binding.Raw.uvRect=uv;var fit=LobbyUiGeometry.Fit(binding.Holder.rect.width,binding.Holder.rect.height,(float)texture.width/texture.height);Place(binding.Raw.rectTransform,new Rect(fit[0],fit[1],fit[2],fit[3]));}
 static void BindPortrait(Button button,SanctuaryLobbyState.FactionChoice choice,bool menu,LobbyPlayer player=null){
  if(choice==null||choice.FactionId=="Random"||choice.Identity=="Random")return;
  var sprite=LobbyRosterNativeData.CommanderPortrait(choice);if(sprite!=null&&SetButtonPortrait(button,sprite))return;
  (menu?popupPortraits:rowPortraits).Add(new PortraitBinding{Button=button,Faction=choice.FactionId,Commander=choice.Identity,Player=player});
 }
 static void RefreshPortraits(){RefreshPortraitBindings(rowPortraits);RefreshPortraitBindings(popupPortraits);}
 static void RefreshPortraitBindings(List<PortraitBinding> bindings){
  for(int i=bindings.Count-1;i>=0;i--){var binding=bindings[i];if(binding.Button==null){bindings.RemoveAt(i);continue;}
   // Pending selected art must still match visible intent; never resolve Random
   // via the concrete native faction byte while awaiting late-mounted assets.
   if(binding.Player!=null){var intent=SanctuaryLobbyState.PresentationChoice(binding.Player);if(intent==null||intent.faction!=binding.Faction||intent.commander!=binding.Commander){bindings.RemoveAt(i);continue;}}
   var choice=SanctuaryLobbyState.Choices().FirstOrDefault(c=>c.FactionId==binding.Faction&&c.Identity==binding.Commander);if(choice==null){bindings.RemoveAt(i);continue;}
   var sprite=LobbyRosterNativeData.CommanderPortrait(choice);if(sprite==null||!SetButtonPortrait(binding.Button,sprite))continue;bindings.RemoveAt(i);
  }
 }

 static void RebuildMarkers(LobbyState state){
  if(markers!=null){markers.SetActive(false);UnityEngine.Object.Destroy(markers);}markers=Element(mapContent,"Synchronized spawn markers");Stretch(markers.GetComponent<RectTransform>());
  if(mapImage.sprite==null)return;
  if(map==null||map.Error!=null){Label(markers.transform,"Spawn locations unavailable",new Rect(8,8,250,24),12,Muted);return;}
  float pw=mapContent.rect.width,ph=mapContent.rect.height;
  foreach(var spawn in map.Spawns){var p=state.GetPlayerAtSlot(spawn.Army);bool occupied=p!=null&&p.type!=PlayerType.Empty;Color color=occupied?SanctuaryLobbyState.ColorFor(SanctuaryLobbyState.Id(p),spawn.Army):Neutral;
   // preview.png already contains numbered markers. Opaque neutral/occupied tiles
   // cover those pixels at coordinates transformed through the map PlayableArea.
   float size=27;var tile=Panel(markers.transform,"Spawn "+spawn.Army,new Rect(spawn.U*pw-size/2,spawn.V*ph-size/2,size,size),color);tile.GetComponent<Image>().raycastTarget=false;
   var intent=occupied?SanctuaryLobbyState.PresentationChoice(p):null;var icon=LobbyFactionIcons.Get(PresentationFaction(intent),nativeLobby);
   if(icon!=null){DrawMarkerArtwork(tile,icon,new Rect(3,3,size-6,size-6));var badge=Panel(tile,"Slot number",new Rect(size-12,size-12,12,12),new Color(.02f,.03f,.05f,1));badge.GetComponent<Image>().raycastTarget=false;Label(badge,spawn.Army.ToString(),new Rect(0,0,12,12),9,Color.white,TextAlignmentOptions.Center);}
   else Label(tile,spawn.Army.ToString(),new Rect(0,0,size,size),12,LobbyUiGeometry.DarkLabel(color.r,color.g,color.b)?new Color(.02f,.03f,.05f):Color.white,TextAlignmentOptions.Center);
  }
 }

 static void ShowPlayerActions(ulong id){var p=SanctuaryLobbyState.PlayerById(id) as LobbyPlayer;if(!SanctuaryLobbyState.IsHost||p==null||LobbyManager.IsLocalPlayer(p)||!p.IsHumanPlayer)return;var panel=Modal("PLAYER: "+p.name,400,150);Button(panel,"KICK",new Rect(18,75,174,34),()=>{SanctuaryLobbyState.Kick(id);CloseModal();Invalidate();});Button(panel,"CANCEL",new Rect(208,75,174,34),CloseModal);}
 static void ConfirmDelete(){var preset=LobbyPresetRuntime.PresetEntries().FirstOrDefault(entry=>entry.Number==selectedPreset);if(preset==null)return;int id=selectedPreset;var p=Modal("DELETE: "+preset.Name,440,160);Label(p,"Previous file is retained as presets.ini.bak",new Rect(18,45,404,26),13,Muted);Button(p,"DELETE",new Rect(18,94,196,34),()=>{Message(LobbyPresetRuntime.DeletePresetFromUi(id));selectedPreset=0;CloseModal();});Button(p,"CANCEL",new Rect(226,94,196,34),CloseModal);}
 static void ShowPersonal(){
  int favorite=PersonalPreferences.Favorite,second=PersonalPreferences.Second;string faction=PersonalPreferences.Faction,commander=PersonalPreferences.Commander;
  var p=Modal("PERSONAL SETTINGS",500,338);Button fav=null,sec=null,fac=null,cmd=null;
  Action redraw=()=>{fav.GetComponentInChildren<TextMeshProUGUI>().text="First favorite color: "+ColorName(favorite);sec.GetComponentInChildren<TextMeshProUGUI>().text="Second favorite color: "+ColorName(second);fac.GetComponentInChildren<TextMeshProUGUI>().text="Preferred faction: "+faction;cmd.GetComponentInChildren<TextMeshProUGUI>().text="Preferred commander: "+CommanderName(commander);};
  fav=Button(p,"",new Rect(18,55,464,36),()=>OpenChoices(fav.GetComponent<RectTransform>(),new[]{new Option("None / Off",()=>{favorite=-1;redraw();})}.Concat(Enumerable.Range(0,SanctuaryLobbyState.Palette.Length).Select(i=>new Option(SanctuaryLobbyState.ColorNames[i],()=>{favorite=i;if(second==i)second=-1;redraw();},SanctuaryLobbyState.Palette[i]))).ToArray()));
  sec=Button(p,"",new Rect(18,99,464,36),()=>OpenChoices(sec.GetComponent<RectTransform>(),new[]{new Option("None / Off",()=>{second=-1;redraw();})}.Concat(Enumerable.Range(0,SanctuaryLobbyState.Palette.Length).Where(i=>i!=favorite).Select(i=>new Option(SanctuaryLobbyState.ColorNames[i],()=>{second=i;redraw();},SanctuaryLobbyState.Palette[i]))).ToArray()));
  fac=Button(p,"",new Rect(18,143,464,36),()=>OpenChoices(fac.GetComponent<RectTransform>(),new[]{new Option("Random",()=>{faction="Random";redraw();})}.Concat(SanctuaryLobbyState.Choices().GroupBy(f=>f.FactionName).Select(g=>new Option(g.Key,()=>{faction=g.Key;if(!SanctuaryLobbyState.Choices().Any(c=>c.Identity==commander&&c.FactionName==faction))commander="Random";redraw();}))).ToArray()));
  cmd=Button(p,"",new Rect(18,187,464,36),()=>OpenChoices(cmd.GetComponent<RectTransform>(),new[]{new Option("Default",()=>{commander="Random";redraw();})}.Concat(SanctuaryLobbyState.Choices().Where(c=>faction=="Random"||c.FactionName==faction).Select(c=>new Option(c.FactionName+" / "+c.CommanderName,()=>{commander=c.Identity;redraw();}))).ToArray()));
  Button(p,"SAVE",new Rect(18,273,226,36),()=>{PersonalPreferences.Favorite=favorite;PersonalPreferences.Second=second;PersonalPreferences.Faction=faction;PersonalPreferences.Commander=commander;PersonalPreferences.Save();CloseModal();});Button(p,"CANCEL",new Rect(256,273,226,36),CloseModal);redraw();
 }
 static string ColorName(int i){return i<0||i>=SanctuaryLobbyState.ColorNames.Length?"None / Off":SanctuaryLobbyState.ColorNames[i];}
 static string CommanderName(string id){var c=SanctuaryLobbyState.Choices().FirstOrDefault(x=>x.Identity==id);return c==null?"Default":c.CommanderName;}
 static RectTransform Modal(string title,float w,float h){CloseModal();ClosePopup();modal=Element(rootRect,"Modal");Stretch(modal.GetComponent<RectTransform>());modal.AddComponent<Image>().color=new Color(0,0,0,.7f);var p=Panel(modal.transform,title,new Rect((rootRect.rect.width-w)/2,(rootRect.rect.height-h)/2,w,h),Ink);Label(p,title,new Rect(18,12,w-36,30),20,Accent);return p;}
 static void CloseModal(){ClosePopup();if(modal!=null){modal.SetActive(false);UnityEngine.Object.Destroy(modal);}modal=null;}
 static void ClosePopup(){popupPortraits.Clear();if(popup!=null){popup.SetActive(false);UnityEngine.Object.Destroy(popup);}popup=null;}
 static void OpenChoices(RectTransform owner,Option[] options){
  ClosePopup();if(options.Length==0)return;var corners=new Vector3[4];owner.GetWorldCorners(corners);Vector2 point;RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect,RectTransformUtility.WorldToScreenPoint(null,corners[0]),null,out point);float x=point.x+rootRect.rect.width/2,y=rootRect.rect.height/2-point.y+2,w=Mathf.Clamp(owner.rect.width,160,460),available=rootRect.rect.height-y-14;
  if(available<60){Message("Scroll the control higher to open its menu.");return;}
  popup=Element(rootRect,"Dropdown dismiss layer");Stretch(popup.GetComponent<RectTransform>());popup.AddComponent<Image>().color=Color.clear;var dismiss=popup.AddComponent<Button>();dismiss.onClick.AddListener(ClosePopup);
  var box=Panel(popup.transform,"Dropdown",new Rect(Mathf.Clamp(x,10,rootRect.rect.width-w-10),y,w,Mathf.Min(250,Mathf.Min(available,options.Length*32+8))),Ink);RectTransform viewport;bool overflow=options.Length*32>box.rect.height-8;var content=Scroll(box,new Rect(4,4,w-8,box.rect.height-8),out viewport,overflow);SetHeight(content,options.Length*32);
  for(int i=0;i<options.Length;i++){Option option=options[i];var b=Button(content,option.Text,new Rect(0,i*32,content.rect.width-6,30),()=>{ClosePopup();option.Select();});if(option.Portrait!=null)BindPortrait(b,option.Portrait,true);else if(option.Icon!=null)SetButtonIcon(b,option.Icon);if(option.Color.HasValue){var swatch=Panel(b.transform,"Swatch",new Rect(3,5,20,20),option.Color.Value);swatch.GetComponent<Image>().raycastTarget=false;}}
 }

 static void HideNativePanels(){foreach(string field in new[]{"playerScrollRect","lobbyInfoPanel"}){var component=nativeLobby.GetType().GetField(field,SanctuaryLobbyState.InstanceAll).GetValue(nativeLobby) as Component;if(component==null)throw new MissingFieldException(field);var group=component.GetComponent<CanvasGroup>();bool added=group==null;if(added)group=component.gameObject.AddComponent<CanvasGroup>();hidden.Add(new HiddenGroup{Group=group,Added=added,Alpha=group.alpha,Interactable=group.interactable,Blocks=group.blocksRaycasts});group.alpha=0;group.interactable=false;group.blocksRaycasts=false;}SuppressChildGroups();HideNativeDecoration();}
 static bool SetButtonPortrait(Button button,Sprite sprite){
  if(button==null||sprite==null)return false;
  try{
   var texture=sprite.texture;if(texture==null)return false;var buttonRect=button.GetComponent<RectTransform>();float h=buttonRect.rect.height,size=h-6;Rect uv,draw;
   if(!LobbyPortraitGeometry.TryFit(sprite.textureRect,texture.width,texture.height,size,size,out uv,out draw))return false;
   // Native art stays owned by the game: no texture readback, copy, mutation or
   // destruction. RawImage accepts signed UVs without negative geometry.
   var existing=button.transform.Find("Commander portrait");var holder=existing==null?Element(button.transform,"Commander portrait").GetComponent<RectTransform>():existing as RectTransform;
   Place(holder,new Rect(5+draw.x,3+draw.y,draw.width,draw.height));var raw=holder.GetComponent<RawImage>()??holder.gameObject.AddComponent<RawImage>();
   raw.texture=texture;raw.uvRect=uv;raw.color=Color.white;raw.raycastTarget=false;
   if(portraitDiagnostics.Count<32&&portraitDiagnostics.Add(sprite.GetInstanceID())&&SanctuaryOverhauledLobbyPlugin.Log!=null)SanctuaryOverhauledLobbyPlugin.Log.LogInfo("Commander portrait bound: texture="+texture.name+", nativeRect="+sprite.textureRect+", draw="+draw+", signedUV="+uv+". Rendering still requires visual acceptance.");
   var label=button.GetComponentInChildren<TextMeshProUGUI>();Place(label.rectTransform,new Rect(h+3,0,buttonRect.rect.width-h-(button.transform.Find("Dropdown chevron")!=null?27:9),h));return true;
  }catch{return false;}
 }
 static void DrawIcon(Transform parent,Sprite icon,Rect rect){var go=Element(parent,"Faction icon");Place(go.GetComponent<RectTransform>(),rect);var image=go.AddComponent<Image>();image.sprite=icon;image.color=Color.white;image.preserveAspect=true;image.raycastTarget=false;}
 static Rect ArtworkBounds(Sprite icon){
  Rect bounds;if(artworkBounds.TryGetValue(icon,out bounds))return bounds;bounds=new Rect(0,0,1,1);
  try{
   // Tight sprite meshes provide a read-only fallback for non-readable textures.
   var vertices=icon.vertices;if(vertices!=null&&vertices.Length>0){float unit=icon.pixelsPerUnit;float x=vertices.Min(v=>v.x)*unit+icon.pivot.x,y=vertices.Min(v=>v.y)*unit+icon.pivot.y,w=(vertices.Max(v=>v.x)-vertices.Min(v=>v.x))*unit,h=(vertices.Max(v=>v.y)-vertices.Min(v=>v.y))*unit;if(w>0&&h>0)bounds=new Rect(x/icon.rect.width,y/icon.rect.height,w/icon.rect.width,h/icon.rect.height);}
   if(icon.texture!=null&&(!icon.packed||icon.packingRotation==SpritePackingRotation.None)){
    var tr=icon.textureRect;int w=(int)tr.width,h=(int)tr.height;
    if(w>0&&h>0&&(long)w*h<=4194304){var pixels=SpritePixels(icon.texture,tr);int xmin=w,ymin=h,xmax=-1,ymax=-1;for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(pixels[y*w+x].a>8/255f){xmin=Math.Min(xmin,x);ymin=Math.Min(ymin,y);xmax=Math.Max(xmax,x);ymax=Math.Max(ymax,y);}if(xmax>=xmin&&ymax>=ymin){var offset=icon.textureRectOffset;bounds=new Rect((offset.x+xmin)/icon.rect.width,(offset.y+ymin)/icon.rect.height,(xmax-xmin+1)/icon.rect.width,(ymax-ymin+1)/icon.rect.height);}}
   }
  }catch{ /* Packed/non-readable sprites keep their verified mesh bounds. */ }
  if(new[]{bounds.x,bounds.y,bounds.width,bounds.height}.Any(v=>float.IsNaN(v)||float.IsInfinity(v))||bounds.width<=0||bounds.height<=0||bounds.x<-.001f||bounds.y<-.001f||bounds.xMax>1.001f||bounds.yMax>1.001f)bounds=new Rect(0,0,1,1);artworkBounds[icon]=bounds;return bounds;
 }
 static Color[] SpritePixels(Texture2D texture,Rect region){
  int w=(int)region.width,h=(int)region.height;if(texture.isReadable)return texture.GetPixels((int)region.x,(int)region.y,w,h);
  // Stock icon textures are non-readable. Read a temporary copy once per Sprite;
  // neither the original texture nor sprite is modified or replaced.
  RenderTexture previous=RenderTexture.active,temporary=null;Texture2D readable=null;
  try{if((long)texture.width*texture.height>4194304)throw new InvalidOperationException("Icon atlas exceeds readback budget.");temporary=RenderTexture.GetTemporary(texture.width,texture.height,0,RenderTextureFormat.ARGB32);Graphics.Blit(texture,temporary);RenderTexture.active=temporary;readable=new Texture2D(w,h,TextureFormat.RGBA32,false);readable.ReadPixels(region,0,0,false);return readable.GetPixels();}
  finally{RenderTexture.active=previous;if(temporary!=null)RenderTexture.ReleaseTemporary(temporary);if(readable!=null)UnityEngine.Object.Destroy(readable);}
 }
 static void DrawMarkerArtwork(Transform parent,Sprite icon,Rect box){var b=ArtworkBounds(icon);var r=LobbyUiGeometry.CenterArtwork(box.x,box.y,box.width,box.height,icon.rect.width,icon.rect.height,b.x,b.y,b.width,b.height);DrawIcon(parent,icon,new Rect(r[0],r[1],r[2],r[3]));}
 static void SetButtonIcon(Button button,Sprite icon){if(icon==null)return;float h=button.GetComponent<RectTransform>().rect.height;var holder=Panel(button.transform,"Inset icon",new Rect(5,3,h-6,h-6),Color.clear);holder.GetComponent<Image>().raycastTarget=false;holder.gameObject.AddComponent<RectMask2D>();DrawIcon(holder,icon,new Rect(0,0,h-6,h-6));var label=button.GetComponentInChildren<TextMeshProUGUI>();Place(label.rectTransform,new Rect(h+3,0,button.GetComponent<RectTransform>().rect.width-h-(button.transform.Find("Dropdown chevron")!=null?27:9),h));}
 static void HideNativeDecoration(){
  if(nativeLobby==null)return;var keep=new List<Transform>();
  foreach(string field in new[]{"chatOutput","chatInput","chatOutputText","lobbyNameText","lobbyNameTransition","leaveButton","settingsButton","readyButton","notReadyButton","hostStartButton","hostReadyButton","hostNotReadyButton"}){
   var f=nativeLobby.GetType().GetField(field,SanctuaryLobbyState.InstanceAll);var c=f==null?null:f.GetValue(nativeLobby) as Component;if(c!=null)keep.Add(c.transform);
   var scroll=c as ScrollRect;if(scroll!=null){if(scroll.verticalScrollbar!=null)keep.Add(scroll.verticalScrollbar.transform);if(scroll.horizontalScrollbar!=null)keep.Add(scroll.horizontalScrollbar.transform);}
   if(field=="settingsButton"&&c!=null){var mods=c.transform.parent.Find("Mods Button");if(mods!=null)keep.Add(mods);}
  }
  var panel=nativeLobby.transform.Find("Mods Lobby Panel");if(panel!=null)keep.Add(panel);
  foreach(var graphic in nativeLobby.GetComponentsInChildren<Graphic>(true)){
   if(keep.Any(t=>graphic.transform==t||graphic.transform.IsChildOf(t)))continue;
   if(!hiddenGraphics.ContainsKey(graphic))hiddenGraphics.Add(graphic,graphic.enabled);graphic.enabled=false;
  }
 }
 static void SuppressChildGroups(){foreach(var parent in hidden){if(parent.Group==null)continue;foreach(var child in parent.Group.GetComponentsInChildren<CanvasGroup>(true)){if(child==parent.Group)continue;if(!ignoredGroups.ContainsKey(child))ignoredGroups.Add(child,child.ignoreParentGroups);child.ignoreParentGroups=false;}}}
 static void RestoreSafely(Action action){try{action();}catch(Exception ex){Log("Native layout restoration continued after: "+SanctuaryLobbyState.Root(ex));}}
 public static void RestoreNativeLayout(){
  RestoreSafely(CloseModal);
  rowPortraits.Clear();popupPortraits.Clear();portraitDiagnostics.Clear();
  foreach(var button in nativeButtonLayouts)RestoreSafely(button.Restore);nativeButtonLayouts.Clear();
  foreach(var text in nativeChatTexts)RestoreSafely(text.Restore);nativeChatTexts.Clear();
  avatars.Clear();
  shownAiActors=new LobbyPlayer[0];
  foreach(var placement in nativePlacements.OrderBy(p=>p.Sibling))RestoreSafely(()=>placement.Restore());
  nativePlacements.Clear();originalPlacements.Clear();
  foreach(var item in hiddenGraphics)RestoreSafely(()=>{if(item.Key!=null)item.Key.enabled=item.Value;});hiddenGraphics.Clear();
  foreach(var item in ignoredGroups)RestoreSafely(()=>{if(item.Key!=null)item.Key.ignoreParentGroups=item.Value;});ignoredGroups.Clear();
  foreach(var item in hidden)RestoreSafely(()=>item.Restore());hidden.Clear();
  RestoreSafely(()=>{if(root!=null){root.SetActive(false);UnityEngine.Object.Destroy(root);}});
  root=null;rootRect=null;nativeLobby=null;nativeFade=null;overlayFade=null;committed=false;rosterKey=markerKey=mapKey=null;diagnostics=0;
 }
 static void ValidateRenderState(){if(root==null||!root.activeInHierarchy||canvas==null||!canvas.enabled||EventSystem.current==null)throw new InvalidOperationException("UGUI root is inactive or cannot receive input.");if(mapImage==null||!mapImage.enabled)throw new InvalidOperationException("Map preview control is unavailable.");Rect screen=new Rect(0,0,Screen.width,Screen.height);foreach(var t in new[]{rosterViewport,mapRect,modeText.rectTransform,presetText.rectTransform}){Rect r=ScreenBounds(t);if(!screen.Overlaps(r)||r.xMin<0||r.yMin<0||r.xMax>Screen.width+1||r.yMax>Screen.height+1)throw new InvalidOperationException("Control is outside screen: "+PathOf(t)+" "+r);}}
 static void LogDiagnostics(){var graphics=root.GetComponentsInChildren<Graphic>();int culled=graphics.Count(g=>g.canvasRenderer.cull);Log("UGUI visibility sample "+diagnostics+": frame="+Time.frameCount+", render="+canvas.renderMode+", order="+canvas.sortingOrder+", active="+root.activeInHierarchy+", graphics="+graphics.Length+", culled="+culled+", map="+ScreenBounds(mapRect)+", sprite="+(mapImage.sprite==null?"pending":mapImage.sprite.name)+", alpha="+mapImage.canvasRenderer.GetInheritedAlpha()+", roster="+ScreenBounds(rosterViewport)+", mode="+ScreenBounds(modeText.rectTransform)+", preset="+ScreenBounds(presetText.rectTransform)+". This records render state, not screenshot acceptance.");}
 static Rect ScreenBounds(RectTransform t){var c=new Vector3[4];t.GetWorldCorners(c);return Rect.MinMaxRect(c[0].x,Screen.height-c[2].y,c[2].x,Screen.height-c[0].y);}
 static string PathOf(Transform t){string p=t.name;for(int i=0;t.parent!=null&&i<6;i++){t=t.parent;p=t.name+"/"+p;}return p;}
 static void Log(string text){if(SanctuaryOverhauledLobbyPlugin.Log!=null)SanctuaryOverhauledLobbyPlugin.Log.LogInfo(text);}

 static GameObject Element(Transform parent,string name){var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return go;}
 static void Place(RectTransform t,Rect r){t.anchorMin=t.anchorMax=t.pivot=new Vector2(0,1);t.anchoredPosition=new Vector2(r.x,-r.y);t.sizeDelta=new Vector2(r.width,r.height);}
 static void Stretch(RectTransform t){t.anchorMin=Vector2.zero;t.anchorMax=Vector2.one;t.offsetMin=t.offsetMax=Vector2.zero;}
 static void SetHeight(RectTransform t,float h){t.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,h);}
 static RectTransform Panel(Transform parent,string name,Rect r,Color color){var go=Element(parent,name);var t=go.GetComponent<RectTransform>();Place(t,r);go.AddComponent<Image>().color=color;if(color.a>.1f&&color!=Color.white)ThinBorder(t);return t;}
 static void ThinBorder(RectTransform parent){
  foreach(var edge in new[]{new Vector4(0,0,1,0),new Vector4(0,1,1,1),new Vector4(0,0,0,1),new Vector4(1,0,1,1)}){
   var go=Element(parent,"Thin outline");var rect=go.GetComponent<RectTransform>();rect.anchorMin=new Vector2(edge.x,edge.y);rect.anchorMax=new Vector2(edge.z,edge.w);rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=Vector2.zero;rect.sizeDelta=new Vector2(edge.x==edge.z?1:0,edge.y==edge.w?1:0);var image=go.AddComponent<Image>();image.color=Border;image.raycastTarget=false;
  }
 }
 static TextMeshProUGUI Label(Transform parent,string text,Rect rect,float size,Color color,TextAlignmentOptions alignment=TextAlignmentOptions.MidlineLeft){var go=Element(parent,"Label");Place(go.GetComponent<RectTransform>(),rect);var label=go.AddComponent<TextMeshProUGUI>();label.font=font;label.fontSize=size;label.color=color;label.alignment=alignment;label.richText=false;label.text=text;label.raycastTarget=false;label.enableWordWrapping=false;label.overflowMode=TextOverflowModes.Ellipsis;return label;}
 static LobbyGlyph Glyph(Transform parent,int kind,Rect rect,Color color){var glyph=Element(parent,kind==1?"Dropdown chevron":"UI symbol").AddComponent<LobbyGlyph>();Place(glyph.rectTransform,rect);glyph.Kind=kind;glyph.color=color;glyph.raycastTarget=false;return glyph;}
 static Button Dropdown(Transform parent,string text,Rect rect,Action action){var button=Button(parent,text,rect,action);var label=button.GetComponentInChildren<TextMeshProUGUI>();label.alignment=TextAlignmentOptions.MidlineLeft;Place(label.rectTransform,new Rect(10,0,rect.width-34,rect.height));Glyph(button.transform,1,new Rect(rect.width-26,(rect.height-20)/2,20,20),Muted);return button;}
 static Button Button(Transform parent,string text,Rect rect,Action action){var p=Panel(parent,text,rect,new Color(13/255f,27/255f,40/255f,1));var b=p.gameObject.AddComponent<Button>();b.targetGraphic=p.GetComponent<Image>();var colors=b.colors;colors.highlightedColor=new Color(.7f,.86f,1,1);colors.pressedColor=new Color(.5f,.7f,.9f,1);b.colors=colors;Label(p,text,new Rect(6,0,rect.width-12,rect.height),14,Color.white,TextAlignmentOptions.Center);if(action!=null)b.onClick.AddListener(()=>{try{action();}catch(Exception ex){Message(SanctuaryLobbyState.Root(ex));}});return b;}
 static TMP_InputField Input(Transform parent,string text,Rect rect){var p=Panel(parent,"Preset name",rect,new Color(.08f,.13f,.18f,1));var input=p.gameObject.AddComponent<TMP_InputField>();var label=Label(p,text,new Rect(8,0,rect.width-16,rect.height),14,Color.white);input.textViewport=p;input.textComponent=label;input.characterLimit=70;input.text=text;return input;}
 static RectTransform Scroll(Transform parent,Rect rect,out RectTransform viewport,bool visibleBar=false){var go=Element(parent,"Scroll");Place(go.GetComponent<RectTransform>(),rect);var scroll=go.AddComponent<ScrollRect>();float width=rect.width-(visibleBar?20:0);viewport=Panel(go.transform,"Viewport",new Rect(0,0,width,rect.height),Color.clear);viewport.gameObject.AddComponent<RectMask2D>();var content=Element(viewport,"Content").GetComponent<RectTransform>();Place(content,new Rect(0,0,width,rect.height));scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=25;
  if(visibleBar){
   var track=Panel(go.transform,"Vertical scrollbar",new Rect(rect.width-16,16,12,Mathf.Max(12,rect.height-32)),new Color(.04f,.11f,.16f,1));var bar=track.gameObject.AddComponent<Scrollbar>();bar.direction=Scrollbar.Direction.BottomToTop;
   var sliding=Element(track,"Sliding area").GetComponent<RectTransform>();Stretch(sliding);sliding.offsetMin=new Vector2(2,2);sliding.offsetMax=new Vector2(-2,-2);var thumb=Panel(sliding,"Thumb",new Rect(0,0,8,20),Accent);Stretch(thumb);bar.handleRect=thumb;bar.targetGraphic=thumb.GetComponent<Image>();scroll.verticalScrollbar=bar;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.Permanent;
   var up=Button(go.transform,"",new Rect(rect.width-18,0,16,15),()=>ScrollRows(scroll,32));var upGlyph=Glyph(up.transform,1,new Rect(2,2,12,11),Accent);upGlyph.Up=true;
   var down=Button(go.transform,"",new Rect(rect.width-18,rect.height-15,16,15),()=>ScrollRows(scroll,-32));Glyph(down.transform,1,new Rect(2,2,12,11),Accent);
   MatchChatScrollbar(track,thumb,up,down);
  }return content;
 }
 static void ScrollRows(ScrollRect scroll,float pixels){float overflow=scroll.content.rect.height-scroll.viewport.rect.height;if(overflow>0){scroll.StopMovement();scroll.verticalNormalizedPosition=Mathf.Clamp01(scroll.verticalNormalizedPosition+pixels/overflow);}}
 static void MatchChatScrollbar(RectTransform track,RectTransform thumb,Button up,Button down){
  // Copy appearance only: native Beam scrollbar scripts own unrelated fades/input.
  var chat=NativeField("chatOutput") as ScrollRect;var source=chat==null?null:chat.verticalScrollbar;if(source==null)return;
  CopyImageStyle(source.handleRect==null?null:source.handleRect.GetComponent<Image>(),thumb.GetComponent<Image>());
  var background=source.transform.Find("Content/Background");if(background!=null){var line=Panel(track,"Native-style track",new Rect(5.5f,0,1,track.rect.height),Color.clear);line.GetComponent<Image>().raycastTarget=false;CopyImageStyle(background.GetComponent<Image>(),line.GetComponent<Image>());line.SetAsFirstSibling();}
  MatchChatArrow(source.transform.Find("Top Arrow/Icon"),up);MatchChatArrow(source.transform.Find("Bottom Arrow/Icon"),down);
 }
 static void CopyImageStyle(Image source,Image target){if(source==null||target==null)return;target.sprite=source.sprite;target.type=source.type;target.color=source.color;target.preserveAspect=source.preserveAspect;}
 static void MatchChatArrow(Transform source,Button button){var original=source==null?null:source.GetComponent<Image>();if(original==null||original.sprite==null)return;var glyph=button.GetComponentInChildren<LobbyGlyph>();if(glyph!=null)glyph.enabled=false;var icon=Element(button.transform,"Native-style arrow");Place(icon.GetComponent<RectTransform>(),new Rect(2,3,12,8));var image=icon.AddComponent<Image>();CopyImageStyle(original,image);image.raycastTarget=false;}
}
