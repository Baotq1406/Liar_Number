using System;
using System.Collections.Generic;

[Serializable]
public class TurnUpdateEvent
{
    public string roomId;
    public int destinyCard = -1;
    public string currentTurnPlayerId;
    public string currentTurnPlayerName;
    public int currentTurnIndex;
    public List<string> turnOrderPlayerIds;
    public string phase;
}

[Serializable]
public class GameStartedHandEvent
{
    public string playerName;
    public List<int> cards;
}

[Serializable]
public class GameStartedEvent
{
    public string roomId;
    public List<string> players;
    public int initialCardCount;
    public int destinyCard = -1;
    public List<GameStartedHandEvent> hands;
}

[Serializable]
public class ShowWaitingEvent
{
    public string roomId;
    public int playId;
    public string message;
    public string actorPlayerId;
    public int playedCardCount;
    public string phase;
}

[Serializable]
public class ShowLiarPanelEvent
{
    public string roomId;
    public int playId;
    public string actorPlayerId;
    public string actorPlayerName;
    public int declaredNumber;
    public int playedCardCount;
    public List<int> previewCards;
    public string phase;
}

[Serializable]
public class ResolveResultEvent
{
    public string roomId;
    public int playId;
    public string accuserPlayerId;
    public string accusedPlayerId;
    public string punishedPlayerId;
    public bool liar;
    public string reason;
    public int destinyCard = -1;
    public List<int> revealedCards;
    public RouletteResultEvent roulette;
}

[Serializable]
public class RouletteResultEvent
{
    public int stageBefore;
    public int stageAfter;
    public bool hit;
    public bool isDead;
}

[Serializable]
public class RevealPlayedCardsEvent
{
    public string roomId;
    public int playId;
    public string actorPlayerId;
    public string actorPlayerName;
    public List<int> cards;
}

[Serializable]
public class RoundResetHandEvent
{
    public string playerId;
    public string playerName;
    public string nickname;
    public List<int> cards;
}

[Serializable]
public class RoundResetEvent
{
    public string roomId;
    public int playId;
    public int destinyCard = -1;
    public int cardsPerPlayer;
    public List<RoundResetHandEvent> hands;
    public List<string> deadPlayerIds;
}

[Serializable]
public class ErrorEvent
{
    public string code;
    public string message;
}

[Serializable]
public class PlayCardRequest
{
    public string roomId;
    public string playerId;
    public List<int> cards;
    public int declaredNumber;
}

[Serializable]
public class SkipRequest
{
    public string roomId;
    public string playerId;
}

[Serializable]
public class LiarRequest
{
    public string roomId;
    public string playerId;
}
