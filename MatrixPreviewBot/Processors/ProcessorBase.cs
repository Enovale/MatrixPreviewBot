using LibMatrix.EventTypes.Spec;
using LibMatrix.RoomTypes;

namespace MatrixPreviewBot.Processors;

public abstract class ProcessorBase
{
    public abstract Task<IEnumerable<RoomMessageEventContent>?> ProcessUriAsync(GenericRoom room, Uri uri);
}