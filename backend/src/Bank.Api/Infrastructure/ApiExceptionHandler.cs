using Bank.Application.Common;
using Bank.Domain.Common;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Bank.Api.Infrastructure;

/// <summary>Maps application/domain exceptions to RFC 7807 ProblemDetails. Anything else stays a 500.</summary>
internal class ApiExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        ProblemDetails? problem = exception switch
        {
            ValidationException e => new ValidationProblemDetails(
                e.Errors
                    .GroupBy(x => JsonName(x.PropertyName))
                    .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage).Distinct().ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Dados inválidos.",
            },
            DomainException e => new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = e.Message,
                Extensions = { ["code"] = e.Code },
            },
            NotFoundException e => new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = e.Message },
            ConflictException e => new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = e.Message },
            UnauthorizedException e => new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = e.Message },
            _ => null,
        };
        if (problem is null) return false;

        http.Response.StatusCode = problem.Status!.Value;
        return await problems.TryWriteAsync(new() { HttpContext = http, ProblemDetails = problem, Exception = exception });
    }

    private static string JsonName(string property) =>
        string.IsNullOrEmpty(property) ? property : char.ToLowerInvariant(property[0]) + property[1..];
}
