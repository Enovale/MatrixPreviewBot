using LibMatrix.EventTypes.Spec;
using LibMatrix.RoomTypes;

namespace MatrixPreviewBot.Processors;

public class TumblrProcessor : ProcessorBase
{
    public override Task<IEnumerable<RoomMessageEventContent>?> ProcessUriAsync(GenericRoom room, Uri uri)
    {
        return Task.FromResult<IEnumerable<RoomMessageEventContent>?>(null!);
    }
}