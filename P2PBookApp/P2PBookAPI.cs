using Microsoft.AspNetCore.Mvc;
using P2PBookApp.Contracts;

namespace P2PBookApp;

public static class P2PBookAPI
{
    public static void MapEndpoints(WebApplication app)
    {
        app.MapPost("/auth/login", async (
            [FromServices] P2PBookLogic logic,
            [FromBody] LoginRequest req
        ) =>
        {
            var user = await logic.LoginAsync(req);
            if (user is null) return Api.Bad("Invalid email or password");
            return Api.Ok(user, "Login success");
        });

        app.MapPost("/user/create", async (
            [FromServices] P2PBookLogic logic,
            [FromBody] CreateLocalUserRequest req
        ) =>
        {
            var result = await logic.RegisterUserAsync(req);
            return Api.Created(result, "User created");
        });

        app.MapPost("/user/details", async (
            [FromServices] P2PBookLogic logic,
            [FromBody] UpdateUserProfileRequest req
        ) =>
        {
            var updated = await logic.UpdateUserDetailsAsyncLogic(req);
            if (updated == 0) return Api.NotFound("User not found or no changes");
            return Api.Ok(new { updated }, "User updated");
        });

        app.MapGet("/genres", async (P2PBookLogic logic) =>
        {
            var list = await logic.GetAllGenresAsyncLogic();
            return Api.Ok(list, list.Count > 0 ? "Success" : "No genres found");
        });

        app.MapPost("/user/getGenres", async (
            [FromServices] P2PBookLogic logic,
            [FromBody] UserIdRequest request
        ) =>
        {
            if (request.UserId == Guid.Empty)
                return Api.Bad("UserId must not be empty");

            var genres = await logic.GetUserGenresAsyncLogic(request.UserId);
            return Api.Ok(genres, genres.Count > 0 ? "Success" : "No genres found");
        });

        app.MapPost("/user/createUserGenres", async (
            [FromBody] UserGenreRequest request,
            [FromServices] P2PBookLogic logic
        ) =>
        {
            if (request.UserId == Guid.Empty)
                return Api.Bad("UserId must not be empty");
            if (request.GenreList == null || request.GenreList.Count == 0)
                return Api.Bad("GenreList must not be empty");

            var created = await logic.PostCreateUserGenreLogic(request.UserId, request.GenreList);
            return Api.Ok(new { request.UserId, created }, created > 0 ? "Genres added" : "No genres added");
        });

        app.MapDelete("/user/deleteUserGenres", async (
            [FromBody] UserGenreRequest request,
            [FromServices] P2PBookLogic logic
        ) =>
        {
            if (request.UserId == Guid.Empty)
                return Api.Bad("UserId must not be empty");
            if (request.GenreList == null || request.GenreList.Count == 0)
                return Api.Bad("GenreList must not be empty");

            var deleted = await logic.PostDeleteUserGenreLogic(request.UserId, request.GenreList);
            return Api.Ok(new { request.UserId, deleted }, deleted > 0 ? "Genres removed" : "No genres removed");
        });

        app.MapPost("/books/search", async (
            [FromServices] P2PBookLogic logic,
            [FromBody] BookRequest request
        ) =>
        {
            var result = await logic.GetBooksAsyncLogic(request);
            return Api.Ok(result, result.Count > 0 ? "Success" : "No books found");
        });

        app.MapPost("/books/create", async (
            [FromServices] P2PBookLogic logic,
            [FromBody] CreateBookRequest request
        ) =>
        {
            var created = await logic.PostCreateBookAsyncLogic(request);
            if (created == 0) return Api.Bad("Book not created");
            return Api.Created(new { created }, "Book created");
        });

        app.MapPost("/books/update", async (
            [FromServices] P2PBookLogic logic,
            [FromBody] UpdateBookRequest request
        ) =>
        {
            var updated = await logic.PostUpdateBookAsyncLogic(request);
            if (updated == 0) return Api.Bad("Book not updated");
            return Api.Ok(new { updated }, "Book Updated");
        });

        app.MapPost("/trades/create", async (
            [FromServices] P2PBookLogic logic,
            [FromBody] CreateTradeRequest req
        ) =>
        {
            try
            {
                var id = await logic.CreateTradeAsyncLogic(req);
                return Api.Created(new { tradeId = id }, "Trade created");
            }
            catch (ArgumentException ex)
            {
                return Api.Bad(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Api.Bad(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Api.Bad(ex.Message);
            }
            catch (Exception)
            {
                return Api.Error("Unexpected server error");
            }
        });

        app.MapPost("/trades/status", async (P2PBookLogic logic, UpdateTradeStatusRequest req) =>
        {
            var rows = await logic.UpdateTradeStatusAsyncLogic(req);
            if (rows == 0) return Api.Bad("Trade not updated");
            return Api.Ok(new { updated = rows }, "Trade status updated");
        });
        app.MapPost("/trades/inbox", async (
            [FromServices] P2PBookLogic logic,
            [FromBody] UserIdRequest request
        ) =>
        {
            if (request.UserId == Guid.Empty)
                return Api.Bad("UserId must not be empty");
            var list = await logic.GetTradeInboxAsyncLogic(request.UserId);
            return Api.Ok(list, list.Count > 0 ? "Success" : "No trades in inbox");
        });

        app.MapPost("/trades/outbox", async (
            [FromServices] P2PBookLogic logic,
            [FromBody] UserIdRequest request
        ) =>
        {
            if (request.UserId == Guid.Empty)
                return Api.Bad("UserId must not be empty");
            var list = await logic.GetTradeOutboxAsyncLogic(request.UserId);
            return Api.Ok(list, list.Count > 0 ? "Success" : "No trades in outbox");
        });
        app.MapPost("/points/history", async (
            [FromServices] P2PBookLogic logic,
            [FromBody] UserIdRequest req
        ) =>
        {
            var list = await logic.GetUserPointHistoryAsyncLogic(req.UserId);

            return Api.Ok(list, list.Count > 0 ? "Success" : "No point history found");
        });

    }
}

