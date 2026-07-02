using System;
using System.Collections;
using System.Collections.Generic;

using Unity.Netcode;

using UnityEngine;
using UnityEngine.SceneManagement;

// Server-authoritative match logic: spawning players, per-client checkpoints, respawning when a player
// falls off the map, the level timer, and the finish line. Lobby and connection flow live elsewhere
// (RBGameLobby, RubberBandMultiplayer).
public class RBGameManager : NetworkBehaviour
{
    public static RBGameManager Instance { get; private set; }

    public class LevelCompletedEventArgs : EventArgs
    {
        public float TimeSeconds { get; }
        public string FormattedTime { get; }

        public LevelCompletedEventArgs(float timeSeconds)
        {
            TimeSeconds = Mathf.Max(0f, timeSeconds);
            FormattedTime = FormatTime(TimeSeconds);
        }
    }

    // Raised on every peer (host included) when the level is finished.
    public event EventHandler<LevelCompletedEventArgs> OnLevelCompleted;

    // Raised on every peer when the run resets back to the initial spawn (replay).
    public event EventHandler OnLevelReset;

    [Header("Level Finish")]
    [SerializeField] private Collider finishTrigger;

    [Header("Spawning")]
    [SerializeField] private Transform playerPrefab;
    [Tooltip("Drag one empty GameObject per player here. Place them next to each other, " +
             "within the rope's rest length so the band starts relaxed (not pre-stretched).")]
    [SerializeField] private List<Transform> spawnPoints = new List<Transform>();
    [Tooltip("Fallback ONLY if Spawn Points is empty/short: players are fanned out this far " +
             "along X so they never spawn inside each other at the origin.")]
    [SerializeField] private float fallbackSpawnSpacing = 3f;
    [SerializeField] private float fallbackSpawnHeight = 2f;

    // Delay before rebuilding the rope after a respawn, so both player NetworkObjects are
    // fully spawned and networked before the rope tries to grab them.
    private const float RopeRespawnDelay = 0.1f;

    private int nextSpawnIndex;

    // Last reached checkpoint per client (server-authoritative). A checkpoint only counts when
    // its number beats the one the client already holds, so progress can never go backwards.
    private readonly Dictionary<ulong, Vector3> playerCheckpoints = new Dictionary<ulong, Vector3>();
    private readonly Dictionary<ulong, int> playerCheckpointNumbers = new Dictionary<ulong, int>();

    private RubberBandSpawner rubberBandSpawner;

    // Level timer.
    private float levelStartTime;
    private bool isLevelTimerRunning;
    private float lastLevelTime;
    private bool hasLevelCompleted;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    private void Start()
    {
        StartLevelTimer();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += SceneManager_OnLoadEventCompleted;

            // The finish collider lives on a SEPARATE object (assigned to finishTrigger). Unity's
            // OnTriggerEnter only fires on the object that owns the collider, so we attach a small
            // relay there that forwards a player crossing back to us. Server-only: the host
            // simulates both player bodies, so that's where the crossing actually happens.
            if (finishTrigger != null && finishTrigger.GetComponent<FinishTriggerRelay>() == null)
                finishTrigger.gameObject.AddComponent<FinishTriggerRelay>();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= SceneManager_OnLoadEventCompleted;
    }

    // ------------------------------------------------------------------ Level timer

    public void StartLevelTimer()
    {
        levelStartTime = Time.time;
        isLevelTimerRunning = true;
        lastLevelTime = 0f;
    }

    public float GetCurrentLevelTime()
    {
        return isLevelTimerRunning ? Time.time - levelStartTime : lastLevelTime;
    }

    public float GetLastLevelTime()
    {
        return lastLevelTime;
    }

    public static string FormatTime(float timeSeconds)
    {
        timeSeconds = Mathf.Max(0f, timeSeconds);
        int totalSeconds = Mathf.FloorToInt(timeSeconds);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        int milliseconds = Mathf.FloorToInt((timeSeconds - totalSeconds) * 1000f);
        return string.Format("{0:00}:{1:00}.{2:000}", minutes, seconds, milliseconds);
    }

    // ------------------------------------------------------------------ Finish line

    public void CompleteLevel()
    {
        if (hasLevelCompleted) return;
        hasLevelCompleted = true;

        if (isLevelTimerRunning)
        {
            lastLevelTime = Time.time - levelStartTime;
            isLevelTimerRunning = false;
        }

        OnLevelCompleted?.Invoke(this, new LevelCompletedEventArgs(lastLevelTime));
    }

    public void SetFinishTrigger(Collider trigger)
    {
        finishTrigger = trigger;
    }

    // Server-only, idempotent: the level has been finished. Called by FinishTriggerRelay when a
    // player crosses the finish collider assigned to finishTrigger.
    public void ReachFinish()
    {
        if (!IsServer || hasLevelCompleted) return;

        CompleteLevel();
        LevelCompletedClientRpc(lastLevelTime);
    }

    [ClientRpc]
    private void LevelCompletedClientRpc(float timeSeconds)
    {
        // Host already completed locally in CompleteLevel(); this guard stops a double-fire.
        if (hasLevelCompleted) return;

        hasLevelCompleted = true;
        lastLevelTime = Mathf.Max(0f, timeSeconds);
        isLevelTimerRunning = false;

        OnLevelCompleted?.Invoke(this, new LevelCompletedEventArgs(lastLevelTime));
    }

    // ------------------------------------------------------------------ Checkpoints

    // Server-only: record a client's checkpoint, but only if it's further along than their current one.
    public void SetCheckpoint(ulong clientId, Vector3 position, int checkpointNumber)
    {
        if (!IsServer) return;

        if (!playerCheckpointNumbers.TryGetValue(clientId, out int current) || checkpointNumber > current)
        {
            playerCheckpoints[clientId] = position;
            playerCheckpointNumbers[clientId] = checkpointNumber;
        }
    }

    // ------------------------------------------------------------------ Spawning / respawning

    private void SceneManager_OnLoadEventCompleted(string sceneName, LoadSceneMode loadSceneMode,
        List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        // Deterministic: first client -> spawnPoints[0], second -> spawnPoints[1].
        nextSpawnIndex = 0;
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            SpawnPlayer(clientId);
    }

    // Server-only: respawn BOTH players (they're tied together by the rope, so one falling resets both).
    public void RespawnBothPlayers()
    {
        if (!IsServer) return;

        // Snapshot the ids before mutating any player objects.
        var clientIds = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);

        foreach (ulong clientId in clientIds)
            DespawnPlayer(clientId);

        nextSpawnIndex = 0; // deterministic fallback ordering when a player has no checkpoint yet
        foreach (ulong clientId in clientIds)
            SpawnPlayer(clientId);

        // Rebuild the rope once both player objects are guaranteed spawned & networked.
        StartCoroutine(RespawnRopeAfterDelay());
    }

    // Server-only: send BOTH players back to the level's INITIAL spawn points for a fresh run
    // (the win screen's replay button). Clears checkpoints so it's a true restart, then resets
    // the completion state / timer / UI on every peer.
    public void RespawnBothPlayersAtStart()
    {
        if (!IsServer) return;

        playerCheckpoints.Clear();
        playerCheckpointNumbers.Clear();

        var clientIds = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);

        foreach (ulong clientId in clientIds)
            DespawnPlayer(clientId);

        nextSpawnIndex = 0; // start at spawnPoints[0] again
        foreach (ulong clientId in clientIds)
            SpawnPlayer(clientId);

        StartCoroutine(RespawnRopeAfterDelay());

        ResetLevelClientRpc();
    }

    // Clears the "level complete" state, restarts the timer, and lets the UI hide the win screen,
    // on every peer (host included).
    [ClientRpc]
    private void ResetLevelClientRpc()
    {
        hasLevelCompleted = false;
        StartLevelTimer();
        OnLevelReset?.Invoke(this, EventArgs.Empty);
    }

    // Server-only: despawn and destroy a client's current player object, if any.
    private void DespawnPlayer(ulong clientId)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client)) return;

        NetworkObject player = client.PlayerObject;
        if (player != null && player.IsSpawned)
            player.Despawn(true); // true = also destroy the server-side GameObject
    }

    // Server-only: instantiate the player prefab at the client's checkpoint (if any) or the next spawn point.
    private void SpawnPlayer(ulong clientId)
    {
        Vector3 position;
        Quaternion rotation = Quaternion.identity;

        if (playerCheckpoints.TryGetValue(clientId, out Vector3 checkpoint))
            position = checkpoint;
        else
            GetNextSpawnPose(out position, out rotation);

        Transform player = Instantiate(playerPrefab, position, rotation);
        player.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
    }

    private IEnumerator RespawnRopeAfterDelay()
    {
        yield return new WaitForSeconds(RopeRespawnDelay);
        RespawnRubberBandClientRpc();
    }

    // Tells every peer (host included) to rebuild its local rope instance.
    [ClientRpc]
    private void RespawnRubberBandClientRpc()
    {
        if (rubberBandSpawner == null)
            rubberBandSpawner = FindObjectOfType<RubberBandSpawner>();

        rubberBandSpawner?.ResetRope();
    }

    // Next spawn pose. Uses spawnPoints in order; if the list is empty or has a null entry, fans players
    // out along X (by fallbackSpawnSpacing) so they can never spawn stacked at the origin.
    private void GetNextSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        int index = nextSpawnIndex++;

        if (spawnPoints != null && spawnPoints.Count > 0)
        {
            Transform sp = spawnPoints[index % spawnPoints.Count];
            if (sp != null)
            {
                position = sp.position;
                rotation = sp.rotation;
                return;
            }
        }

        position = new Vector3(index * fallbackSpawnSpacing, fallbackSpawnHeight, 0f);
        rotation = Quaternion.identity;
    }
}
