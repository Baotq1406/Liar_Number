using System;
using UnityEngine;

public class GameMessageHandler : MonoBehaviour
{
    private bool _isRegistered;

    private void Awake()
    {
        RegisterHandlers();
    }

    private void OnEnable()
    {
        if (!_isRegistered)
        {
            RegisterHandlers();
        }
    }

    private void OnDisable()
    {
        if (!_isRegistered || NetworkClient.Instant == null || NetworkClient.Instant.Dispatcher == null)
        {
            return;
        }

        var dispatcher = NetworkClient.Instant.Dispatcher;
        dispatcher.UnregisterHandler("TURN_UPDATE", OnTurnUpdate);
        dispatcher.UnregisterHandler("SHOW_WAITING", OnShowWaiting);
        dispatcher.UnregisterHandler("SHOW_LIAR_PANEL", OnShowLiarPanel);
        dispatcher.UnregisterHandler("REVEAL_PLAYED_CARDS", OnRevealPlayedCards);
        dispatcher.UnregisterHandler("RESOLVE_RESULT", OnResolveResult);
        dispatcher.UnregisterHandler("ROUND_RESET", OnRoundReset);
        dispatcher.UnregisterHandler("ERROR", OnError);

        _isRegistered = false;
    }

    private void RegisterHandlers()
    {
        if (NetworkClient.Instant == null || NetworkClient.Instant.Dispatcher == null)
        {
            Invoke(nameof(RegisterHandlers), 0.1f);
            return;
        }

        if (_isRegistered)
        {
            return;
        }

        var dispatcher = NetworkClient.Instant.Dispatcher;
        dispatcher.RegisterHandler("TURN_UPDATE", OnTurnUpdate);
        dispatcher.RegisterHandler("SHOW_WAITING", OnShowWaiting);
        dispatcher.RegisterHandler("SHOW_LIAR_PANEL", OnShowLiarPanel);
        dispatcher.RegisterHandler("REVEAL_PLAYED_CARDS", OnRevealPlayedCards);
        dispatcher.RegisterHandler("RESOLVE_RESULT", OnResolveResult);
        dispatcher.RegisterHandler("ROUND_RESET", OnRoundReset);
        dispatcher.RegisterHandler("ERROR", OnError);

        _isRegistered = true;
    }

    private void OnRoundReset(string payloadJson)
    {
        try
        {
            var payload = JsonUtility.FromJson<RoundResetEvent>(payloadJson);
            if (payload == null)
            {
                Debug.LogWarning("[GameMessageHandler] ROUND_RESET payload null: " + payloadJson);
                return;
            }

            if (IsPayloadRoomMismatch(payload.roomId))
            {
                Debug.LogWarning("[GameMessageHandler] Bo qua ROUND_RESET khac room. roomId=" + payload.roomId + ", currentRoomId=" + GameManager.Instant?.CurrentRoomId);
                return;
            }

            if (payload.hands == null)
            {
                payload.hands = new System.Collections.Generic.List<RoundResetHandEvent>();
            }

            if (payload.deadPlayerIds == null)
            {
                payload.deadPlayerIds = new System.Collections.Generic.List<string>();
            }

            GameManager.Instant?.SetRoundReset(payload);
            Debug.Log("[GameMessageHandler] ROUND_RESET: " + payloadJson);
        }
        catch (Exception e)
        {
            Debug.LogError("[GameMessageHandler] Parse ROUND_RESET loi: " + e.Message);
        }
    }

    private void OnTurnUpdate(string payloadJson)
    {
        try
        {
            var payload = JsonUtility.FromJson<TurnUpdateEvent>(payloadJson);
            if (payload == null)
            {
                Debug.LogWarning("[GameMessageHandler] TURN_UPDATE payload null: " + payloadJson);
                return;
            }

            if (string.IsNullOrEmpty(payload.currentTurnPlayerId) && string.IsNullOrEmpty(payload.currentTurnPlayerName))
            {
                Debug.LogWarning("[GameMessageHandler] TURN_UPDATE thieu currentTurnPlayerId/currentTurnPlayerName.");
            }

            if (string.IsNullOrEmpty(payload.phase))
            {
                Debug.LogWarning("[GameMessageHandler] TURN_UPDATE thieu phase.");
            }

            if (IsPayloadRoomMismatch(payload.roomId))
            {
                Debug.LogWarning("[GameMessageHandler] Bo qua TURN_UPDATE khac room. roomId=" + payload.roomId + ", currentRoomId=" + GameManager.Instant?.CurrentRoomId);
                return;
            }

            GameManager.Instant?.SetTurnUpdate(payload);
            Debug.Log("[GameMessageHandler] TURN_UPDATE: " + payloadJson);
        }
        catch (Exception e)
        {
            Debug.LogError("[GameMessageHandler] Parse TURN_UPDATE loi: " + e.Message);
        }
    }

    private void OnRevealPlayedCards(string payloadJson)
    {
        try
        {
            var payload = JsonUtility.FromJson<RevealPlayedCardsEvent>(payloadJson);
            if (payload == null)
            {
                Debug.LogWarning("[GameMessageHandler] REVEAL_PLAYED_CARDS payload null: " + payloadJson);
                return;
            }

            if (string.IsNullOrEmpty(payload.actorPlayerId))
            {
                Debug.LogWarning("[GameMessageHandler] REVEAL_PLAYED_CARDS thieu actorPlayerId.");
            }

            if (IsPayloadRoomMismatch(payload.roomId))
            {
                Debug.LogWarning("[GameMessageHandler] Bo qua REVEAL_PLAYED_CARDS khac room. roomId=" + payload.roomId + ", currentRoomId=" + GameManager.Instant?.CurrentRoomId);
                return;
            }

            if (payload.cards == null)
            {
                payload.cards = new System.Collections.Generic.List<int>();
            }

            GameManager.Instant?.SetRevealPlayedCards(payload);
            Debug.Log("[GameMessageHandler] REVEAL_PLAYED_CARDS: " + payloadJson);
        }
        catch (Exception e)
        {
            Debug.LogError("[GameMessageHandler] Parse REVEAL_PLAYED_CARDS loi: " + e.Message);
        }
    }

    private void OnShowWaiting(string payloadJson)
    {
        try
        {
            var payload = JsonUtility.FromJson<ShowWaitingEvent>(payloadJson);
            if (payload == null)
            {
                Debug.LogWarning("[GameMessageHandler] SHOW_WAITING payload null: " + payloadJson);
                return;
            }

            var gameManager = GameManager.Instant;
            if (gameManager == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(payload.actorPlayerId))
            {
                Debug.LogWarning("[GameMessageHandler] SHOW_WAITING thieu actorPlayerId.");
                gameManager.SetShowWaiting(null);
                return;
            }

            if (IsPayloadRoomMismatch(payload.roomId))
            {
                Debug.LogWarning("[GameMessageHandler] Bo qua SHOW_WAITING khac room. roomId=" + payload.roomId + ", currentRoomId=" + gameManager.CurrentRoomId);
                return;
            }

            if (payload.playedCardCount < 0)
            {
                Debug.LogWarning("[GameMessageHandler] SHOW_WAITING playedCardCount am, fallback ve 0.");
                payload.playedCardCount = 0;
            }

            if (string.IsNullOrEmpty(payload.phase))
            {
                Debug.LogWarning("[GameMessageHandler] SHOW_WAITING thieu phase, fallback WaitingResponses.");
                payload.phase = "WaitingResponses";
            }

            gameManager.SetShowWaiting(payload);

            Debug.Log("[GameMessageHandler] SHOW_WAITING: " + payloadJson);
        }
        catch (Exception e)
        {
            Debug.LogError("[GameMessageHandler] Parse SHOW_WAITING loi: " + e.Message);
        }
    }

    private void OnShowLiarPanel(string payloadJson)
    {
        try
        {
            var payload = JsonUtility.FromJson<ShowLiarPanelEvent>(payloadJson);
            if (payload == null)
            {
                Debug.LogWarning("[GameMessageHandler] SHOW_LIAR_PANEL payload null: " + payloadJson);
                return;
            }

            var gameManager = GameManager.Instant;
            if (gameManager == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(payload.actorPlayerId))
            {
                Debug.LogWarning("[GameMessageHandler] SHOW_LIAR_PANEL thieu actorPlayerId.");
                gameManager.SetShowLiarPanel(null);
                return;
            }

            if (IsPayloadRoomMismatch(payload.roomId))
            {
                Debug.LogWarning("[GameMessageHandler] Bo qua SHOW_LIAR_PANEL khac room. roomId=" + payload.roomId + ", currentRoomId=" + gameManager.CurrentRoomId);
                return;
            }

            if (payload.playedCardCount < 0)
            {
                Debug.LogWarning("[GameMessageHandler] SHOW_LIAR_PANEL playedCardCount am, fallback ve 0.");
                payload.playedCardCount = 0;
            }

            if (string.IsNullOrEmpty(payload.phase))
            {
                Debug.LogWarning("[GameMessageHandler] SHOW_LIAR_PANEL thieu phase.");
            }

            gameManager.SetShowLiarPanel(payload);
            Debug.Log("[GameMessageHandler] SHOW_LIAR_PANEL: " + payloadJson);
        }
        catch (Exception e)
        {
            Debug.LogError("[GameMessageHandler] Parse SHOW_LIAR_PANEL loi: " + e.Message);
        }
    }

    private void OnResolveResult(string payloadJson)
    {
        try
        {
            var payload = JsonUtility.FromJson<ResolveResultEvent>(payloadJson);
            if (payload == null)
            {
                Debug.LogWarning("[GameMessageHandler] RESOLVE_RESULT payload null: " + payloadJson);
                return;
            }

            if (string.IsNullOrEmpty(payload.roomId))
            {
                Debug.LogWarning("[GameMessageHandler] RESOLVE_RESULT thieu roomId.");
            }

            if (IsPayloadRoomMismatch(payload.roomId))
            {
                Debug.LogWarning("[GameMessageHandler] Bo qua RESOLVE_RESULT khac room. roomId=" + payload.roomId + ", currentRoomId=" + GameManager.Instant?.CurrentRoomId);
                return;
            }

            GameManager.Instant?.SetResolveResult(payload);
            Debug.Log("[GameMessageHandler] RESOLVE_RESULT: " + payloadJson);
        }
        catch (Exception e)
        {
            Debug.LogError("[GameMessageHandler] Parse RESOLVE_RESULT loi: " + e.Message);
        }
    }

    private static bool IsPayloadRoomMismatch(string payloadRoomId)
    {
        if (GameManager.Instant == null)
        {
            return false;
        }

        var currentRoomId = GameManager.Instant.CurrentRoomId;
        if (string.IsNullOrWhiteSpace(payloadRoomId) || string.IsNullOrWhiteSpace(currentRoomId))
        {
            return false;
        }

        return !string.Equals(payloadRoomId.Trim(), currentRoomId.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void OnError(string payloadJson)
    {
        try
        {
            var payload = JsonUtility.FromJson<ErrorEvent>(payloadJson);
            if (payload == null)
            {
                payload = new ErrorEvent
                {
                    code = "UNKNOWN",
                    message = "Cannot parse error payload"
                };
            }

            GameManager.Instant?.SetError(payload);
            Debug.LogWarning("[GameMessageHandler] ERROR: code=" + payload?.code + ", message=" + payload?.message);
        }
        catch (Exception e)
        {
            Debug.LogError("[GameMessageHandler] Parse ERROR loi: " + e.Message + " | payload=" + payloadJson);
        }
    }
}
