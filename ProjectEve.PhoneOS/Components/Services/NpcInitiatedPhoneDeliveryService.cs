using Microsoft.Data.Sqlite;
using ProjectEve.Core.Phone;
using ProjectEve.Core.Time;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEve.PhoneOS.Services;

/// <summary>
/// PhoneOS delivery adapter.
///
/// ProjectEve already generated/staged the exact NPC text.
/// This class only places that text into the phone database.
/// </summary>
public sealed class NpcInitiatedPhoneDeliveryService
{
    private readonly PhoneMessagingService _messaging;
    private readonly PhoneThreadPresenceService _threadPresence;
    private readonly IGameTimeService _clock;

    public NpcInitiatedPhoneDeliveryService(
        PhoneMessagingService messaging,
        PhoneThreadPresenceService threadPresence,
        IGameTimeService clock)
    {
        _messaging = messaging;
        _threadPresence = threadPresence;
        _clock = clock;

        EnsureSchema();
    }

    public Task<NpcInitiatedDeliveryResult> DeliverAsync(
        NpcInitiatedOutboundMessage message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (message == null)
            throw new ArgumentNullException(nameof(message));

        var contact = _messaging
            .GetContacts(message.PlayerId)
            .FirstOrDefault(x => x.NpcId == message.NpcId);

        if (contact?.IsBlocked == true)
        {
            return Task.FromResult(new NpcInitiatedDeliveryResult
            {
                Delivered = false,
                Reason = "blocked"
            });
        }

        if (contact == null)
        {
            if (!message.AllowUnknownNumber)
            {
                return Task.FromResult(new NpcInitiatedDeliveryResult
                {
                    Delivered = false,
                    Reason = "player_does_not_have_contact"
                });
            }

            _messaging.EnsureKnownContact(
                message.PlayerId,
                message.NpcId,
                message.NpcName,
                source: "received_text",
                contactTier: 4);
        }

        long threadId = _messaging.GetOrCreateThread(
            message.PlayerId,
            message.NpcId,
            message.NpcName);

        using var conn = Open();

        // Idempotency across crash between phone insert and world MarkDelivered.
        using (var existing = conn.CreateCommand())
        {
            existing.CommandText = """
                SELECT Id
                FROM PhoneMessage
                WHERE InitiatedTriggerId=$trigger
                LIMIT 1;
                """;
            existing.Parameters.AddWithValue("$trigger", message.TriggerId);

            var value = existing.ExecuteScalar();
            if (value != null && value != DBNull.Value)
            {
                return Task.FromResult(new NpcInitiatedDeliveryResult
                {
                    Delivered = true,
                    PhoneMessageId = Convert.ToInt64(
                        value,
                        CultureInfo.InvariantCulture),
                    Reason = "already_delivered"
                });
            }
        }

        using var tx = conn.BeginTransaction();

        bool viewingThread = _threadPresence.IsActive(
            message.PlayerId,
            message.NpcId);

        string deliveryState =
            viewingThread ? "seen" : "delivered";

        long phoneMessageId;

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
                 SentGameTime,ReplyDueGameTime,SimulationDelayMinutes,
                 InitiatedTriggerId)
                VALUES
                ($thread,$player,$playerName,$npc,
                 'npc',$text,$sent,
                 $delivery,'none',NULL,
                 NULL,$session,
                 $notice,0,'npc_initiated',
                 $sentGame,NULL,0,
                 $trigger);
                SELECT last_insert_rowid();
                """;

            insert.Parameters.AddWithValue("$thread", threadId);
            insert.Parameters.AddWithValue("$player", message.PlayerId);
            insert.Parameters.AddWithValue("$playerName", message.PlayerName);
            insert.Parameters.AddWithValue("$npc", message.NpcId);
            insert.Parameters.AddWithValue("$text", message.Text);
            insert.Parameters.AddWithValue("$sent", DateTime.UtcNow.ToString("O"));
            insert.Parameters.AddWithValue("$delivery", deliveryState);
            insert.Parameters.AddWithValue("$session", message.ConversationSessionId);
            insert.Parameters.AddWithValue("$notice", viewingThread ? "seen" : "unseen");
            insert.Parameters.AddWithValue(
                "$sentGame",
                message.GameTime == default
                    ? _clock.Now.ToString("O")
                    : message.GameTime.ToString("O"));
            insert.Parameters.AddWithValue("$trigger", message.TriggerId);

            phoneMessageId = Convert.ToInt64(
                insert.ExecuteScalar(),
                CultureInfo.InvariantCulture);
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
            thread.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));
            thread.Parameters.AddWithValue("$session", message.ConversationSessionId);
            thread.Parameters.AddWithValue("$id", threadId);
            thread.ExecuteNonQuery();
        }

        tx.Commit();

        return Task.FromResult(new NpcInitiatedDeliveryResult
        {
            Delivered = true,
            PhoneMessageId = phoneMessageId,
            Reason = viewingThread
                ? "delivered_while_viewing_thread"
                : "delivered"
        });
    }

    private void EnsureSchema()
    {
        using var conn = Open();

        EnsureColumn(
            conn,
            "PhoneMessage",
            "InitiatedTriggerId",
            "INTEGER NULL");

        using var index = conn.CreateCommand();
        index.CommandText = """
            CREATE UNIQUE INDEX IF NOT EXISTS UX_PhoneMessage_InitiatedTrigger
            ON PhoneMessage(InitiatedTriggerId)
            WHERE InitiatedTriggerId IS NOT NULL;
            """;
        index.ExecuteNonQuery();
    }

    private static void EnsureColumn(
        SqliteConnection conn,
        string table,
        string column,
        string definition)
    {
        using var info = conn.CreateCommand();
        info.CommandText = $"PRAGMA table_info({table});";

        using (var r = info.ExecuteReader())
        {
            while (r.Read())
            {
                if (r.GetString(1).Equals(
                        column,
                        StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }

        using var alter = conn.CreateCommand();
        alter.CommandText =
            $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(
            "Data Source=" + _messaging.DatabasePath);

        conn.Open();

        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();

        return conn;
    }
}

public sealed class NpcInitiatedDeliveryResult
{
    public bool Delivered { get; set; }
    public long PhoneMessageId { get; set; }
    public string Reason { get; set; } = "";
}
