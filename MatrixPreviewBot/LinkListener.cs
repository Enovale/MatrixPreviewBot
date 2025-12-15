using System.Text.RegularExpressions;
using ArcaneLibs.Extensions;
using LibMatrix;
using LibMatrix.EventTypes.Spec;
using LibMatrix.Helpers;
using LibMatrix.Homeservers;

namespace MatrixPreviewBot;

public partial class LinkListener(
    AuthenticatedHomeserverGeneric hs,
    ILogger<LinkListener> logger,
    LinkListenerConfiguration config,
    BotConfiguration botConfig) : IHostedService
{
    public delegate void NewUriSentDelegate(MatrixEventResponse @event, List<Uri> uris, bool containsOtherText);
    public static event NewUriSentDelegate? NewUriSent;

    private Task? _listenerTask;
    private readonly CancellationTokenSource _cts = new();
    private readonly long _startupTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [GeneratedRegex(
        @"https?:\/\/(?:www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b(?:[-a-zA-Z0-9()@:%_\+.~#?&\/=]*)")]
    public static partial Regex LinkRegex();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _listenerTask = Run(cancellationToken);
        logger.LogInformation("Started link listener!");
        return Task.CompletedTask;
    }

    public async Task Run(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting link listener!");

        var syncHelper = new SyncHelper(hs, logger)
        {
            Timeout = config.Timeout ?? 30_000,
            MinimumDelay = config.MinimumSyncTime ?? TimeSpan.Zero,
            SetPresence = config.Presence // ?? botConfig.Presence,
        };

        syncHelper.SyncReceivedHandlers.Add(async sync =>
        {
            logger.LogDebug("Sync received!");
            foreach (var roomResp in sync.Rooms?.Join ?? [])
            {
                if (roomResp.Value.Timeline?.Events is null) continue;
                foreach (var @event in roomResp.Value.Timeline.Events)
                {
                    @event.RoomId = roomResp.Key;
                    if (@event.OriginServerTs < _startupTime) continue; // ignore events older than startup time

                    try
                    {
                        if (@event is { Type: "m.room.message", TypedContent: RoomMessageEventContent message })
                        {
                            logger.LogCritical(message.BodyWithoutReplyFallback);
                            var matches = LinkRegex().Matches(message.BodyWithoutReplyFallback);
                            var negativeBody = message.BodyWithoutReplyFallback;
                            if (matches.Count > 0)
                            {
                                NewUriSent?.Invoke(@event, matches.Select(match =>
                                {
                                    logger.LogCritical(
                                        $"New URI in {@event.RoomId} from {@event.Sender}: {match.Value}");
                                    negativeBody = negativeBody.Remove(match.Index - (message.BodyWithoutReplyFallback.Length - negativeBody.Length), match.Length);
                                    return Uri.TryCreate(match.Value, UriKind.Absolute, out var uri) ? uri : null;
                                }).Where(match => match != null).Cast<Uri>().ToList(), !negativeBody.IsWhiteSpace());
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Error in link listener!");
                        Console.WriteLine(@event.ToJson(ignoreNull: false, indent: true));
                    }
                }
            }
        });

        await syncHelper.RunSyncLoopAsync(cancellationToken: _cts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Link listener shutting down!");
        if (_listenerTask is null)
        {
            logger.LogError("Could not shut down link listener task because it was null!");
            return;
        }

        await _cts.CancelAsync();
    }
}

public class LinkListenerConfiguration
{
    public LinkListenerConfiguration(IConfiguration config) => config.GetSection("LinkListener").Bind(this);

    public TimeSpan? MinimumSyncTime { get; set; }

    public int? Timeout { get; set; }

    public string? Presence { get; set; }
}