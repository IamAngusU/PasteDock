namespace PasteDock.Core.Models;

public sealed record EditorDraft(Guid Id, Guid? ClipId, Guid? BaseBlobId, string Text, DateTimeOffset SavedAt)
{
    public const int MaxCharacters = 1_048_576;
    public static TimeSpan Lifetime => TimeSpan.FromDays(7);
    public void Validate()
    {
        if (Id == Guid.Empty || ClipId == Guid.Empty || BaseBlobId == Guid.Empty
            || ClipId.HasValue != BaseBlobId.HasValue || string.IsNullOrWhiteSpace(Text)
            || Text.Length > MaxCharacters || SavedAt == default)
            throw new InvalidDataException("The editor recovery record is invalid. It has not been replaced.");
    }
}

internal sealed record EditorDraftState(int Version, EditorDraft Draft);
