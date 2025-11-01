namespace P2PBookApp.Contracts;

public record CreateLocalUserRequest(
    string Email,
    string Password,
    string Name,
    string? Location
);

public record UpdateUserProfileRequest(
    Guid UserId,
    string? Name,
    string? Password,
    string? Location,
    string? Bio
);

public record LoginRequest(
    string Email,
    string Password
);

public record UserResponse(
    Guid UserId,           // UUID column: user_id
    string Email,
    string Name,
    string? Location,
    bool EmailVerified,
    string AuthProvider
);

public record GenreResponse(
    int Genre,
    string Name,
    string description
);
public record UserGenreRequest(
    Guid UserId,
    List<int> GenreList
);
public record UserIdRequest(
    Guid UserId
);
public record UpdateBookRequest(
    Guid UserId,
    int BookId,
    string? Title,
    string? Subtitle,
    string? Description,
    int? PublicationYear,
    int? LocationId,
    int? ConditionId,
    int? GenreId,
    int? StatusId,
    string? AuthorName
);
public record CreateBookRequest(
    Guid UserId,
    string Title,
    string? Subtitle,
    string? Description,
    int PublicationYear,
    int? LocationId,
    int ConditionId,
    int GenreId,
    int? StatusId,
    string AuthorName
);
public record BookRequest(
    Guid? Id,
    string? Search,
    int? StartYear,
    int? EndYear
);

public record BookList(
    List<BookRecord> List
);

public record BookRecord(
    int BookId,
    string Title,
    string Subtitle,
    string Description,
    int PublicationYear,
    string LocationName,
    string ConditionName,
    string GenreName,
    string StatusName,
    string AuthorName
);

public record CreateTradeRequest(
    Guid RequesterUserId,
    int RequestedBookId,
    int? OfferedBookId,
    string? Note
);

public record UpdateTradeStatusRequest(
    Guid ActingUserId,
    int TradeId,
    int StatusId
);

public record TradeRecord(
    int TradeId,
    string StatusName,
    Guid RequesterUserId,
    Guid OwnerUserId,
    int RequestedBookId,
    string RequestedTitle,
    int? OfferedBookId,
    string? OfferedTitle,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record UserPointHistoryRecord(
    int HistoryId,
    Guid UserId,
    int Points,
    int BasePoints,
    decimal Multiplier,
    string ActionType,
    int? RelatedId,
    DateTime CreatedAt
);