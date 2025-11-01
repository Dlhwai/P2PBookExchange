namespace P2PBookApp;

public static class Api
{
    public static IResult Ok<T>(T data, string message = "Success")
        => Results.Json(new { statusCode = 200, message, data });

    public static IResult Created<T>(T data, string message = "Created")
        => Results.Json(new { statusCode = 201, message, data });

    public static IResult Bad(string message = "Bad Request", object? details = null)
        => Results.Json(new { statusCode = 400, message, details });

    public static IResult NotFound(string message = "Not Found")
        => Results.Json(new { statusCode = 404, message, data = (object?)null });

    public static IResult Error(string message = "Internal Server Error", object? details = null)
        => Results.Json(new { statusCode = 500, message, details });
}