using HarmonyLib;
using Unity.Collections;

[HarmonyPatch(typeof(EM.UI.LobbyInterface),"UpdateData",new[]{typeof(EM.Network.Lobby.LobbyState)})]
static class OverhaulUiDataPatch {
 // Native UpdateData completes before entry returns and before the first
 // visible render. TickCanvas owns its reentrancy and lifecycle guards.
 static void Postfix(){SanctuaryLobbyUi.TickCanvas();}
}

[HarmonyPatch(typeof(EM.Network.LobbyManager),"HandleOnHost")]
static class OverhaulHostMessagePatch {
 static bool Prefix(EM.Network.PlayerID __0,NativeArray<byte> __1){return SanctuaryLobbyState.OnHostMessage(__0,__1);}
}

[HarmonyPatch(typeof(EM.Network.LobbyManager),"HandleOnClient")]
static class OverhaulClientMessagePatch {
 static bool Prefix(NativeArray<byte> __0){return SanctuaryLobbyState.OnClientMessage(__0);}
}

[HarmonyPatch(typeof(EM.Network.LobbyManager),"CanStartGame")]
static class OverhaulCanStartPatch {
 static void Postfix(ref bool __result){__result=SanctuaryProtocolRules.StartAllowed(__result,SanctuaryLobbyState.StartBlockReason());}
}

[HarmonyPatch(typeof(EM.Network.LobbyManager),"RequestStartGame")]
static class OverhaulStartPatch {
 static bool Prefix(){string reason=SanctuaryLobbyState.StartBlockReason();if(string.IsNullOrWhiteSpace(reason))return true;SanctuaryLobbyState.Chat(reason);return false;}
}
