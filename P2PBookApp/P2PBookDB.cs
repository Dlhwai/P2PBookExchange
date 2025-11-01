using System.Data;        // <-- for ConnectionState
using Npgsql;
using NpgsqlTypes;
using P2PBookApp.Contracts;

namespace P2PBookApp;

public class P2PBookDB
{
    private readonly NpgsqlConnection _conn;
    public P2PBookDB(NpgsqlConnection conn) => _conn = conn;

    private async Task EnsureOpenAsync()
    {
        if (_conn.State != ConnectionState.Open)
            await _conn.OpenAsync();
    }

    public async Task<Guid> CreateLocalUserAsync(string email, string passwordHash, string name, string? location)
    {
        await EnsureOpenAsync();

        const string sql = @"
            SELECT (bookx.sp_user_create_local(@p_email, @p_password_hash, @p_name, @p_location)->>'id')::uuid;
        ";


        await using var cmd = new NpgsqlCommand(sql, _conn);
        cmd.Parameters.AddWithValue("p_email", email);
        cmd.Parameters.AddWithValue("p_password_hash", passwordHash);
        cmd.Parameters.AddWithValue("p_name", name);
        cmd.Parameters.AddWithValue("p_location", (object?)location ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync();
        return (Guid)result!;
    }

    public async Task<(string PasswordHash, UserResponse User)?> GetUserByEmailAsync(string email)
    {
        await EnsureOpenAsync();   // <-- and open here

        const string sql = @"
            SELECT user_id, email, password_hash, name, location, auth_provider, email_verified
            FROM bookx.app_user_b
            WHERE LOWER(email) = LOWER(@p_email)
            LIMIT 1;";
        await using var cmd = new NpgsqlCommand(sql, _conn);
        cmd.Parameters.AddWithValue("p_email", email);

        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return null;

        var user = new UserResponse(
            rdr.GetGuid(rdr.GetOrdinal("user_id")),
            rdr.GetString(rdr.GetOrdinal("email")),
            rdr.GetString(rdr.GetOrdinal("name")),
            rdr.IsDBNull(rdr.GetOrdinal("location")) ? null : rdr.GetString(rdr.GetOrdinal("location")),
            rdr.GetBoolean(rdr.GetOrdinal("email_verified")),
            rdr.GetString(rdr.GetOrdinal("auth_provider"))
        );

        return (rdr.GetString(rdr.GetOrdinal("password_hash")), user);
    }

    public async Task<List<GenreResponse>> GetAllGenresAsyncDB()
    {
        await EnsureOpenAsync();

        const string sql = @"
            SELECT genre_id, name, description
            FROM bookx.genre_b
            WHERE active = true
            ORDER BY genre_id;
        ";

        await using var cmd = new NpgsqlCommand(sql, _conn);
        await using var rdr = await cmd.ExecuteReaderAsync();

        var genres = new List<GenreResponse>();

        while (await rdr.ReadAsync())
        {
            var genre = new GenreResponse(
                rdr.GetInt32(rdr.GetOrdinal("genre_id")),
                rdr.GetString(rdr.GetOrdinal("name")),
                rdr.GetString(rdr.GetOrdinal("description"))
            );

            genres.Add(genre);
        }

        return genres;
    }
    public async Task<int> SwapBookOwnerAsyncDB(int tradeId)
    {
        await EnsureOpenAsync();

        await using var tx = await _conn.BeginTransactionAsync();

        try
        {
            Guid requesterId, ownerId;
            int requestedBookId;
            int? offeredBookId;

            // 1) Fetch the parties & books for this trade (lock the trade row)
            const string sqlGet = @"
                SELECT requester_user_id, owner_user_id, requested_book_id, offered_book_id
                FROM bookx.book_trade_request_b
                WHERE trade_id = @p_trade_id
                FOR UPDATE;
            ";
            await using (var getCmd = new NpgsqlCommand(sqlGet, _conn, tx))
            {
                getCmd.Parameters.AddWithValue("p_trade_id", tradeId);

                await using var rdr = await getCmd.ExecuteReaderAsync();
                if (!await rdr.ReadAsync())
                    throw new KeyNotFoundException("Trade not found");

                requesterId     = rdr.GetGuid(0);
                ownerId         = rdr.GetGuid(1);
                requestedBookId = rdr.GetInt32(2);
                offeredBookId   = rdr.IsDBNull(3) ? null : rdr.GetInt32(3);
            }

            var totalUpdated = 0;

            // 2) Transfer requested book → requester
            const string sqlUpdateRequested = @"
                UPDATE bookx.book_b
                SET user_id = @p_new_owner
                WHERE book_id = @p_book_id;
            ";
            await using (var updReqCmd = new NpgsqlCommand(sqlUpdateRequested, _conn, tx))
            {
                updReqCmd.Parameters.AddWithValue("p_new_owner", requesterId);
                updReqCmd.Parameters.AddWithValue("p_book_id", requestedBookId);
                totalUpdated += await updReqCmd.ExecuteNonQueryAsync();
            }

            // 3) If there is an offered book, transfer it → owner
            if (offeredBookId.HasValue)
            {
                const string sqlUpdateOffered = @"
                    UPDATE bookx.book_b
                    SET user_id = @p_new_owner
                    WHERE book_id = @p_book_id;
                ";
                await using var updOffCmd = new NpgsqlCommand(sqlUpdateOffered, _conn, tx);
                updOffCmd.Parameters.AddWithValue("p_new_owner", ownerId);
                updOffCmd.Parameters.AddWithValue("p_book_id", offeredBookId.Value);
                totalUpdated += await updOffCmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            return totalUpdated; // 1 or 2 depending on whether offered_book_id existed
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<int> PostUpdateBookAsyncDB(UpdateBookRequest req)
    {
        await EnsureOpenAsync();

        const string sql = @"
            UPDATE bookx.book_b
            SET
                title = COALESCE(@p_title, title),
                subtitle = COALESCE(@p_subtitle, subtitle),
                description = COALESCE(@p_description, description),
                publication_year = COALESCE(@p_publication_year, publication_year),
                location_id = COALESCE(@p_location_id, location_id),
                condition_id = COALESCE(@p_condition_id, condition_id),
                genre_id = COALESCE(@p_genre_id, genre_id),
                status_id = COALESCE(@p_status_id, status_id),
                author_name = COALESCE(@p_author_name, author_name),
                updated_at = NOW()
            WHERE 
                book_id = @p_book_id
                AND user_id = @p_user_id;
        ";

        await using var cmd = new NpgsqlCommand(sql, _conn);

        // Required identifiers
        cmd.Parameters.AddWithValue("p_book_id", req.BookId);
        cmd.Parameters.AddWithValue("p_user_id", req.UserId);

        // Nullable/optional params
        cmd.Parameters.AddWithValue("p_title", (object?)req.Title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("p_subtitle", (object?)req.Subtitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("p_description", (object?)req.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("p_publication_year", (object?)req.PublicationYear ?? DBNull.Value);
        cmd.Parameters.AddWithValue("p_location_id", (object?)req.LocationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("p_condition_id", (object?)req.ConditionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("p_genre_id", (object?)req.GenreId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("p_status_id", (object?)req.StatusId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("p_author_name", (object?)req.AuthorName ?? DBNull.Value);

        return await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<BookRecord>> GetBooksAsyncDB(BookRequest request)
    {
        await EnsureOpenAsync();

        const string sql = @"
            SELECT 
                b.book_id,
                b.title,
                b.subtitle,
                b.description,
                b.publication_year,
                loc.location_name,
                cond.condition_name,
                g.name AS genre_name,
                s.status_name,
                b.author_name,
                b.user_id
            FROM bookx.book_b b
            LEFT JOIN bookx.book_location_b   loc  ON loc.location_id = b.location_id
            LEFT JOIN bookx.book_condition_b  cond ON cond.condition_id = b.condition_id
            LEFT JOIN bookx.genre_b           g    ON g.genre_id = b.genre_id
            LEFT JOIN bookx.book_status_b     s    ON s.status_id = b.status_id
            WHERE
                (
                    @p_search IS NULL 
                    OR b.title ILIKE '%' || @p_search || '%'
                    OR b.subtitle ILIKE '%' || @p_search || '%'
                    OR b.description ILIKE '%' || @p_search || '%'
                    OR loc.location_name ILIKE '%' || @p_search || '%'
                    OR cond.condition_name ILIKE '%' || @p_search || '%'
                    OR g.name ILIKE '%' || @p_search || '%'
                    OR s.status_name ILIKE '%' || @p_search || '%'
                    OR b.author_name ILIKE '%' || @p_search || '%'
                )
                AND
                (
                    (@p_start_year IS NULL AND @p_end_year IS NULL)
                    OR
                    (
                        (@p_start_year IS NULL OR b.publication_year >= @p_start_year)
                        AND 
                        (@p_end_year   IS NULL OR b.publication_year <= @p_end_year)
                    )
                )
                AND
                (
                    @p_user_id IS NULL
                    OR b.user_id = @p_user_id
                )
            ORDER BY b.book_id;
        ";

        await using var cmd = new NpgsqlCommand(sql, _conn);

        cmd.Parameters.Add("p_search", NpgsqlDbType.Text)
            .Value = string.IsNullOrWhiteSpace(request.Search)
                ? (object?)DBNull.Value
                : request.Search;


        cmd.Parameters.Add("p_start_year", NpgsqlDbType.Integer)
            .Value = request.StartYear.HasValue
                ? request.StartYear.Value
                : (object)DBNull.Value;

        cmd.Parameters.Add("p_end_year", NpgsqlDbType.Integer)
            .Value = request.EndYear.HasValue
                ? request.EndYear.Value
                : (object)DBNull.Value;

        cmd.Parameters.AddWithValue("p_user_id",
            request.Id.HasValue ? request.Id.Value : (object)DBNull.Value);
        cmd.Parameters["p_user_id"].NpgsqlDbType = NpgsqlDbType.Uuid;

        var list = new List<BookRecord>();

        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            list.Add(new BookRecord(
                rdr.GetInt32(0),
                rdr.GetString(1),
                rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                rdr.IsDBNull(3) ? "" : rdr.GetString(3),
                rdr.GetInt32(4),
                rdr.IsDBNull(5) ? "" : rdr.GetString(5),
                rdr.IsDBNull(6) ? "" : rdr.GetString(6),
                rdr.GetString(7),
                rdr.GetString(8),
                rdr.IsDBNull(9) ? "" : rdr.GetString(9)
            ));
        }

        return list;
    }

    public async Task<int> PostCreateBookAsyncDB(CreateBookRequest request)
    {
        await EnsureOpenAsync();

        const string sql = @"
            INSERT INTO bookx.book_b(
                title, 
                subtitle, 
                description, 
                publication_year, 
                location_id, 
                condition_id, 
                genre_id, 
                active, 
                created_at, 
                updated_at, 
                user_id, 
                status_id, 
                author_name
            )
            VALUES(
                @p_title, 
                @p_subtitle, 
                @p_description,
                @p_publication_year,
                @p_location_id,
                @p_condition_id,
                @p_genre_id,
                true,
                NOW(),
                NOW(),
                @p_user_id,
                @p_status_id,
                @p_author_name
            );
        ";
        await using var cmd = new NpgsqlCommand(sql, _conn);
        cmd.Parameters.AddWithValue("p_title", request.Title);
        cmd.Parameters.AddWithValue("p_author_name", request.AuthorName);
        cmd.Parameters.AddWithValue("p_subtitle", (object?)request.Subtitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("p_description", (object?)request.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("p_publication_year", request.PublicationYear);
        cmd.Parameters.AddWithValue("p_location_id", (object?)request.LocationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("p_condition_id", request.ConditionId);
        cmd.Parameters.AddWithValue("p_genre_id", request.GenreId);
        cmd.Parameters.AddWithValue("p_status_id", (object?)request.StatusId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("p_user_id", request.UserId);
        return await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> UpdateUserDetailsAsyncDB(UpdateUserProfileRequest req)
    {
        await EnsureOpenAsync();

        const string sql = @"
            UPDATE bookx.app_user_b
            SET 
                name = COALESCE(@p_name, name),
                password_hash = COALESCE(@p_password_hash, password_hash),
                location = COALESCE(@p_location, location),
                bio = COALESCE(@p_bio, bio)
            WHERE user_id = @p_user_id;
        ";

        await using var cmd = new NpgsqlCommand(sql, _conn);

        cmd.Parameters.AddWithValue("p_user_id", req.UserId);
        cmd.Parameters.AddWithValue("p_name", (object?)req.Name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("p_password_hash", (object?)req.Password ?? DBNull.Value);
        cmd.Parameters.AddWithValue("p_location", (object?)req.Location ?? DBNull.Value);
        cmd.Parameters.AddWithValue("p_bio", (object?)req.Bio ?? DBNull.Value);

        return await cmd.ExecuteNonQueryAsync();
    }
    public async Task<int> PostCreateUserGenreDB(Guid userId, int genreId)
    {
        await EnsureOpenAsync();

        const string sql = @"
            INSERT INTO bookx.app_user_genre_r (user_id, genre_id)
            VALUES (@p_user_id, @p_genre_id)
            ON CONFLICT DO NOTHING;
        ";

        await using var cmd = new NpgsqlCommand(sql, _conn);
        cmd.Parameters.AddWithValue("p_user_id", userId);
        cmd.Parameters.AddWithValue("p_genre_id", genreId);

        var result = await cmd.ExecuteNonQueryAsync();

        return result;
    }
    public async Task<int> PostDeleteUserGenreDB(Guid userId, int genreId)
    {
        await EnsureOpenAsync();

        const string sql = @"
            DELETE FROM bookx.app_user_genre_r
            WHERE user_id = @p_user_id
            AND genre_id = @p_genre_id;
        ";

        await using var cmd = new NpgsqlCommand(sql, _conn);
        cmd.Parameters.AddWithValue("p_user_id", userId);
        cmd.Parameters.AddWithValue("p_genre_id", genreId);

        var result = await cmd.ExecuteNonQueryAsync();

        return result;
    }
    public async Task<List<GenreResponse>> GetUserGenresAsyncDB(Guid userId)
    {
        await EnsureOpenAsync();

        const string sql = @"
            SELECT g.genre_id, g.name, g.description
            FROM bookx.app_user_genre_r aug
            LEFT JOIN bookx.genre_b g ON g.genre_id = aug.genre_id
            WHERE aug.user_id = @p_user_id
            ORDER BY g.genre_id;
        ";

        await using var cmd = new NpgsqlCommand(sql, _conn);
        cmd.Parameters.AddWithValue("p_user_id", userId);
        await using var rdr = await cmd.ExecuteReaderAsync();
        var genres = new List<GenreResponse>();

        while (await rdr.ReadAsync())
        {
            var genre = new GenreResponse(
                rdr.GetInt32(rdr.GetOrdinal("genre_id")),
                rdr.GetString(rdr.GetOrdinal("name")),
                rdr.GetString(rdr.GetOrdinal("description"))
            );

            genres.Add(genre);
        }

        return genres;
    }
    //
    //  TRADES DB
    //
    // Owner lookup from book_b
    public async Task<Guid?> GetBookOwnerAsync(int bookId)
    {
        await EnsureOpenAsync();
        const string sql = @"SELECT user_id FROM bookx.book_b WHERE book_id = @p_book_id;";
        await using var cmd = new NpgsqlCommand(sql, _conn);
        cmd.Parameters.AddWithValue("p_book_id", bookId);
        var o = await cmd.ExecuteScalarAsync();
        return o is Guid g ? g : (Guid?)null;
    }

    // Status helpers that use book_status_b
    public async Task<int?> ResolveBookStatusIdByNameAsync(string name)
    {
        await EnsureOpenAsync();
        const string sql = @"SELECT status_id FROM bookx.book_status_b WHERE status_name = @p_name;";
        await using var cmd = new NpgsqlCommand(sql, _conn);
        cmd.Parameters.AddWithValue("p_name", name);
        var o = await cmd.ExecuteScalarAsync();
        return o is int i ? i : (int?)null;
    }
    public async Task<string?> ResolveBookStatusNameByIdAsync(int id)
    {
        await EnsureOpenAsync();
        const string sql = @"SELECT status_name FROM bookx.book_status_b WHERE status_id = @p_id;";
        await using var cmd = new NpgsqlCommand(sql, _conn);
        cmd.Parameters.AddWithValue("p_id", id);
        var o = await cmd.ExecuteScalarAsync();
        return o as string;
    }

    // Create trade (uses book_status_b)
    public async Task<int> CreateTradeAsyncDB(CreateTradeRequest req, Guid ownerUserId, int requestedStatusId)
    {
        await EnsureOpenAsync();

        const string sql = @"
            INSERT INTO bookx.book_trade_request_b
                (requester_user_id, owner_user_id, requested_book_id, offered_book_id, status_id, note, created_at, updated_at)
            VALUES
                (@p_requester_user_id, @p_owner_user_id, @p_requested_book_id, @p_offered_book_id, @p_status_id, @p_note, NOW(), NOW())
            RETURNING trade_id;
        ";

        await using var cmd = new NpgsqlCommand(sql, _conn);
        cmd.Parameters.AddWithValue("p_requester_user_id", req.RequesterUserId);
        cmd.Parameters.AddWithValue("p_owner_user_id", ownerUserId);
        cmd.Parameters.AddWithValue("p_requested_book_id", req.RequestedBookId);
        cmd.Parameters.AddWithValue("p_offered_book_id", (object?)req.OfferedBookId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("p_status_id", requestedStatusId);
        cmd.Parameters.AddWithValue("p_note", (object?)req.Note ?? DBNull.Value);

        var o = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(o);
    }

    // Minimal header for authz & comparisons
    public async Task<(Guid requesterId, Guid ownerId, int statusId)?> GetTradeHeaderAsync(int tradeId)
    {
        await EnsureOpenAsync();
        const string sql = @"SELECT requester_user_id, owner_user_id, status_id
                            FROM bookx.book_trade_request_b
                            WHERE trade_id = @p_trade_id;";
        await using var cmd = new NpgsqlCommand(sql, _conn);
        cmd.Parameters.AddWithValue("p_trade_id", tradeId);

        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return null;
        return (rdr.GetGuid(0), rdr.GetGuid(1), rdr.GetInt32(2));
    }

    // Update status
    public async Task<int> UpdateTradeStatusAsyncDB(int tradeId, int statusId)
    {
        await EnsureOpenAsync();
        const string sql = @"UPDATE bookx.book_trade_request_b
                            SET status_id = @p_status_id, updated_at = NOW()
                            WHERE trade_id = @p_trade_id;";
        await using var cmd = new NpgsqlCommand(sql, _conn);
        cmd.Parameters.AddWithValue("p_status_id", statusId);
        cmd.Parameters.AddWithValue("p_trade_id", tradeId);
        return await cmd.ExecuteNonQueryAsync();
    }

    // Inbox (owner)
    public async Task<List<TradeRecord>> ListTradesForOwnerAsync(Guid ownerUserId)
    {
        await EnsureOpenAsync();

        const string sql = @"
            SELECT 
                t.trade_id,
                bs.status_name,
                t.requester_user_id,
                t.owner_user_id,
                t.requested_book_id,
                rb.title AS requested_title,
                t.offered_book_id,
                ob.title AS offered_title,
                t.note,
                t.created_at,
                t.updated_at
            FROM bookx.book_trade_request_b t
            JOIN bookx.book_status_b bs ON bs.status_id = t.status_id
            JOIN bookx.book_b rb ON rb.book_id = t.requested_book_id
            LEFT JOIN bookx.book_b ob ON ob.book_id = t.offered_book_id
            WHERE t.owner_user_id = @p_owner
                AND t.use_yn = true 
            ORDER BY t.created_at DESC;
        ";

        await using var cmd = new NpgsqlCommand(sql, _conn);
        cmd.Parameters.AddWithValue("p_owner", ownerUserId);

        var list = new List<TradeRecord>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            list.Add(new TradeRecord(
                rdr.GetInt32(0),
                rdr.GetString(1),
                rdr.GetGuid(2),
                rdr.GetGuid(3),
                rdr.GetInt32(4),
                rdr.GetString(5),
                rdr.IsDBNull(6) ? (int?)null : rdr.GetInt32(6),
                rdr.IsDBNull(7) ? null : rdr.GetString(7),
                rdr.IsDBNull(8) ? null : rdr.GetString(8),
                rdr.GetFieldValue<DateTimeOffset>(9),
                rdr.GetFieldValue<DateTimeOffset>(10)
            ));
        }
        return list;
    }

    // Outbox (requester)
    public async Task<List<TradeRecord>> ListTradesForRequesterAsync(Guid requesterUserId)
    {
        await EnsureOpenAsync();

        const string sql = @"
            SELECT 
                t.trade_id,
                bs.status_name,
                t.requester_user_id,
                t.owner_user_id,
                t.requested_book_id,
                rb.title AS requested_title,
                t.offered_book_id,
                ob.title AS offered_title,
                t.note,
                t.created_at,
                t.updated_at
            FROM bookx.book_trade_request_b t
            JOIN bookx.book_status_b bs ON bs.status_id = t.status_id
            JOIN bookx.book_b rb ON rb.book_id = t.requested_book_id
            LEFT JOIN bookx.book_b ob ON ob.book_id = t.offered_book_id
            WHERE t.requester_user_id = @p_requester
                AND t.use_yn = true 
            ORDER BY t.created_at DESC;
        ";

        await using var cmd = new NpgsqlCommand(sql, _conn);
        cmd.Parameters.AddWithValue("p_requester", requesterUserId);

        var list = new List<TradeRecord>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            list.Add(new TradeRecord(
                rdr.GetInt32(0),
                rdr.GetString(1),
                rdr.GetGuid(2),
                rdr.GetGuid(3),
                rdr.GetInt32(4),
                rdr.GetString(5),
                rdr.IsDBNull(6) ? (int?)null : rdr.GetInt32(6),
                rdr.IsDBNull(7) ? null : rdr.GetString(7),
                rdr.IsDBNull(8) ? null : rdr.GetString(8),
                rdr.GetFieldValue<DateTimeOffset>(9),
                rdr.GetFieldValue<DateTimeOffset>(10)
            ));
        }
        return list;
    }
    public async Task<int> AcceptTradeAndPruneAsync(int tradeId, int acceptedStatusId)
    {
        await EnsureOpenAsync();

        var requestedStatusId = await ResolveBookStatusIdByNameAsync("Requested")
            ?? throw new InvalidOperationException("Status 'Requested' missing");

        await using var tx = await _conn.BeginTransactionAsync();

        try
        {
            int? requestedBookId = null;
            int? offeredBookId = null;

            // 1) Get requested & offered book IDs
            {
                const string sql = @"
                    SELECT requested_book_id, offered_book_id
                    FROM bookx.book_trade_request_b
                    WHERE trade_id = @p_trade_id
                    FOR UPDATE;
                ";

                await using var cmd = new NpgsqlCommand(sql, _conn, tx);
                cmd.Parameters.AddWithValue("p_trade_id", tradeId);
                await using var rdr = await cmd.ExecuteReaderAsync();

                if (await rdr.ReadAsync())
                {
                    requestedBookId = rdr.GetInt32(0);
                    offeredBookId = rdr.IsDBNull(1) ? null : rdr.GetInt32(1);
                }
            }

            // 2) Mark THIS trade as Accepted AND set use_yn = false
            {
                const string sql = @"
                    UPDATE bookx.book_trade_request_b
                    SET 
                        status_id = @p_status_id,
                        use_yn = false,
                        updated_at = NOW()
                    WHERE trade_id = @p_trade_id;
                ";

                await using var cmd = new NpgsqlCommand(sql, _conn, tx);
                cmd.Parameters.AddWithValue("p_status_id", acceptedStatusId);
                cmd.Parameters.AddWithValue("p_trade_id", tradeId);
                await cmd.ExecuteNonQueryAsync();
            }

            // 3) Delete all other requested trades involving either book
            {
                const string sql = @"
                    DELETE FROM bookx.book_trade_request_b
                    WHERE 
                        trade_id <> @p_trade_id
                        AND status_id = @p_requested
                        AND (
                            requested_book_id = @p_req_book
                            OR offered_book_id = @p_req_book
                            OR requested_book_id = @p_off_book
                            OR offered_book_id = @p_off_book
                        );
                ";

                await using var cmd = new NpgsqlCommand(sql, _conn, tx);
                cmd.Parameters.AddWithValue("p_trade_id", tradeId);
                cmd.Parameters.AddWithValue("p_requested", requestedStatusId);
                cmd.Parameters.AddWithValue("p_req_book", requestedBookId ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("p_off_book", offeredBookId ?? (object)DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            return 1;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task AddUserPointsAsync(Guid userId, int basePoints, decimal multiplier, string actionType, int? relatedId)
    {
        await EnsureOpenAsync();

        var awarded = (int)Math.Round(basePoints * multiplier, MidpointRounding.AwayFromZero);

        using var tx = await _conn.BeginTransactionAsync();
        try
        {
            // 1) Update balances
            const string sqlUser = @"
                UPDATE bookx.app_user_b
                SET points_balance = points_balance + @p_awarded,
                    cumulative_points = cumulative_points + @p_awarded
                WHERE user_id = @p_user_id;
            ";
            await using (var cmd = new NpgsqlCommand(sqlUser, _conn, tx))
            {
                cmd.Parameters.AddWithValue("p_awarded", awarded);
                cmd.Parameters.AddWithValue("p_user_id", userId);
                await cmd.ExecuteNonQueryAsync();
            }

            // 2) Insert history with base_points & multiplier
            const string sqlHist = @"
                INSERT INTO bookx.user_points_history_l
                    (user_id, points, base_points, multiplier, action_type, related_id)
                VALUES
                    (@p_user_id, @p_awarded, @p_base, @p_mult, @p_action, @p_related);
            ";
            await using (var cmd = new NpgsqlCommand(sqlHist, _conn, tx))
            {
                cmd.Parameters.AddWithValue("p_user_id", userId);
                cmd.Parameters.AddWithValue("p_awarded", awarded);
                cmd.Parameters.AddWithValue("p_base", basePoints);
                cmd.Parameters.AddWithValue("p_mult", multiplier);
                cmd.Parameters.AddWithValue("p_action", actionType);
                cmd.Parameters.AddWithValue("p_related", (object?)relatedId ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<decimal> GetMultiplierFromDbAsync(double reputation)
    {
        await EnsureOpenAsync();

        const string sql = @"
            SELECT multiplier
            FROM bookx.user_point_multiplier_b
            WHERE @p_rep >= min_reputation
            AND @p_rep <= max_reputation
            LIMIT 1;
        ";

        await using var cmd = new NpgsqlCommand(sql, _conn);
        cmd.Parameters.AddWithValue("p_rep", reputation);

        var result = await cmd.ExecuteScalarAsync();
        return result is null ? 1.0m : (decimal)result;
    }
    public async Task<double> GetUserReputationAsync(Guid userId)
    {
        await EnsureOpenAsync();

        const string sql = @"
            SELECT reputation_sum, reputation_count
            FROM bookx.app_user_b
            WHERE user_id = @p_user_id;
        ";

        await using var cmd = new NpgsqlCommand(sql, _conn);
        cmd.Parameters.AddWithValue("p_user_id", userId);

        await using var rdr = await cmd.ExecuteReaderAsync();

        if (!await rdr.ReadAsync())
            return 0.0;   // user not found → fallback (should not happen)

        int sum   = rdr.IsDBNull(0) ? 0 : rdr.GetInt32(0);
        int count = rdr.IsDBNull(1) ? 0 : rdr.GetInt32(1);

        if (count == 0)
            return 0.0;   // no reviews yet → neutral reputation

        return (double)sum / count;
    }

    public async Task<List<UserPointHistoryRecord>> GetUserPointHistoryAsync(Guid userId)
    {
        await EnsureOpenAsync();

        const string sql = @"
            SELECT history_id, user_id, points, base_points, multiplier,
                action_type, related_id, created_at
            FROM bookx.user_points_history_l
            WHERE user_id = @p_user_id
            ORDER BY created_at DESC;
        ";

        await using var cmd = new NpgsqlCommand(sql, _conn);
        cmd.Parameters.AddWithValue("p_user_id", userId);

        var list = new List<UserPointHistoryRecord>();
        await using var rdr = await cmd.ExecuteReaderAsync();

        while (await rdr.ReadAsync())
        {
            list.Add(new UserPointHistoryRecord(
                rdr.GetInt32(0),
                rdr.GetGuid(1),
                rdr.GetInt32(2),
                rdr.GetInt32(3),
                rdr.GetDecimal(4),
                rdr.GetString(5),
                rdr.IsDBNull(6) ? null : rdr.GetInt32(6),
                rdr.GetDateTime(7)
            ));
        }

        return list;
    }
    public async Task AwardPointsBothAsync(
        Guid userA, int baseA, decimal multA,
        Guid userB, int baseB, decimal multB,
        string actionType, int? relatedId)
    {
        await EnsureOpenAsync();

        using var tx = await _conn.BeginTransactionAsync();
        try
        {
            await AddUserPointsAsyncInternal(userA, baseA, multA, actionType, relatedId, tx);
            await AddUserPointsAsyncInternal(userB, baseB, multB, actionType, relatedId, tx);

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private async Task AddUserPointsAsyncInternal(
        Guid userId, int basePoints, decimal multiplier,
        string actionType, int? relatedId, NpgsqlTransaction tx)
    {
        var awarded = (int)Math.Round(basePoints * multiplier, MidpointRounding.AwayFromZero);

        // 1) Update balances
        const string sqlUser = @"
            UPDATE bookx.app_user_b
            SET points_balance = points_balance + @p_awarded,
                cumulative_points = cumulative_points + @p_awarded
            WHERE user_id = @p_user_id;
        ";
        await using (var cmd = new NpgsqlCommand(sqlUser, _conn, tx))
        {
            cmd.Parameters.AddWithValue("p_awarded", awarded);
            cmd.Parameters.AddWithValue("p_user_id", userId);
            await cmd.ExecuteNonQueryAsync();
        }

        // 2) Insert into history with base_points & multiplier
        const string sqlHist = @"
            INSERT INTO bookx.user_points_history_l
                (user_id, points, base_points, multiplier, action_type, related_id, created_at)
            VALUES
                (@p_user_id, @p_awarded, @p_base, @p_mult, @p_action, @p_related, NOW());
        ";
        await using (var cmd = new NpgsqlCommand(sqlHist, _conn, tx))
        {
            cmd.Parameters.AddWithValue("p_user_id", userId);
            cmd.Parameters.AddWithValue("p_awarded", awarded);
            cmd.Parameters.AddWithValue("p_base", basePoints);
            cmd.Parameters.AddWithValue("p_mult", multiplier);
            cmd.Parameters.AddWithValue("p_action", actionType);
            cmd.Parameters.AddWithValue("p_related", (object?)relatedId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
    }




}

