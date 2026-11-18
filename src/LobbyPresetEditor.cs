using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public static partial class LobbyPresetRuntime {
 static GameObject editor;
 static UnityEngine.UI.Button updateButton;
 static PresetSelection updateSelection;
 static DateTime nextUpdateAvailability;
 // Mod Manager 0.7 hot-loads UI DLLs from bytes, so Assembly.Location is not
 // a usable filesystem path. Resolve the data file from the game root instead.
 static string PresetPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"SanctuaryMods","SanctuaryOverhauledLobby","presets.ini"); } }
 static void CloseEditor() { if(editor!=null) UnityEngine.Object.Destroy(editor); editor=null;updateButton=null;updateSelection=null; }
 static RectTransform Rect(GameObject go,Vector2 anchor,Vector2 size,Vector2 position) {
  var r=go.GetComponent<RectTransform>(); r.anchorMin=r.anchorMax=anchor;r.pivot=new Vector2(.5f,.5f);r.sizeDelta=size;r.anchoredPosition=position;return r;
 }
 static GameObject Element(string name,Transform parent) {
  var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return go;
 }
 static TextMeshProUGUI Label(Transform parent,string text,Vector2 size,Vector2 pos) {
  var go=Element("Label",parent);Rect(go,new Vector2(.5f,.5f),size,pos);
  var label=go.AddComponent<TextMeshProUGUI>();
  var header=(TextMeshProUGUI)selectorDropdown.GetType().GetField("headerText",InstanceAll).GetValue(selectorDropdown);
  label.font=header.font;label.fontSize=20;label.color=new Color(.2f,.7f,1);label.alignment=TextAlignmentOptions.Center;label.text=text;label.raycastTarget=false;
  return label;
 }
 static UnityEngine.UI.Button Button(Transform parent,string caption,float x,float y,Action action) {
  var go=Element(caption,parent);Rect(go,new Vector2(.5f,.5f),new Vector2(180,44),new Vector2(x,y));
  go.AddComponent<Image>().color=new Color(.1f,.22f,.32f,1);
  var button=go.AddComponent<UnityEngine.UI.Button>();button.onClick.AddListener(()=>{try {action();}catch(Exception ex){Chat(RootMessage(ex));}});
  Label(go.transform,caption,new Vector2(172,40),Vector2.zero);return button;
 }
 static Transform Dialog(string title) {
  CloseEditor();
  var canvas=((GameObject)selectorObject).GetComponentInParent<Canvas>().rootCanvas;
  editor=Element("Lobby preset dialog",canvas.transform);
  var r=editor.GetComponent<RectTransform>();r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
  editor.AddComponent<Image>().color=new Color(0,0,0,.65f);
  var panel=Element("Panel",editor.transform);Rect(panel,new Vector2(.5f,.5f),new Vector2(560,230),Vector2.zero);
  panel.AddComponent<Image>().color=new Color(.06f,.11f,.16f,1);
  Label(panel.transform,title,new Vector2(520,70),new Vector2(0,65));
  return panel.transform;
 }
 static TMP_InputField NameInput(Transform parent,string initial) {
  var go=Element("Preset name",parent);Rect(go,new Vector2(.5f,.5f),new Vector2(480,44),new Vector2(0,5));
  go.AddComponent<Image>().color=new Color(.12f,.2f,.26f,1);
  var field=go.AddComponent<TMP_InputField>();
  var text=Label(go.transform,"",new Vector2(460,40),Vector2.zero);text.alignment=TextAlignmentOptions.MidlineLeft;
  field.textViewport=go.GetComponent<RectTransform>();field.textComponent=text;field.characterLimit=70;field.text=initial;field.ActivateInputField();return field;
 }
 static void RefreshPresets() {
  CloseEditor(); if(selectorObject!=null) UnityEngine.Object.Destroy((GameObject)selectorObject);
  selectorObject=null;selectorDropdown=null;selectorPresetNumbers.Clear();
 }
 static string ValidName(string name,System.Collections.Generic.List<Preset> presets,int ignore) {
  name=name.Trim();
  if(name.Length==0 || name.IndexOfAny(new[]{'[',']','\r','\n'})>=0) throw new InvalidDataException("Enter a name without brackets or newlines.");
  if(presets.Any(p=>p.Number!=ignore && string.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("That preset name already exists.");
  return name;
 }
 static void ShowCreate() {
  if(!InLobby() || !IsHost() || pending!=null)return;
  var snapshot=CaptureCurrentLayout();
  var panel=Dialog("SAVE CURRENT LOBBY AS A PRESET");var input=NameInput(panel,"New preset");
  Button(panel,"SAVE",-105,-70,()=>{
   if(!CanEditPresets())return;
   var presets=LoadPresets();snapshot.Number=1;while(presets.Any(p=>p.Number==snapshot.Number))snapshot.Number++;
   snapshot.Name=ValidName(input.text,presets,0);presets.Add(snapshot);WritePresets(presets);Chat("Saved '"+snapshot.Name+"'.");RefreshPresets();
  });
  Button(panel,"CANCEL",105,-70,CloseEditor);
 }
 static void ShowRename(int number) {
  var preset=LoadPresets().First(p=>p.Number==number);var panel=Dialog("RENAME PRESET");var input=NameInput(panel,preset.Name);
  Button(panel,"SAVE",-105,-70,()=>{var presets=LoadPresets();var p=presets.First(x=>x.Number==number);p.Name=ValidName(input.text,presets,number);WritePresets(presets);RefreshPresets();});
  Button(panel,"CANCEL",105,-70,CloseEditor);
 }
 static void ShowDelete(int number) {
  var preset=LoadPresets().First(p=>p.Number==number);var panel=Dialog("ARE YOU SURE?\nDelete '"+preset.Name+"'?");
  Button(panel,"DELETE",-105,-70,()=>{var presets=LoadPresets();presets.RemoveAll(p=>p.Number==number);WritePresets(presets);Chat("Preset deleted. Previous configuration retained in presets.ini.bak.");RefreshPresets();});
  Button(panel,"CANCEL",105,-70,CloseEditor);
 }
 static void ShowContext(int number) {
  if(!CanEditPresets())return;
  var selection=SelectPreset(number);var panel=Dialog(selection.Name);
  Button(panel,"RENAME",-105,0,()=>ShowRename(number));
  Button(panel,"DELETE",105,0,()=>ShowDelete(number));
  updateSelection=selection;
  updateButton=Button(panel,"UPDATE",-105,-70,()=>{
   bool changed=UpdatePreset(selection);
   Chat(changed?"Updated '"+selection.Name+"' from the current lobby. Previous configuration retained in presets.ini.bak.":"'"+selection.Name+"' already matches the current lobby.");
   RefreshPresets();
  });
  updateButton.interactable=CanUpdatePreset(selection);
  Button(panel,"CANCEL",105,-70,CloseEditor);
 }
 static void RefreshUpdateAvailability() {
  if(updateButton==null || DateTime.UtcNow<nextUpdateAvailability)return;
  nextUpdateAvailability=DateTime.UtcNow.AddMilliseconds(500);
  updateButton.interactable=CanUpdatePreset(updateSelection);
 }
 static void AttachPresetContextMenus() {
  var items=(IList)selectorDropdown.GetType().GetField("items",InstanceAll).GetValue(selectorDropdown);
  for(int i=1;i<items.Count-1;i++) {
   int number=selectorPresetNumbers[i-1];var item=items[i];
   var row=(Component)item.GetType().GetField("itemButton",InstanceAll).GetValue(item);
   if(row==null)throw new InvalidOperationException("Preset row button was not created.");
   var trigger=row.gameObject.GetComponent<EventTrigger>()??row.gameObject.AddComponent<EventTrigger>();
   var entry=new EventTrigger.Entry {eventID=EventTriggerType.PointerClick};
   entry.callback.AddListener(data=>{var pointer=data as PointerEventData;if(pointer!=null && pointer.button==PointerEventData.InputButton.Right){pointer.Use();ShowContext(number);}});
   trigger.triggers.Add(entry);
  }
 }
}

