using BCrypt.Net;
using P2PBookApp.Contracts;

namespace P2PBookApp;

public class P2PBookLogic
{
    private readonly P2PBookDB _db;

    public P2PBookLogic(P2PBookDB db)
    {
        _db = db;
    }

    public async Task<UserResponse> RegisterUserAsync(CreateLocalUserRequest req)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(req.Password);
        var userId = await _db.CreateLocalUserAsync(req.Email, hash, req.Name, req.Location);

        return new UserResponse(
            userId,
            req.Email,
            req.Name,
            req.Location,
            false,
            "local"
        );
    }
    public async Task<int> PostCreateBookAsyncLogic(CreateBookRequest request)
    {
        var result = await _db.PostCreateBookAsyncDB(request);
        return result;
    }
    
    public async Task<List<BookRecord>> GetBooksAsyncLogic(BookRequest request)
    {
        var list = await _db.GetBooksAsyncDB(request);
        return list ?? new List<BookRecord>();
    }

    public async Task<int> UpdateUserDetailsAsyncLogic(UpdateUserProfileRequest req)
    {
        string? hashed = null;
        if (!string.IsNullOrWhiteSpace(req.Password))
            hashed = BCrypt.Net.BCrypt.HashPassword(req.Password);

        var newRequest = new UpdateUserProfileRequest(
            req.UserId,
            req.Name,
            hashed,
            req.Location,
            req.Bio
        );

        var result = await _db.UpdateUserDetailsAsyncDB(newRequest);
        return result;
    }
    public async Task<UserResponse?> LoginAsync(LoginRequest req)
    {
        var record = await _db.GetUserByEmailAsync(req.Email);
        if (record == null) return null;

        var (passwordHash, user) = record.Value;

        if (!BCrypt.Net.BCrypt.Verify(req.Password, passwordHash))
            return null;

        return user;
    }

    public async Task<List<GenreResponse>> GetAllGenresAsyncLogic()
    {
        var list = await _db.GetAllGenresAsyncDB();
        return list ?? new List<GenreResponse>();
    }

    public async Task<List<GenreResponse>> GetUserGenresAsyncLogic(Guid userId)
    {
        var list = await _db.GetUserGenresAsyncDB(userId);
        return list ?? new List<GenreResponse>();
    }

    public async Task<int> PostUpdateBookAsyncLogic(UpdateBookRequest request)
    {
        var rows = await _db.PostUpdateBookAsyncDB(request);
        return rows;
    }

    public async Task<int> PostCreateUserGenreLogic(Guid userId, List<int> genreList)
    {
        int rows = 0;
        foreach (var item in genreList)
        {
            rows += await _db.PostCreateUserGenreDB(userId, item);
        }

        return rows;
    }
    public async Task<int> PostDeleteUserGenreLogic(Guid userId, List<int> genreList)
    {
        int rowsDeleted = 0;
        foreach (var item in genreList)
        {
            rowsDeleted += await _db.PostDeleteUserGenreDB(userId, item);
        }

        return rowsDeleted;
    }
    // Create trade: derive owner from book_b, set status = "Requested" (book_status_b)
    public async Task<int> CreateTradeAsyncLogic(CreateTradeRequest req)
    {
        if (req.RequesterUserId == Guid.Empty)
            throw new ArgumentException("RequesterUserId required");

        // 1) Find the book owner from book_b
        var owner = await _db.GetBookOwnerAsync(req.RequestedBookId);
        if (owner is null)
            throw new KeyNotFoundException("Requested book not found");
        if (owner.Value == req.RequesterUserId)
            throw new InvalidOperationException("You cannot request your own book");

        // 2) If requester offers a book, verify ownership
        if (req.OfferedBookId is not null)
        {
            var offeredOwner = await _db.GetBookOwnerAsync(req.OfferedBookId.Value);
            if (offeredOwner is null || offeredOwner.Value != req.RequesterUserId)
                throw new InvalidOperationException("Offered book does not belong to the requester");
        }

        // 3) Resolve status_id for "Requested" from book_status_b
        var requestedStatusId = await _db.ResolveBookStatusIdByNameAsync("Requested")
                            ?? throw new InvalidOperationException("Status 'Requested' missing in book_status_b");

        // 4) Create trade
        var tradeId = await _db.CreateTradeAsyncDB(req, owner.Value, requestedStatusId);
        return tradeId;
    }

    // Update status: only allow owner to Accept/Reject. (Requested is set on create)
    public async Task<int> UpdateTradeStatusAsyncLogic(UpdateTradeStatusRequest req)
    {
        if (req.ActingUserId == Guid.Empty) 
            throw new ArgumentException("ActingUserId required");

        var header = await _db.GetTradeHeaderAsync(req.TradeId);
        if (header is null) throw new KeyNotFoundException("Trade not found");

        var (requesterId, ownerId, currentStatusId) = header.Value;

        var statusName = await _db.ResolveBookStatusNameByIdAsync(req.StatusId)
                        ?? throw new InvalidOperationException("Invalid status id");

        if (statusName == "Accepted")
        {
            var updated = await _db.AcceptTradeAndPruneAsync(req.TradeId, req.StatusId);
            if (updated == 0) return 0;

            var swapped = await _db.SwapBookOwnerAsyncDB(req.TradeId);

            var requesterRep = await _db.GetUserReputationAsync(requesterId);
            var ownerRep     = await _db.GetUserReputationAsync(ownerId);
            var requesterMult = await _db.GetMultiplierFromDbAsync(requesterRep);
            var ownerMult     = await _db.GetMultiplierFromDbAsync(ownerRep);

            const int basePoints = 50;
            await _db.AwardPointsBothAsync(
                userA: requesterId, baseA: basePoints, multA: requesterMult,
                userB: ownerId,     baseB: basePoints, multB: ownerMult,
                actionType: "exchange_completed",
                relatedId: req.TradeId
            );

            return updated;
        }

        bool allowed = statusName switch
        {
            "Accepted"  => req.ActingUserId == ownerId,
            "Rejected"  => req.ActingUserId == ownerId,
            _           => false
        };
        if (!allowed) throw new UnauthorizedAccessException("User not allowed to change to this status");

        if (currentStatusId == req.StatusId) return 0;

        // If accepting, do it atomically: update + prune competing trades with same offered_book_id
        if (statusName == "Accepted")
        {
            
            return await _db.AcceptTradeAndPruneAsync(req.TradeId, req.StatusId);
        }

        // Otherwise, normal status update
        return await _db.UpdateTradeStatusAsyncDB(req.TradeId, req.StatusId);
    }


    // Inbox / Outbox
    public Task<List<TradeRecord>> GetTradeInboxAsyncLogic(Guid ownerUserId)
    {
        if (ownerUserId == Guid.Empty) throw new ArgumentException("ownerUserId required");
        return _db.ListTradesForOwnerAsync(ownerUserId);
    }
    public Task<List<TradeRecord>> GetTradeOutboxAsyncLogic(Guid requesterUserId)
    {
        if (requesterUserId == Guid.Empty) throw new ArgumentException("requesterUserId required");
        return _db.ListTradesForRequesterAsync(requesterUserId);
    }
    public async Task AwardPointsAsync(Guid userId, int basePoints, decimal multiplier, string actionType, int? relatedId = null)
    {
        await _db.AddUserPointsAsync(userId, basePoints, multiplier, actionType, relatedId);
    }
    public async Task<List<UserPointHistoryRecord>> GetUserPointHistoryAsyncLogic(Guid userId)
    {
        return await _db.GetUserPointHistoryAsync(userId);
    }




}
