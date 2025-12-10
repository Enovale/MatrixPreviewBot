using LibMatrix.EventTypes.Spec;
using LibMatrix.Utilities.Bot.Interfaces;

namespace MatrixPreviewBot.Handlers;

public static class InviteHandler {
    public static async Task HandleAsync(RoomInviteContext invite) {
        var room = invite.Homeserver.GetRoom(invite.RoomId);
        await room.JoinAsync();
        await room.SendMessageEventAsync(new RoomMessageEventContent("m.notice", "Hello! I'm UrlPreviewBot!"));
    }
}