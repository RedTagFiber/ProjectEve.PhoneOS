using Microsoft.Data.Sqlite;
using ProjectEve.Core.Chat;
using ProjectEve.Core.Phone;
using ProjectEve.Core.Time;

namespace ProjectEve.PhoneOS.Services;

/// <summary>
/// Server-owned persisted phone messaging.
///
/// The Razor page never owns conversation truth.
/// Player lines are accepted into ProjectEve's ConversationManager immediately.
/// NPC replies may happen later in the background and still use the complete
/// active section even if the player leaves the page.
///
/// ProjectEve's hidden phone scheduler decides when a reply is reconsidered.
/// Work can delay but never automatically blocks texting. Explicit living-world
/// states (sleep/driving/emergency/etc.) stay hidden from the player.
/// </summary>
public sealed class PhoneMessagingService : BackgroundService
{
    private readonly IConversationChatService _conversation;
    private readonly IPhoneResponseScheduler _scheduler;
    private readonly IGameTimeService _gameTime;
    private readonly PhoneThreadPresenceService _presence;
    private readonly ILogger<PhoneMessagingService> _log;
    private readonly string _dbPath;
    private readonly object _claimGate = new();

    public PhoneMessagingService(
        IConversationChatService conversation,
        IPhoneResponseScheduler scheduler,
        IGameTimeService gameTime,
        PhoneThreadPresenceService presence,
        ILogger<PhoneMessagingService> log)
    {
        _conversation = conversation;
        _scheduler = scheduler;
        _gameTime = gameTime;
        _presence = presence;
        _log = log;

        var eveData = @"D:\ProjectEve\EveData\db";
        var local = Path.Combine(AppContext.BaseDirectory, "Data");
        var dir = Directory.Exists(@"D:\ProjectEve\EveData")
            ? eveData
            : local;

        Directory.CreateDirectory(dir);

        _dbPath =
            Environment.GetEnvironmentVariable("EVE_PHONE_DB_PATH")
            ?? Path.Combine(dir, "phone_messages.db");

        EnsureSchema();
        RecoverInterruptedReplyWork();
    }

    public string DatabasePath => _dbPath;

    // ------------------------------------------------------------
    // Contacts
    // ------------------------------------------------------------

    public void EnsureKnownContact(
        string playerId,
        int npcId,
        string displayName,
        string? phoneNumber = null,
        string source = "existing_thread",
        int contactTier = 1)
    {
        if (string.IsNullOrWhiteSpace(playerId))
            throw new ArgumentException(
                "playerId is required",
                nameof(playerId));

        using var conn = Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            INSERT INTO PlayerPhoneContact
                (PlayerId,NpcId,DisplayName,PhoneNumber,
                 ContactSource,ContactTier,
                 IsBlocked,IsMuted,IsFavorite,
                 CreatedUtc,UpdatedUtc)
            VALUES
                ($player,$npc,$name,$phone,$source,$tier,
                 0,0,0,$utc,$utc)
            ON CONFLICT(PlayerId,NpcId) DO UPDATE SET
                DisplayName=excluded.DisplayName,
                PhoneNumber=CASE
                    WHEN excluded.PhoneNumber <> ''
                    THEN excluded.PhoneNumber
                    ELSE PlayerPhoneContact.PhoneNumber
                END,
                ContactTier=excluded.ContactTier,
                UpdatedUtc=excluded.UpdatedUtc;
            """;

        cmd.Parameters.AddWithValue("$player", playerId);
        cmd.Parameters.AddWithValue("$npc", npcId);
        cmd.Parameters.AddWithValue(
            "$name",
            Clean(displayName, $"NPC {npcId}"));
        cmd.Parameters.AddWithValue(
            "$phone",
            phoneNumber?.Trim() ?? "");
        cmd.Parameters.AddWithValue(
            "$source",
            Clean(source, "existing_thread"));
        cmd.Parameters.AddWithValue(
            "$tier",
            Math.Max(1, contactTier));
        cmd.Parameters.AddWithValue(
            "$utc",
            DateTime.UtcNow.ToString("O"));

        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<PhoneContactRow>
        GetContacts(string playerId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            SELECT PlayerId,NpcId,DisplayName,PhoneNumber,
                   ContactSource,ContactTier,
                   IsBlocked,IsMuted,IsFavorite,
                   CreatedUtc,UpdatedUtc
            FROM PlayerPhoneContact
            WHERE PlayerId=$player
            ORDER BY IsFavorite DESC,
                     DisplayName COLLATE NOCASE;
            """;

        cmd.Parameters.AddWithValue("$player", playerId);

        var rows = new List<PhoneContactRow>();
        using var r = cmd.ExecuteReader();

        while (r.Read())
        {
            rows.Add(new PhoneContactRow
            {
                PlayerId = r.GetString(0),
                NpcId = r.GetInt32(1),
                DisplayName = r.GetString(2),
                PhoneNumber = r.GetString(3),
                ContactSource = r.GetString(4),
                ContactTier = r.GetInt32(5),
                IsBlocked = r.GetInt32(6) != 0,
                IsMuted = r.GetInt32(7) != 0,
                IsFavorite = r.GetInt32(8) != 0,
                CreatedUtc = ParseUtc(r.GetString(9)),
                UpdatedUtc = ParseUtc(r.GetString(10))
            });
        }

        return rows;
    }

    // ------------------------------------------------------------
    // Threads / transcript display
    // ------------------------------------------------------------

    public long GetOrCreateThread(
        string playerId,
        int npcId,
        string npcName)
    {
        EnsureKnownContact(
            playerId,
            npcId,
            npcName);

        using var conn = Open();

        using (var find = conn.CreateCommand())
        {
            find.CommandText = """
                SELECT Id
                FROM PhoneThread
                WHERE PlayerId=$player
                  AND NpcId=$npc
                LIMIT 1;
                """;

            find.Parameters.AddWithValue(
                "$player",
                playerId);
            find.Parameters.AddWithValue(
                "$npc",
                npcId);

            object? existing = find.ExecuteScalar();

            if (existing is not null &&
                existing != DBNull.Value)
                return Convert.ToInt64(existing);
        }

        using var insert = conn.CreateCommand();

        insert.CommandText = """
            INSERT INTO PhoneThread
                (PlayerId,NpcId,NpcName,
                 CreatedUtc,LastActivityUtc,
                 ActiveConversationSessionId)
            VALUES
                ($player,$npc,$name,$utc,$utc,NULL);
            SELECT last_insert_rowid();
            """;

        insert.Parameters.AddWithValue(
            "$player",
            playerId);
        insert.Parameters.AddWithValue(
            "$npc",
            npcId);
        insert.Parameters.AddWithValue(
            "$name",
            Clean(npcName, $"NPC {npcId}"));
        insert.Parameters.AddWithValue(
            "$utc",
            DateTime.UtcNow.ToString("O"));

        return Convert.ToInt64(
            insert.ExecuteScalar());
    }

    public IReadOnlyList<PhoneMessageRow> GetMessages(
        string playerId,
        int npcId,
        string npcName,
        int limit = 500)
    {
        limit = Math.Clamp(limit, 1, 5000);

        long threadId =
            GetOrCreateThread(
                playerId,
                npcId,
                npcName);

        using var conn = Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            SELECT Id,ThreadId,PlayerId,PlayerName,NpcId,
                   Sender,MessageText,SentUtc,
                   DeliveryState,ReplyState,ReplyDueUtc,
                   ReplyToMessageId,ConversationSessionId,
                   NoticeState,ResponseAttemptCount,LastSchedulerDecision,
                   SentGameTime,ReplyDueGameTime,SimulationDelayMinutes
            FROM PhoneMessage
            WHERE ThreadId=$thread
            ORDER BY Id DESC
            LIMIT $limit;
            """;

        cmd.Parameters.AddWithValue(
            "$thread",
            threadId);
        cmd.Parameters.AddWithValue(
            "$limit",
            limit);

        var reversed =
            new List<PhoneMessageRow>();

        using var r = cmd.ExecuteReader();

        while (r.Read())
            reversed.Add(ReadMessage(r));

        reversed.Reverse();
        return reversed;
    }

    /// <summary>
    /// Saves the phone line AND immediately archives the exact player words
    /// into ProjectEve's active ConversationSession.
    /// The AI reply is intentionally deferred to the background worker.
    /// </summary>
    public async Task<PhoneMessageRow> SendPlayerMessageAsync(
        string playerId,
        string playerName,
        int npcId,
        string npcName,
        string text,
        CancellationToken cancellationToken = default)
    {
        text = text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException(
                "Message text is required",
                nameof(text));

        playerId = Clean(
            playerId,
            "legacy-player");

        playerName = Clean(
            playerName,
            "Player");

        long threadId =
            GetOrCreateThread(
                playerId,
                npcId,
                npcName);

        // This is the key Phase 3 change:
        // ProjectEve records the exact player line now, not 20 seconds later
        // when the reply worker happens to wake up.
        var accepted =
            await _conversation.AcceptPlayerMessageAsync(
                new ConversationPlayerMessageRequest
                {
                    PlayerId = playerId,
                    PlayerName = playerName,
                    NpcId = npcId,
                    NpcNameHint = npcName,
                    Channel = "text",
                    Location = "phone",
                    Message = text
                },
                cancellationToken);

        var now = DateTime.UtcNow;
        var sentGameTime = _gameTime.Now;

        var initialPlan =
            await _scheduler.PlanInitialAsync(
                new PhoneResponseRequest
                {
                    PlayerId = playerId,
                    PlayerName = playerName,
                    NpcId = npcId,
                    Message = text,
                    SentUtc = now,
                    NoticeState = "unseen",
                    AttemptCount = 0,
                    PlayerActivelyViewingThread = _presence.IsActive(playerId, npcId)
                },
                cancellationToken);

        var due =
            initialPlan.NextCheckUtc <= now
                ? now.AddSeconds(1)
                : initialPlan.NextCheckUtc;

        using var conn = Open();
        using var tx = conn.BeginTransaction();

        long messageId;

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;

            cmd.CommandText = """
                INSERT INTO PhoneMessage
                    (ThreadId,PlayerId,PlayerName,NpcId,
                     Sender,MessageText,SentUtc,
                     DeliveryState,ReplyState,ReplyDueUtc,
                     ReplyToMessageId,ConversationSessionId,
                     NoticeState,ResponseAttemptCount,LastSchedulerDecision,
                     SentGameTime,ReplyDueGameTime,SimulationDelayMinutes)
                VALUES
                    ($thread,$player,$playerName,$npc,
                     'player',$text,$sent,
                     'delivered','pending',$due,
                     NULL,$session,
                     $notice,0,$decision,
                     $sentGame,$dueGame,$simMinutes);
                SELECT last_insert_rowid();
                """;

            cmd.Parameters.AddWithValue(
                "$thread",
                threadId);
            cmd.Parameters.AddWithValue(
                "$player",
                playerId);
            cmd.Parameters.AddWithValue(
                "$playerName",
                playerName);
            cmd.Parameters.AddWithValue(
                "$npc",
                npcId);
            cmd.Parameters.AddWithValue(
                "$text",
                text);
            cmd.Parameters.AddWithValue(
                "$sent",
                now.ToString("O"));
            cmd.Parameters.AddWithValue(
                "$due",
                due.ToString("O"));
            cmd.Parameters.AddWithValue(
                "$session",
                accepted.SessionId);
            cmd.Parameters.AddWithValue(
                "$notice",
                initialPlan.NoticeState);
            cmd.Parameters.AddWithValue(
                "$decision",
                initialPlan.DecisionCode ?? "");
            cmd.Parameters.AddWithValue(
                "$sentGame",
                sentGameTime.ToString("O"));
            cmd.Parameters.AddWithValue(
                "$dueGame",
                initialPlan.NextCheckGameTime == default
                    ? sentGameTime.ToString("O")
                    : initialPlan.NextCheckGameTime.ToString("O"));
            cmd.Parameters.AddWithValue(
                "$simMinutes",
                initialPlan.SimulatedDelayMinutes);

            messageId =
                Convert.ToInt64(
                    cmd.ExecuteScalar());
        }

        using (var thread = conn.CreateCommand())
        {
            thread.Transaction = tx;
            thread.CommandText = """
                UPDATE PhoneThread
                SET LastActivityUtc=$utc,
                    ActiveConversationSessionId=$session
                WHERE Id=$id;
                """;
            thread.Parameters.AddWithValue(
                "$utc",
                now.ToString("O"));
            thread.Parameters.AddWithValue(
                "$session",
                accepted.SessionId);
            thread.Parameters.AddWithValue(
                "$id",
                threadId);
            thread.ExecuteNonQuery();
        }

        tx.Commit();

        _log.LogInformation(
            "[Phone] queued | NPC {NpcId} | message {MessageId} | session {SessionId} | due {DueLocal} | scheduler {Decision}",
            npcId,
            messageId,
            accepted.SessionId,
            due.ToLocalTime(),
            initialPlan.DecisionCode ?? "");

        return new PhoneMessageRow
        {
            Id = messageId,
            ThreadId = threadId,
            PlayerId = playerId,
            PlayerName = playerName,
            NpcId = npcId,
            Sender = "player",
            MessageText = text,
            SentUtc = now,
            DeliveryState = "delivered",
            ReplyState = "pending",
            ReplyDueUtc = due,
            ConversationSessionId =
                accepted.SessionId,
            NoticeState =
                initialPlan.NoticeState,
            ResponseAttemptCount = 0,
            LastSchedulerDecision =
                initialPlan.DecisionCode ?? "",
            SentGameTime = sentGameTime,
            ReplyDueGameTime = initialPlan.NextCheckGameTime == default
                ? sentGameTime
                : initialPlan.NextCheckGameTime,
            SimulationDelayMinutes = initialPlan.SimulatedDelayMinutes
        };
    }

    public int GetPendingReplyCount(
        string playerId,
        int npcId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            SELECT COUNT(*)
            FROM PhoneMessage
            WHERE PlayerId=$player
              AND NpcId=$npc
              AND Sender='player'
              AND ReplyState IN (
                    'pending',
                    'processing');
            """;

        cmd.Parameters.AddWithValue(
            "$player",
            playerId);
        cmd.Parameters.AddWithValue(
            "$npc",
            npcId);

        return Convert.ToInt32(
            cmd.ExecuteScalar());
    }

    /// <summary>
    /// True while the background worker has already claimed a player message
    /// and is inside scheduler/AI processing. Next Event uses this only to
    /// avoid racing the worker and falsely reporting that nothing happened.
    /// </summary>
    public bool HasProcessingPhoneWork(string playerId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT EXISTS(
                SELECT 1
                FROM PhoneMessage
                WHERE PlayerId=$player
                  AND Sender='player'
                  AND ReplyState='processing'
            );
            """;
        cmd.Parameters.AddWithValue(
            "$player",
            Clean(playerId, "legacy-player"));

        return Convert.ToInt32(cmd.ExecuteScalar()) != 0;
    }

    /// <summary>
    /// Earliest pending in-world phone opportunity for this player. This is
    /// intentionally not shown as an event by itself; Next Event can use it
    /// as a hidden stepping stone until an actual contact/reply happens.
    /// </summary>
    public DateTimeOffset? GetNextPendingPhoneGameTime(string playerId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT ReplyDueGameTime
            FROM PhoneMessage
            WHERE PlayerId=$player
              AND Sender='player'
              AND ReplyState='pending'
              AND ReplyDueGameTime IS NOT NULL
            ORDER BY ReplyDueGameTime,Id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$player", Clean(playerId, "legacy-player"));
        var raw = cmd.ExecuteScalar()?.ToString();
        return string.IsNullOrWhiteSpace(raw) ? null : ParseGameTime(raw);
    }

    public long GetLatestNpcMessageId(string playerId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(MAX(Id),0)
            FROM PhoneMessage
            WHERE PlayerId=$player
              AND Sender='npc';
            """;
        cmd.Parameters.AddWithValue("$player", Clean(playerId, "legacy-player"));
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>
    /// After the authoritative game clock is advanced, make any matching
    /// phone work runnable immediately instead of forcing the human player to
    /// wait for the prior wall-clock pacing timer.
    /// </summary>
    public void WakePhoneWorkForGameTime(string playerId, DateTimeOffset gameNow)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE PhoneMessage
            SET ReplyDueUtc=$realNow
            WHERE PlayerId=$player
              AND Sender='player'
              AND ReplyState='pending'
              AND ReplyDueGameTime IS NOT NULL
              AND ReplyDueGameTime <= $gameNow;
            """;
        cmd.Parameters.AddWithValue("$realNow", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$player", Clean(playerId, "legacy-player"));
        cmd.Parameters.AddWithValue("$gameNow", gameNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Used by the Next Event controller to process one due phone burst now.
    /// The same claim lock is shared with the background worker.
    /// </summary>
    public async Task<bool> ProcessOneDuePhoneBurstAsync(
        string playerId,
        CancellationToken cancellationToken = default)
    {
        var work = ClaimNextDueConversationBatch(playerId);
        if (work is null)
            return false;

        await ProcessNpcReplyAsync(work, cancellationToken);
        return true;
    }

    // ------------------------------------------------------------
    // Background NPC reply worker
    // ------------------------------------------------------------

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _log.LogInformation(
            "PhoneMessagingService started. DB: {DbPath}",
            _dbPath);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var work =
                    ClaimNextDueConversationBatch();

                if (work is null)
                {
                    await Task.Delay(
                        1000,
                        stoppingToken);

                    continue;
                }

                _log.LogInformation(
                    "[Phone] worker claimed | NPC {NpcId} | message {MessageId} | session {SessionId} | attempt {Attempt} | notice {Notice}",
                    work.NpcId,
                    work.Id,
                    work.ConversationSessionId,
                    work.ResponseAttemptCount,
                    work.NoticeState);

                await ProcessNpcReplyAsync(
                    work,
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(
                    ex,
                    "Phone reply worker loop failed.");

                try
                {
                    await Task.Delay(
                        1500,
                        stoppingToken);
                }
                catch
                {
                }
            }
        }
    }

    /// <summary>
    /// If the player sends several texts before the NPC answers,
    /// treat them as one conversational burst.
    ///
    /// The whole active section is already in ProjectEve, so we generate
    /// ONE reply to the latest player line instead of mechanically creating
    /// one AI response per bubble.
    /// </summary>
    private PhoneMessageRow? ClaimNextDueConversationBatch(string? playerId = null)
    {
        lock (_claimGate)
            return ClaimNextDueConversationBatchCore(playerId);
    }

    private PhoneMessageRow? ClaimNextDueConversationBatchCore(string? playerId)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        long? threadId = null;

        using (var findDue = conn.CreateCommand())
        {
            findDue.Transaction = tx;

            findDue.CommandText = """
                SELECT ThreadId
                FROM PhoneMessage
                WHERE Sender='player'
                  AND ReplyState='pending'
                  AND ReplyDueUtc <= $now
                  AND ($player='' OR PlayerId=$player)
                ORDER BY ReplyDueUtc,Id
                LIMIT 1;
                """;

            findDue.Parameters.AddWithValue(
                "$now",
                DateTime.UtcNow.ToString("O"));
            findDue.Parameters.AddWithValue(
                "$player",
                string.IsNullOrWhiteSpace(playerId) ? "" : playerId.Trim());

            object? v =
                findDue.ExecuteScalar();

            if (v is not null &&
                v != DBNull.Value)
                threadId =
                    Convert.ToInt64(v);
        }

        if (!threadId.HasValue)
        {
            tx.Commit();
            return null;
        }

        long? latestId = null;

        using (var latest = conn.CreateCommand())
        {
            latest.Transaction = tx;

            latest.CommandText = """
                SELECT Id
                FROM PhoneMessage
                WHERE ThreadId=$thread
                  AND Sender='player'
                  AND ReplyState='pending'
                ORDER BY Id DESC
                LIMIT 1;
                """;

            latest.Parameters.AddWithValue(
                "$thread",
                threadId.Value);

            object? v =
                latest.ExecuteScalar();

            if (v is not null &&
                v != DBNull.Value)
                latestId =
                    Convert.ToInt64(v);
        }

        if (!latestId.HasValue)
        {
            tx.Commit();
            return null;
        }

        // Claim every unsatisfied player bubble in this burst.
        using (var claim = conn.CreateCommand())
        {
            claim.Transaction = tx;

            claim.CommandText = """
                UPDATE PhoneMessage
                SET ReplyState='processing'
                WHERE ThreadId=$thread
                  AND Sender='player'
                  AND ReplyState='pending'
                  AND Id <= $latest;
                """;

            claim.Parameters.AddWithValue(
                "$thread",
                threadId.Value);
            claim.Parameters.AddWithValue(
                "$latest",
                latestId.Value);

            claim.ExecuteNonQuery();
        }

        PhoneMessageRow? row = null;

        using (var get = conn.CreateCommand())
        {
            get.Transaction = tx;

            get.CommandText = """
                SELECT Id,ThreadId,PlayerId,PlayerName,NpcId,
                       Sender,MessageText,SentUtc,
                       DeliveryState,ReplyState,ReplyDueUtc,
                       ReplyToMessageId,ConversationSessionId,
                       NoticeState,ResponseAttemptCount,LastSchedulerDecision,
                       SentGameTime,ReplyDueGameTime,SimulationDelayMinutes
                FROM PhoneMessage
                WHERE Id=$id;
                """;

            get.Parameters.AddWithValue(
                "$id",
                latestId.Value);

            using var r = get.ExecuteReader();

            if (r.Read())
                row = ReadMessage(r);
        }

        tx.Commit();
        return row;
    }

    private async Task ProcessNpcReplyAsync(
        PhoneMessageRow playerMessage,
        CancellationToken stoppingToken)
    {
        try
        {
            var activeThread = _presence.IsActive(
                playerMessage.PlayerId,
                playerMessage.NpcId);

            // REAL TIME vs GAME TIME:
            // A short wall-clock timer may wake this worker, but background
            // world time is not allowed to advance just because a laptop sat
            // powered on. If the player is actively waiting in this thread,
            // game pacing may advance the authoritative clock to the response
            // opportunity. Otherwise we wait for normal game-time advancement.
            if (playerMessage.ReplyDueGameTime.HasValue &&
                playerMessage.ReplyDueGameTime.Value > _gameTime.Now)
            {
                if (activeThread)
                {
                    await _gameTime.AdvanceUntilAsync(
                        playerMessage.PlayerId,
                        playerMessage.ReplyDueGameTime.Value,
                        "active_text_game_pacing",
                        stoppingToken);
                }

                if (playerMessage.ReplyDueGameTime.Value > _gameTime.Now)
                {
                    HoldConversationUntilGameTime(
                        playerMessage.ThreadId,
                        playerMessage.ReplyDueGameTime.Value);
                    return;
                }
            }

            // ----------------------------------------------------
            // HIDDEN SCHEDULER PASS
            // ----------------------------------------------------
            // Do NOT call the AI just because a DB timer became due.
            // First ask ProjectEve whether this NPC noticed the message,
            // can answer, and wants to answer now.
            var decision =
                await _scheduler.ReconsiderAsync(
                    new PhoneResponseRequest
                    {
                        PlayerId =
                            playerMessage.PlayerId,
                        PlayerName =
                            playerMessage.PlayerName,
                        NpcId =
                            playerMessage.NpcId,
                        Message =
                            playerMessage.MessageText,
                        SentUtc =
                            playerMessage.SentUtc,
                        NoticeState =
                            playerMessage.NoticeState,
                        AttemptCount =
                            playerMessage.ResponseAttemptCount,
                        PlayerActivelyViewingThread = activeThread
                    },
                    stoppingToken);

            _log.LogInformation(
                "[Phone] scheduler | NPC {NpcId} | message {MessageId} | action {Action} | notice {Notice} | code {Code} | next {NextLocal}",
                playerMessage.NpcId,
                playerMessage.Id,
                decision.Action,
                decision.NoticeState,
                decision.DecisionCode,
                decision.NextCheckUtc.ToLocalTime());

            if (decision.LeaveUnanswered)
            {
                _log.LogInformation(
                    "[Phone] left unanswered | NPC {NpcId} | message {MessageId}",
                    playerMessage.NpcId,
                    playerMessage.Id);

                MarkConversationBatchUnanswered(
                    playerMessage.ThreadId,
                    decision);

                return;
            }

            if (!decision.ShouldReplyNow)
            {
                _log.LogInformation(
                    "[Phone] rescheduled | NPC {NpcId} | message {MessageId} | next {NextLocal}",
                    playerMessage.NpcId,
                    playerMessage.Id,
                    decision.NextCheckUtc.ToLocalTime());

                RescheduleConversationBatch(
                    playerMessage.ThreadId,
                    decision);

                return;
            }

            // ----------------------------------------------------
            // ACTUAL COGNITION / REPLY
            // ----------------------------------------------------
            ConversationTurnResult result;

            _log.LogInformation(
                "[Phone] AI start | NPC {NpcId} | message {MessageId} | session {SessionId}",
                playerMessage.NpcId,
                playerMessage.Id,
                playerMessage.ConversationSessionId);

            if (playerMessage.ConversationSessionId.HasValue &&
                playerMessage.ConversationSessionId.Value > 0)
            {
                result =
                    await _conversation.GenerateNpcReplyAsync(
                        new ConversationReplyRequest
                        {
                            SessionId =
                                playerMessage
                                    .ConversationSessionId
                                    .Value,
                            NpcId =
                                playerMessage.NpcId,
                            PlayerMessage =
                                playerMessage.MessageText,
                            Channel = "text",
                            Location = "phone"
                        },
                        stoppingToken);
            }
            else
            {
                // Migration fallback for old phone DB rows created before
                // ConversationSessionId existed.
                result =
                    await _conversation.ReplyNowAsync(
                        new ConversationPlayerMessageRequest
                        {
                            PlayerId =
                                playerMessage.PlayerId,
                            PlayerName =
                                Clean(
                                    playerMessage.PlayerName,
                                    "Player"),
                            NpcId =
                                playerMessage.NpcId,
                            Channel = "text",
                            Location = "phone",
                            Message =
                                playerMessage.MessageText
                        },
                        stoppingToken);
            }

            _log.LogInformation(
                "[Phone] AI finished | NPC {NpcId} | message {MessageId} | source {Source} | chars {Length}",
                playerMessage.NpcId,
                playerMessage.Id,
                result.Source,
                result.Reply?.Length ?? 0);

            if (result.Source.Equals(
                    "section_closed",
                    StringComparison.OrdinalIgnoreCase))
            {
                // Player already moved into another context, such as
                // meeting the NPC in person. Never resurrect old text.
                MarkConversationBatchObsolete(
                    playerMessage.ThreadId);

                return;
            }

            string reply =
                (result.Reply ?? "").Trim();

            if (reply.Length == 0)
                reply = "...";

            using var conn = Open();
            using var tx = conn.BeginTransaction();

            var now = DateTime.UtcNow;

            using (var insert = conn.CreateCommand())
            {
                insert.Transaction = tx;

                insert.CommandText = """
                    INSERT INTO PhoneMessage
                        (ThreadId,PlayerId,PlayerName,NpcId,
                         Sender,MessageText,SentUtc,
                         DeliveryState,ReplyState,ReplyDueUtc,
                         ReplyToMessageId,ConversationSessionId,
                         NoticeState,ResponseAttemptCount,LastSchedulerDecision,
                         SentGameTime,ReplyDueGameTime,SimulationDelayMinutes)
                    VALUES
                        ($thread,$player,$playerName,$npc,
                         'npc',$text,$sent,
                         'delivered','none',NULL,
                         $replyTo,$session,
                         'seen',0,'npc_reply',
                         $sentGame,NULL,0);
                    """;

                insert.Parameters.AddWithValue(
                    "$thread",
                    playerMessage.ThreadId);
                insert.Parameters.AddWithValue(
                    "$player",
                    playerMessage.PlayerId);
                insert.Parameters.AddWithValue(
                    "$playerName",
                    playerMessage.PlayerName);
                insert.Parameters.AddWithValue(
                    "$npc",
                    playerMessage.NpcId);
                insert.Parameters.AddWithValue(
                    "$text",
                    reply);
                insert.Parameters.AddWithValue(
                    "$sent",
                    now.ToString("O"));
                insert.Parameters.AddWithValue(
                    "$sentGame",
                    _gameTime.Now.ToString("O"));
                insert.Parameters.AddWithValue(
                    "$replyTo",
                    playerMessage.Id);
                insert.Parameters.AddWithValue(
                    "$session",
                    (object?)playerMessage
                        .ConversationSessionId
                        ?? DBNull.Value);

                insert.ExecuteNonQuery();
            }

            using (var done = conn.CreateCommand())
            {
                done.Transaction = tx;

                done.CommandText = """
                    UPDATE PhoneMessage
                    SET ReplyState='done',
                        NoticeState=$notice,
                        LastSchedulerDecision=$decision
                    WHERE ThreadId=$thread
                      AND Sender='player'
                      AND ReplyState='processing';
                    """;

                done.Parameters.AddWithValue(
                    "$notice",
                    decision.NoticeState);
                done.Parameters.AddWithValue(
                    "$decision",
                    decision.DecisionCode ?? "reply_now");
                done.Parameters.AddWithValue(
                    "$thread",
                    playerMessage.ThreadId);

                done.ExecuteNonQuery();
            }

            TouchThread(
                conn,
                tx,
                playerMessage.ThreadId,
                now);

            tx.Commit();

            if (!activeThread)
            {
                try
                {
                    await _gameTime.SchedulePlayerEventAsync(
                        new GameEventScheduleRequest
                        {
                            PlayerId = playerMessage.PlayerId,
                            EventType = "npc_contact",
                            Title = GetNpcName(playerMessage.ThreadId, playerMessage.NpcId) + " sent you a message",
                            GameTime = _gameTime.Now,
                            InterruptFastForward = true,
                            SourceKey = $"phone-reply:{playerMessage.Id}"
                        },
                        stoppingToken);
                }
                catch (Exception eventEx)
                {
                    _log.LogDebug(eventEx, "[Phone] could not queue npc_contact event");
                }
            }

            _log.LogInformation(
                "[Phone] reply stored | NPC {NpcId} | reply-to {MessageId} | thread {ThreadId}",
                playerMessage.NpcId,
                playerMessage.Id,
                playerMessage.ThreadId);
        }
        catch (Exception ex)
        {
            _log.LogWarning(
                ex,
                "[Phone] pipeline ERROR | NPC {NpcId} | message {MessageId} | session {SessionId}",
                playerMessage.NpcId,
                playerMessage.Id,
                playerMessage.ConversationSessionId);

            // AI/infrastructure failure is not an NPC choice.
            // Retry later without pretending the NPC ignored the message.
            using var conn = Open();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = """
                UPDATE PhoneMessage
                SET ReplyState='pending',
                    ReplyDueUtc=$due,
                    ResponseAttemptCount=ResponseAttemptCount+1,
                    LastSchedulerDecision='worker_error'
                WHERE ThreadId=$thread
                  AND Sender='player'
                  AND ReplyState='processing';
                """;

            cmd.Parameters.AddWithValue(
                "$due",
                DateTime.UtcNow
                    .AddSeconds(20)
                    .ToString("O"));

            cmd.Parameters.AddWithValue(
                "$thread",
                playerMessage.ThreadId);

            cmd.ExecuteNonQuery();
        }
    }

    private void HoldConversationUntilGameTime(
        long threadId,
        DateTimeOffset dueGameTime)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE PhoneMessage
            SET ReplyState='pending',
                ReplyDueUtc=$due,
                ReplyDueGameTime=$dueGame
            WHERE ThreadId=$thread
              AND Sender='player'
              AND ReplyState='processing';
            """;
        cmd.Parameters.AddWithValue("$due", DateTime.UtcNow.AddSeconds(3).ToString("O"));
        cmd.Parameters.AddWithValue("$dueGame", dueGameTime.ToString("O"));
        cmd.Parameters.AddWithValue("$thread", threadId);
        cmd.ExecuteNonQuery();
    }

    private string GetNpcName(long threadId, int npcId)
    {
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT NpcName FROM PhoneThread WHERE Id=$id LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", threadId);
            var name = cmd.ExecuteScalar()?.ToString();
            return Clean(name, $"NPC {npcId}");
        }
        catch
        {
            return $"NPC {npcId}";
        }
    }

    private void RescheduleConversationBatch(
        long threadId,
        PhoneResponseDecision decision)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        DateTime due =
            decision.NextCheckUtc <= DateTime.UtcNow
                ? DateTime.UtcNow.AddSeconds(2)
                : decision.NextCheckUtc;

        cmd.CommandText = """
            UPDATE PhoneMessage
            SET ReplyState='pending',
                ReplyDueUtc=$due,
                NoticeState=$notice,
                ResponseAttemptCount=ResponseAttemptCount+1,
                LastSchedulerDecision=$decision,
                ReplyDueGameTime=$dueGame,
                SimulationDelayMinutes=$simMinutes
            WHERE ThreadId=$thread
              AND Sender='player'
              AND ReplyState='processing';
            """;

        cmd.Parameters.AddWithValue(
            "$due",
            due.ToString("O"));
        cmd.Parameters.AddWithValue(
            "$notice",
            decision.NoticeState ?? "unseen");
        cmd.Parameters.AddWithValue(
            "$decision",
            decision.DecisionCode ?? "");
        cmd.Parameters.AddWithValue(
            "$dueGame",
            decision.NextCheckGameTime == default
                ? _gameTime.Now.ToString("O")
                : decision.NextCheckGameTime.ToString("O"));
        cmd.Parameters.AddWithValue(
            "$simMinutes",
            decision.SimulatedDelayMinutes);
        cmd.Parameters.AddWithValue(
            "$thread",
            threadId);

        cmd.ExecuteNonQuery();
    }

    private void MarkConversationBatchUnanswered(
        long threadId,
        PhoneResponseDecision decision)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            UPDATE PhoneMessage
            SET ReplyState='left_unanswered',
                ReplyDueUtc=NULL,
                NoticeState=$notice,
                ResponseAttemptCount=ResponseAttemptCount+1,
                LastSchedulerDecision=$decision
            WHERE ThreadId=$thread
              AND Sender='player'
              AND ReplyState='processing';
            """;

        cmd.Parameters.AddWithValue(
            "$notice",
            decision.NoticeState ?? "seen");
        cmd.Parameters.AddWithValue(
            "$decision",
            decision.DecisionCode ?? "chose_not_to_reply");
        cmd.Parameters.AddWithValue(
            "$thread",
            threadId);

        cmd.ExecuteNonQuery();
    }

    private void MarkConversationBatchObsolete(
        long threadId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            UPDATE PhoneMessage
            SET ReplyState='obsolete_context_changed',
                ReplyDueUtc=NULL,
                LastSchedulerDecision='context_changed'
            WHERE ThreadId=$thread
              AND Sender='player'
              AND ReplyState='processing';
            """;

        cmd.Parameters.AddWithValue(
            "$thread",
            threadId);

        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// If the app/debugger was stopped while a message was "processing",
    /// that row would otherwise stay there forever. Recover it on startup.
    /// This is infrastructure recovery, not NPC behavior.
    /// </summary>
    private void RecoverInterruptedReplyWork()
    {
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = """
                UPDATE PhoneMessage
                SET ReplyState='pending',
                    ReplyDueUtc=$due,
                    LastSchedulerDecision='recovered_after_restart'
                WHERE Sender='player'
                  AND ReplyState='processing';
                """;

            cmd.Parameters.AddWithValue(
                "$due",
                DateTime.UtcNow.AddSeconds(2).ToString("O"));

            int recovered = cmd.ExecuteNonQuery();

            if (string.Equals(
                    Environment.GetEnvironmentVariable("EVE_PHONE_DEV_DIAGNOSTICS"),
                    "1",
                    StringComparison.OrdinalIgnoreCase))
            {
                using var clamp = conn.CreateCommand();
                clamp.CommandText = """
                    UPDATE PhoneMessage
                    SET ReplyDueUtc=$maxDue,
                        LastSchedulerDecision='dev_due_clamped'
                    WHERE Sender='player'
                      AND ReplyState='pending'
                      AND ReplyDueUtc > $maxDue;
                    """;

                string maxDue =
                    DateTime.UtcNow.AddSeconds(60).ToString("O");

                clamp.Parameters.AddWithValue("$maxDue", maxDue);
                int clamped = clamp.ExecuteNonQuery();

                if (clamped > 0)
                {
                    _log.LogWarning(
                        "[Phone] DEV clamped {Count} pending reply due-time(s) to <= 60 seconds.",
                        clamped);
                }
            }

            if (recovered > 0)
            {
                _log.LogWarning(
                    "[Phone] recovered {Count} interrupted processing message(s).",
                    recovered);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(
                ex,
                "[Phone] could not recover interrupted processing rows.");
        }
    }

    // ------------------------------------------------------------
    // DB
    // ------------------------------------------------------------

    private SqliteConnection Open()
    {
        var conn =
            new SqliteConnection(
                $"Data Source={_dbPath}");

        conn.Open();
        return conn;
    }

    private void EnsureSchema()
    {
        using var conn = Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;

                CREATE TABLE IF NOT EXISTS PlayerPhoneContact(
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PlayerId TEXT NOT NULL,
                    NpcId INTEGER NOT NULL,
                    DisplayName TEXT NOT NULL,
                    PhoneNumber TEXT NOT NULL DEFAULT '',
                    ContactSource TEXT NOT NULL,
                    ContactTier INTEGER NOT NULL DEFAULT 1,
                    IsBlocked INTEGER NOT NULL DEFAULT 0,
                    IsMuted INTEGER NOT NULL DEFAULT 0,
                    IsFavorite INTEGER NOT NULL DEFAULT 0,
                    CreatedUtc TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL,
                    UNIQUE(PlayerId,NpcId)
                );

                CREATE TABLE IF NOT EXISTS PhoneThread(
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PlayerId TEXT NOT NULL,
                    NpcId INTEGER NOT NULL,
                    NpcName TEXT NOT NULL,
                    CreatedUtc TEXT NOT NULL,
                    LastActivityUtc TEXT NOT NULL,
                    ActiveConversationSessionId INTEGER NULL,
                    UNIQUE(PlayerId,NpcId)
                );

                CREATE TABLE IF NOT EXISTS PhoneMessage(
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ThreadId INTEGER NOT NULL,
                    PlayerId TEXT NOT NULL,
                    PlayerName TEXT NOT NULL DEFAULT 'Player',
                    NpcId INTEGER NOT NULL,
                    Sender TEXT NOT NULL,
                    MessageText TEXT NOT NULL,
                    SentUtc TEXT NOT NULL,
                    DeliveryState TEXT NOT NULL,
                    ReplyState TEXT NOT NULL DEFAULT 'none',
                    ReplyDueUtc TEXT NULL,
                    ReplyToMessageId INTEGER NULL,
                    ConversationSessionId INTEGER NULL,
                    NoticeState TEXT NOT NULL DEFAULT 'unseen',
                    ResponseAttemptCount INTEGER NOT NULL DEFAULT 0,
                    LastSchedulerDecision TEXT NOT NULL DEFAULT '',
                    FOREIGN KEY(ThreadId)
                        REFERENCES PhoneThread(Id)
                        ON DELETE CASCADE
                );
                """;

            cmd.ExecuteNonQuery();
        }

        // Migration-safe for v2 phone DBs.
        EnsureColumn(
            conn,
            "PhoneMessage",
            "PlayerName",
            "TEXT NOT NULL DEFAULT 'Player'");

        EnsureColumn(
            conn,
            "PhoneMessage",
            "ConversationSessionId",
            "INTEGER NULL");

        EnsureColumn(
            conn,
            "PhoneMessage",
            "NoticeState",
            "TEXT NOT NULL DEFAULT 'unseen'");

        EnsureColumn(
            conn,
            "PhoneMessage",
            "ResponseAttemptCount",
            "INTEGER NOT NULL DEFAULT 0");

        EnsureColumn(
            conn,
            "PhoneMessage",
            "LastSchedulerDecision",
            "TEXT NOT NULL DEFAULT ''");

        EnsureColumn(
            conn,
            "PhoneMessage",
            "SentGameTime",
            "TEXT NULL");

        EnsureColumn(
            conn,
            "PhoneMessage",
            "ReplyDueGameTime",
            "TEXT NULL");

        EnsureColumn(
            conn,
            "PhoneMessage",
            "SimulationDelayMinutes",
            "REAL NOT NULL DEFAULT 0");

        EnsureColumn(
            conn,
            "PhoneThread",
            "ActiveConversationSessionId",
            "INTEGER NULL");

        using var indexes = conn.CreateCommand();

        indexes.CommandText = """
            CREATE INDEX IF NOT EXISTS
                IX_PhoneMessage_Thread
                ON PhoneMessage(ThreadId,Id);

            CREATE INDEX IF NOT EXISTS
                IX_PhoneMessage_Pending
                ON PhoneMessage(
                    ReplyState,
                    ReplyDueUtc);

            CREATE INDEX IF NOT EXISTS
                IX_PlayerPhoneContact_Player
                ON PlayerPhoneContact(
                    PlayerId,
                    DisplayName);

            CREATE INDEX IF NOT EXISTS
                IX_PhoneMessage_ConversationSession
                ON PhoneMessage(
                    ConversationSessionId);
            """;

        indexes.ExecuteNonQuery();
    }

    private static void EnsureColumn(
        SqliteConnection conn,
        string table,
        string column,
        string sqlTypeAndDefault)
    {
        bool exists = false;

        using (var info = conn.CreateCommand())
        {
            info.CommandText =
                $"PRAGMA table_info({table});";

            using var r =
                info.ExecuteReader();

            while (r.Read())
            {
                if (string.Equals(
                    r.GetString(1),
                    column,
                    StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
            return;

        using var alter =
            conn.CreateCommand();

        alter.CommandText =
            $"ALTER TABLE {table} ADD COLUMN {column} {sqlTypeAndDefault};";

        alter.ExecuteNonQuery();
    }

    private static void TouchThread(
        SqliteConnection conn,
        SqliteTransaction tx,
        long threadId,
        DateTime atUtc)
    {
        using var cmd =
            conn.CreateCommand();

        cmd.Transaction = tx;

        cmd.CommandText = """
            UPDATE PhoneThread
            SET LastActivityUtc=$utc
            WHERE Id=$id;
            """;

        cmd.Parameters.AddWithValue(
            "$utc",
            atUtc.ToString("O"));

        cmd.Parameters.AddWithValue(
            "$id",
            threadId);

        cmd.ExecuteNonQuery();
    }

    private static PhoneMessageRow ReadMessage(
        SqliteDataReader r)
        => new()
        {
            Id = r.GetInt64(0),
            ThreadId = r.GetInt64(1),
            PlayerId = r.GetString(2),
            PlayerName = r.GetString(3),
            NpcId = r.GetInt32(4),
            Sender = r.GetString(5),
            MessageText = r.GetString(6),
            SentUtc = ParseUtc(r.GetString(7)),
            DeliveryState = r.GetString(8),
            ReplyState = r.GetString(9),
            ReplyDueUtc =
                r.IsDBNull(10)
                    ? null
                    : ParseUtc(r.GetString(10)),
            ReplyToMessageId =
                r.IsDBNull(11)
                    ? null
                    : r.GetInt64(11),
            ConversationSessionId =
                r.IsDBNull(12)
                    ? null
                    : r.GetInt64(12),
            NoticeState =
                r.IsDBNull(13)
                    ? "unseen"
                    : r.GetString(13),
            ResponseAttemptCount =
                r.IsDBNull(14)
                    ? 0
                    : r.GetInt32(14),
            LastSchedulerDecision =
                r.IsDBNull(15)
                    ? ""
                    : r.GetString(15),
            SentGameTime =
                r.IsDBNull(16)
                    ? null
                    : ParseGameTime(r.GetString(16)),
            ReplyDueGameTime =
                r.IsDBNull(17)
                    ? null
                    : ParseGameTime(r.GetString(17)),
            SimulationDelayMinutes =
                r.IsDBNull(18)
                    ? 0
                    : r.GetDouble(18)
        };

    private static DateTime ParseUtc(
        string value)
        => DateTime.TryParse(
            value,
            null,
            System.Globalization
                .DateTimeStyles
                .RoundtripKind,
            out var dt)
            ? dt
            : DateTime.UtcNow;

    private static DateTimeOffset? ParseGameTime(string value)
        => DateTimeOffset.TryParse(value, out var dt)
            ? dt
            : null;

    private static string Clean(
        string? value,
        string fallback)
        => string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
}

public sealed class PhoneMessageRow
{
    public long Id { get; set; }
    public long ThreadId { get; set; }
    public string PlayerId { get; set; } = "";
    public string PlayerName { get; set; } = "Player";
    public int NpcId { get; set; }
    public string Sender { get; set; } = "";
    public string MessageText { get; set; } = "";
    public DateTime SentUtc { get; set; }
    public string DeliveryState { get; set; } = "";
    public string ReplyState { get; set; } = "none";
    public DateTime? ReplyDueUtc { get; set; }
    public long? ReplyToMessageId { get; set; }
    public long? ConversationSessionId { get; set; }

    // Hidden scheduler state. PhoneOS UI must not display these reasons.
    public string NoticeState { get; set; } = "unseen";
    public int ResponseAttemptCount { get; set; }
    public string LastSchedulerDecision { get; set; } = "";
    public DateTimeOffset? SentGameTime { get; set; }
    public DateTimeOffset? ReplyDueGameTime { get; set; }
    public double SimulationDelayMinutes { get; set; }

    public bool IsMine =>
        Sender.Equals(
            "player",
            StringComparison.OrdinalIgnoreCase);
}

public sealed class PhoneContactRow
{
    public string PlayerId { get; set; } = "";
    public int NpcId { get; set; }
    public string DisplayName { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string ContactSource { get; set; } = "";
    public int ContactTier { get; set; }
    public bool IsBlocked { get; set; }
    public bool IsMuted { get; set; }
    public bool IsFavorite { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
